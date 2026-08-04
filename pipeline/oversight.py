"""Phase B: building oversight trials with known ground truth.

study-design-v2.md section 3. Not approved yet -- this exists so the design can be
argued about concretely, and because the mechanic is small enough that building it is
cheaper than describing it.

The whole point is that the experiment knows what is wrong with each stimulus. A
participant saying "the lighting is wrong" is only scoreable if the lighting is, in fact,
what was broken. So every trial carries a `ground_truth` block recording exactly what was
done to it, and attribution accuracy falls out of comparing a response against that.

Four conditions, per the design doc:

    faithful              the agent's real output, nothing injected
    swapped             one variable replaced with the agent's choice for another emotion
    random                every variable drawn uniformly, unreasoned
    rationale_mismatched  real room, but shown with another emotion's rationale

The swapped condition deliberately borrows from a *different emotion's own config*
rather than picking a random replacement. A random replacement would often be obviously
wrong; borrowing produces an error the agent itself considered reasonable somewhere else,
which is the realistic case and the harder one.
"""

from __future__ import annotations

import random
from typing import Any, Sequence

FAITHFUL = "faithful"
SWAPPED = "swapped"
RANDOM = "random"
RATIONALE_MISMATCHED = "rationale_mismatched"

CONDITIONS: tuple[str, ...] = (FAITHFUL, SWAPPED, RANDOM, RATIONALE_MISMATCHED)

#: Variables a participant can be asked to attribute an error to. Kept here rather than
#: derived from pools.py because Phase B asks about the agent's *decisions*, and which
#: decisions exist is a design question, not a pool question.
ATTRIBUTABLE: tuple[str, ...] = (
    "hue",
    "saturation",
    "material",
    "roughness",
    "brightness",
)

#: The answer for a faithful trial. Offered to participants as an option, otherwise the
#: task forces them to invent a fault and detection cannot be measured.
NOTHING_WRONG = "nothing_wrong"


class OversightError(ValueError):
    pass


def _attributable_fields(config: dict) -> list[str]:
    return [f for f in ATTRIBUTABLE if f in config]


def swap(config: dict, donor: dict, field: str) -> dict:
    """Replace one field with the donor's value for that same field.

    The donor should be another emotion's config, so the injected value is one the agent
    genuinely chose somewhere, just in the wrong place.
    """
    if field not in config:
        raise OversightError(f"{field!r} is not in the config being swapped")
    if field not in donor:
        raise OversightError(f"{field!r} is not in the donor config")
    if config[field] == donor[field]:
        raise OversightError(
            f"donor has the same {field!r} ({config[field]!r}), so this would inject "
            f"nothing. Pick a donor that actually differs."
        )

    out = dict(config)
    out[field] = donor[field]
    return out


def swappable_fields(config: dict, donor: dict) -> list[str]:
    """Fields where the donor actually differs, so a swap would be visible.

    Worth checking rather than assuming: if two emotions converge on the same parameters,
    which is exactly the tense and depressed worry, there may be nothing to swap
    between them. That is itself worth logging.
    """
    return [
        f
        for f in _attributable_fields(config)
        if f in donor and config[f] != donor[f]
    ]


def make_trial(
    config: dict,
    condition: str,
    *,
    rng: random.Random,
    donors: Sequence[dict] = (),
    pool_sampler=None,
) -> dict:
    """Build one Phase B trial, with its ground truth attached."""
    if condition not in CONDITIONS:
        raise OversightError(f"unknown condition {condition!r}")

    target = config.get("target_emotion")
    trial: dict[str, Any] = {
        "condition": condition,
        "target_emotion_shown": target,
        "stimulus": dict(config),
        "rationale_shown": config.get("rationale"),
        "ground_truth": {
            "swapped_field": None,
            "original_value": None,
            "swapped_in_value": None,
            "donor_emotion": None,
            "rationale_is_wrong": False,
        },
    }

    if condition == FAITHFUL:
        return trial

    if condition == SWAPPED:
        usable = [
            (d, swappable_fields(config, d))
            for d in donors
            if d.get("target_emotion") != target
        ]
        usable = [(d, fields) for d, fields in usable if fields]
        if not usable:
            raise OversightError(
                f"no donor differs from the {target!r} config on any attributable field, "
                f"so nothing can be swapped. If this happens often the emotions are not "
                f"being separated by the pool, which is a finding rather than a bug."
            )
        donor, fields = rng.choice(usable)
        field = rng.choice(fields)

        trial["stimulus"] = swap(config, donor, field)
        trial["ground_truth"] = {
            "swapped_field": field,
            "original_value": config[field],
            "swapped_in_value": donor[field],
            "donor_emotion": donor.get("target_emotion"),
            "rationale_is_wrong": False,
        }
        return trial

    if condition == RANDOM:
        if pool_sampler is None:
            raise OversightError("the random condition needs a pool_sampler")
        stimulus = dict(config)
        stimulus.update(pool_sampler(rng))
        trial["stimulus"] = stimulus
        trial["ground_truth"] = {
            "swapped_field": None,  # everything is unreasoned; no single culprit
            "original_value": None,
            "swapped_in_value": None,
            "donor_emotion": None,
            "rationale_is_wrong": False,
            "all_variables_random": True,
        }
        return trial

    # RATIONALE_MISMATCHED: the room is genuine, only the explanation is wrong. Tests
    # whether people judge the artifact or the text attached to it.
    others = [d for d in donors if d.get("target_emotion") != target and d.get("rationale")]
    if not others:
        raise OversightError("no donor with a rationale to mismatch against")
    donor = rng.choice(others)
    trial["rationale_shown"] = donor["rationale"]
    trial["ground_truth"] = {
        "swapped_field": None,
        "original_value": None,
        "swapped_in_value": None,
        "donor_emotion": donor.get("target_emotion"),
        "rationale_is_wrong": True,
    }
    return trial


def build_oversight_block(
    configs: Sequence[dict],
    *,
    seed: int,
    participant: str,
    per_condition: int = 4,
    pool_sampler=None,
) -> dict:
    """One participant's Phase B block, balanced across conditions and shuffled.

    Balanced because detection sensitivity compares faithful against swapped: an
    unbalanced block would confound sensitivity with response bias.
    """
    if len(configs) < 2:
        raise OversightError("need at least two configs so there is a donor to draw from")

    rng = random.Random(seed)
    trials: list[dict] = []

    conditions = list(CONDITIONS) if pool_sampler else [c for c in CONDITIONS if c != RANDOM]

    for condition in conditions:
        # Spread each condition evenly over the configs rather than drawing with
        # replacement. Sampling independently lets a condition cluster on one or two
        # emotions by chance, which confounds condition with emotion: if most swapped
        # trials happen to be calm rooms, attribution accuracy for "swapped" is partly
        # an accuracy figure for calm. Cycling a reshuffled list caps the imbalance at
        # one trial per config.
        chosen: list[dict] = []
        while len(chosen) < per_condition:
            block = list(configs)
            rng.shuffle(block)
            chosen.extend(block[: per_condition - len(chosen)])

        for config in chosen:
            trials.append(
                make_trial(
                    config,
                    condition,
                    rng=rng,
                    donors=configs,
                    pool_sampler=pool_sampler,
                )
            )

    rng.shuffle(trials)
    for index, trial in enumerate(trials, start=1):
        trial["trial_index"] = index
        trial["trial_id"] = f"{participant}_b{index:03d}"

    return {
        "participant": participant,
        "seed": seed,
        "phase": "B",
        "conditions": conditions,
        "per_condition": per_condition,
        "trials": trials,
    }


def score_response(trial: dict, response: dict) -> dict:
    """Score one response against the trial's ground truth.

    `response` may carry:

        detected                bool   did they say something was wrong
        detection_confidence    0..1   how sure, on the detection judgement
        attributed_field        str    a field name, or NOTHING_WRONG
        attribution_confidence  0..1   how sure, on WHICH decision was wrong
        corrected_value                what they say it should have been
        duration_ms             int    time on this trial

    Confidence is taken separately on detection and attribution because they answer
    different questions and can come apart. Someone can be certain a room is wrong while
    having no idea which variable did it. More importantly, design section 3.3's most
    interesting outcome is "people converge and are confidently wrong", and that case is
    invisible unless confidence is attached to the attribution itself.

    Nothing is thresholded here. Confidence is recorded raw and calibration is computed
    over a block by `summarise`, because what counts as "confident" is an analysis
    decision rather than something to bake into collection.
    """
    truth = trial.get("ground_truth", {})
    condition = trial.get("condition")
    has_fault = bool(condition in (SWAPPED, RANDOM) or truth.get("rationale_is_wrong"))

    detected = bool(response.get("detected"))
    attributed = response.get("attributed_field")

    out: dict[str, Any] = {
        "condition": condition,
        "has_fault": has_fault,
        # Signal detection cells, so d-prime and criterion can be computed over a block.
        "hit": has_fault and detected,
        "false_alarm": (not has_fault) and detected,
        "miss": has_fault and not detected,
        "correct_rejection": (not has_fault) and not detected,
        "detection_confidence": response.get("detection_confidence"),
        "attribution_scoreable": condition == SWAPPED,
        "attribution_correct": None,
        "attribution_confidence": response.get("attribution_confidence"),
        "attribution_brier": None,
        "correction_correct": None,
        # Oversight cost. Free to collect and a real dependent variable: if accurate
        # attribution takes four times as long as a wrong one, that says something about
        # whether this kind of supervision scales.
        "duration_ms": response.get("duration_ms"),
    }

    if condition == SWAPPED:
        correct = attributed == truth.get("swapped_field")
        out["attribution_correct"] = correct

        confidence = response.get("attribution_confidence")
        if confidence is not None:
            # Brier score: squared gap between stated confidence and being right. 0 is
            # perfect, 1 is maximally confident and wrong. This is the number that makes
            # "confidently wrong" a measurement rather than an impression.
            out["attribution_brier"] = round((float(confidence) - (1.0 if correct else 0.0)) ** 2, 4)

        if "corrected_value" in response:
            out["correction_correct"] = response["corrected_value"] == truth.get("original_value")

    return out


def _dprime(hits: int, signal: int, false_alarms: int, noise: int) -> tuple[float, float]:
    """d-prime and criterion, with the standard log-linear correction.

    Rates of exactly 0 or 1 send the normal quantile to infinity, which happens easily in
    a short block, so both counts get the usual +0.5 / +1 adjustment before converting.
    """
    from statistics import NormalDist

    if signal <= 0 or noise <= 0:
        return float("nan"), float("nan")

    hit_rate = (hits + 0.5) / (signal + 1)
    fa_rate = (false_alarms + 0.5) / (noise + 1)

    z = NormalDist().inv_cdf
    zh, zf = z(hit_rate), z(fa_rate)
    return round(zh - zf, 4), round(-(zh + zf) / 2.0, 4)


def summarise(scored: Sequence[dict]) -> dict:
    """Block-level measures over a list of `score_response` outputs."""
    if not scored:
        raise OversightError("nothing to summarise")

    signal = [s for s in scored if s["has_fault"]]
    noise = [s for s in scored if not s["has_fault"]]
    hits = sum(1 for s in signal if s["hit"])
    false_alarms = sum(1 for s in noise if s["false_alarm"])

    d, criterion = _dprime(hits, len(signal), false_alarms, len(noise))

    attributable = [s for s in scored if s["attribution_scoreable"]]
    correct = [s for s in attributable if s["attribution_correct"]]
    wrong = [s for s in attributable if s["attribution_correct"] is False]

    def mean(values):
        values = [v for v in values if v is not None]
        return round(sum(values) / len(values), 4) if values else None

    def median(values):
        values = sorted(v for v in values if v is not None)
        if not values:
            return None
        mid = len(values) // 2
        return values[mid] if len(values) % 2 else (values[mid - 1] + values[mid]) / 2

    accuracy = round(len(correct) / len(attributable), 4) if attributable else None
    mean_confidence = mean([s["attribution_confidence"] for s in attributable])

    return {
        "n_trials": len(scored),
        # Detection
        "d_prime": d,
        "criterion": criterion,
        "hit_rate": round(hits / len(signal), 4) if signal else None,
        "false_alarm_rate": round(false_alarms / len(noise), 4) if noise else None,
        # Attribution
        "attribution_accuracy": accuracy,
        "attribution_mean_confidence": mean_confidence,
        "attribution_brier": mean([s["attribution_brier"] for s in attributable]),
        # Positive means stated confidence runs ahead of actual accuracy, which is the
        # "confidently wrong" signature. Near zero means well calibrated.
        "overconfidence": (
            round(mean_confidence - accuracy, 4)
            if mean_confidence is not None and accuracy is not None
            else None
        ),
        # Oversight cost, split by whether the attribution was right.
        "median_ms_overall": median([s["duration_ms"] for s in scored]),
        "median_ms_correct_attribution": median([s["duration_ms"] for s in correct]),
        "median_ms_wrong_attribution": median([s["duration_ms"] for s in wrong]),
    }
