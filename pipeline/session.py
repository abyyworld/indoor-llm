"""Building one participant's trial list (design-spec.md section 6).

The spec's budget is hard arithmetic, not a guideline:

    30 s exposure + ~45 s questionnaire + ~15 s transition = 1.5 min per room
    ~25 min of actual trial time      -> ~14-16 rooms per participant

SHAPE IS WITHIN-SUBJECTS, as of Mengkai's 2 Aug 2026 decision. Every participant sees
all 8 scenes: 4 emotions x 2 shapes.

This reverses the earlier between-subjects reading, and her reason is a power argument
rather than a change of mind about the design. Between-subjects spends power on
between-person variance that within-subjects removes, because each participant acts as
their own baseline. Concretely she puts it at roughly 20-30 participants within-subjects
against 40-60 between, which is the whole recruitment budget.

    build_session(rooms, participant=..., seed=..., variants_per_emotion=1)
    -> 8 trials, both shapes crossed within every emotion

That is the function default, so this change needed no rework. The between-subjects
path still works by passing a single-entry `shapes`; it is simply no longer the design.

WHAT WITHIN-SUBJECTS COSTS, and why counterbalancing is not optional here. Each
participant now meets every emotion twice, once per shape. Two risks follow:

  * the manipulation becomes guessable, and someone who notices they are in "the same
    room but curved" may rate the comparison rather than their own feeling
  * what a participant just saw can colour what they report next

Plain random ordering balances position only in expectation, and at 8 conditions with
20-30 participants that is visibly lumpy. `counterbalance="williams"` balances position
AND first-order carryover by construction. Use it for the real study.

Budget: 8 rooms is 12 min of trial time against the 25 min constant, so `over_budget`
still will not fire. Do not read the outline's "45-minute budget" as a new value for
that constant -- the 45 covers consent, practice and reporting too, whereas this is
pure trial time from design-spec.md section 6. Different quantities, so it is left alone.
"""

from __future__ import annotations

import random
from dataclasses import dataclass, field

from .pools import EMOTIONS, NEUTRAL_LABEL, SHAPES, UNASSIGNED_LABEL

# design-spec.md section 6, kept as named components rather than one magic number so
# that when a duration changes the arithmetic follows instead of going quietly stale.
# Exposure dropped from 30 s to 20 s on 1 Aug 2026 (Mengkai); the constant below did not
# follow it until 2 Aug, which is exactly the failure this decomposition prevents.
EXPOSURE_SECONDS = 20.0
QUESTIONNAIRE_SECONDS = 45.0
TRANSITION_SECONDS = 15.0

MINUTES_PER_ROOM = (EXPOSURE_SECONDS + QUESTIONNAIRE_SECONDS + TRANSITION_SECONDS) / 60.0
TRIAL_BUDGET_MINUTES = 25.0


@dataclass
class Session:
    participant: str
    seed: int
    trials: list[dict] = field(default_factory=list)
    #: How trial order was decided. Recorded because it is a methods-section fact and
    #: a reviewer will ask; it should not have to be reconstructed from the code later.
    counterbalance: str = "random"
    #: Which Williams row this participant got, when counterbalanced.
    participant_index: int | None = None

    @property
    def minutes(self) -> float:
        return len(self.trials) * MINUTES_PER_ROOM

    @property
    def over_budget(self) -> bool:
        return self.minutes > TRIAL_BUDGET_MINUTES


def _by_label(rooms: list[dict]) -> dict[str, list[dict]]:
    grouped: dict[str, list[dict]] = {}
    for room in rooms:
        grouped.setdefault(room["target_emotion"], []).append(room)
    return grouped


def williams_square(n: int) -> list[list[int]]:
    """A Williams design: `n` orderings of `n` conditions, balanced two ways.

    Plain random ordering balances position effects only in expectation. At the sample
    sizes here (20-30 participants over 8 conditions) that is visibly lumpy, and it is
    the first thing a reviewer asks about in a within-subjects design.

    A Williams design guarantees two things instead of hoping for them:

      * every condition appears in every position exactly once across the square
      * every condition immediately follows every other exactly once, which balances
        first-order carryover

    Carryover matters here specifically. With shape within-subjects each participant
    meets the same emotion twice, once per shape, so what a person just saw can colour
    what they report next. A plain Latin square balances position but not sequence; the
    Williams construction balances both.

    Even `n` needs one square, odd `n` needs the square plus its reverse. Only even is
    implemented, because the design is 8 conditions.
    """
    if n < 2:
        raise ValueError("need at least two conditions")
    if n % 2 != 0:
        raise NotImplementedError(
            f"n={n} is odd; a Williams design then needs the square plus its mirror. "
            f"The study has 8 conditions, so this has not been built."
        )

    # First row: 0, 1, n-1, 2, n-2, 3, ... which is what produces the carryover balance.
    first: list[int] = []
    low, high = 0, n - 1
    while low <= high:
        first.append(low)
        if low != high:
            first.append(high)
        low += 1
        high -= 1

    return [[(value + shift) % n for value in first] for shift in range(n)]


def separated_order(trials: list[dict], participant_index: int) -> list[dict]:
    """Order 8 trials so the two sharing an emotion are as far apart as possible.

    This is the recommended ordering, and it exists because of a specific property of
    this design rather than as a general preference.

    Shape is within-subjects, so every participant meets each emotion twice, once per
    shape. Those two rooms may share a general character, since they target the same
    emotion from the same pool. Land them near each other and a participant may start
    rating the difference between them rather than how the room actually makes them
    feel, which biases the within-person shape contrast, and that contrast is precisely
    the moderation effect the study exists to estimate.

    Note what this argument does NOT rest on. An earlier version of this docstring said
    the two rooms are identical on every appearance parameter, citing the formative data.
    Mengkai corrected that on 2 Aug: the shapes are sampled independently, so the same
    emotion can land on different values across linear and curved. The identical values
    in the formative batches were how that earlier, unfinalised pool happened to fall,
    not a property of the design. The adjacency risk survives the correction, because
    similar character is enough to invite comparison, but it is weaker than identity and
    the write-up should say so.

    Neither plain randomisation nor a Williams square prevents this. Randomisation does
    nothing about it, and Williams balances first-order carryover while still happily
    placing an emotion pair two positions apart.

    Construction: split into two halves of four, each holding one instance of every
    emotion. Every pair is then separated by exactly four positions, which is the
    maximum achievable with eight trials. Which shape goes in the first half alternates
    by participant, and emotion order within each half rotates, so position and shape
    stay balanced across the sample.

    THE COST, which Mengkai identified and the measurements confirm. This guarantees the
    maximum gap by imposing a fixed macro-structure: the first half always holds each
    emotion once and the second half repeats them. Across 200 simulated participants it
    produces only EIGHT distinct orders, and all 200 have every emotion once in the first
    half. No pair is ever adjacent, but the session's shape is nearly identical for
    everyone, which is its own detectable pattern.

    `constrained_order` trades the guarantee for variety and is the better default here.
    This is kept because it is the right choice if exact position balance matters more
    than unpredictability, and because it documents the tradeoff rather than hiding it.
    """
    by_emotion: dict[str, list[dict]] = {}
    for trial in trials:
        by_emotion.setdefault(trial["target_emotion"], []).append(trial)

    if any(len(v) != 2 for v in by_emotion.values()):
        raise ValueError(
            "separated ordering expects exactly two trials per emotion, one per shape. "
            "Got: " + ", ".join(f"{k}={len(v)}" for k, v in sorted(by_emotion.items()))
        )

    emotions = sorted(by_emotion)
    n = len(emotions)

    # Rotate which emotion leads, so first position is balanced across participants.
    rotation = participant_index % n
    ordered_emotions = emotions[rotation:] + emotions[:rotation]

    # Alternate which shape occupies the first half.
    flip = (participant_index // n) % 2

    # Alternate shape position-by-position rather than giving the first half one shape
    # and the second the other. Blocking by shape would separate the pairs just as well,
    # but it confounds shape with session half inside every participant: any drift over
    # the session, fatigue or adaptation to the headset, would load onto the shape
    # contrast. Interleaving keeps the gap at n and removes that confound.
    first_half, second_half = [], []
    for position, emotion in enumerate(ordered_emotions):
        pair = sorted(by_emotion[emotion], key=lambda t: t["shape"])
        if (position + flip) % 2:
            pair = pair[::-1]
        first_half.append(pair[0])
        second_half.append(pair[1])

    # Keep both halves in the SAME emotion order. That gives every pair a gap of exactly
    # n, the maximum available. Reversing the second half looks tidier and is actively
    # worse: it puts the last emotion of the first half directly next to its own pair,
    # producing the single worst case the ordering exists to avoid.
    return first_half + second_half


def constrained_order(
    trials: list[dict],
    rng: random.Random,
    min_separation: int = 2,
    max_attempts: int = 2000,
) -> list[dict]:
    """Reshuffle until no two trials sharing an emotion sit closer than `min_separation`.

    Mengkai's suggestion, 2 Aug, and it addresses a real weakness in `separated`.
    `separated` guarantees the maximum gap, but it does so with a fixed macro-structure:
    the first half always holds each emotion once and the second half repeats them in the
    same order. No single pair is adjacent, but the SHAPE of the session is identical for
    everyone, and a participant could notice that regularity as its own pattern.

    This trades the guarantee for variety. Every participant gets a genuinely different
    order, and the constraint only rules out the close pairings that matter. The cost is
    that position balance across the sample becomes approximate rather than exact.

    Neither option removes both risks. Which one is right depends on whether you are more
    worried about a detectable session structure or about uneven position balance, and
    that is a judgement call rather than something the code can settle.
    """
    if not trials:
        return trials

    ordered = list(trials)
    for _ in range(max_attempts):
        rng.shuffle(ordered)
        gaps = pair_separations(ordered)
        if not gaps or min(gaps.values()) >= min_separation:
            return ordered

    raise ValueError(
        f"no ordering of {len(trials)} trials met a minimum separation of "
        f"{min_separation} within {max_attempts} attempts. Lower min_separation, or use "
        f"counterbalance='separated', which guarantees the maximum gap by construction."
    )


def pair_separations(trials: list[dict]) -> dict[str, int]:
    """Positional gap between the two trials sharing each emotion. For checking."""
    positions: dict[str, list[int]] = {}
    for index, trial in enumerate(trials):
        positions.setdefault(trial["target_emotion"], []).append(index)
    return {e: abs(p[1] - p[0]) for e, p in positions.items() if len(p) == 2}


def _check_williams(square: list[list[int]]) -> list[str]:
    """Verify the two balance properties. Used by the tests, and cheap enough to keep."""
    n = len(square)
    errors: list[str] = []

    for position in range(n):
        seen = sorted(row[position] for row in square)
        if seen != list(range(n)):
            errors.append(f"position {position} is not balanced: {seen}")

    pairs: dict[tuple[int, int], int] = {}
    for row in square:
        for a, b in zip(row, row[1:]):
            pairs[(a, b)] = pairs.get((a, b), 0) + 1
    off_diagonal = [(a, b) for a in range(n) for b in range(n) if a != b]
    unbalanced = [p for p in off_diagonal if pairs.get(p, 0) != 1]
    if unbalanced:
        errors.append(f"{len(unbalanced)} ordered pairs are not followed exactly once")

    return errors


def build_session(
    rooms: list[dict],
    *,
    participant: str,
    seed: int,
    emotions: tuple[str, ...] = EMOTIONS,
    # 1, not 2. The design is 4 emotions x 2 shapes = 8 trials. The old default of 2 was
    # from the superseded 16-trial design and silently produced 16 trials, which also
    # made counterbalance="separated" impossible since it needs exactly one pair per
    # emotion. A default that cannot express the study design is a trap, not a default.
    variants_per_emotion: int = 1,
    shapes: tuple[str, ...] = SHAPES,
    neutral_trials: int = 0,
    random_trials: int = 0,
    # Defaults to "constrained" rather than "random" so the recommended methodology is
    # what you get without asking. "separated" guarantees a bigger gap but yields only
    # eight distinct orders at any sample size, which is a detectable session structure.
    counterbalance: str = "constrained",
    participant_index: int | None = None,
    min_separation: int = 2,
) -> Session:
    """Draw one participant's trial list from a pool of validated rooms.

    Each selected emotion room is shown once per shape, so shape is crossed within
    room: a participant sees the same parameter set in both geometries. This is the
    study design as of 2 Aug 2026 (Mengkai): shape is WITHIN subjects, giving 8 trials
    per participant, chosen because within-subjects needs roughly half the N of
    between-subjects for the same power and recruitment is the binding constraint.

    For a between-subjects arm, pass a single-entry `shapes` such as shapes=("curved",)
    and assign the condition per participant upstream. That path still works; it is no
    longer the design.

    `counterbalance`:

        "random"    shuffle by seed. Balances position only in expectation.
        "williams"  assign this participant a row of a Williams square, so position and
                    first-order carryover are both balanced by construction. Needs
                    `participant_index`, and the trial count must be even.

    Prefer "williams" for the real study. With 8 conditions and 20-30 participants,
    random ordering leaves visible position imbalance, and each participant meets every
    emotion twice here, so what they just saw can colour what they report next.
    """
    if counterbalance not in ("random", "williams", "separated", "constrained"):
        raise ValueError(f"unknown counterbalance {counterbalance!r}")

    rng = random.Random(seed)
    grouped = _by_label(rooms)
    trials: list[dict] = []

    for emotion in emotions:
        available = grouped.get(emotion, [])
        if len(available) < variants_per_emotion:
            raise ValueError(
                f"need {variants_per_emotion} '{emotion}' rooms, batch has {len(available)}"
            )
        for room in rng.sample(available, variants_per_emotion):
            for shape in shapes:
                trials.append({**room, "shape": shape})

    def add_controls(label: str, wanted: int) -> None:
        if wanted <= 0:
            return
        available = grouped.get(label, [])
        if len(available) < wanted:
            raise ValueError(
                f"need {wanted} '{label}' rooms, batch has {len(available)}"
            )
        for index, room in enumerate(rng.sample(available, wanted)):
            trials.append({**room, "shape": shapes[index % len(shapes)]})

    add_controls(NEUTRAL_LABEL, neutral_trials)
    add_controls(UNASSIGNED_LABEL, random_trials)

    if counterbalance == "constrained":
        trials.sort(key=lambda t: (t["target_emotion"], t["shape"], t["id"]))
        trials = constrained_order(trials, rng, min_separation=min_separation)
    elif counterbalance == "separated":
        if participant_index is None:
            raise ValueError("counterbalance='separated' needs participant_index")
        trials.sort(key=lambda t: (t["target_emotion"], t["shape"], t["id"]))
        trials = separated_order(trials, participant_index)
    elif counterbalance == "williams":
        if participant_index is None:
            raise ValueError("counterbalance='williams' needs participant_index")
        if len(trials) % 2 != 0:
            raise ValueError(
                f"a Williams design needs an even number of trials, got {len(trials)}. "
                f"Either use counterbalance='random' or make the trial count even."
            )
        # Order the trials deterministically first, so the square is applied to a stable
        # list rather than to whatever order the loops above happened to produce.
        trials.sort(key=lambda t: (t["target_emotion"], t["shape"], t["id"]))
        square = williams_square(len(trials))
        order = square[participant_index % len(square)]
        trials = [trials[i] for i in order]
    else:
        rng.shuffle(trials)

    # Ids repeat across shapes, so give each trial its own key for the response log.
    for index, trial in enumerate(trials, start=1):
        trial["trial_index"] = index
        trial["trial_id"] = f"{trial['id']}_{trial['shape']}"

    return Session(
        participant=participant,
        seed=seed,
        trials=trials,
        counterbalance=counterbalance,
        participant_index=participant_index,
    )
