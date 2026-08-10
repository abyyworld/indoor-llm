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

#: The main detection block. RATIONALE_MISMATCHED is deliberately NOT here.
#:
#: It is coded as a corruption but is perceptually identical to a faithful room -- the
#: room is correct and only the stated reasoning is wrong. Scored alongside the others it
#: contaminates d-prime, because a participant who correctly sees nothing wrong with the
#: room is marked as having missed a corruption. It also has no answer in the attribution
#: instrument: none of the five variables is at fault, so "nothing wrong" is
#: simultaneously the closest-to-correct response and the one that ends the trial.
#: It runs as its own short block with its own question instead.
CONDITIONS: tuple[str, ...] = (FAITHFUL, SWAPPED, RANDOM)

#: Every condition the study can present, including the one that lives in its own block.
ALL_CONDITIONS: tuple[str, ...] = (FAITHFUL, SWAPPED, RANDOM, RATIONALE_MISMATCHED)

#: Half the trials are faithful.
#:
#: At 25% faithful a participant works out within a few trials that most rooms are broken
#: and shifts criterion hard toward "something is wrong". Hit rate then looks excellent
#: and means nothing, and criterion measures the block's base rate rather than the person.
#: An even split is what makes criterion a property of the participant.
FAITHFUL_SHARE: float = 0.5

#: Corrections applied as the participant asked, versus a different legal value.
#:
#: Without this comparison the correction effect has an obvious alternative explanation
#: and no defence: someone who diagnoses a fault, chooses a fix, watches it applied and
#: then rates the result will rate it higher because it was theirs. That is
#: self-consistency, not correction quality. Half the corrected trials therefore apply a
#: value the participant did not choose, unannounced, so own-correction can be compared
#: against a matched one.
OWN: str = "own"
YOKED: str = "yoked"

#: Variables a participant can be asked to attribute an error to. Kept here rather than
#: derived from pools.py because Phase B asks about the agent's *decisions*, and which
#: decisions exist is a design question, not a pool question.
# Both vocabularies on purpose. This repo's configs call the material axis `texture`;
# Mengkai's call it `material`. `_attributable_fields` keeps only the keys a given
# config actually has, so listing both makes the swap work either way -- and listing
# only `material`, as this did, meant no trial could ever swap the material axis at
# all, silently dropping one of the five variables from the oversight block.
ATTRIBUTABLE: tuple[str, ...] = (
    "hue",
    "saturation",
    "texture",
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


def pool_distance(config: dict, donor: dict, field: str) -> float:
    """How far apart two values sit inside their own pool, as a 0..1 fraction.

    Perceptibility is not comparable across variables -- degrees of hue and lux are
    different units -- so each is normalised by its own pool's span. Categorical
    fields (material, roughness) are either the same or different, and a difference
    is always fully visible, so they score 1.
    """
    from pipeline import pools

    a, b = config.get(field), donor.get(field)
    if a is None or b is None or a == b:
        return 0.0

    table = {
        "hue": pools.HUES,
        "saturation": pools.SATURATIONS,
        "brightness": pools.BRIGHTNESSES,
    }
    values = table.get(field)
    if values is None:
        return 1.0            # texture, roughness: categorical, fully visible

    span = max(values) - min(values)
    if span <= 0:
        return 1.0
    if field == "hue":
        # Hue is a circle: 330 and 0 are neighbours, not opposites.
        gap = abs(float(a) - float(b)) % 360.0
        return min(gap, 360.0 - gap) / 180.0
    return abs(float(a) - float(b)) / float(span)


def make_trial(
    config: dict,
    condition: str,
    *,
    rng: random.Random,
    donors: Sequence[dict] = (),
    pool_sampler=None,
) -> dict:
    """Build one Phase B trial, with its ground truth attached."""
    if condition not in ALL_CONDITIONS:
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
        # Prefer the swap a person could actually see.
        #
        # A donor value one pool step from the original is a real manipulation and an
        # invisible one: saturation 0.20 -> 0.40 in a dim room, or one hue category
        # over, changes the data and not the experience. Detection and attribution
        # then sit at floor and measure nothing except that the swap was too small,
        # which is a property of our sampling rather than a finding about oversight.
        # Ranking candidates by how far the value moves within its own pool keeps the
        # manipulation perceptible; ties and non-numeric fields fall back to chance,
        # so this narrows the draw without ever emptying it.
        candidates = [(d, f) for d, fields in usable for f in fields]
        best = max(pool_distance(config, d, f) for d, f in candidates)
        if best > 0:
            candidates = [
                (d, f) for d, f in candidates
                if pool_distance(config, d, f) >= best * 0.5
            ]
        donor, field = rng.choice(candidates)

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
    trials_total: int = 32,
    pool_sampler=None,
) -> dict:
    """One participant's Phase B block: half faithful, half corrupted, shuffled.

    32 trials by default, not 12. Detection sensitivity is estimated per participant from
    the faithful/corrupted contrast, and three faithful trials give a false-alarm rate
    that can only be 0, .33, .67 or 1 -- a d-prime computed from that is not an estimate.
    Sixteen of each is the smallest split that supports the analysis.

    Corrupted trials are further split between swapped and random, and each corrupted
    trial is pre-assigned to have the participant's own correction applied or a different
    one, balanced, so the yoked comparison is by design rather than by whatever happened.
    """
    if len(configs) < 2:
        raise OversightError("need at least two configs so there is a donor to draw from")
    if trials_total < 4:
        raise OversightError("a block needs at least four trials to be balanced")

    rng = random.Random(seed)
    trials: list[dict] = []

    faithful_count = int(round(trials_total * FAITHFUL_SHARE))
    corrupted_count = trials_total - faithful_count

    corrupt_kinds = [SWAPPED, RANDOM] if pool_sampler else [SWAPPED]
    per_kind = corrupted_count // len(corrupt_kinds)
    counts = {FAITHFUL: faithful_count}
    for i, kind in enumerate(corrupt_kinds):
        # Any remainder goes to the first kind rather than being dropped.
        counts[kind] = per_kind + (corrupted_count - per_kind * len(corrupt_kinds)
                                   if i == 0 else 0)

    conditions = [FAITHFUL] + corrupt_kinds

    # EXPLANATION is crossed with fidelity, balanced within each condition so the
    # 2x2 is even: half of the faithful trials carry the system's stated reasoning
    # and half do not, and likewise for each corrupted kind. Assigned per condition
    # rather than over the whole block, because balancing globally can leave one
    # condition entirely explained and another entirely bare - which turns the
    # factor into a confound with fidelity rather than a crossing of it.
    #
    # On corrupted trials the reasoning describes the ORIGINAL design, not what is
    # on the wall. That is the manipulation: a fluent, plausible justification for
    # a room that no longer matches it.
    for condition in conditions:
        per_condition = counts[condition]
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

        explained = [True] * (per_condition // 2)
        explained += [False] * (per_condition - len(explained))
        rng.shuffle(explained)

        for config, show in zip(chosen, explained):
            trial = make_trial(
                config,
                condition,
                rng=rng,
                donors=configs,
                pool_sampler=pool_sampler,
            )
            trial["explanation_shown"] = show
            trials.append(trial)

    rng.shuffle(trials)

    # Assign the yoked half among the corrupted trials only -- a faithful room has
    # nothing to correct. Assigned from a shuffled balanced list rather than a coin flip
    # per trial, so every participant gets the same split instead of a binomial draw.
    corrupted = [t for t in trials if t["condition"] != FAITHFUL]
    sources = [OWN, YOKED] * (len(corrupted) // 2 + 1)
    sources = sources[: len(corrupted)]
    rng.shuffle(sources)
    for trial, source in zip(corrupted, sources):
        trial["correction_source"] = source
        # Fixed in advance rather than drawn when the moment arrives.
        #
        # Which value a yoked trial substitutes depends on what the participant chose,
        # so it cannot be written out here -- but the draw can still be deterministic.
        # Seeding it from the block means the substitution is reproducible from the
        # trial file alone, identical on both platforms, and auditable afterwards
        # rather than being a runtime coin flip nobody can reconstruct.
        trial["sham_seed"] = rng.randrange(1 << 30)
    for trial in trials:
        trial.setdefault("correction_source", "")

    for index, trial in enumerate(trials, start=1):
        trial["trial_index"] = index
        trial["trial_id"] = f"{participant}_b{index:03d}"

    return {
        "participant": participant,
        "seed": seed,
        "phase": "B",
        # What a yoked trial substitutes, stated once so the write-up and the ethics
        # application describe the same thing: a different legal value for the variable
        # the participant named, drawn from the same pool, never their own choice and
        # never the value that would repair the room. No other participant's data is
        # used, so nothing about one person is shown to another.
        "sham_rule": "same-pool value, excluding the participant's choice and the "
                     "original (correct) value",
        "conditions": conditions,
        "counts": counts,
        "trials_total": len(trials),
        "faithful_share": FAITHFUL_SHARE,
        "trials": trials,
    }


def build_rationale_block(
    configs: Sequence[dict],
    *,
    seed: int,
    participant: str,
    trials_total: int = 6,
) -> dict:
    """The rationale check, as its own block with its own question.

    Asked as "does the stated reasoning match the room?" rather than "is anything wrong
    with the room?", because the room is correct in both halves. Half the trials carry
    the model's own rationale and half carry another emotion's, so this is its own
    two-alternative detection task and cannot contaminate the room-detection d-prime.
    """
    if len(configs) < 2:
        raise OversightError("need at least two configs so a rationale can be swapped")

    rng = random.Random(seed + 9973)
    matched = trials_total // 2
    trials: list[dict] = []

    pool = list(configs)
    rng.shuffle(pool)
    for i in range(trials_total):
        config = pool[i % len(pool)]
        if i < matched:
            trials.append({
                "condition": "rationale_matched",
                "stimulus": dict(config),
                "target_emotion_shown": config.get("target_emotion"),
                "rationale_shown": config.get("rationale", ""),
                "ground_truth": {"rationale_is_wrong": False, "donor_emotion": None},
            })
        else:
            donors = [c for c in configs
                      if c.get("target_emotion") != config.get("target_emotion")]
            if not donors:
                raise OversightError("no donor with a different emotion for the rationale block")
            donor = rng.choice(donors)
            trials.append({
                "condition": "rationale_mismatched",
                "stimulus": dict(config),
                "target_emotion_shown": config.get("target_emotion"),
                "rationale_shown": donor.get("rationale", ""),
                "ground_truth": {"rationale_is_wrong": True,
                                 "donor_emotion": donor.get("target_emotion")},
            })

    rng.shuffle(trials)
    for index, trial in enumerate(trials, start=1):
        trial["trial_index"] = index
        trial["trial_id"] = f"{participant}_r{index:03d}"

    return {
        "participant": participant,
        "seed": seed,
        "phase": "B-rationale",
        "question": "Does the stated reasoning match the room?",
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
