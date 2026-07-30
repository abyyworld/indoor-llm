"""Building one participant's trial list (design-spec.md section 6).

The spec's budget is hard arithmetic, not a guideline:

    30 s exposure + ~45 s questionnaire + ~15 s transition = 1.5 min per room
    ~25 min of actual trial time      -> ~14-16 rooms per participant

SHAPE IS BETWEEN-SUBJECTS. The old 4 emotions x 2 shapes x 2 variants = 16 is
superseded. Two of Mengkai's documents agree:

  * research/paper-outline-260727-to-be-determined.html section 4: "4 emotions
    within-subjects x 2 shapes between-subjects (baseline dropped)", and
    "Participants: ... shape between-subjects grouping".
  * research/scene-brief-for-akbar-260720.md sections 1-2: "Each participant is
    assigned to one of two room-shape conditions".

So one participant sees FOUR rooms -- four emotions in their one assigned shape.
The brief's "4 emotions x 2 shapes = 8 configurations (not 16)" counts scene
configurations to *build* across both arms, not one participant's trial list.
Read the covering email's "8 trials total" as that same count of configurations
loosely worded; taken literally it would mean shape within-subjects, which
contradicts both documents above.

Call this per participant:

    build_session(rooms, participant=..., seed=...,
                  shapes=("curved",),       # the participant's assigned arm
                  variants_per_emotion=1)   # -> 4 trials

Both are already parameters, so no code change was needed to support this. The
default `shapes=SHAPES` still crosses shape within participant; it is kept only
so the older within-subjects call keeps working, and it is NOT the study design.

Budget consequence: 4 rooms is 6 min of trial time, so TRIAL_BUDGET_MINUTES stops
binding and `over_budget` will not fire. Do not read the outline's "45-minute
budget" as a new value for it -- that 45 covers consent, practice and reporting as
well, whereas this constant is pure trial time from design-spec.md section 6. The
two are not the same quantity, so the constant is left alone.
"""

from __future__ import annotations

import random
from dataclasses import dataclass, field

from .pools import EMOTIONS, NEUTRAL_LABEL, SHAPES, UNASSIGNED_LABEL

#: design-spec.md section 6.
MINUTES_PER_ROOM = 1.5
TRIAL_BUDGET_MINUTES = 25.0


@dataclass
class Session:
    participant: str
    seed: int
    trials: list[dict] = field(default_factory=list)

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


def build_session(
    rooms: list[dict],
    *,
    participant: str,
    seed: int,
    emotions: tuple[str, ...] = EMOTIONS,
    variants_per_emotion: int = 2,
    shapes: tuple[str, ...] = SHAPES,
    neutral_trials: int = 0,
    random_trials: int = 0,
) -> Session:
    """Draw one participant's trial list from a pool of validated rooms.

    Each selected emotion room is shown once per shape, so shape is crossed within
    room: a participant sees the same parameter set in both geometries, which is what
    makes shape testable as a moderator rather than a between-rooms confound.

    That within-subjects crossing is the module docstring's open question. For the
    between-subjects reading the brief gives, pass a single-entry `shapes` --
    e.g. shapes=("curved",) -- and assign the condition per participant upstream.
    """
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

    rng.shuffle(trials)

    # Ids repeat across shapes, so give each trial its own key for the response log.
    for index, trial in enumerate(trials, start=1):
        trial["trial_index"] = index
        trial["trial_id"] = f"{trial['id']}_{trial['shape']}"

    return Session(participant=participant, seed=seed, trials=trials)
