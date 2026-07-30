# Project instructions

VR affect study: an LLM picks room appearance parameters from frozen discrete pools to
convey a target emotion; a validator gates the output; a Unity loader builds the room.

- **[design-spec.md](design-spec.md) is the authority.** It tags what the supervisor
  actually said `[MEETING]`, defaults to confirm `[PROPOSED]`, and unresolved decisions
  `[OPEN]`. Do not quietly resolve an `[OPEN]` item - the collaborator (Mengkai) has to.
- **[README.md](README.md)** records every assumption taken and what to change if the
  answer differs. Keep it in sync when a decision lands.

## Layout

```
pipeline/   generation, validation, controls, session building   (Python 3, stdlib + anthropic)
unity/      scene loader                                         (C#, drop into Assets/Scripts/EmotionRooms/)
configs/    pools.json (pool values as data) + hand-written configs
            + INVALID_do_not_ship.json (every room there must fail)
tests/      60 tests, no API key or network needed
runs/       generated output, git-ignored
```

## Commands

```bash
python3 -m unittest discover -s tests        # must stay green
python3 -m pipeline.cli --help               # pools, validate, generate, generate-all,
                                             # random-control, merge, build-session,
                                             # export-unity, emit-unity-pools
python3 -m pipeline.cli validate configs/INVALID_do_not_ship.json   # must exit 1
```

## Invariants - do not break these

1. **`pipeline/pools.py` is the single source of truth, and its values live in
   `configs/pools.json`.** The prompt text, the JSON schema, the validator, the random
   arm and `unity/PoolConstants.cs` all derive from it. Never hardcode a pool value
   anywhere else. Changing which values are permitted is a data edit; changing *which
   variables exist* is not - that is `[OPEN]` question 1 below and needs Mengkai.
   `configs/pools.json` carries a `provisional` flag that a test asserts is still true.
2. **`unity/PoolConstants.cs` is generated.** After changing pools:
   `python3 -m pipeline.cli emit-unity-pools --out unity/PoolConstants.cs`. A test fails
   if it goes stale.
3. **No unvalidated config reaches a participant** (spec §4). Validation runs on the raw
   candidate, on the assembled config, and again in C# at load time. The third one is not
   redundant: a config hand-edited on the headset gets no Python process.
4. **The LLM never sets `id`, `target_emotion` or `source`.** The pipeline assigns them,
   so id uniqueness is our invariant rather than something we hope for.
5. **Rejected candidates stay in the run file.** How often the model breaks constraints
   is a result, not noise. Same for the duplicate-combination rate.
6. **The LLM controls four parameters only**: hue, saturation, brightness, texture.
   Room dimensions, shape, furniture, object positions and the spawn point are
   researcher-set and the loader must never move them.

## Claude API usage in this repo

`claude-opus-5`, `thinking={"type": "adaptive"}`, streamed, structured outputs via
`output_config={"format": {"type": "json_schema", ...}}` (not the deprecated
`output_format` kwarg on `create`). Key from `ANTHROPIC_API_KEY` - never hardcoded,
never written to disk.

## Status

Built and tested: pools, schemas, validator, generation with reject-and-re-ask, the
neutral and random control arms, session building with the spec's time budget, the Unity
loader, hand-written fixtures.

Not built, deliberately: the trial runner / questionnaire / response logging (the loader
fires `RoomLoaded` as the hook, spec §6); the ranking-and-filtering pass (spec §5 -
needs a decision on who judges and against what criterion first); the Overleaf rewrite.

## Answered by Mengkai, 30 Jul 2026

`research/` now holds her scene brief, meeting notes and thesis outline. The four
former open questions are settled: shape is researcher-fixed and **between-subjects**
(4 rooms per participant, not 8); light stays neutral white with hue on wall/floor
material only; the emotions are **calm/excited/depressed/tense** (`depressed`, not
`sad`); the neutral baseline is dropped and the random arm is still undecided. Details
and citations in [README.md](README.md) under "Assumptions - resolved".

## Open questions for Mengkai - do not answer these yourself

1. **Three manipulated variables or four?** Brief §4 and outline §3.2 name three (hue
   category, material roughness, illuminance), which would fix `saturation` as a
   constant. Brief §7 still says "hue/saturation/roughness". Do not touch `pools.py`
   until this lands.
2. **The pool values themselves.** Brief §4 explicitly says do not build against
   specific numbers or category labels yet - they are pending her literature review.
   Everything in `pools.py` is a placeholder, including the 12 numeric hues, which the
   10 Jul note suggests become warm/cool/neutral categories.
3. **Is `material` a roughness tier or a named material?** `TEXTURES` is categorical;
   roughness is a scalar. The brief uses both words.
4. **The JSON contract.** Brief §5: field names and the category→parameter config table
   are still to be synced. Also whether the pipeline must emit her `hue_detail` field.
5. **The random-parameter arm**: her email says dropped, her brief the same day says
   undecided. Left built and defaulted off until she resolves it.
