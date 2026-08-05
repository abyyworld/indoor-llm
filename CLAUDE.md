# Project instructions

VR affect study: an LLM picks room appearance parameters from frozen discrete pools to
convey a target emotion; a validator gates the output; a Unity loader builds the room.

- **[design-spec.md](design-spec.md) is the authority.** It tags what the supervisor
  actually said `[MEETING]`, defaults to confirm `[PROPOSED]`, and unresolved decisions
  `[OPEN]`. Do not quietly resolve an `[OPEN]` item - the collaborator (Mengkai) has to.
- **[build-decisions.md](build-decisions.md)** records my own build decisions (engine,
  render pipeline, headset, lux handling, scene structure) so they are not re-litigated.
- **[README.md](README.md)** records every assumption taken and what to change if the
  answer differs. Keep it in sync when a decision lands.

## Layout

```
pipeline/   generation, validation, controls, session building   (Python 3, stdlib + anthropic)
unity/      scene loader                                         (C#, drop into Assets/Scripts/EmotionRooms/)
configs/    pools.json (pool values as data) + hand-written configs
            + INVALID_do_not_ship.json (every room there must fail)
tests/      164 tests, no API key or network needed
forms/      Apps Script that generates the consent and post-session Google Forms
runs/       generated output, git-ignored
```

## Commands

```bash
python3 -m unittest discover -s tests        # must stay green
python3 -m pipeline.cli --help               # pools, validate, generate, generate-all,
                                             # random-control, merge, build-session,
                                             # export-unity, emit-unity-pools,
                                             # validate-handoff, check-separability,
                                             # oversight-block, bundle-participant
./test-participant.sh p01 42 0               # build one participant and stage the files
                                             # where the Unity editor reads them
python3 -m pipeline.cli validate configs/INVALID_do_not_ship.json   # must exit 1
```

## Invariants - do not break these

1. **`pipeline/pools.py` is the single source of truth, and its values live in
   `configs/pools.json`.** The prompt text, the JSON schema, the validator, the random
   arm and `unity/Assets/Scripts/EmotionRooms/PoolConstants.cs` all derive from it. Never hardcode a pool value
   anywhere else. Changing which values are permitted is a data edit; changing *which
   variables exist* is not - that is `[OPEN]` question 1 below and needs Mengkai.
   `configs/pools.json` carries `provisional: false` as of 3 Aug 2026: every value is
   Mengkai's final one. A test asserts the flag is false.
2. **`unity/Assets/Scripts/EmotionRooms/PoolConstants.cs` is generated.** After changing pools:
   `python3 -m pipeline.cli emit-unity-pools --out unity/Assets/Scripts/EmotionRooms/PoolConstants.cs`. A test fails
   if it goes stale.
3. **No unvalidated config reaches a participant** (spec §4). Validation runs on the raw
   candidate, on the assembled config, and again in C# at load time. The third one is not
   redundant: a config hand-edited on the headset gets no Python process.
4. **The LLM never sets `id`, `target_emotion` or `source`.** The pipeline assigns them,
   so id uniqueness is our invariant rather than something we hope for.
5. **Rejected candidates stay in the run file.** How often the model breaks constraints
   is a result, not noise. Same for the duplicate-combination rate.
6. **The LLM controls five parameters**: hue, saturation, brightness (lux), texture
   (material type) and roughness.
   Room dimensions, shape, furniture, object positions and the spawn point are
   researcher-set and the loader must never move them.

## Claude API usage in this repo

`claude-opus-5`, `thinking={"type": "adaptive"}`, streamed, structured outputs via
`output_config={"format": {"type": "json_schema", ...}}` (not the deprecated
`output_format` kwarg on `create`). Key from `ANTHROPIC_API_KEY` - never hardcoded,
never written to disk.

## Status

**The study runs end to end.** `Emotion Rooms > Study Control Panel` (Cmd-Shift-E) is
the whole researcher interface: five steps from building the scene to bundling the logs.
[unity/RUNBOOK.md](unity/RUNBOOK.md) is the procedure.

Built and tested: pools, schemas, validator, generation with reject-and-re-ask, the
control arms, session building with the spec's time budget, the Unity loader, the trial
runner, the affect grid, the oversight review block with its questionnaire panels, event
and 20 Hz telemetry logging, the consent gate and withdrawal path, per-participant log
bundling, hand-written fixtures.

Not built, deliberately: the ranking-and-filtering pass (spec §5 - needs a decision on
who judges and against what criterion first); the Overleaf rewrite.

Conventions worth not re-deriving:

- **`StudyBootstrap` owns `participantId`** and pushes it into the runners and both
  writers in `Awake`. `EventLog` and `StudyTelemetry` therefore open their files in
  `Start`, never `Awake` - Unity runs every `Awake` before any `Start`, and opening
  earlier named the log after the previous participant.
- **`QuestionPanel` resolves clicks with `RaycastAll` filtered to its own colliders.**
  A plain `Raycast` lets room geometry nearer than the panel eat the hit, and the review
  block blocks on an answer, so a swallowed click is a hang.
- **Furniture is never tinted.** `CollectTintables` selects on the `TintableSurface`
  component, which walls, floors and ceilings carry and furnishing does not.
- **`FurnitureSet`** optionally replaces the procedural furnishing with real models,
  placed at the same anchors and scaled to the same footprint so a cosmetic swap cannot
  become a confound. Keep exactly one such asset in the project.

## Answered by Mengkai, 30 Jul 2026

`research/` now holds her scene brief, meeting notes and thesis outline. The four
former open questions are settled: shape is researcher-fixed and **within-subjects**
(8 rooms per participant: shape moved to within-subjects on 2 Aug for statistical power); light stays neutral white with hue on wall/floor
material only; the emotions are **calm/excited/depressed/tense** (`depressed`, not
`sad`); the neutral baseline is dropped and the random arm is still undecided. Details
and citations in [README.md](README.md) under "Assumptions - resolved".

## Answered by her research/ folder - read it before asking again

Her formative-testing template and the 23 Jul meeting note answer most of what used to
be open here. The current contract is **four** variables with these names and levels:
`hue_category` in {warm, cool, neutral}, `saturation` as a discrete 1-5 scale (reinstated
23 Jul, overturning 10 Jul), `material` in {rough, smooth}, and `brightness` as a **lux**
value with an emotion-conditional band. Plus `hue_detail` (free text, logging only) and
`free_elements_description`. Every one of those differs from what this repo implements.
The comparison table and citations are in [README.md](README.md).

**Do not refactor to it yet** - see below. The variable set went four to three to four in
three weeks, and the values are still moving.

## Answered by Mengkai's email, 31 Jul 2026

Email from Mengkai Chen to Akbar. Source: direct message.

1. **Saturation: two levels, 20% and 40%** (Yi and Kang 2020). The current pool
   `{0.20, 0.50, 0.80}` becomes `{0.20, 0.40}`. Do not build against this yet -- it
   lands with the hue values below as one coordinated data edit.
2. **Light colour temperature: 4500K, neutral white.** Settled. Matches what's built.
3. **Hue: 10 calibrated categories + black + white** (Febbraio et al. 2025 Munsell-to-HSV
   mapping; Song et al. 2025 warm/cool grouping). Exact HSV values will be pushed to
   `research/variable-pool` on GitHub -- treat that file as authoritative. Do not
   build against hue until that file lands.
4. **Material splits into two variables: material type + roughness.** Type: plaster,
   concrete, textile. Roughness: rough/smooth, literature values pending. This is a
   structural schema change (5 LLM-controlled variables instead of 4, or `texture`
   replaced by `material`+`roughness`). Do not refactor until both pools are complete.
5. **Trial structure: 8-cell (4 emotions x 2 shapes) confirmed.** Shape was
   between-subjects here; SUPERSEDED 2 Aug, it is now within-subjects, 8 trials per
   participant. See the 2 Aug section below.

## Answered by Mengkai's email, 1 Aug 2026

Settled and buildable: **achromatic rule** - when saturation is 0 the scene is
achromatic, black or white by V, and the stored hue is meaningless (do not read it).
**Albedo** - V=100% stays the documented colour spec, the renderer applies it at ~0.85,
preserving her 1.21x / 1.54x ratios. **Materials** - build against plaster, concrete,
textile; she will flag a swap. **Exposure** - 20 seconds. **Random arm - CANCELLED,
final.** **Trial count** - she believes 8 stands, but she did not check with Daniele as
asked, so the 20-32 reading is unresolved rather than refuted.

Pending with dates: illuminance ranges and roughness levels (Monday), furniture list
(weekend), her config file (Thursday). Pending with **no date**: the aggregation method,
and the Affect Grid / SAM spec.

### She has retracted research/formative-testing and research/meeting-notes

Her words: "You can disregard both folders, they won't reflect the final design." Also
the Mostafavi-derived 45-150 / 670-780 lux figures - exploratory, not for the final
ranges. Anything in this repo citing those sources is therefore citing retracted
material and must not be treated as current.

Two things follow that need raising rather than absorbing:

  * `CONTRIBUTORS.md` cites `research/formative-testing/` as what "justified selecting
    the naturalistic-fixed-template + variable-pool mode", and the thesis outline builds
    §3.1 on that same data. Both cannot hold at once. Worth clarifying whether that work
    is still in the thesis.
  * Discarding the archive removes the early warning, not the risk. The tense/depressed
    overlap was never mainly about mode selection: calm, tense and depressed all sit in
    the cool half of the circumplex, so with warm/cool as the hue split three of four
    emotions still compete for the same region. That is a property of the design, not of
    the exploratory data, and it will resurface when the real values land.

## Open questions for Mengkai - do not answer these yourself

1. **Exact hue values** -- she will push to `research/variable-pool`. Wait for that
   file before touching `HUES` in pools.py.
2. **Roughness values** -- literature review pending, same push.
3. **Lux ranges** -- illuminance/lux values for each emotion still pending. The
   loader's current normalised 0.2-1.0 mapping is structurally correct but numerically
   wrong. Stays as-is until the calibration table lands (brief §5, §7.3).
4. **How `tense` gets separated from `calm` and `depressed`.** In her ④a data all three
   are "cool", tense and depressed are both cool/rough/dim. Design question, not code.
5. **Does the calm/tense lux pairing get forced in the prompt?** ④a lists both bands
   without mapping them.
6. **The random-parameter arm** -- email doesn't address it. Left built, default off.
7. **Furniture list** -- she said she'll send by the weekend.

## For the supervisor, not Mengkai

- **The neutral baseline was a `[MEETING]` item** (design-spec.md §5: non-emotional
  control rooms as the baseline) and has been dropped, booked as a limitation. Confirm.
- **Two of four emotions cannot be manipulation-checked**, since excited/depressed have
  no literature illuminance range.

## Answered by Mengkai's email, 2 Aug 2026

**Shape moves to WITHIN-subjects.** Every participant sees all 8 scenes (4 emotions x 2
shapes) rather than 4. Her reason is power: between-subjects spends power on
between-person variance that within-subjects removes, so she puts recruitment at roughly
20-30 participants instead of 40-60. The interaction (shape x emotion) is the research
question, and within-subjects is the right place to test an interaction, so this is
correct rather than merely convenient.

No rework: within-subjects is already the `build_session` default.

**Ordering is where the care goes, and her "fully randomised, no counterbalancing" is
the one part to push back on.** Each participant now meets every emotion twice, once per
shape, and her formative data had curved and linear sharing identical appearance
parameters. So the two trials for an emotion are the same room in a different geometry.
Land them close together and people rate the comparison rather than their own feeling,
biasing the shape contrast the study exists to measure.

`counterbalance="separated"` holds every pair 4 trials apart, the maximum available, and
keeps shape even across session halves so shape is not confounded with fatigue. Measured
over 24 participants: 0 adjacent pairs, first emotion balanced 6/6/6/6, first shape 12/12.
Plain randomisation leaves ~24% of pairs adjacent. A Williams square is *worse* here,
putting every pair within 2 positions, because it balances carryover rather than distance.

## Answered by Mengkai, 3 Aug 2026 -- the pools are FINAL

`configs/pools.json` now carries `provisional: false`. Every manipulated value is hers:

- **hue** 10 Munsell-calibrated categories, **saturation** {0.20, 0.40} (30 Jul)
- **material type** {plaster, concrete, textile}, **roughness** {rough, smooth} (3 Aug)
- **illuminance** {150, 300, 500, 750} lux (3 Aug)
- **aggregation** medoid, 30 samples per cell, 8 cells, equal weights across the five
  fields. She adopted the implementation in `pipeline/aggregate.py`.

**Illuminance is ONE pool shared by all four emotions**, not a band per emotion. That is
a change of kind: there is no longer a per-emotion illuminance expectation, so the
illuminance manipulation check has nothing to test. `emotion_illuminance_bands` is all
null, every cell reports "no locked range", and `match_rate` is None rather than 1.0 so
an absent check cannot read as a passing one. Worth stating plainly in the write-up.

Still outstanding: her eight-cell config file, which is the medoid output over her
sampling runs. Nothing else.
