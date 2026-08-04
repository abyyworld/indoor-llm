"""Can the eight cells actually be told apart?

This is the single largest scientific risk in the study, and it is checkable in seconds
the moment Mengkai's final config arrives rather than after data collection.

The problem, in her own formative data: calm, tense and depressed all came out "cool",
and tense and depressed were both cool/rough/dim. If two target emotions land on nearly
the same parameters, then:

  * Phase A cannot separate them. The primary analysis is distance to each emotion's
    coordinate, and two identical rooms cannot produce different affect.
  * Phase B cannot swap between them. `swappable_fields` returns nothing, so those
    trials fail to build.

Both failures are late and expensive. This module makes them a one-command check.

It deliberately does NOT judge whether a room is a good "calm" room. That is the
empirical question and nobody knows the answer. It only asks whether the eight
configurations are distinguishable from each other, which is a precondition for the
study being able to answer anything at all.
"""

from __future__ import annotations

from typing import Iterable, Sequence

from .aggregate import CATEGORICAL, CONTINUOUS, _ranges, distance

#: Below this Gower distance two cells are treated as effectively the same room.
#: 0.25 means they agree on three of four variables. Not a statistical threshold, a
#: practical one: if the only thing separating two target emotions is a single variable,
#: the manipulation is resting entirely on that variable working.
TOO_CLOSE = 0.25


def _key(config: dict) -> tuple:
    return (config.get("target_emotion"), config.get("shape"))


def differing_fields(a: dict, b: dict) -> list[str]:
    """Which manipulated variables actually differ between two configs."""
    fields = []
    for name in list(CATEGORICAL) + list(CONTINUOUS):
        if name in a and name in b and a[name] != b[name]:
            fields.append(name)
    return fields


def pairwise(configs: Sequence[dict]) -> list[dict]:
    """Distance between every pair of cells, closest first."""
    spans = _ranges(configs, CONTINUOUS)
    out = []
    for i, a in enumerate(configs):
        for b in configs[i + 1:]:
            differing = differing_fields(a, b)
            out.append(
                {
                    "a": _key(a),
                    "b": _key(b),
                    "distance": round(distance(a, b, spans), 4),
                    "differing_fields": differing,
                    "identical": not differing,
                    # Same emotion in two shapes is EXPECTED to be similar, so it is
                    # flagged differently from two different emotions colliding.
                    "same_emotion": a.get("target_emotion") == b.get("target_emotion"),
                }
            )
    return sorted(out, key=lambda p: p["distance"])


def check(configs: Sequence[dict], too_close: float = TOO_CLOSE) -> dict:
    """Full separability report over a set of cells."""
    configs = list(configs)
    if len(configs) < 2:
        raise ValueError("need at least two configs to compare")

    pairs = pairwise(configs)
    cross = [p for p in pairs if not p["same_emotion"]]

    collisions = [p for p in cross if p["identical"]]
    close = [p for p in cross if not p["identical"] and p["distance"] < too_close]

    # Which variable does the work? If one variable carries every separation, the whole
    # manipulation rests on it, and if it turns out not to drive affect the study has
    # nothing left.
    carrying: dict[str, int] = {}
    for pair in cross:
        for field in pair["differing_fields"]:
            carrying[field] = carrying.get(field, 0) + 1

    # A variable that never differs between any two emotions is doing nothing at all.
    present = set()
    for config in configs:
        present.update(f for f in list(CATEGORICAL) + list(CONTINUOUS) if f in config)
    inert = sorted(f for f in present if carrying.get(f, 0) == 0)

    return {
        "n_cells": len(configs),
        "n_cross_emotion_pairs": len(cross),
        "min_distance": cross[0]["distance"] if cross else None,
        "closest_pair": (cross[0]["a"], cross[0]["b"]) if cross else None,
        "identical_pairs": collisions,
        "close_pairs": close,
        "carrying_variables": dict(sorted(carrying.items(), key=lambda kv: -kv[1])),
        "inert_variables": inert,
        "safe": not collisions and not close and not inert,
    }


def format_report(report: dict, too_close: float = TOO_CLOSE) -> str:
    """Human-readable summary, written to be pasted into an email unedited."""
    lines = [
        f"Separability across {report['n_cells']} cells, "
        f"{report['n_cross_emotion_pairs']} cross-emotion pairs"
    ]

    if report["min_distance"] is not None:
        a, b = report["closest_pair"]
        lines.append(
            f"  closest cross-emotion pair: {a[0]}/{a[1]} vs {b[0]}/{b[1]} "
            f"at {report['min_distance']}"
        )

    for pair in report["identical_pairs"]:
        lines.append(
            f"  IDENTICAL: {pair['a'][0]}/{pair['a'][1]} and {pair['b'][0]}/{pair['b'][1]} "
            f"differ on nothing. Phase A cannot separate them and Phase B cannot swap "
            f"between them."
        )

    for pair in report["close_pairs"]:
        lines.append(
            f"  CLOSE ({pair['distance']}): {pair['a'][0]}/{pair['a'][1]} vs "
            f"{pair['b'][0]}/{pair['b'][1]}, separated only by "
            f"{', '.join(pair['differing_fields'])}"
        )

    if report["carrying_variables"]:
        lines.append("  separation carried by: " + ", ".join(
            f"{k} ({v} pairs)" for k, v in report["carrying_variables"].items()))

    for field in report["inert_variables"]:
        lines.append(
            f"  INERT: {field} takes the same value in every emotion, so it "
            f"distinguishes nothing and is manipulated in name only."
        )

    lines.append("  OK, all cells distinguishable" if report["safe"]
                 else f"  NOT SAFE: pairs below {too_close} need resolving before collection")
    return "\n".join(lines)
