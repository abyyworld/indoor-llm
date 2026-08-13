"""Generate the system's stated reasoning for each room.

The explanation factor turns on these being persuasive. A rationale that reads as
a colour note -- "pale warm cream, faintly buttery" -- is not a justification, and
if participants do not find it convincing then a null on the explanation effect is
a measurement failure rather than a result. The manipulation check asks them
directly, but it can only report that the manipulation was hollow after the
participants have been spent.

So these are written by the same model that chose the parameters, asked to justify
its own choices in the way a design system would explain itself to a client: fluent,
specific to the values actually chosen, and appealing to the target emotion. That is
what makes the corrupted-trial version of the manipulation work -- on those trials
the reasoning still describes the original design, so it is a good explanation for a
room that is no longer there.

Generation is offline and one-off. The rationales ship inside the config, so a
session needs no key and no network.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

from pipeline.generate import DEFAULT_MODEL, GenerationError, _first_text

#: Long enough to reason, short enough to read inside the exposure the trial allows.
SENTENCES = 2
MAX_TOKENS = 4000

SYSTEM = """You are the design system that chose the appearance of a virtual room \
to convey a target emotion. You are explaining your own choice to the person \
standing in the room.

Write the explanation as you would defend a deliberate design decision: name the \
specific choices you made, and say what each contributes to the feeling you were \
asked for. Be concrete and confident. Do not hedge, do not mention that you are an \
AI, and do not describe the room as an image or a render -- the reader is inside it.

Exactly two sentences. No more."""


def rationale_schema(count: int) -> dict:
    """One rationale per room, in the order the rooms were given."""
    return {
        "type": "object",
        "properties": {
            "rationales": {
                "type": "array",
                "minItems": 1,
                "maxItems": count,
                "items": {
                    "type": "object",
                    "properties": {
                        "id": {"type": "string"},
                        "rationale": {"type": "string"},
                    },
                    "required": ["id", "rationale"],
                    "additionalProperties": False,
                },
            }
        },
        "required": ["rationales"],
        "additionalProperties": False,
    }


def describe(room: dict) -> str:
    """The room as the model chose it, in the vocabulary it chose it with."""
    parts = [
        f"target emotion: {room.get('target_emotion')}",
        f"hue: {room.get('hue')} degrees",
        f"saturation: {room.get('saturation')}",
        f"illuminance: {room.get('brightness')} lux",
        f"wall material: {room.get('texture')}",
    ]
    if room.get("roughness"):
        parts.append(f"surface roughness: {room['roughness']}")
    return "; ".join(str(p) for p in parts)


#: Plain-language readings of each pool value, for the offline composer. These are
#: the same words the correction buttons use, so a rationale never names a property
#: in vocabulary the participant is not offered back.
HUE_WORDS = {0: "red", 30: "orange", 60: "yellow", 90: "yellow-green", 120: "green",
             180: "blue-green", 240: "blue", 270: "blue-violet", 300: "purple",
             330: "pink"}
LIGHT_WORDS = {150: "dim", 300: "medium", 500: "bright", 750: "very bright"}
#: Material without any smoothness claim in it: roughness is a separate variable and
#: saying "a smooth painted finish left rough" is both nonsense and a giveaway.
MATERIAL_WORDS = {"plaster": "painted plaster", "concrete": "bare concrete",
                  "textile": "woven cloth"}
#: Saturation, in the two words the buttons use. "restrained" and "saturated" were
#: the previous pair and both are wrong for this audience: one is a register nobody
#: speaks in, the other is the technical term for the very thing being described.
SATURATION_WORDS = {False: "faint", True: "vivid"}


def compose(room: dict) -> str:
    """Write a room's rationale from its own parameters, with no API call.

    The manipulation needs reasoning that is fluent, specific and plausible. It does
    not need to be authored by a language model: what matters is that it names the
    choices actually made and ties them to the target emotion, and that it reads the
    same way across rooms so the explanation condition differs from its control by
    content rather than by style.

    Composing it is also better controlled than sampling it. Every rationale gets the
    same structure and roughly the same length, so a difference between conditions
    cannot be a difference in how well one sentence happened to be written. The
    write-up should say the reasoning is generated from the system's parameter
    choices by template, which is true and is not a weakness.
    """
    hue = HUE_WORDS.get(int(room.get("hue", 0)), "muted")
    sat = SATURATION_WORDS[float(room.get("saturation", 0.2)) > 0.3]
    lux = LIGHT_WORDS.get(int(float(room.get("brightness", 300))), "medium")
    material = MATERIAL_WORDS.get(room.get("texture"), "a plain surface")
    rough = room.get("roughness")
    surface = (rough + " " + material) if rough else material
    emotion = room.get("target_emotion", "the target feeling")

    return (
        f"I chose a {sat} {hue} for the walls and floor, lit at a {lux} level, on "
        f"{surface}. Together the colour, the light and the surface are what carry "
        f"{emotion} in a room this size."
    )


def generate(rooms: list[dict], model: str = DEFAULT_MODEL, client: Any = None) -> dict:
    """Return {room id: rationale}. Needs ANTHROPIC_API_KEY unless a client is given."""
    if not rooms:
        return {}

    if client is None:
        try:
            import anthropic
        except ImportError as exc:  # pragma: no cover - depends on the environment
            raise GenerationError(
                "the anthropic package is not installed; pip install anthropic"
            ) from exc
        client = anthropic.Anthropic()

    listing = "\n".join(
        f"{i + 1}. id={room.get('id')} -- {describe(room)}"
        for i, room in enumerate(rooms)
    )
    prompt = (
        "Explain each of these rooms, one explanation per room, in the same order.\n\n"
        f"{listing}\n\n"
        "Return the id with each explanation so they cannot be mismatched."
    )

    with client.messages.stream(
        model=model,
        max_tokens=MAX_TOKENS,
        thinking={"type": "adaptive"},
        system=SYSTEM,
        messages=[{"role": "user", "content": prompt}],
        output_config={
            "format": {"type": "json_schema", "schema": rationale_schema(len(rooms))}
        },
    ) as stream:
        message = stream.get_final_message()

    payload = json.loads(_first_text(message))
    written = payload.get("rationales")
    if not isinstance(written, list):
        raise GenerationError("response JSON had no `rationales` array")

    by_id = {}
    for item in written:
        room_id, text = item.get("id"), item.get("rationale")
        if room_id and text:
            by_id[str(room_id)] = text.strip()

    missing = [str(r.get("id")) for r in rooms if str(r.get("id")) not in by_id]
    if missing:
        raise GenerationError(
            f"no rationale came back for {missing}. The explanation factor needs one "
            f"per room, so this is a hard failure rather than a partial write."
        )
    return by_id


def apply_to_config(path: Path, rationales: dict, out: Path) -> int:
    """Write the config back with each room's rationale replaced. Returns the count."""
    doc = json.loads(path.read_text(encoding="utf-8"))
    rooms = doc.get("rooms") or doc.get("cells") or []

    written = 0
    for room in rooms:
        text = rationales.get(str(room.get("id")))
        if text:
            room["rationale"] = text
            written += 1

    out.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")
    return written
