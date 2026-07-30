"""Frozen parameter pools -- the single source of truth for the whole pipeline.

design-spec.md section 3. Every other component derives from this module:

  * the LLM prompt text and its JSON schema   -> pipeline/schema.py
  * the validator that gates Unity            -> pipeline/validate.py
  * the random-selection control arm          -> pipeline/controls.py
  * the C# pool constants used by the loader  -> unity/PoolConstants.cs

The C# file is GENERATED from this module, so the engine and the pipeline cannot
drift apart:

    python3 -m pipeline.cli emit-unity-pools

Never hardcode a pool value anywhere else. If a pool changes, change it here,
re-run the generator, and re-validate every run you have already collected.

PROVISIONAL VALUES -- NOT STUDY READY
=====================================
The *structure* below is settled. The specific numbers and category labels are
not. research/scene-brief-for-akbar-260720.md section 4 is explicit:

    "Exactly which values are permitted for these three is still being finalised
     through the literature review and not yet locked in; please do not build
     against specific numbers or category labels for these three variables."

So treat HUES / SATURATIONS / BRIGHTNESSES / TEXTURES as placeholders that keep
the pipeline runnable and testable, not as the study's pools. Two open points
have to land before this module is final -- see README.md:

  1. Three variables or four? The brief section 4 names three ("hue category,
     material, and brightness"), which would drop SATURATIONS. But section 7
     step 2 says "hue/saturation/roughness" and section 6 item 3 says "hue and
     roughness as two separate dimensions". The brief contradicts itself.
  2. Is "material" the same axis as TEXTURES here, and is it a roughness tier
     (the brief's word in sections 6 and 7) or a named material? Roughness is a
     scalar; texture is a categorical map. They are not interchangeable.

Until both are answered, changing these tuples is a data edit and nothing else
in the pipeline needs to move -- which is the point of keeping them here.
"""

from __future__ import annotations

import json
import os
from itertools import product
from pathlib import Path
from typing import Iterator

# --------------------------------------------------------------------------
# Pool values are DATA (configs/pools.json), so finalising the
# literature-derived numbers is a data edit that touches no code.
# --------------------------------------------------------------------------

#: Overridable so an alternative pool set can be tried without editing the
#: checked-in file -- e.g. testing Mengkai's candidate values before adopting them:
#:     EMOTION_ROOMS_POOLS=configs/pools_candidate.json python3 -m pipeline.cli pools
POOL_FILE: Path = Path(
    os.environ.get(
        "EMOTION_ROOMS_POOLS",
        Path(__file__).resolve().parent.parent / "configs" / "pools.json",
    )
)


def _load(path: Path = POOL_FILE) -> dict:
    """Read and check the pool file.

    Every failure here is fatal and loud. This module is what the validator, the
    schema and the engine constants all derive from, so a malformed pool file must
    never degrade quietly into a pool that accepts more than it should.
    """
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        raise RuntimeError(f"pool file missing: {path}") from None
    except json.JSONDecodeError as exc:
        raise RuntimeError(f"pool file is not valid JSON: {path}: {exc}") from None

    manipulated = raw.get("manipulated")
    if not isinstance(manipulated, dict):
        raise RuntimeError(f"{path}: 'manipulated' must be an object")

    missing = [k for k in ("hue", "saturation", "brightness", "texture") if k not in manipulated]
    if missing:
        raise RuntimeError(
            f"{path}: 'manipulated' is missing {missing}. Dropping a variable is an "
            f"[OPEN] design decision (see this module's docstring), not a data edit -- "
            f"the schema, validator, prompt and C# constants all name these four."
        )

    for name, pool in manipulated.items():
        if not isinstance(pool, list) or not pool:
            raise RuntimeError(f"{path}: pool '{name}' must be a non-empty list")
        if len(set(pool)) != len(pool):
            raise RuntimeError(f"{path}: pool '{name}' has duplicate values: {pool}")

    researcher = raw.get("researcher_set")
    if not isinstance(researcher, dict) or "shapes" not in researcher or "wall_value" not in researcher:
        raise RuntimeError(f"{path}: 'researcher_set' needs 'shapes' and 'wall_value'")

    emotions = raw.get("emotions")
    if not isinstance(emotions, list) or len(emotions) != 4:
        raise RuntimeError(
            f"{path}: 'emotions' must be a list of 4 -- the protocol arithmetic "
            f"assumes four quadrants of the circumplex"
        )

    return raw


_POOLS = _load()

#: True while the values are placeholders pending Mengkai's literature review.
#: Scene brief section 4 asks that nothing be built against specific values yet.
PROVISIONAL: bool = bool(_POOLS.get("provisional", True))

# HSV hue of the wall colour, in degrees.
HUES: tuple[int, ...] = tuple(_POOLS["manipulated"]["hue"])

# HSV saturation of the wall colour.
SATURATIONS: tuple[float, ...] = tuple(_POOLS["manipulated"]["saturation"])

# Normalised intensity of the room's light source -- NOT a surface property.
BRIGHTNESSES: tuple[float, ...] = tuple(_POOLS["manipulated"]["brightness"])

# Wall material. The maps themselves are greyscale so the hue tints them.
TEXTURES: tuple[str, ...] = tuple(_POOLS["manipulated"]["texture"])

# The fields the LLM is allowed to choose, in canonical order.
LLM_CONTROLLED: tuple[str, ...] = tuple(_POOLS["manipulated"].keys())

# --------------------------------------------------------------------------
# Researcher-set factors -- the LLM never emits these
# --------------------------------------------------------------------------

# [CONFIRMED -- scene brief section 8, 30 Jul 2026]
# Room shape is researcher-fixed, never an LLM output, and does not enter the
# variable pool: the pool is applied independently within each shape condition.
# Shape is BETWEEN-subjects: one condition per participant, so a participant sees
# four rooms rather than eight. pipeline/session.py documents how to call that.
SHAPES: tuple[str, ...] = tuple(_POOLS["researcher_set"]["shapes"])

# [CONFIRMED -- scene brief sections 4 and 8, 30 Jul 2026]
# Lighting is neutral/white and hue is applied to wall/floor material only, never
# to the light. Wall HSV *value* is therefore a fixed constant, not a variable:
# how light or dark the room looks is the light's job. 0.85 rather than 1.0 keeps
# the albedo inside a physically sane range so bright rooms do not blow out.
WALL_VALUE: float = float(_POOLS["researcher_set"]["wall_value"])

# --------------------------------------------------------------------------
# Labels (not appearance parameters -- they record which arm a room belongs to)
# --------------------------------------------------------------------------

# [CONFIRMED -- scene brief section 1, 30 Jul 2026] The four diagonal quadrants of
# Russell's circumplex. Named by Mengkai: "depressed", not "sad".
EMOTIONS: tuple[str, ...] = tuple(_POOLS["emotions"])

# [DROPPED -- scene brief section 8, 30 Jul 2026] "a neutral/baseline condition is
# not being used". The arm is kept because it costs nothing to keep and resurrecting
# it is free; build-session only emits neutral rooms when asked with --neutral.
NEUTRAL_LABEL = "neutral"

# [UNDECIDED -- scene brief section 8, 30 Jul 2026] "Whether an additional
# random-parameter condition will be added is still being decided." Kept built for
# the same reason. Do not delete on the strength of the covering email alone, which
# says dropped while the brief of the same date says undecided.
UNASSIGNED_LABEL = "unassigned"

TARGET_LABELS: tuple[str, ...] = EMOTIONS + (NEUTRAL_LABEL, UNASSIGNED_LABEL)

# How a room's parameters were chosen. This is the experimental arm.
SOURCES: tuple[str, ...] = ("llm", "random", "handwritten")

# --------------------------------------------------------------------------
# Numeric tolerance
# --------------------------------------------------------------------------

# Pool gaps are >= 0.2, so any tolerance well below that cannot accept a
# neighbouring pool value. Python parses JSON floats as float64 and needs
# essentially nothing; Unity narrows to float32, hence the looser engine value.
FLOAT_TOL: float = 1e-9
UNITY_FLOAT_TOL: float = 1e-4


def in_pool(value: float, pool: tuple[float, ...], tol: float = FLOAT_TOL) -> bool:
    """True if `value` matches a pool member within `tol`."""
    return any(abs(value - member) <= tol for member in pool)


def canonical(value: float, pool: tuple[float, ...], tol: float = FLOAT_TOL) -> float:
    """Snap `value` onto its pool member, so 0.20 and 0.2 serialise identically."""
    for member in pool:
        if abs(value - member) <= tol:
            return member
    raise ValueError(f"{value!r} is not in pool {pool!r}")


def design_space_size(include_shape: bool = False) -> int:
    """Total distinct rooms the pools can express (spec section 3: 720, or 1440)."""
    size = len(HUES) * len(SATURATIONS) * len(BRIGHTNESSES) * len(TEXTURES)
    return size * len(SHAPES) if include_shape else size


def enumerate_rooms(include_shape: bool = False) -> Iterator[dict]:
    """Yield every point in the design space. Finite and enumerable, by design."""
    shapes = SHAPES if include_shape else (None,)
    for hue, sat, bright, tex, shape in product(
        HUES, SATURATIONS, BRIGHTNESSES, TEXTURES, shapes
    ):
        room = {
            "hue": hue,
            "saturation": sat,
            "brightness": bright,
            "texture": tex,
        }
        if shape is not None:
            room["shape"] = shape
        yield room
