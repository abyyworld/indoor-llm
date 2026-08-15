"""The questionnaires, as data, with their scoring and their citations.

Single source of truth in the same sense as `pools.py`: the item text, the response
options, the scoring formulas and the point in the session where each form appears all
live here, and the Unity side is generated from it. Two hand-maintained copies of a
16-item symptom checklist diverge on the first typo, and a mis-transcribed item in a
validated instrument quietly makes the score incomparable to every published norm --
which is the entire reason for using a validated instrument.

Everything here is a published scale used at its published wording, scored by its
published formula. Where an item is reverse-keyed, that is recorded as data rather than
applied by hand later.

WHEN EACH FORM APPEARS. Nothing runs during the trials. The affect rating is the only
thing collected inside a room, because it measures how the room feels while you are in
it. Everything else brackets the session:

    before   consent, demographics, SSQ baseline
    after    SSQ again, NASA-TLX, trust, presence, open questions, debrief

NONE OF THEM BLOCK. A participant who declines a form, or a researcher who is out of
time, proceeds. The session records what was and was not completed, and the end screen
names anything missing. A questionnaire that blocks a session is a questionnaire that
gets answered carelessly to make it go away.
"""

from __future__ import annotations

from typing import Any

# --------------------------------------------------------------------------- scales

FOUR_POINT = ["None", "Slight", "Moderate", "Severe"]
SEVEN_POINT = ["1", "2", "3", "4", "5", "6", "7"]

# --------------------------------------------------------------------- demographics

DEMOGRAPHICS = {
    "id": "demographics",
    "title": "About you",
    "when": "before",
    "phases": ['A', 'B'],
    "citation": "",
    "instruction": "A few details before we start. Say so if you would rather not answer.",
    "items": [
        {"id": "age", "text": "Age", "type": "text"},
        {"id": "gender", "text": "Gender", "type": "choice",
         "options": ["Woman", "Man", "Non-binary", "Prefer to self-describe",
                     "Prefer not to say"]},
        {"id": "vision_correction", "text": "Do you wear glasses or contact lenses?",
         "type": "choice",
         "options": ["No", "Yes, wearing them now", "Yes, not wearing them now"]},
        {"id": "colour_vision",
         "text": "Do you have any colour vision deficiency?",
         "help": "The study varies room colour, so this matters for the results. "
                 "It does not stop you taking part.",
         "type": "choice", "options": ["No", "Yes", "Not sure"]},
        {"id": "vr_experience", "text": "How often have you used virtual reality before?",
         "type": "choice",
         "options": ["Never", "Once or twice", "A few times a year", "Monthly",
                     "Weekly or more"]},
        {"id": "rested", "text": "How well rested do you feel right now?",
         "type": "scale", "min": 1, "max": 5,
         "min_label": "Not at all", "max_label": "Very well rested"},
    ],
}

# ------------------------------------------------------------------------------ SSQ

# Kennedy, Lane, Berbaum & Lilienthal (1993). Simulator Sickness Questionnaire: an
# enhanced method for quantifying simulator sickness. International Journal of Aviation
# Psychology, 3(3), 203-220.
#
# Run twice. A post-exposure score alone cannot separate sickness the study caused from
# a headache someone walked in with, and the baseline is what makes the difference
# interpretable. It is also the safety measure: a rising score is the reason to stop.

SSQ_SYMPTOMS = [
    ("general_discomfort", "General discomfort"),
    ("fatigue", "Fatigue"),
    ("headache", "Headache"),
    ("eyestrain", "Eyestrain"),
    ("difficulty_focusing", "Difficulty focusing"),
    ("increased_salivation", "Increased salivation"),
    ("sweating", "Sweating"),
    ("nausea", "Nausea"),
    ("difficulty_concentrating", "Difficulty concentrating"),
    ("fullness_of_head", "Fullness of head"),
    ("blurred_vision", "Blurred vision"),
    ("dizzy_eyes_open", "Dizziness with eyes open"),
    ("dizzy_eyes_closed", "Dizziness with eyes closed"),
    ("vertigo", "Vertigo"),
    ("stomach_awareness", "Stomach awareness"),
    ("burping", "Burping"),
]

SSQ_SUBSCALES = {
    "nausea": (
        ["general_discomfort", "increased_salivation", "sweating", "nausea",
         "difficulty_concentrating", "stomach_awareness", "burping"],
        9.54,
    ),
    "oculomotor": (
        ["general_discomfort", "fatigue", "headache", "eyestrain",
         "difficulty_focusing", "difficulty_concentrating", "blurred_vision"],
        7.58,
    ),
    "disorientation": (
        ["difficulty_focusing", "nausea", "fullness_of_head", "blurred_vision",
         "dizzy_eyes_open", "dizzy_eyes_closed", "vertigo"],
        13.92,
    ),
}

SSQ_TOTAL_WEIGHT = 3.74


def _ssq(form_id: str, when: str, title: str) -> dict[str, Any]:
    return {
        "id": form_id,
        "title": title,
        "when": when,
        # Both halves. Simulator sickness is a safety measure before it is a variable,
        # so it is asked of everyone who puts the headset on.
        "phases": ["A", "B"],
        "citation": "Kennedy, Lane, Berbaum & Lilienthal (1993), IJAP 3(3), 203-220.",
        "instruction": "For each one, choose how much you feel it right now.",
        "items": [
            {"id": key, "text": text, "type": "choice", "options": FOUR_POINT}
            for key, text in SSQ_SYMPTOMS
        ],
    }


SSQ_BEFORE = _ssq("ssq_before", "before", "How you feel right now")
SSQ_AFTER = _ssq("ssq_after", "after", "How you feel now")

# ------------------------------------------------------------------------ NASA-TLX

# Hart & Staveland (1988). Development of NASA-TLX: results of empirical and theoretical
# research. In Human Mental Workload, 139-183.
#
# Raw TLX: the six subscales unweighted, rather than the original pairwise-comparison
# weighting. Hart (2006) reports raw TLX to be equally or more sensitive in most
# applications, and the 15 pairwise comparisons cost more participant time than they buy.
# Reported as raw so it stays comparable to the large raw-TLX literature.
#
# It is asked about the REVIEW block, not the whole session. Standing in a room and
# saying how it feels is not a task with a workload; deciding whether an AI system got
# something wrong, saying which part, and proposing a fix is. That second task is the
# one this study exists to characterise.

TLX_ITEMS = [
    ("mental_demand", "Mental demand",
     "How mentally demanding was the task?", "Very low", "Very high", False),
    ("physical_demand", "Physical demand",
     "How physically demanding was the task?", "Very low", "Very high", False),
    ("temporal_demand", "Temporal demand",
     "How hurried or rushed was the pace of the task?", "Very low", "Very high", False),
    ("performance", "Performance",
     "How successful were you in accomplishing what you were asked to do?",
     "Perfect", "Failure", True),
    ("effort", "Effort",
     "How hard did you have to work to accomplish your level of performance?",
     "Very low", "Very high", False),
    ("frustration", "Frustration",
     "How insecure, discouraged, irritated, stressed and annoyed were you?",
     "Very low", "Very high", False),
]

NASA_TLX = {
    "id": "nasa_tlx",
    "title": "How that felt to do",
    "when": "after",
    "phases": ['B'],
    "citation": "Hart & Staveland (1988); raw TLX per Hart (2006).",
    "instruction": "These are about the second half, where you were asked whether "
                   "anything looked wrong in a room and what you would change.",
    "items": [
        {
            "id": key,
            "text": label,
            "help": question,
            "type": "scale",
            # 21 points presented, scored 0-100 in steps of 5, as published.
            "min": 0, "max": 100, "step": 5,
            "min_label": low, "max_label": high,
            "reversed": reverse,
        }
        for key, label, question, low, high, reverse in TLX_ITEMS
    ],
}

# --------------------------------------------------------------------------- trust

# Jian, Bisantz & Drury (2000). Foundations for an empirically determined scale of trust
# in automated systems. International Journal of Cognitive Ergonomics, 4(1), 53-71.
#
# The instrument that makes this study's oversight half comparable to the wider
# human-automation literature. Items 1-5 are distrust and reverse-scored.

TRUST_ITEMS = [
    ("deceptive", "The system is deceptive", True),
    ("underhanded", "The system behaves in an underhanded manner", True),
    ("suspicious", "I am suspicious of the system's intent, action, or outputs", True),
    ("wary", "I am wary of the system", True),
    ("harmful", "The system's actions will have a harmful or injurious outcome", True),
    ("confident", "I am confident in the system", False),
    ("security", "The system provides security", False),
    ("integrity", "The system has integrity", False),
    ("dependable", "The system is dependable", False),
    ("reliable", "The system is reliable", False),
    ("trust", "I can trust the system", False),
    ("familiar", "I am familiar with the system", False),
]

TRUST = {
    "id": "trust",
    "title": "The system that designed the rooms",
    "when": "after",
    "phases": ['B'],
    "citation": "Jian, Bisantz & Drury (2000), IJCE 4(1), 53-71.",
    "instruction": "The rooms were designed by an AI system. For each statement, "
                   "1 means not at all and 7 means very much.",
    "items": [
        {"id": key, "text": text, "type": "scale", "min": 1, "max": 7,
         "min_label": "Not at all", "max_label": "Very much", "reversed": reverse}
        for key, text, reverse in TRUST_ITEMS
    ],
}

# ------------------------------------------------------------------------ presence

# Schubert, Friedmann & Regenbrecht (2001). The experience of presence: factor analytic
# insights. Presence, 10(3), 266-281. The igroup Presence Questionnaire.
#
# Presence is a covariate here, not an outcome. If the curved shell produces more
# presence than the linear one, a difference in affect ratings between shapes has a
# second explanation, and that has to be measurable rather than assumed away.

IPQ_ITEMS = [
    ("g1", "In the computer generated world I had a sense of being there",
     "Not at all", "Very much", False, "general"),
    ("sp1", "Somehow I felt that the virtual world surrounded me",
     "Fully disagree", "Fully agree", False, "spatial"),
    ("sp2", "I felt like I was just perceiving pictures",
     "Fully disagree", "Fully agree", True, "spatial"),
    ("sp3", "I did not feel present in the virtual space",
     "Did not feel", "Felt present", True, "spatial"),
    ("sp4", "I had a sense of acting in the virtual space, rather than operating "
            "something from outside", "Fully disagree", "Fully agree", False, "spatial"),
    ("sp5", "I felt present in the virtual space",
     "Fully disagree", "Fully agree", False, "spatial"),
    ("inv1", "How aware were you of the real world surrounding while navigating in the "
             "virtual world?", "Extremely aware", "Not aware at all", True, "involvement"),
    ("inv2", "I was not aware of my real environment",
     "Fully disagree", "Fully agree", False, "involvement"),
    ("inv3", "I still paid attention to the real environment",
     "Fully disagree", "Fully agree", True, "involvement"),
    ("inv4", "I was completely captivated by the virtual world",
     "Fully disagree", "Fully agree", False, "involvement"),
    ("real1", "How real did the virtual world seem to you?",
     "Completely real", "Not real at all", True, "realism"),
    ("real2", "How much did your experience in the virtual environment seem consistent "
              "with your real world experience?",
     "Not consistent", "Very consistent", False, "realism"),
    ("real3", "How real did the virtual world seem to you?",
     "Like an imagined world", "Indistinguishable from the real world", False, "realism"),
    ("real4", "The virtual world seemed more realistic than the real world",
     "Fully disagree", "Fully agree", False, "realism"),
]

PRESENCE = {
    "id": "presence",
    "title": "Being there",
    "when": "after",
    "phases": ['A'],
    "citation": "Schubert, Friedmann & Regenbrecht (2001), Presence 10(3), 266-281 (IPQ).",
    "instruction": "About the rooms you stood in. Scored 1 to 7.",
    "items": [
        {"id": key, "text": text, "type": "scale", "min": 1, "max": 7,
         "min_label": low, "max_label": high, "reversed": reverse, "subscale": subscale}
        for key, text, low, high, reverse, subscale in IPQ_ITEMS
    ],
}

# ------------------------------------------------------------- the study's own items

# Not validated instruments, and labelled as such. These ask what no off-the-shelf scale
# asks: how someone decided a system had got something wrong. Free text because the
# point is to find strategies nobody thought to put on a multiple-choice list.

STRATEGY = {
    "id": "strategy",
    "title": "The second half",
    "when": "after",
    "phases": ['B'],
    "citation": "",
    "instruction": "In your own words. There are no right answers here.",
    "items": [
        # Manipulation check. Without it, a null on the explanation effect cannot be
        # told apart from an explanation nobody found convincing -- a measurement
        # failure and a real absence of effect look identical in the d-prime.
        {"id": "reasoning_convincing", "type": "scale", "min": 1, "max": 7,
         "text": "On the rooms where the system explained its reasoning, how convincing "
                 "was that reasoning?",
         "min_label": "Not at all", "max_label": "Very convincing"},
        {"id": "reasoning_influence", "type": "scale", "min": 1, "max": 7,
         "text": "How much did the reasoning affect your judgement of whether the room "
                 "had been altered?",
         "min_label": "Not at all", "max_label": "A great deal"},
        {"id": "reasoning_noticed", "type": "paragraph",
         "text": "Did you notice anything about when the system did and did not explain "
                 "itself?",
         "help": "Guessing is fine."},
        {"id": "how_decided", "type": "paragraph",
         "text": "When you were asked whether something looked wrong in a room, how did "
                 "you decide?",
         "help": "Whatever you actually did, including guessing."},
        {"id": "unnameable", "type": "paragraph",
         "text": "Was there anything that felt wrong but you could not put your finger "
                 "on what?"},
        {"id": "correction_goal", "type": "paragraph",
         "text": "When you changed something about a room, what were you trying to "
                 "achieve?"},
        {"id": "anything_else", "type": "paragraph",
         "text": "Anything that felt odd, uncomfortable, unclear, or worth telling us?"},
    ],
}

DEBRIEF = {
    "id": "debrief",
    "title": "What this was about",
    "when": "after",
    "phases": ['A', 'B'],
    "citation": "",
    "instruction": (
        "The rooms were designed by an AI system asked to make a space feel a particular "
        "way, by choosing colour, brightness and materials.\n\n"
        "In the second half, some rooms had one of those choices deliberately replaced "
        "with a value the system had picked for a different feeling. We did not tell you "
        "which, because knowing would have changed what you noticed. We were measuring "
        "whether people can tell when a system like this has got something wrong, and "
        "whether they can say which part.\n\n"
        "Nothing you did was right or wrong. Rooms where you noticed nothing are as "
        "informative as rooms where you did. If you want your data removed, say so now "
        "or contact the researcher within two weeks."
    ),
    "items": [
        {"id": "read", "type": "choice", "options": ["Yes"],
         "text": "I have read the explanation above."},
    ],
}

# ---------------------------------------------------------------------------- consent

CONSENT = {
    "id": "consent",
    "title": "Information and consent",
    "when": "before",
    "phases": ['A', 'B'],
    "citation": "",
    "instruction": (
        "Please read this before deciding whether to take part. Ask the researcher "
        "anything at any point, including after you have started.\n\n"
        "WHAT HAPPENS. You will wear a VR headset and stand in a series of virtual "
        "rooms, about 20 seconds each. After each one you say how the room made you "
        "feel, using a grid you point at inside the headset. In the second half you see "
        "rooms again and are asked whether anything looks wrong and what you would "
        "change. About 45 minutes including breaks.\n\n"
        "WHAT WE RECORD. Your answers, and how you moved your head and the controller. "
        "No video, no audio, no images of you, nothing about your face or eyes. Data is "
        "stored under a participant code, not your name.\n\n"
        "VOLUNTARY. You can stop at any moment without giving a reason and nothing "
        "follows from it. You can ask us to delete your data within two weeks.\n\n"
        "RISKS. Some people feel briefly dizzy or nauseous in VR. Tell the researcher "
        "and stop if so. You will be standing; say if that is a problem and we will "
        "seat you."
    ),
    "items": [
        {"id": "understood", "type": "choice", "options": ["Yes"],
         "text": "I have read and understood the information above."},
        {"id": "questions", "type": "choice", "options": ["Yes"],
         "text": "I have had the chance to ask questions, and they were answered."},
        {"id": "voluntary", "type": "choice", "options": ["Yes"],
         "text": "I understand that taking part is voluntary and I can stop at any time."},
        {"id": "recording", "type": "choice", "options": ["Yes"],
         "text": "I understand what will be recorded and that it is stored under a code."},
        {"id": "sharing", "type": "choice", "options": ["Yes"],
         "text": "I understand anonymised data may be published and shared, and that I "
                 "cannot be identified from it."},
        {"id": "age", "type": "choice", "options": ["Yes"],
         "text": "I am 18 or over."},
        {"id": "agree", "type": "choice", "options": ["Yes"],
         "text": "I agree to take part."},
    ],
}

# ------------------------------------------------------------------ baseline mood

# Two single items rather than full PANAS, which is 20 items and would cost more
# participant patience than it buys here.
#
# The reason to ask at all: the dependent variable is how a room makes someone feel, and
# someone who arrives cheerful rates every room higher than someone who arrives flat.
# Within-subjects design removes that from the shape and emotion contrasts, but the
# thesis will want to report the sample's starting state, and a reviewer who asks "were
# your participants in a normal mood?" needs an answer that is not a shrug.

BASELINE_MOOD = {
    "id": "baseline_mood",
    "title": "How you feel at the moment",
    "when": "before",
    "phases": ['A', 'B'],
    "citation": "Single-item valence and arousal, after Russell's circumplex.",
    "instruction": "Before we start, how are you feeling right now? Not about the study, "
                   "just generally.",
    "items": [
        {"id": "valence", "type": "scale", "min": 1, "max": 9,
         "text": "Right now I feel", "min_label": "Very unpleasant",
         "max_label": "Very pleasant"},
        {"id": "arousal", "type": "scale", "min": 1, "max": 9,
         "text": "Right now I feel", "min_label": "Very calm / sleepy",
         "max_label": "Very alert / worked up"},
    ],
}

# ---------------------------------------------------------- awareness and preference

# Two things a thesis on this design will be asked about, and neither is answerable from
# the trial data.
#
# AWARENESS. If participants worked out that colour, brightness and material were being
# varied to produce particular feelings, their ratings may be reporting what they thought
# was wanted rather than what they felt. This is the standard demand-characteristics
# check and it is cheap. Asking openly first, before the checklist, matters: the
# checklist itself tells people what varied, so a free-text answer given afterwards is
# worthless.
#
# PREFERENCE. Liking and affect are not the same thing and they correlate. Without a
# preference measure, "the warm room was rated more pleasant" cannot be separated from
# "people preferred the warm room", and those support different claims.

AWARENESS = {
    "id": "awareness",
    "title": "What you noticed",
    "when": "after",
    "phases": ['A', 'B'],
    "citation": "",
    "instruction": "There are no right answers here, and guessing is fine.",
    "items": [
        {"id": "guessed_purpose", "type": "paragraph",
         "text": "What do you think this study was trying to find out?",
         "help": "Your honest guess, even if you are not sure."},
        {"id": "noticed_varying", "type": "paragraph",
         "text": "What, if anything, did you notice changing between the rooms?"},
        # Shape only. Colour, brightness and material used to be asked here too and
        # are gone: the block measures noticing far better than a post-hoc yes/no
        # does. It asks about a specific variable thirty-two times, against ground
        # truth, with a confidence rating attached. A tick box at the end adds nothing
        # to that and costs three items and some risk of leading.
        #
        # Shape is different because nothing in the block ever asks about it. It is
        # researcher-set, never manipulated, never attributable, so this is the only
        # place it is checked at all.
        {"id": "noticed_shape", "type": "choice", "options": ["Yes", "No", "Not sure"],
         "text": "Did you notice that some rooms were curved and some were square?"},
        {"id": "tried_to_please", "type": "scale", "min": 1, "max": 7,
         "text": "I found myself answering the way I thought the researcher wanted.",
         "min_label": "Not at all", "max_label": "Very much"},
    ],
}

PREFERENCE = {
    "id": "preference",
    "title": "What you liked",
    "when": "after",
    "phases": ['A'],
    "citation": "",
    "instruction": "Separately from how the rooms made you feel: which did you like?",
    "items": [
        {"id": "shape_preference", "type": "choice",
         "options": ["The curved rooms", "The square rooms", "No preference"],
         "text": "Which room shape did you prefer?"},
        {"id": "shape_reason", "type": "paragraph",
         "text": "Why?"},
        {"id": "attention_check", "type": "choice",
         "options": ["Strongly disagree", "Disagree", "Neither", "Agree", "Strongly agree"],
         "text": "Please select \"Disagree\" for this item.",
         "help": "This one checks the form is being read. It is not about you."},
    ],
}

# ----------------------------------------------------------------------------- order

FORMS = [
    CONSENT,
    DEMOGRAPHICS,
    BASELINE_MOOD,
    SSQ_BEFORE,
    SSQ_AFTER,
    NASA_TLX,
    TRUST,
    PRESENCE,
    AWARENESS,
    PREFERENCE,
    STRATEGY,
    DEBRIEF,
]


def before(phase: str | None = None) -> list[dict[str, Any]]:
    return due("before", phase)


def after(phase: str | None = None) -> list[dict[str, Any]]:
    return due("after", phase)


def due(when: str, phase: str | None = None) -> list[dict[str, Any]]:
    """Forms for this point in the session, and this participant's phases.

    A Phase B participant is never asked how much they liked the curved rooms: they
    were not rating rooms, they were auditing an agent's choices, and an item somebody
    has no basis to answer is burden that produces noise. Passing no phase returns
    everything, which is what a participant doing both halves gets.
    """
    out = []
    for form in FORMS:
        if form["when"] != when:
            continue
        if phase is not None and phase not in form.get("phases", ["A", "B"]):
            continue
        out.append(form)
    return out


def as_dict() -> dict[str, Any]:
    return {
        "version": 1,
        "forms": FORMS,
        "scoring": {
            "ssq_subscales": {k: {"items": v[0], "weight": v[1]}
                              for k, v in SSQ_SUBSCALES.items()},
            "ssq_total_weight": SSQ_TOTAL_WEIGHT,
        },
    }


# --------------------------------------------------------------------------- scoring

def score_ssq(answers: dict[str, int]) -> dict[str, float]:
    """SSQ subscale and total scores from 0-3 severities, per Kennedy et al. (1993)."""
    out: dict[str, float] = {}
    raw_total = 0.0
    for name, (items, weight) in SSQ_SUBSCALES.items():
        raw = sum(answers.get(item, 0) for item in items)
        out[name] = raw * weight
        raw_total += raw
    out["total"] = raw_total * SSQ_TOTAL_WEIGHT
    return out


def score_tlx(answers: dict[str, float]) -> dict[str, float]:
    """Raw TLX: the mean of the six subscales, each 0-100.

    Performance is reverse-keyed in the published scale, so a high score means poor
    performance and the subscale is stored as presented rather than flipped -- flipping
    it here would make the number disagree with every published raw-TLX figure.
    """
    values = [float(answers[key]) for key, *_ in TLX_ITEMS if key in answers]
    out = {key: float(answers[key]) for key, *_ in TLX_ITEMS if key in answers}
    out["raw_tlx"] = sum(values) / len(values) if values else 0.0
    return out


def score_trust(answers: dict[str, int]) -> dict[str, float]:
    """Mean trust on 1-7 with the five distrust items reversed."""
    total, count = 0.0, 0
    for key, _, reverse in TRUST_ITEMS:
        if key not in answers:
            continue
        value = float(answers[key])
        total += (8.0 - value) if reverse else value
        count += 1
    return {"trust_mean": total / count if count else 0.0, "items_answered": count}


ATTENTION_CHECK = ("preference", "attention_check", "Disagree")


def passed_attention_check(answers: dict[str, str]) -> bool | None:
    """True, False, or None when the item was not answered.

    None rather than False on a blank, because "did not answer" and "answered wrongly"
    are different exclusion decisions and the analysis should get to make its own.
    """
    _, item, expected = ATTENTION_CHECK
    if item not in answers or not answers[item]:
        return None
    return answers[item] == expected


def score_baseline_mood(answers: dict[str, str]) -> dict[str, float]:
    """Starting valence and arousal on 1-9, on the same axes as the affect grid.

    Same scale on purpose: it makes "did they start where they ended up" a subtraction
    rather than a modelling decision.
    """
    out: dict[str, float] = {}
    for key in ("valence", "arousal"):
        if answers.get(key):
            try:
                out["baseline_" + key] = float(answers[key])
            except ValueError:
                pass
    return out


def score_awareness(answers: dict[str, str]) -> dict[str, Any]:
    """How much of the manipulation the participant worked out.

    `noticed_count` is the headline: 0 means the manipulation was invisible to them, 4
    means they saw all of it. Reported per participant rather than aggregated away,
    because the interesting analysis is whether ratings differ between the people who
    noticed and the people who did not -- and if they do not, that is the strongest
    single answer to a demand-characteristics objection.
    """
    # Only shape is asked here now; the rest comes off the attribution data, which is
    # a stronger measure of the same thing.
    checks = ("noticed_shape",)
    noticed = {c: answers.get(c, "") for c in checks}
    out: dict[str, Any] = dict(noticed)
    out["noticed_count"] = sum(1 for c in checks if noticed[c] == "Yes")
    out["noticed_any"] = out["noticed_count"] > 0

    if answers.get("tried_to_please"):
        try:
            out["demand_pressure"] = float(answers["tried_to_please"])
        except ValueError:
            pass

    # Free text is kept verbatim. Coding it is a human judgement and belongs in the
    # write-up, not in a scoring function that would quietly decide what counts as
    # "guessed the hypothesis".
    out["guessed_purpose"] = answers.get("guessed_purpose", "")
    out["noticed_varying"] = answers.get("noticed_varying", "")
    return out


def score_preference(answers: dict[str, str]) -> dict[str, Any]:
    """Which shape they liked, separately from how it made them feel."""
    choice = answers.get("shape_preference", "")
    return {
        "shape_preference": choice,
        # Coded so a preference-vs-affect comparison is a join rather than a lookup.
        "prefers_curved": choice == "The curved rooms",
        "prefers_linear": choice == "The square rooms",
        "shape_reason": answers.get("shape_reason", ""),
        "attention_check_passed": passed_attention_check(answers),
    }


def score_presence(answers: dict[str, int]) -> dict[str, float]:
    """IPQ subscale means on 1-7, reverse-keyed items flipped."""
    sums: dict[str, list[float]] = {}
    for key, _, _, _, reverse, subscale in IPQ_ITEMS:
        if key not in answers:
            continue
        value = float(answers[key])
        sums.setdefault(subscale, []).append((8.0 - value) if reverse else value)
    out = {name: sum(v) / len(v) for name, v in sums.items() if v}
    every = [x for v in sums.values() for x in v]
    out["presence_mean"] = sum(every) / len(every) if every else 0.0
    return out
