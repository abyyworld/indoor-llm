"""The handover file: Mengkai's finalised values for the 8 emotion x shape cells.

She runs the sampling and aggregation herself and hands over one file. This module is
the gate on that file, so an out-of-range value is caught on arrival rather than in the
headset (CLAUDE.md invariant 3).

    python3 -m pipeline.cli validate-handoff configs/handoff_from_mengkai.json

Why the file declares its own variables
---------------------------------------
The variable set has changed four times in three weeks: four variables, then three,
then four with saturation as a discrete 1-5 scale, and now saturation as a percentage
drawn from two bands, with `material` possibly splitting into roughness plus a material
type. Hardcoding any of that here guarantees this module is wrong again next week.

So the file carries a `variables` block describing its own contract, and cells are
checked against that. Three shapes are supported, which between them cover everything
that has been floated so far:

    "hue_category":  {"type": "enum", "values": ["warm", "cool", "neutral"]}
    "saturation_pct":{"type": "bands", "bands": [[10, 20], [30, 40]], "unit": "%"}
    "brightness_lux":{"type": "per_emotion_bands", "unit": "lx",
                      "bands": {"calm": [45, 150], "tense": [670, 780],
                                "excited": null, "depressed": null}}

`enum` means the value must be one of the listed options. `bands` means it must fall
inside one of the listed ranges, which is how "a percentage from one of two bands"
is expressed. `per_emotion_bands` means the range depends on the cell's emotion, and a
null band declares that emotion exploratory: any positive value passes and is reported
as "no locked range", never as a failure (23 Jul meeting note).

Adding a variable, such as a separate `material_type`, is therefore a data edit on her
side. Marking one `"optional": true` lets it be absent from cells while it is still
being decided.

What is checked against this repo is only what is settled: the four emotion names and
the two shape names.
"""

from __future__ import annotations

from typing import Any

from .pools import EMOTIONS, SHAPES

FORMAT = "emotion-rooms-handoff/v2"

VALID_TYPES = ("enum", "bands", "per_emotion_bands")


def _err(errors: list[str], where: str, message: str) -> None:
    errors.append(f"{where}: {message}")


def _is_number(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def _valid_range(band: Any) -> bool:
    return (
        isinstance(band, list)
        and len(band) == 2
        and all(_is_number(v) for v in band)
        and band[0] < band[1]
    )


def _check_variables(doc: dict, errors: list[str]) -> dict:
    """Validate the contract the file declares about itself."""
    variables = doc.get("variables")
    if not isinstance(variables, dict) or not variables:
        _err(errors, "variables", "missing -- the file must declare its own contract")
        return {}

    checked: dict[str, dict] = {}
    for name, spec in variables.items():
        if name.startswith("_"):
            continue
        where = f"variables.{name}"
        if not isinstance(spec, dict):
            _err(errors, where, "must be an object with a 'type'")
            continue

        kind = spec.get("type")
        if kind not in VALID_TYPES:
            _err(errors, where, f"type={kind!r} must be one of {list(VALID_TYPES)}")
            continue

        if kind == "enum":
            values = spec.get("values")
            if not isinstance(values, list) or not values:
                _err(errors, where, "'values' must be a non-empty list")
            elif len(set(map(str, values))) != len(values):
                _err(errors, where, f"'values' has duplicates: {values}")

        elif kind == "bands":
            bands = spec.get("bands")
            if not isinstance(bands, list) or not bands:
                _err(errors, where, "'bands' must be a non-empty list of [low, high]")
            else:
                for index, band in enumerate(bands):
                    if not _valid_range(band):
                        _err(errors, f"{where}.bands[{index}]", f"must be [low, high] with low < high, got {band!r}")

        elif kind == "per_emotion_bands":
            bands = spec.get("bands")
            if not isinstance(bands, dict):
                _err(errors, where, "'bands' must be an object keyed by emotion")
            else:
                for emotion in EMOTIONS:
                    if emotion not in bands:
                        _err(
                            errors,
                            f"{where}.bands.{emotion}",
                            "missing -- use null to declare it exploratory with no locked range",
                        )
                    elif bands[emotion] is not None and not _valid_range(bands[emotion]):
                        _err(errors, f"{where}.bands.{emotion}", f"must be [low, high] or null, got {bands[emotion]!r}")

        checked[name] = spec

    return checked


def _check_value(name: str, spec: dict, cell: dict, emotion: Any, where: str, errors: list[str]) -> None:
    if name not in cell or cell[name] is None:
        if not spec.get("optional"):
            _err(errors, where, f"missing {name}")
        return

    value = cell[name]
    kind = spec.get("type")
    unit = spec.get("unit", "")

    if kind == "enum":
        allowed = spec.get("values") or []
        if value not in allowed:
            _err(errors, where, f"{name}={value!r} not in declared pool {allowed}")

    elif kind == "bands":
        if not _is_number(value):
            _err(errors, where, f"{name}={value!r} must be a number{' in ' + unit if unit else ''}")
            return
        bands = spec.get("bands") or []
        if not any(_valid_range(b) and b[0] <= value <= b[1] for b in bands):
            _err(errors, where, f"{name}={value}{unit} outside every declared band {bands}")

    elif kind == "per_emotion_bands":
        if not _is_number(value):
            _err(errors, where, f"{name}={value!r} must be a number{' in ' + unit if unit else ''}")
            return
        if value <= 0:
            _err(errors, where, f"{name}={value}{unit} must be positive")
            return
        band = (spec.get("bands") or {}).get(emotion)
        if band is None:
            return  # exploratory by design, never an error
        if _valid_range(band) and not band[0] <= value <= band[1]:
            _err(errors, where, f"{name}={value}{unit} outside declared band for {emotion}: {band}")


def _check_cell(cell: Any, index: int, variables: dict, errors: list[str]) -> tuple | None:
    where = f"cells[{index}]"
    if not isinstance(cell, dict):
        _err(errors, where, "must be an object")
        return None

    emotion = cell.get("target_emotion")
    shape = cell.get("shape")
    if emotion not in EMOTIONS:
        _err(errors, where, f"target_emotion={emotion!r} not one of {list(EMOTIONS)}")
    if shape not in SHAPES:
        _err(errors, where, f"shape={shape!r} not one of {list(SHAPES)}")

    where = f"cells[{index}] ({emotion}/{shape})"
    for name, spec in variables.items():
        _check_value(name, spec, cell, emotion, where, errors)

    if "hue_detail" in cell and cell["hue_detail"] is not None and not isinstance(cell["hue_detail"], str):
        _err(errors, where, "hue_detail must be free text")

    return (emotion, shape) if emotion in EMOTIONS and shape in SHAPES else None


def validate_handoff(doc: Any) -> list[str]:
    """Return a list of problems. Empty means the file is safe to build against."""
    errors: list[str] = []

    if not isinstance(doc, dict):
        return ["top level: must be a JSON object"]

    if doc.get("format") != FORMAT:
        _err(errors, "format", f"expected {FORMAT!r}, got {doc.get('format')!r}")

    variables = _check_variables(doc, errors)

    cells = doc.get("cells")
    if not isinstance(cells, list):
        return errors + ["cells: must be a list"]

    seen: dict[tuple, int] = {}
    for index, cell in enumerate(cells):
        key = _check_cell(cell, index, variables, errors)
        if key is None:
            continue
        if key in seen:
            _err(errors, f"cells[{index}]", f"duplicate cell {key}, first seen at index {seen[key]}")
        else:
            seen[key] = index

    expected = {(e, s) for e in EMOTIONS for s in SHAPES}
    for missing in sorted(expected - set(seen)):
        _err(errors, "cells", f"missing cell for {missing[0]}/{missing[1]}")

    return errors


def exploratory_cells(doc: dict) -> list[tuple]:
    """Cells holding a value whose emotion has no locked band.

    Reported as "no locked range", not as failures -- 23 Jul meeting note.
    """
    variables = doc.get("variables") or {}
    out = []
    for cell in doc.get("cells") or []:
        if not isinstance(cell, dict):
            continue
        for name, spec in variables.items():
            if not isinstance(spec, dict) or spec.get("type") != "per_emotion_bands":
                continue
            if (spec.get("bands") or {}).get(cell.get("target_emotion"), "missing") is None:
                out.append((cell.get("target_emotion"), cell.get("shape"), name, cell.get(name)))
    return out
