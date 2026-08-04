"""Prompt construction (design-spec.md section 7).

The prompt text is built from `pools.py` rather than written out by hand, so the
constraints the model is told about can never disagree with the constraints the
validator enforces.
"""

from __future__ import annotations

from typing import Sequence

from .pools import (
    ROUGHNESSES,
    BRIGHTNESSES,
    EMOTIONS,
    HUES,
    NEUTRAL_LABEL,
    SATURATIONS,
    TEXTURES,
    WALL_VALUE,
)


def _roughness_line() -> str:
    """Roughness is only mentioned to the model once its levels exist."""
    if not ROUGHNESSES:
        return ""
    return f"\n  roughness   string,  surface roughness                 -- one of: {_values(ROUGHNESSES)}"


def _values(values: Sequence[object]) -> str:
    return ", ".join(str(v) for v in values)


def build_system_prompt() -> str:
    """The standing instructions: what the model controls and what it does not."""
    return f"""You are choosing appearance parameters for a virtual reality room that \
will be built in Unity and shown to a human participant wearing a VR headset. The \
participant will look around for about 30 seconds and then self-report how the room \
made them feel.

You are not designing a room. The researchers have already fixed the room dimensions, \
the room shape, the furniture layout, the object positions, and the participant's spawn \
point. You cannot change any of them, you cannot add or remove objects, and you must not \
describe them.

You choose exactly four parameters, and for each one you may only pick a value from the \
fixed pool below. Anything outside these pools is rejected by an automatic validator and \
the room is never built.

  hue         integer, wall colour, HSV hue in degrees   -- one of: {_values(HUES)}
  saturation  number,  wall colour, HSV saturation       -- one of: {_values(SATURATIONS)}
  brightness  number,  intensity of the room's light     -- one of: {_values(BRIGHTNESSES)}
  texture     string,  wall material                     -- one of: {_values(TEXTURES)}{_roughness_line()}

How these are applied in the engine, so you know what you are actually choosing:

- hue and saturation tint the WALLS only. Hue 0 is red, 120 is green, 240 is blue.
- wall HSV value is fixed at {WALL_VALUE} and is not yours to set. You do not control how \
light or dark a surface is.
- the wall textures are greyscale (black and white) maps, so the wall hue tints them. \
Choosing a texture chooses surface pattern and feel, never colour.
- brightness is the intensity of the room's single light source, which stays neutral \
white. It is not a surface property. Low brightness means a dim room with deep shadows; \
high brightness means an evenly, strongly lit room.

For each room also give a rationale of one or two sentences: why should that combination \
read as the target to a person standing inside the room? Argue from the room as \
experienced, not from colour-symbolism trivia.

Output only the requested JSON. No commentary."""


def build_emotion_prompt(emotion: str, count: int) -> str:
    """Ask for `count` candidate rooms targeting one emotion (spec section 5)."""
    return f"""Target emotion: {emotion}

Produce {count} distinct candidate rooms intended to make the participant feel \
{emotion}.

There is no single correct {emotion} room, and we are sampling the distribution of \
plausible answers rather than trusting one. Spread the set across the pools: vary hue, \
saturation, brightness and texture. Do not hand back {count} variations of a single idea, \
and do not converge on one hue family unless every rationale genuinely requires it. Some \
candidates may be non-obvious as long as the rationale holds up."""


def build_neutral_prompt(count: int) -> str:
    """Ask for the neutral control arm (spec section 5 -- this is the baseline)."""
    return f"""Target: no emotion at all. These are neutral control rooms.

Produce {count} distinct candidate rooms that are deliberately NOT designed to convey \
any emotion. A participant standing in one should be able to answer "this room makes me \
feel nothing in particular" and be right.

Avoid combinations you would reach for to push someone toward {', '.join(EMOTIONS[:-1])} \
or {EMOTIONS[-1]}. Note that "{NEUTRAL_LABEL}" is not the same as "average": a room can be \
unremarkable at several different points in the pools, and we want that spread, not {count} \
copies of the mid-point. Each rationale should say why the room reads as affectively flat."""


def build_reask_prompt(errors: str, remaining: int) -> str:
    """Corrective turn after a validation failure (spec section 4: reject and re-ask)."""
    return f"""Some candidates were rejected by the validator:

{errors}

Every value must come from the pools exactly as listed. Return {remaining} replacement \
candidates that satisfy the constraints. Do not repeat the rejected combinations."""


def build_continuation_prompt(seen: Sequence[tuple], remaining: int) -> str:
    """Ask for more candidates while telling the model what it already produced."""
    already = "\n".join(
        # Unpacks whatever _combo produces, so adding a variable does not break the
        # continuation prompt. The model is shown what it has already produced, and a
        # combo it cannot read is a combo it will happily repeat.
        "  " + " ".join(
            f"{name}={value}"
            for name, value in zip(
                ("hue", "saturation", "brightness", "texture", "roughness"), combo
            )
            if value is not None
        )
        for combo in seen
    )
    return f"""You have already produced these combinations:

{already}

Produce {remaining} more distinct candidates for the same target. None may duplicate a \
combination above, and keep spreading across the pools."""
