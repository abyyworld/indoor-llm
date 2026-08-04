# Emotion-conveying interior appearance via LLM

Implementation of [design-spec.md](design-spec.md): the LLM selects VR room appearance
parameters from frozen discrete pools to hit a target emotion, a validator gates
everything before it reaches a participant, and a Unity loader builds the room.

```
pipeline/            generation, validation, controls, session building  (Python)
unity/               the scene loader                                    (C#)
build-decisions.md   engine, headset, lux and scene-structure decisions (mine to make)
configs/             pools.json (values as data), demo_batch.json (runnable without
                     an API key) + hand-written configs,
                     including a deliberately broken one
tests/               156 tests, no API key or network needed
runs/                generated output (git-ignored)
```

## Quick start

```bash
# 1. See the frozen pools and the design space (480 rooms, 960 with shape)
python3 -m pipeline.cli pools

# 2. Prove the gate works before trusting anything to it
python3 -m unittest discover -s tests
python3 -m pipeline.cli validate configs/handwritten_calm_001.json   # exit 0
python3 -m pipeline.cli validate configs/INVALID_do_not_ship.json    # exit 1

# 3. Build the Unity loader against the hand-written config (spec section 8.3)
#    -> unity/README.md

# 4. Only then generate
pip install -r requirements.txt
export ANTHROPIC_API_KEY=...        # never committed, never written to disk
python3 -m pipeline.cli generate-all --count 50 --out runs/llm_rooms.json

# 5. The control arms and one participant's trial list
python3 -m pipeline.cli random-control --count 16 --seed 20260726 --out runs/random_control.json
python3 -m pipeline.cli merge runs/llm_rooms.json runs/random_control.json --out runs/all_rooms.json
python3 -m pipeline.cli build-session --batch runs/all_rooms.json --participant p01 --seed 42 \
    --out runs/session_p01.json
python3 -m pipeline.cli export-unity runs/session_p01.json --out runs/unity_p01.json
```

`python3 -m pipeline.cli <command> --help` for the rest.

## How it hangs together

`pipeline/pools.py` is the single source of truth, and it reads its **values** from
`configs/pools.json`. That split is deliberate: when Mengkai's literature review lands,
filling in the numbers is a data edit that touches no code, which is what scene brief §7
step 4 asks for ("ideally this step only touches data"). A test proves it - swap the data
and the prompt, schema, validator and generated C# all follow. To try candidate values
without editing the checked-in file:

```bash
EMOTION_ROOMS_POOLS=configs/pools_candidate.json python3 -m pipeline.cli pools
```

A malformed pool file is fatal rather than silently permissive, since a pool that quietly
lost a constraint would widen the gate. Everything else derives from the module:

- the prompt text the model sees (`prompts.py`) - so the constraints it is told about
  cannot disagree with the constraints enforced on it
- the JSON schema constraining its output (`schema.py`) - every field is `enum`-bounded,
  so `"hue": 217` is mechanically hard to produce in the first place
- the validator (`validate.py`)
- the random control arm (`controls.py`)
- `unity/PoolConstants.cs`, which is **generated** - a test fails if it goes stale

The model never sets `id`, `target_emotion` or `source`; the pipeline assigns those, so
id uniqueness is our invariant rather than something we hope for. Enum-constrained
schemas do not remove the need to validate: the validator runs on everything that comes
back, rejections are re-asked with the exact violations, and rejected candidates stay in
the run file because how often the model breaks constraints is a result, not noise.

Validation runs three times, on purpose: on the raw candidate, on the assembled config,
and again in C# at load time. The last one matters because a config hand-edited on the
headset never gets a Python process to protect it.

## Deliberate design decisions worth knowing

**Rationales are kept** (spec section 4) and so is a `duplicate combinations` figure per
run. If a model returns 50 candidates that collapse onto six distinct combinations, that
is visible in the run summary rather than buried.

**`--sketch`** (spec section 7) asks for a small ASCII swatch as a sanity check on what
the model thinks it is describing. It is stored as `_sketch` and stripped before Unity.

**Both control arms are implemented.** The neutral arm is part of `generate-all`
(participants need something legitimately ratable as "nothing in particular"); the random
arm is a seeded uniform draw from the same pools, which is what makes "the LLM steers
emotion" falsifiable rather than indistinguishable from "any blue dim room feels calm".

**The session budget is enforced, not documented.** `build-session` prints trial minutes
at the spec's 1.5 min/room and warns past 25 minutes. Shape is within-subjects, so that
is 8 rooms = 12 min, still inside the budget.

## Assumptions - resolved 30 Jul 2026

All four are now answered by `research/scene-brief-for-akbar-260720.md` and
`research/paper-outline-260727-to-be-determined.html`. Kept here as a record of what
changed rather than deleted.

1. **Room shape is researcher-fixed, and WITHIN-subjects.** Shape is never an LLM
   output and does not enter the variable pool: the pool is applied within each shape
   condition independently (brief §8). Shape moved to within-subjects on 2 Aug 2026,
   reversing the earlier reading. The reason is power, not a change of mind: between
   subjects spends power on between-person variance that within-subjects removes, so
   Mengkai puts it at roughly 20-30 participants rather than 40-60, which is the whole
   recruitment budget. Each participant sees **8 trials**, 4 emotions x 2 shapes. This
   is the `build_session` default, so the change needed no rework; the between-subjects
   path still works by passing a single-entry `shapes`.

   **Ordering is not a detail here.** Each participant meets every emotion twice, once
   per shape, and those two rooms may share a general character since they target the
   same emotion from the same pool. If they land near each other, people may rate the
   comparison instead of their own feeling, which biases exactly the shape contrast the
   study exists to measure.

   Note the two shapes are sampled *independently*, so the same emotion can land on
   different values across linear and curved. An earlier version of this note said they
   were identical, citing the formative batches; Mengkai corrected that on 2 Aug, and it
   was a property of that unfinalised pool rather than of the design. The adjacency risk
   survives the correction but is weaker than identity.

   Default is `counterbalance="constrained"`: reshuffle until no same-emotion pair is
   closer than `min_separation` (2). Measured over 200 participants: zero adjacent pairs
   and 199 distinct orders. `"separated"` guarantees a gap of 4, the maximum, but yields
   only **8 distinct orders at any sample size** and always puts each emotion once in the
   first half, which is a detectable session structure of its own. Plain randomisation
   leaves about a quarter of pairs adjacent. Neither option removes both risks, and the
   write-up should describe it as a tradeoff rather than a solved problem.

2. **Light is neutral white; hue is wall/floor material only.** Confirmed exactly as
   built (brief §4, §8). `ApplyLight` needs no change, and
   the fixtures must stay unmovable with colour temperature untied from intensity.

3. **The four emotions are `calm, excited, depressed, tense`.** Confirmed (brief §1) -
   the diagonal quadrants of Russell's circumplex. Note the label is **depressed**, not
   the `sad` this repo used; renamed throughout. Literature support is solid for
   calm/tense and still thin for excited/depressed (10 Jul meeting note), which is a
   write-up caveat rather than a code problem.

4. **Both control arms are out of the participant design.** The neutral/baseline
   condition is dropped (brief §8, outline §4 "baseline dropped"). The random-parameter
   arm is **still being decided** - the brief says "will confirm before it would affect
   the build", while the covering email says it is dropped; the brief wins on a
   disagreement of the same date. Both arms are left built and default to 0 trials, so
   neither costs anything and either can be turned on with a flag. Do not delete them.
   With four trials per participant the 25-minute budget no longer binds.

### The contract her formative testing actually used

Read out of `research/formative-testing/prompt-template/five-llm-freedom-modes-prompt-template-260714.md`
(v2, `hue_detail` added 21 Jul) and the 44 logged samples. This is the real target, and it
differs from what this repo currently implements on every axis. Mode ④a is the selected
one (brief §2).

| | This repo now | Her ④a template |
|---|---|---|
| hue | `hue` 12 ints, degrees | `hue_category` in {warm, cool, neutral} |
| saturation | `saturation` 3 floats | reinstated 23 Jul as a discrete 1-5 scale |
| material | `texture` 4 named maps | `material` in {rough, smooth} |
| brightness | `brightness` 5 normalised floats | `brightness` in **lux**, band per emotion |
| secondary hue | absent | `hue_detail`, free text, not analysed |
| rationale | `rationale` | `free_elements_description` |

**Four variables, not three.** The 23 Jul note settles it and reverses the 10 Jul
position: "Saturation as a discrete 1-5 slider ... This overturns the earlier decision to
lock saturation to a low constant", on Wilms & Oberfeld (2018), where saturation's arousal
effect is no weaker than hue's and hue effects vanish at low saturation. Brief §4's
"three" is stale, not ambiguous: the outline itself says the 260723 saturation adjustment
is "pending merge". Do not treat §4 as current on this point.

**Brightness is not a pool.** It is a continuous lux value with an emotion-conditional
constraint, which nothing here anticipates:

- calm ~45-150 lx, tense ~670-780 lx (Mostafavi et al.)
- excited / depressed: **no literature range**, free field, LLM decides and justifies

So `brightness` cannot be enum-bounded, and the validator needs per-emotion range checks
plus an explicit "unlocked / exploratory" state for two of the four emotions. Per the
23 Jul note those two must be marked "no locked range", never "failed to match".

### Still genuinely open

- **The concrete values.** Colour blocks are to be literature-driven (Jonauskaite 2020:
  black, red, yellow-orange, gray, blue, white are low-variance; purple/pink are not),
  but the block list is not fixed. What saturation 1-5 maps to numerically is unstated.
  Excited/depressed illuminance has no range at all.
- **Illuminance needs a lux to Unity-intensity calibration**, measured in the headset.
  This is the "category maps to parameter value" table of brief §5 and §7.3, and it is on
  the critical path for the build. `unity/README.md` currently maps a normalised 0.2-1.0,
  which is the wrong contract.
- **Lighting colour**: brief §4 says neutral white, the 23 Jul note says "neutral/warm-white".
  For a study manipulating wall hue, that difference matters.
- **No windows** (23 Jul: they would contaminate the illuminance manipulation). Not yet
  recorded as a build constraint.

### Validity problems in her own data, not code problems

These are hers to decide, but they are the reason a study-ready build is not just a
refactor. All are visible in her batch-2 logs and analysis.

- **`tense` collapses onto its neighbours.** Observed ④a: calm = cool/smooth/low,
  tense = cool/rough/low, depressed = cool/rough/~35 lx. Tense and depressed are
  identical on hue and material and both dim. Calm and tense differ only in material.
- **The LLM's `tense` contradicts the literature.** Across two batches it chose low
  illuminance and, when unlocked, smooth material, against the literature's high
  illuminance plus rough. She flags this as "highest priority" for the supervisor.
- **The ④a prompt does not force the calm/tense lux mapping**; it lists both bands side
  by side, which is why tense took calm's band. A prompt fix, once she confirms the
  pairing.
- **`neutral` hue was never selected once** in the locked-pool samples, so hue is
  effectively binary in practice.
- **The dropped neutral baseline was a `[MEETING]` item.** design-spec.md §5 records the
  supervisor asking for non-emotional control rooms as the baseline. The outline drops it
  and books it as a limitation. Worth re-confirming with the supervisor, not just with
  Mengkai.

## Not built

- **Trial timing, the questionnaire, and response logging** (spec section 6). The loader
  fires `RoomLoaded`; the 30 s exposure and the valence–arousal form hang off that hook.
- **The ranking / filtering pass** (spec section 5). Generation, validation and the
  controls are here, but the human-in-the-loop or crowd judgement step needs a decision
  on who judges and against what criterion first. The run files are the input to it.
- **The Overleaf rewrite** (spec section 8.5) - nothing to do with this code.
