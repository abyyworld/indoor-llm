"""The Affect Grid: response coding, target coordinates, and the primary measure.

This closes the gap that has been open since the design was written: the primary
analysis is "distance to the target emotion's coordinate" and no coordinates existed.

INSTRUMENT
----------
A 9x9 Affect Grid (Russell, Weiss and Mendelsohn, 1989), with SAM-style pictorial
anchors replacing the original corner wording. One click gives both dimensions.

    x = valence  1 unpleasant  ->  9 pleasant
    y = arousal  1 sleepy      ->  9 high arousal

9x9 is the published resolution and worth keeping rather than inventing another, since
it makes the data comparable with anything else measured on the grid.

TARGET COORDINATES
------------------
These are not fabricated and they are not an empirical claim. The Affect Grid labels
its own four corners, and the study's four emotions are defined as the diagonal
quadrants of the circumplex, so the mapping is definitional:

    grid corner (published)     study emotion
    top-left     stress         tense
    top-right    excitement     excited
    bottom-left  depression     depressed
    bottom-right relaxation     calm

The only real choice is how far into each corner a target sits. Placing targets at the
absolute corners (1,9), (9,9), (1,1), (9,1) would make the maximum achievable distance
different for each emotion and reward extreme responding. Targets are therefore set one
step inside, at the centre of each quadrant, which keeps every target equidistant from
the neutral centre and makes the four conditions symmetric.

Mengkai owns this decision. `TARGETS` is one edit if she wants them elsewhere, and
nothing else in the module hardcodes a coordinate.
"""

from __future__ import annotations

from math import hypot
from typing import Iterable, Sequence

#: Grid extent. Both axes run 1..GRID_MAX inclusive.
GRID_MIN = 1
GRID_MAX = 9
GRID_CENTRE = (GRID_MAX + GRID_MIN) / 2.0  # 5.0, the neutral point

#: (valence, arousal) per target emotion. Quadrant centres, not corners -- see above.
TARGETS: dict[str, tuple[float, float]] = {
    "calm": (7.0, 3.0),        # pleasant, low arousal
    "excited": (7.0, 7.0),     # pleasant, high arousal
    "tense": (3.0, 7.0),       # unpleasant, high arousal
    "depressed": (3.0, 3.0),   # unpleasant, low arousal
}

#: Worst possible distance on this grid, used to normalise. Corner to opposite corner.
MAX_DISTANCE = hypot(GRID_MAX - GRID_MIN, GRID_MAX - GRID_MIN)


class AffectError(ValueError):
    pass


def validate_response(valence: float, arousal: float) -> None:
    """Reject anything off the grid. A silently clamped response is a fabricated one."""
    for name, value in (("valence", valence), ("arousal", arousal)):
        if not isinstance(value, (int, float)) or isinstance(value, bool):
            raise AffectError(f"{name}={value!r} must be a number")
        if not GRID_MIN <= value <= GRID_MAX:
            raise AffectError(f"{name}={value} is outside the {GRID_MIN}..{GRID_MAX} grid")


def target_for(emotion: str) -> tuple[float, float]:
    if emotion not in TARGETS:
        raise AffectError(f"no target coordinate for {emotion!r}; known: {sorted(TARGETS)}")
    return TARGETS[emotion]


def congruence(emotion: str, valence: float, arousal: float) -> dict:
    """The primary measure for one trial.

    Returns the raw distance, a normalised version, and the signed error on each axis.
    The axis errors are kept because "the room was pleasant enough but not calming"
    is a different failure from "it was calming but unpleasant", and a single distance
    hides which one happened.
    """
    validate_response(valence, arousal)
    tx, ty = target_for(emotion)

    distance = hypot(valence - tx, arousal - ty)
    return {
        "target_emotion": emotion,
        "target": (tx, ty),
        "response": (valence, arousal),
        "distance": round(distance, 4),
        # 0 is a perfect hit, 1 is the far corner. Comparable across emotions.
        "congruence": round(1.0 - (distance / MAX_DISTANCE), 4),
        "valence_error": round(valence - tx, 4),
        "arousal_error": round(arousal - ty, 4),
    }


def nearest_target(valence: float, arousal: float) -> str:
    """Which emotion the response lands closest to, regardless of what was intended.

    This is the confusion-matrix measure. If tense responses consistently land nearest
    the depressed target, that is the collapse showing up in participant data rather
    than in the parameters, and it is the cleanest way to report it.
    """
    validate_response(valence, arousal)
    return min(TARGETS, key=lambda e: hypot(valence - TARGETS[e][0], arousal - TARGETS[e][1]))


def confusion_matrix(trials: Iterable[dict]) -> dict[str, dict[str, int]]:
    """Counts of intended emotion against nearest target.

    `trials` need `target_emotion`, `valence` and `arousal`. Off-diagonal mass is
    exactly the tense/depressed question, measured.
    """
    matrix = {intended: {actual: 0 for actual in TARGETS} for intended in TARGETS}
    for trial in trials:
        intended = trial["target_emotion"]
        if intended not in matrix:
            raise AffectError(f"unknown target_emotion {intended!r}")
        matrix[intended][nearest_target(trial["valence"], trial["arousal"])] += 1
    return matrix


def summarise_congruence(trials: Sequence[dict]) -> dict:
    """Per-emotion congruence over a set of trials, plus the hit rate."""
    if not trials:
        raise AffectError("no trials to summarise")

    by_emotion: dict[str, list[dict]] = {}
    for trial in trials:
        scored = congruence(trial["target_emotion"], trial["valence"], trial["arousal"])
        by_emotion.setdefault(trial["target_emotion"], []).append(scored)

    out: dict[str, dict] = {}
    for emotion, scored in by_emotion.items():
        distances = [s["distance"] for s in scored]
        hits = sum(
            1
            for s in scored
            if nearest_target(*s["response"]) == emotion
        )
        out[emotion] = {
            "n": len(scored),
            "mean_distance": round(sum(distances) / len(distances), 4),
            "mean_congruence": round(
                sum(s["congruence"] for s in scored) / len(scored), 4
            ),
            # Proportion landing nearest their own target. Chance is 0.25 with four
            # equally spaced targets, so this is directly interpretable.
            "hit_rate": round(hits / len(scored), 4),
            "mean_valence_error": round(
                sum(s["valence_error"] for s in scored) / len(scored), 4
            ),
            "mean_arousal_error": round(
                sum(s["arousal_error"] for s in scored) / len(scored), 4
            ),
        }
    return out


# ---------------------------------------------------------------------------
# Manipulation check: did the scene's illuminance suit its target emotion?
# ---------------------------------------------------------------------------

def illuminance_bands() -> dict[str, tuple[float, float] | None]:
    """Per-emotion illuminance bands, read from configs/pools.json.

    These are ENGINEERING DEFAULTS, not literature. `check_illuminance` reports
    "no locked range" rather than a pass or fail for any emotion whose band is absent,
    so an emotion with no evidence behind it is never scored as having failed.
    """
    from .pools import _POOLS

    raw = _POOLS.get("emotion_illuminance_bands") or {}
    out: dict[str, tuple[float, float] | None] = {}
    for emotion in TARGETS:
        band = raw.get(emotion)
        out[emotion] = (float(band[0]), float(band[1])) if isinstance(band, list) else None
    return out


def check_illuminance(emotion: str, lux: float) -> dict:
    """One room's illuminance against its emotion's band."""
    if emotion not in TARGETS:
        raise AffectError(f"unknown emotion {emotion!r}")

    band = illuminance_bands().get(emotion)
    if band is None:
        # 23 Jul note: emotions without a range are "no locked range", never failures.
        return {"emotion": emotion, "lux": lux, "band": None,
                "status": "no_locked_range", "matches": None}

    low, high = band
    matches = low <= lux <= high
    return {"emotion": emotion, "lux": lux, "band": (low, high),
            "status": "match" if matches else "outside_band", "matches": matches}


def manipulation_check(rooms) -> dict:
    """Manipulation check across a set of rooms.

    Counts matches, misses and unscoreable cells separately. Lumping the last two
    together would report an emotion with no evidence behind it as a failure, which is
    the specific mistake the 23 Jul note warns against.
    """
    matched = missed = unscoreable = 0
    detail = []
    for room in rooms:
        result = check_illuminance(room["target_emotion"], room["brightness"])
        detail.append(result)
        if result["matches"] is None:
            unscoreable += 1
        elif result["matches"]:
            matched += 1
        else:
            missed += 1

    scoreable = matched + missed
    return {
        "n": len(detail),
        "matched": matched,
        "missed": missed,
        "no_locked_range": unscoreable,
        "match_rate": round(matched / scoreable, 4) if scoreable else None,
        "detail": detail,
    }


# ---------------------------------------------------------------------------
# The correction loop: did a participant's own correction improve their own
# affective response to the room they corrected?
# ---------------------------------------------------------------------------

def correction_effect(
    emotion: str,
    valence_before: float,
    arousal_before: float,
    valence_after: float,
    arousal_after: float,
) -> dict:
    """Change in congruence between a room and the participant's correction of it.

    This is what makes the correction question answerable, and it sidesteps the problem
    Mengkai raised on 2 Aug: that part 3 was not analysable because no reference point
    existed, and comparing corrections would need a distance metric across the pool.

    Both objections apply to asking "did the correction move toward the right value",
    because that needs a right value defined first. Neither applies here. The reference
    is the participant's OWN first rating of the same room, so the comparison is
    self-contained: did the room they produced feel closer to the target than the room
    they were given?

    It also changes what the participant is. Rating someone else's room makes you a
    judge; changing a room and then living with the result makes you the one who acted.
    """
    before = congruence(emotion, valence_before, arousal_before)
    after = congruence(emotion, valence_after, arousal_after)

    return {
        "target_emotion": emotion,
        "before": before["response"],
        "after": after["response"],
        "distance_before": before["distance"],
        "distance_after": after["distance"],
        # Positive means the correction moved the room closer to its target.
        "improvement": round(before["distance"] - after["distance"], 4),
        "improved": after["distance"] < before["distance"],
        "congruence_before": before["congruence"],
        "congruence_after": after["congruence"],
    }


def summarise_corrections(records) -> dict:
    """Aggregate the correction loop over a set of trials.

    `records` need `target_emotion`, `valence_before`, `arousal_before`,
    `valence_after`, `arousal_after`, and optionally `correction_applied`.

    Rows where the correction was never applied are counted separately rather than
    dropped. "The correction did not help" and "the correction never happened" look
    identical in the outcome column and mean opposite things.
    """
    scored, not_applied, incomplete = [], 0, 0

    for record in records:
        if record.get("correction_applied") is False:
            not_applied += 1
            continue
        missing = [
            k for k in ("valence_before", "arousal_before", "valence_after", "arousal_after")
            if record.get(k) in (None, -1)
        ]
        if missing:
            incomplete += 1
            continue
        scored.append(
            correction_effect(
                record["target_emotion"],
                record["valence_before"], record["arousal_before"],
                record["valence_after"], record["arousal_after"],
            )
        )

    if not scored:
        return {"n": 0, "not_applied": not_applied, "incomplete": incomplete,
                "improved": 0, "improvement_rate": None, "mean_improvement": None}

    improved = sum(1 for s in scored if s["improved"])
    return {
        "n": len(scored),
        "not_applied": not_applied,
        "incomplete": incomplete,
        "improved": improved,
        # Chance is 0.5 if corrections were random with respect to congruence, so this
        # is directly interpretable without a baseline condition.
        "improvement_rate": round(improved / len(scored), 4),
        "mean_improvement": round(sum(s["improvement"] for s in scored) / len(scored), 4),
        "mean_distance_before": round(sum(s["distance_before"] for s in scored) / len(scored), 4),
        "mean_distance_after": round(sum(s["distance_after"] for s in scored) / len(scored), 4),
    }


# ---------------------------------------------------------------------------
# LLM-designed rooms against uniformly-drawn ones, from the review block's
# before-ratings. This is the comparison the main study does not make.
# ---------------------------------------------------------------------------

def compare_conditions(records, baseline: str = "random", target: str = "faithful") -> dict:
    """Congruence of one review condition against another, paired within participant.

    `records` need `participant`, `condition`, `target_emotion_shown`,
    `valence_before` and `arousal_before`.

    Why this exists. Mengkai declined a random control arm in the main study, for three
    reasons, and two of them are right: at N around 20-24 a between-condition comparison
    is underpowered, and four fixed random rooms would be too sparse a sample of the pool
    to stand for "arbitrary design".

    Neither objection applies to the review block, and that is not a loophole, it is a
    property of how the block is built:

      * The comparison is WITHIN participant. Everyone rates both LLM-designed and
        uniformly-drawn rooms in the same sitting, so it is paired rather than
        between-groups, which is where her power objection came from.
      * The random draw is per participant, not fixed. Across 24 participants at three
        random trials each, that is roughly 66 distinct rooms rather than 4, which is a
        real sample of the pool rather than four arbitrary points.

    So the falsifiability claim her design cannot make is available here at no extra
    cost: were LLM-designed rooms rated closer to their stated target than rooms drawn
    at random from the same pool?

    The before-rating is the right one to use. It is taken when the room is first shown,
    before any question about what might be off, so it is an affective response rather
    than an evaluation.
    """
    by_condition: dict[str, list[float]] = {}
    per_participant: dict[str, dict[str, list[float]]] = {}

    for record in records:
        condition = record.get("condition")
        if condition not in (baseline, target):
            continue
        valence = record.get("valence_before")
        arousal = record.get("arousal_before")
        if valence in (None, -1) or arousal in (None, -1):
            continue

        scored = congruence(record["target_emotion_shown"], valence, arousal)
        by_condition.setdefault(condition, []).append(scored["distance"])

        who = record.get("participant", "?")
        per_participant.setdefault(who, {}).setdefault(condition, []).append(scored["distance"])

    def mean(values):
        return round(sum(values) / len(values), 4) if values else None

    # Paired differences, one per participant who saw both conditions. This is the
    # analysis that actually uses the within-subject design.
    paired = []
    for who, conditions in per_participant.items():
        if baseline in conditions and target in conditions:
            paired.append(mean(conditions[target]) - mean(conditions[baseline]))

    return {
        "target_condition": target,
        "baseline_condition": baseline,
        f"n_{target}": len(by_condition.get(target, [])),
        f"n_{baseline}": len(by_condition.get(baseline, [])),
        f"mean_distance_{target}": mean(by_condition.get(target, [])),
        f"mean_distance_{baseline}": mean(by_condition.get(baseline, [])),
        "n_participants_paired": len(paired),
        # Negative means the target condition landed CLOSER to its stated emotion than
        # the baseline did, which is the direction the study predicts.
        "mean_paired_difference": mean(paired),
        "participants_favouring_target": sum(1 for d in paired if d < 0),
    }


# ---------------------------------------------------------------------------
# Do independent people correct the same way? The training-signal question.
# ---------------------------------------------------------------------------

def correction_convergence(records) -> dict:
    """Agreement between participants correcting the same swapped variable.

    Whether a correction helped the person who made it is one question, answered by
    `summarise_corrections`. Whether corrections are usable as training signal is a
    different one, and it turns on convergence: if ten people shown the same swapped
    room push the variable in ten different directions, the signal is unusable no matter
    how sincere each correction was.

    Grouped by (target emotion, swapped variable), because that is the unit a training
    signal would actually be aggregated over. Reports:

      * mode share, the proportion choosing the most common value. 1.0 is unanimous.
      * recovery rate, the proportion who chose the ORIGINAL value, which is the
        strongest possible agreement: independent people reconstructing what the agent
        had chosen before it was swapped.

    Recovery is the more interesting of the two. Mode share can be high because everyone
    made the same mistake; recovery can only be high if people are tracking something
    real about the target emotion.
    """
    groups: dict[tuple, list[dict]] = {}
    for record in records:
        field = record.get("attributed_field")
        swapped = record.get("swapped_field")
        value = record.get("corrected_value")
        if not field or value in (None, ""):
            continue
        # Only trials where a swap was actually made can be scored for recovery.
        if not swapped:
            continue
        groups.setdefault((record.get("target_emotion_shown"), swapped), []).append(record)

    out: dict[str, dict] = {}
    for (emotion, swapped_field), rows in sorted(groups.items(), key=lambda kv: str(kv[0])):
        values = [str(r["corrected_value"]) for r in rows]
        counts: dict[str, int] = {}
        for v in values:
            counts[v] = counts.get(v, 0) + 1
        top_value, top_count = max(counts.items(), key=lambda kv: (kv[1], kv[0]))

        # Did they land back on what the agent originally chose?
        originals = [str(r["original_value"]) for r in rows if r.get("original_value") not in (None, "")]
        recovered = sum(
            1 for r in rows
            if r.get("original_value") not in (None, "")
            and str(r["corrected_value"]) == str(r["original_value"])
        )

        # How many people correctly identified WHICH variable was swapped, since a
        # correction to the wrong variable is not evidence about this one.
        on_target = sum(1 for r in rows if r.get("attributed_field") == swapped_field)

        out[f"{emotion}/{swapped_field}"] = {
            "n": len(rows),
            "distinct_values": len(counts),
            "modal_value": top_value,
            "mode_share": round(top_count / len(rows), 4),
            "recovery_rate": round(recovered / len(originals), 4) if originals else None,
            "attributed_correctly": on_target,
            "distribution": dict(sorted(counts.items(), key=lambda kv: -kv[1])),
        }

    if not out:
        return {"groups": {}, "n_groups": 0, "mean_mode_share": None, "mean_recovery_rate": None}

    shares = [g["mode_share"] for g in out.values()]
    recoveries = [g["recovery_rate"] for g in out.values() if g["recovery_rate"] is not None]
    return {
        "groups": out,
        "n_groups": len(out),
        "mean_mode_share": round(sum(shares) / len(shares), 4),
        "mean_recovery_rate": round(sum(recoveries) / len(recoveries), 4) if recoveries else None,
    }
