"""The random-selection control arm (design-spec.md section 5, [PROPOSED]).

Without it, "the LLM steers emotion" is not falsifiable: you cannot separate LLM
competence from "any blue dim room feels calm". Rooms here are uniform draws from
the same pools the LLM selects from, so the two arms differ only in who chose.

Draws are seeded and reproducible. They are deliberately NOT de-duplicated by
default: filtering collisions out would stop the arm being a uniform sample, which
is the entire point of the comparison.
"""

from __future__ import annotations

import random

from .pools import (
    BRIGHTNESSES,
    HUES,
    SATURATIONS,
    TEXTURES,
    UNASSIGNED_LABEL,
)
from .schema import room_id
from .validate import format_violations, validate_room_config


def random_rooms(
    count: int,
    *,
    seed: int,
    prefix: str = "random",
    unique: bool = False,
) -> list[dict]:
    """`count` rooms with parameters drawn uniformly from the pools.

    `unique=True` rejects repeat combinations. Use it only if a duplicate room
    inside one participant's session would be a problem for the protocol -- it
    makes the draw non-uniform, so say so in the paper if you turn it on.
    """
    rng = random.Random(seed)
    rooms: list[dict] = []
    combos: set[tuple] = set()
    attempts = 0

    while len(rooms) < count:
        attempts += 1
        if attempts > 1000 * max(count, 1):  # pragma: no cover - pool exhausted
            raise RuntimeError("could not draw enough unique rooms from the pools")

        combo = (
            rng.choice(HUES),
            rng.choice(SATURATIONS),
            rng.choice(BRIGHTNESSES),
            rng.choice(TEXTURES),
        )
        if unique and combo in combos:
            continue
        combos.add(combo)

        hue, saturation, brightness, texture = combo
        room = {
            "id": room_id(prefix, len(rooms) + 1),
            "target_emotion": UNASSIGNED_LABEL,
            "source": "random",
            "hue": hue,
            "saturation": saturation,
            "brightness": brightness,
            "texture": texture,
            "rationale": (
                f"Control room: parameters drawn uniformly at random from the pools "
                f"(seed {seed}). No emotion was targeted."
            ),
        }
        violations = validate_room_config(room)
        if violations:  # pragma: no cover - would mean the pools are inconsistent
            raise RuntimeError(
                f"random draw failed validation:\n{format_violations(violations)}"
            )
        rooms.append(room)

    return rooms
