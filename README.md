# Emotion-conveying interior appearance via LLM

Implementation of [design-spec.md](design-spec.md): the LLM selects VR room appearance
parameters from frozen discrete pools to hit a target emotion, a validator gates
everything before it reaches a participant, and a Unity loader builds the room.

## Scope of this repository

This is the software half of a two-person MSc study. The pipeline, the Unity loader and
the tests here are my own work. The experimental design, the literature grounding for
each manipulated variable and the formative LLM testing are my collaborator's, and her
research documents are not redistributed here.

That means some notes below cite documents you will not find in this repo, by design:
the scene brief and its sections, the supervision meeting notes, and the thesis outline.
Where a design decision came from one of those, the citation is kept so the reasoning is
traceable for anyone who has access to them. Nothing in this repo depends on those files
at runtime, and the pool values are placeholders pending her literature review, so treat
the numbers in `configs/pools.json` as provisional rather than as the study's parameters.

```
pipeline/            generation, validation, controls, session building  (Python)
unity/               the scene loader                                    (C#)
configs/             pools.json (the pool VALUES as data) + hand-written configs,
                     including a deliberately broken one
tests/               60 tests, no API key or network needed
runs/                generated output (git-ignored)
```

## Quick start

```bash
# 1. See the frozen pools and the design space (720 rooms, 1440 with shape)
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
at the spec's 1.5 min/room and warns past 25 minutes. With shape between-subjects that is 4 rooms = 6 min, so it no longer binds.

## Assumptions - resolved 30 Jul 2026

All four are now answered by `research/scene-brief-for-akbar-260720.md` and
`research/paper-outline-260727-to-be-determined.html`. Kept here as a record of what
changed rather than deleted.

1. **Room shape is researcher-fixed, and BETWEEN-subjects.** Confirmed: shape is never
   an LLM output and does not enter the variable pool - the pool is applied within each
   shape condition independently (brief §8). Each participant is assigned one shape
   (brief §1–2; outline §4: "4 emotions within-subjects × 2 shapes between-subjects").
   So a participant sees **four** rooms, and the brief's "8 configurations" is the count
   of scenes to *build* across both arms, not one participant's trial list. Call
   `build_session(..., shapes=("curved",), variants_per_emotion=1)`. The old
   within-subjects crossing is still the function default so existing calls work, but it
   is not the design.

2. **Light is neutral white; hue is wall/floor material only.** Confirmed exactly as
   built (brief §4, §8). `WALL_VALUE = 0.85` stands, `ApplyLight` needs no change, and
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

### Still blocking a study-ready build

- **The variable pool's actual values are not settled.** Brief §4: "please do not build
  against specific numbers or category labels". `pools.py` values are therefore
  placeholders - see the warning in that module's docstring.
- **Three variables or four.** Brief §4 and outline §3.2 both name three: hue category,
  material roughness, illuminance. That drops `saturation` to a fixed constant, which
  the 10 Jul meeting note supports ("fix saturation low as a constant rather than
  manipulating it - four papers converge on this"). But brief §7 step 2 still says
  "hue/saturation/roughness", and the outline flags "adjustments pending merge from the
  260723 meeting (saturation levels, etc.)". Unresolved; ask before changing `pools.py`.
- **Hue is heading for categories, not degrees.** The 10 Jul note simplifies hue to
  warm/cool/neutral. The current 12 numeric hues are a placeholder for that.
- **`material` vs `texture`.** Roughness is a scalar tier; `TEXTURES` is a categorical
  map. They are not interchangeable and the brief uses both words.
- **The JSON contract is not final.** Brief §5: field names and format still to be
  synced, along with the category→parameter config table Unity reads.
- **`hue_detail`.** Mengkai's formative testing logs this field and `CONTRIBUTORS.md`
  lists it; it does not exist in `schema.py`. Confirm whether the pipeline must emit it.

## Not built

- **Trial timing, the questionnaire, and response logging** (spec section 6). The loader
  fires `RoomLoaded`; the 30 s exposure and the valence–arousal form hang off that hook.
- **The ranking / filtering pass** (spec section 5). Generation, validation and the
  controls are here, but the human-in-the-loop or crowd judgement step needs a decision
  on who judges and against what criterion first. The run files are the input to it.
- **The Overleaf rewrite** (spec section 8.5) - nothing to do with this code.
