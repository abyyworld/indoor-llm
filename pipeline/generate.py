"""LLM candidate generation (design-spec.md sections 4, 5 and 7).

Structure of one run:

  1. Ask Claude for candidates in chunks, with an `enum`-constrained JSON schema so
     out-of-pool values are mechanically hard to produce.
  2. Validate everything that comes back anyway.
  3. On rejection, re-ask on the same conversation with the exact violations, up to
     `MAX_ATTEMPTS_PER_CHUNK` times. Rejected candidates are kept in the run file:
     how often the model breaks the constraints is a result, not noise.
  4. Assign ids and provenance ourselves, then hand off to the validator again as a
     complete Unity-facing config.

The API key comes from the `ANTHROPIC_API_KEY` environment variable. Nothing here
writes a key to disk.
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any

from .pools import (
    BRIGHTNESSES,
    NEUTRAL_LABEL,
    SATURATIONS,
    canonical,
)
from .prompts import (
    build_continuation_prompt,
    build_emotion_prompt,
    build_neutral_prompt,
    build_reask_prompt,
    build_system_prompt,
)
from .schema import candidates_envelope_schema, room_id
from .validate import (
    Violation,
    format_violations,
    validate_candidate,
    validate_room_config,
)

DEFAULT_MODEL = "claude-opus-5"

#: Candidates per request. Small chunks keep each response short and let a single
#: bad chunk be re-asked without discarding the whole run.
CHUNK_SIZE = 25
MAX_ATTEMPTS_PER_CHUNK = 3
MAX_TOKENS = 16000


class GenerationError(RuntimeError):
    """The model could not be made to produce usable candidates."""


@dataclass
class RunResult:
    target_label: str
    model: str
    rooms: list[dict] = field(default_factory=list)
    rejected: list[dict] = field(default_factory=list)
    requests: int = 0

    @property
    def rejection_rate(self) -> float:
        total = len(self.rooms) + len(self.rejected)
        return len(self.rejected) / total if total else 0.0


def make_client():
    """Build an Anthropic client. Key is read from the environment by the SDK."""
    try:
        import anthropic
    except ModuleNotFoundError as exc:  # pragma: no cover - environment problem
        raise GenerationError(
            "the `anthropic` package is not installed -- pip install -r requirements.txt"
        ) from exc

    if not os.environ.get("ANTHROPIC_API_KEY"):
        raise GenerationError(
            "ANTHROPIC_API_KEY is not set. Export it, or run `ant auth login`."
        )
    return anthropic.Anthropic()


def _first_text(message: Any) -> str:
    """Pull the JSON payload out of the response, stepping over thinking blocks."""
    if getattr(message, "stop_reason", None) == "refusal":
        details = getattr(message, "stop_details", None)
        reason = getattr(details, "explanation", None) or "no explanation given"
        raise GenerationError(f"model declined the request: {reason}")
    for block in message.content:
        if block.type == "text":
            return block.text
    raise GenerationError("response contained no text block")


def _ask(client, model: str, messages: list[dict], count: int, sketch: bool) -> list[Any]:
    """One request. Streamed, because long candidate lists otherwise risk a timeout."""
    with client.messages.stream(
        model=model,
        max_tokens=MAX_TOKENS,
        thinking={"type": "adaptive"},
        system=build_system_prompt(),
        messages=messages,
        output_config={
            "format": {
                "type": "json_schema",
                "schema": candidates_envelope_schema(count, include_sketch=sketch),
            }
        },
    ) as stream:
        message = stream.get_final_message()

    payload = json.loads(_first_text(message))
    candidates = payload.get("candidates")
    if not isinstance(candidates, list):
        raise GenerationError("response JSON had no `candidates` array")
    messages.append({"role": "assistant", "content": message.content})
    return candidates


def _combo(candidate: dict) -> tuple:
    return (
        candidate["hue"],
        candidate["saturation"],
        candidate["brightness"],
        candidate["texture"],
    )


def _normalise(candidate: dict) -> dict:
    """Snap floats onto their pool members so 0.20 and 0.2 serialise identically."""
    out = dict(candidate)
    out["hue"] = int(candidate["hue"])
    out["saturation"] = canonical(float(candidate["saturation"]), SATURATIONS)
    out["brightness"] = canonical(float(candidate["brightness"]), BRIGHTNESSES)
    return out


def generate_candidates(
    client,
    target_label: str,
    count: int,
    *,
    model: str = DEFAULT_MODEL,
    sketch: bool = False,
    chunk_size: int = CHUNK_SIZE,
    verbose: bool = True,
) -> RunResult:
    """Generate `count` validated candidates for one target label."""
    result = RunResult(target_label=target_label, model=model)

    opening = (
        build_neutral_prompt(min(count, chunk_size))
        if target_label == NEUTRAL_LABEL
        else build_emotion_prompt(target_label, min(count, chunk_size))
    )
    messages: list[dict] = [{"role": "user", "content": opening}]

    accepted: list[dict] = []
    seen: list[tuple] = []

    while len(accepted) < count:
        want = min(chunk_size, count - len(accepted))

        # Tracks the shortfall within this chunk, so a re-ask requests exactly as many
        # candidates as the schema will then constrain. Asking the prompt for 3 while the
        # schema demands 25 is how you teach a model to ignore the prompt.
        outstanding = want

        for attempt in range(1, MAX_ATTEMPTS_PER_CHUNK + 1):
            result.requests += 1
            candidates = _ask(client, model, messages, outstanding, sketch)

            violations_all: list[Violation] = []
            got = 0
            for candidate in candidates:
                violations = validate_candidate(candidate, allow_sketch=sketch)
                if violations:
                    violations_all.extend(violations)
                    result.rejected.append(
                        {
                            "target_emotion": target_label,
                            "candidate": candidate,
                            "violations": [str(v) for v in violations],
                            "attempt": attempt,
                        }
                    )
                    continue
                normalised = _normalise(candidate)
                accepted.append(normalised)
                seen.append(_combo(normalised))
                got += 1

            outstanding -= got

            if verbose:
                print(
                    f"  [{target_label}] request {result.requests}: "
                    f"{got} accepted, {len(candidates) - got} rejected "
                    f"({len(accepted)}/{count} total)"
                )

            if outstanding <= 0 or not violations_all:
                break
            if attempt == MAX_ATTEMPTS_PER_CHUNK:
                raise GenerationError(
                    f"{target_label}: still failing validation after "
                    f"{MAX_ATTEMPTS_PER_CHUNK} attempts:\n"
                    + format_violations(violations_all)
                )
            messages.append(
                {
                    "role": "user",
                    "content": build_reask_prompt(
                        format_violations(violations_all), outstanding
                    ),
                }
            )

        if len(accepted) < count:
            messages.append(
                {
                    "role": "user",
                    "content": build_continuation_prompt(
                        seen, min(chunk_size, count - len(accepted))
                    ),
                }
            )

    for index, candidate in enumerate(accepted[:count], start=1):
        room = {
            "id": room_id(target_label, index),
            "target_emotion": target_label,
            "source": "llm",
            "hue": candidate["hue"],
            "saturation": candidate["saturation"],
            "brightness": candidate["brightness"],
            "texture": candidate["texture"],
            "rationale": candidate["rationale"].strip(),
        }
        if sketch and "sketch" in candidate:
            # Kept for the paper's qualitative record; stripped by export-unity.
            room["_sketch"] = candidate["sketch"]

        violations = validate_room_config({k: v for k, v in room.items() if k != "_sketch"})
        if violations:  # pragma: no cover - our own assembly bug, not the model's
            raise GenerationError(
                f"assembled config failed validation:\n{format_violations(violations)}"
            )
        result.rooms.append(room)

    return result


def duplicate_rate(rooms: list[dict]) -> float:
    """Share of rooms whose parameter combination repeats one already in the set.

    Worth logging: a model that returns 50 candidates collapsing onto 6 distinct
    combinations is pattern-matching, and the run file should show that.
    """
    if not rooms:
        return 0.0
    combos = {_combo(room) for room in rooms}
    return 1.0 - len(combos) / len(rooms)


def run_metadata(model: str, extra: dict | None = None) -> dict:
    meta = {
        "generated_at": datetime.now(timezone.utc).isoformat(timespec="seconds"),
        "model": model,
        "pipeline_version": 1,
    }
    if extra:
        meta.update(extra)
    return meta


def save_batch(path: str, meta: dict, rooms: list[dict], rejected: list[dict] | None = None) -> None:
    payload: dict[str, Any] = {"meta": meta, "rooms": rooms}
    if rejected:
        payload["rejected"] = rejected
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, indent=2, ensure_ascii=False)
        handle.write("\n")


def load_batch(path: str) -> dict:
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)
