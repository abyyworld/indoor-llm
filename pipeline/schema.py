"""The frozen JSON contract (design-spec.md section 4).

Two schemas live here and they are deliberately different:

`candidate_schema()`
    What the LLM is allowed to emit. Only the four LLM-controlled fields plus a
    rationale. Every field is `enum`-constrained, so with structured outputs the
    model is mechanically unable to produce `"hue": 217`. The validator still
    runs afterwards -- the spec requires it, and a schema is not a substitute for
    checking what actually arrived.

`ROOM_CONFIG_SCHEMA`
    What Unity consumes. Adds `id`, `target_emotion`, `source` and the optional
    researcher-set `shape`. Ids are assigned by the pipeline, never by the LLM:
    uniqueness is our invariant to keep, not the model's.
"""

from __future__ import annotations

from .pools import (
    BRIGHTNESSES,
    HUES,
    SATURATIONS,
    SHAPES,
    SOURCES,
    TARGET_LABELS,
    TEXTURES,
)

#: Fields Unity requires in every config.
REQUIRED_KEYS: tuple[str, ...] = (
    "id",
    "target_emotion",
    "source",
    "hue",
    "saturation",
    "brightness",
    "texture",
    "rationale",
)

#: Fields Unity tolerates but does not require.
# roughness is optional rather than required while Mengkai's levels are pending, so a
# config written before the material split still validates and hers validates whether or
# not it carries the field.
OPTIONAL_KEYS: tuple[str, ...] = ("shape", "roughness")

ROOM_CONFIG_KEYS: tuple[str, ...] = REQUIRED_KEYS + OPTIONAL_KEYS

#: Ids are `<label>_<NNN>`; kept filesystem- and log-safe.
ID_PATTERN = r"^[a-z][a-z0-9_]*$"


def _appearance_properties() -> dict:
    return {
        "hue": {
            "type": "integer",
            "enum": list(HUES),
            "description": "HSV hue of the wall colour, in degrees.",
        },
        "saturation": {
            "type": "number",
            "enum": list(SATURATIONS),
            "description": "HSV saturation of the wall colour.",
        },
        "brightness": {
            "type": "number",
            "enum": list(BRIGHTNESSES),
            "description": "Normalised intensity of the room's light source.",
        },
        "texture": {
            "type": "string",
            "enum": list(TEXTURES),
            "description": "Greyscale wall material.",
        },
    }


def _roughness_property() -> dict:
    """Roughness, kept separate because it is optional until Mengkai confirms levels."""
    from .pools import ROUGHNESSES

    if not ROUGHNESSES:
        return {}
    return {
        "roughness": {
            "type": "string",
            "enum": list(ROUGHNESSES),
            "description": "Surface roughness, independent of material type.",
        }
    }


def candidate_schema(include_sketch: bool = False) -> dict:
    """Schema for one LLM-authored candidate."""
    props = _appearance_properties()
    props.update(_roughness_property())
    props["rationale"] = {
        "type": "string",
        "description": (
            "One or two sentences on why this combination should read as the "
            "target emotion to a person standing in the room."
        ),
    }
    required = [*_appearance_properties(), "rationale"]

    if include_sketch:
        # design-spec.md section 7: an optional 2D sanity check on what the model
        # thinks it is describing. Stripped before the config reaches Unity.
        props["sketch"] = {
            "type": "string",
            "description": (
                "A small ASCII swatch or floor-plan sketch of what you picture, "
                "at most 6 lines. A sanity check, not scene data."
            ),
        }
        required.append("sketch")

    return {
        "type": "object",
        "properties": props,
        "required": required,
        "additionalProperties": False,
    }


def candidates_envelope_schema(count: int, include_sketch: bool = False) -> dict:
    """Schema for a batch of `count` candidates in one response."""
    return {
        "type": "object",
        "properties": {
            "candidates": {
                "type": "array",
                # minItems 1, not `count`. The API rejects any other value:
                # "For 'array' type, 'minItems' values other than 0 or 1 are not
                # supported." Pinning both to the requested count meant every live
                # generation call failed -- which is how this path ran for months
                # against tests and never once against the real endpoint. The
                # accept/reject/continuation loop in generate.py already tops the batch
                # up to `count`, so the schema does not need to.
                "minItems": 1,
                "maxItems": count,
                "items": candidate_schema(include_sketch),
            }
        },
        "required": ["candidates"],
        "additionalProperties": False,
    }


#: The Unity-facing contract. Also usable as a JSON Schema document for editors.
ROOM_CONFIG_SCHEMA: dict = {
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "title": "EmotionRoomConfig",
    "type": "object",
    "properties": {
        "id": {"type": "string", "pattern": ID_PATTERN},
        "target_emotion": {"type": "string", "enum": list(TARGET_LABELS)},
        "source": {"type": "string", "enum": list(SOURCES)},
        **_appearance_properties(),
        "rationale": {"type": "string", "minLength": 1},
        "shape": {
            "type": "string",
            "enum": list(SHAPES),
            "description": "Researcher-set experimental factor, never an LLM output.",
        },
    },
    "required": list(REQUIRED_KEYS),
    "additionalProperties": False,
}

#: A run file on disk: provenance plus rooms.
BATCH_SCHEMA: dict = {
    "$schema": "https://json-schema.org/draft/2020-12/schema",
    "title": "EmotionRoomBatch",
    "type": "object",
    "properties": {
        "meta": {"type": "object"},
        "rooms": {"type": "array", "items": ROOM_CONFIG_SCHEMA},
    },
    "required": ["rooms"],
}


def room_id(label: str, index: int) -> str:
    """Canonical room id, e.g. `calm_007`."""
    return f"{label}_{index:03d}"


def unity_config(room: dict) -> dict:
    """Strip everything that is not part of the engine contract.

    Run files and session files carry extras -- `trial_index`, `_sketch` -- that are
    ours to reason about, not Unity's. Fields come out in `ROOM_CONFIG_KEYS` order so
    the exported JSON is diffable.
    """
    return {key: room[key] for key in ROOM_CONFIG_KEYS if key in room}
