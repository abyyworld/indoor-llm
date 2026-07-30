"""The gate between the LLM and the participant (design-spec.md section 4).

    "Validate every field against the pools before it reaches Unity. An LLM will
     eventually emit "hue": 217 or "texture": "velvet". Reject and re-ask; do not
     let malformed configs silently reach a participant."

Two entry points, because there are two different contracts:

  * `validate_candidate` -- what came back from the model (four fields + rationale).
    Failures here are recoverable: the generator re-asks with these messages.
  * `validate_room_config` -- what is about to be handed to Unity. Failures here
    are fatal; nothing ships.

The C# loader repeats these pool checks against generated constants. That is
deliberate duplication: a config hand-edited on the headset never gets a Python
process to protect it.
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from typing import Any, Iterable, Sequence

from .pools import (
    BRIGHTNESSES,
    FLOAT_TOL,
    HUES,
    SATURATIONS,
    SHAPES,
    SOURCES,
    TARGET_LABELS,
    TEXTURES,
    in_pool,
)
from .schema import ID_PATTERN, OPTIONAL_KEYS, REQUIRED_KEYS

_ID_RE = re.compile(ID_PATTERN)


@dataclass(frozen=True)
class Violation:
    """One rejected field, phrased so it can go straight back to the model."""

    field: str
    value: Any
    reason: str

    def __str__(self) -> str:
        return f"{self.field}={self.value!r}: {self.reason}"


def _pool_reason(pool: Sequence[Any]) -> str:
    return "not in pool " + ", ".join(str(v) for v in pool)


def _check_int_pool(
    obj: dict, field: str, pool: Sequence[int], out: list[Violation]
) -> None:
    value = obj.get(field)
    if isinstance(value, bool) or not isinstance(value, int):
        out.append(Violation(field, value, "must be an integer"))
        return
    if value not in pool:
        out.append(Violation(field, value, _pool_reason(pool)))


def _check_float_pool(
    obj: dict, field: str, pool: tuple[float, ...], out: list[Violation]
) -> None:
    value = obj.get(field)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        out.append(Violation(field, value, "must be a number"))
        return
    if not in_pool(float(value), pool, FLOAT_TOL):
        out.append(Violation(field, value, _pool_reason(pool)))


def _check_str_pool(
    obj: dict, field: str, pool: Sequence[str], out: list[Violation]
) -> None:
    value = obj.get(field)
    if not isinstance(value, str):
        out.append(Violation(field, value, "must be a string"))
        return
    if value not in pool:
        out.append(Violation(field, value, _pool_reason(pool)))


def _check_appearance(obj: dict, out: list[Violation]) -> None:
    _check_int_pool(obj, "hue", HUES, out)
    _check_float_pool(obj, "saturation", SATURATIONS, out)
    _check_float_pool(obj, "brightness", BRIGHTNESSES, out)
    _check_str_pool(obj, "texture", TEXTURES, out)


def _check_rationale(obj: dict, out: list[Violation]) -> None:
    rationale = obj.get("rationale")
    if not isinstance(rationale, str) or not rationale.strip():
        out.append(Violation("rationale", rationale, "must be a non-empty string"))


def validate_candidate(obj: Any, allow_sketch: bool = False) -> list[Violation]:
    """Validate a raw LLM candidate: the four pool fields plus a rationale."""
    if not isinstance(obj, dict):
        return [Violation("<root>", obj, "must be a JSON object")]

    violations: list[Violation] = []
    _check_appearance(obj, violations)
    _check_rationale(obj, violations)

    allowed = {"hue", "saturation", "brightness", "texture", "rationale"}
    if allow_sketch:
        allowed.add("sketch")
    for key in sorted(set(obj) - allowed):
        violations.append(
            Violation(key, obj[key], "field is not yours to set; omit it")
        )
    return violations


def validate_room_config(obj: Any) -> list[Violation]:
    """Validate a complete Unity-facing config."""
    if not isinstance(obj, dict):
        return [Violation("<root>", obj, "must be a JSON object")]

    violations: list[Violation] = []

    for key in REQUIRED_KEYS:
        if key not in obj:
            violations.append(Violation(key, None, "required field is missing"))

    for key in sorted(set(obj) - set(REQUIRED_KEYS) - set(OPTIONAL_KEYS)):
        violations.append(Violation(key, obj[key], "unknown field"))

    room_id = obj.get("id")
    if not isinstance(room_id, str) or not _ID_RE.match(room_id):
        violations.append(
            Violation("id", room_id, f"must be a string matching {ID_PATTERN}")
        )

    _check_str_pool(obj, "target_emotion", TARGET_LABELS, violations)
    _check_str_pool(obj, "source", SOURCES, violations)
    _check_appearance(obj, violations)
    _check_rationale(obj, violations)

    if "shape" in obj:
        _check_str_pool(obj, "shape", SHAPES, violations)

    return violations


def validate_batch(
    rooms: Iterable[Any], *, check_duplicate_ids: bool = True
) -> tuple[list[dict], list[tuple[Any, list[Violation]]]]:
    """Validate a list of configs. Returns (accepted, [(room, violations), ...]).

    In a room batch, a duplicate id is a violation: Unity looks rooms up by id, so two
    rooms sharing one is silent data corruption. In a *session* file it is expected --
    shape is crossed within room, so each room appears once per shape and `trial_id` is
    what has to be unique. Pass `check_duplicate_ids=False` for session files.
    """
    accepted: list[dict] = []
    rejected: list[tuple[Any, list[Violation]]] = []
    seen: set[str] = set()

    for room in rooms:
        violations = validate_room_config(room)

        # Track ids seen even on rooms that failed for other reasons, otherwise a
        # duplicate passes whenever the room it collides with was itself rejected.
        if check_duplicate_ids and isinstance(room, dict) and isinstance(room.get("id"), str):
            room_id = room["id"]
            if room_id in seen:
                violations.append(Violation("id", room_id, "duplicate id in batch"))
            else:
                seen.add(room_id)

        if violations:
            rejected.append((room, violations))
        else:
            accepted.append(room)  # type: ignore[arg-type]

    return accepted, rejected


def format_violations(violations: Sequence[Violation]) -> str:
    """Render violations as a bullet list -- for logs and for the re-ask prompt."""
    return "\n".join(f"  - {v}" for v in violations)
