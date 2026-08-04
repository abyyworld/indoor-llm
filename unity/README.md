# Unity loader

Three files, no dependencies beyond Unity itself:

| File | Role |
|---|---|
| `PoolConstants.cs` | **Generated** from `pipeline/pools.py`. Do not hand-edit. |
| `RoomConfig.cs` | The JSON contract plus engine-side pool validation. |
| `RoomLoader.cs` | Reads a config and builds the room. |

Copy all three into `Assets/Scripts/EmotionRooms/` in your Unity project. They are
plain C# (namespace `EmotionRooms`) and work with the built-in render pipeline, URP or
HDRP - the loader resolves the shader property names at runtime.

Regenerate the constants whenever a pool changes:

```bash
python3 -m pipeline.cli emit-unity-pools --out unity/PoolConstants.cs
```

`tests/test_pipeline.py` fails if the committed file is stale, so the engine and the
pipeline cannot silently disagree about what a legal value is.

---

## Scene setup

Build the room by hand first. The loader only ever touches wall colour, wall texture
and light intensity - everything else in the scene is yours and it must stay yours.

1. **Build the room geometry.** Walls, floor, ceiling, furniture, props, and the
   participant spawn point. Fix the dimensions and the layout now; they are not
   variables (design-spec.md section 2).
2. **One material on the walls.** Every wall renderer should start from the same
   material. The loader instantiates a copy at runtime and assigns it to all of them,
   so the project asset is never written to.
3. **One light.** A single light the participant reads as the room's light source.
   Leave its colour white - the loader forces white anyway, because hue lives on the
   walls only.
4. **Add `RoomLoader`** to an empty GameObject and wire the inspector:
   - `Wall Renderers` - every renderer carrying the wall material.
   - `Room Light` - that light.
   - `Wall Textures` - one entry per value in the texture pool
     (`plaster`, `brick`, `wood_grain`, `fabric_weave`). The `name` must match the pool
     value **exactly**. Each `greyscaleMap` must be an actual black-and-white texture:
     a coloured map will fight the hue and the manipulation stops being clean.
     `OnValidate` warns in the inspector if a pool value has no entry.
   - `Min Intensity` / `Max Intensity` - the real light intensities that normalised
     brightness `0.2` and `1.0` map to. Tune these in the headset, not on a monitor.
     This mapping is a study parameter: record the values you settle on.

     **This contract is changing.** Mengkai's template specifies brightness as an
     illuminance value in **lux** with a band per emotion (calm ~45-150, tense ~670-780,
     excited/depressed unlocked), not a normalised 0.2-1.0. So this pair becomes a lux to
     intensity mapping. Treat lux as an authoring target rather than a measured quantity
     until that is settled - see [../build-decisions.md](../build-decisions.md) §4.
   - `Linear Room Root` / `Curved Room Root` - only if shape is part of your design.
     Leave both empty otherwise and the loader will not touch the scene's geometry.
5. **Assign a config.** Drag `configs/handwritten_calm_001.json` into the project and
   set it as `Config Asset`.

## Proving it works (design-spec.md section 8.3)

Do this before any LLM is involved.

1. Press play with `configs/handwritten_calm_001.json` assigned. You should get a
   low-saturation blue plaster room at mid brightness, and one console line:
   `[RoomLoader] Loaded handwritten_calm_001 [calm/handwritten] hue=210 ...`
2. Switch `Config Asset` to `configs/handwritten_smoke_batch.json`, tick
   `Config Asset Is Batch`, and use the component's context menu (⋮ →
   **Load Next Room In Batch**) to step through all four. That batch is built to cover
   every texture, both brightness extremes, both shapes and hue 0. If all four look
   right, the loader is done.
3. Switch to `configs/INVALID_do_not_ship.json`. Every room in it **must** fail. You
   should see a `RoomConfigException` naming the offending field and the legal pool,
   and no room should be built. If anything loads, the gate is broken - fix that
   before running a participant.

## Runtime loading on the headset

`Config Asset` is convenient in the editor but a build usually reads a session file
written by the pipeline:

```csharp
var loader = GetComponent<RoomLoader>();

// Validate the whole session up front. A batch with one bad room throws here,
// before the participant is in the headset, rather than mid-session.
loader.LoadBatchFromFile(Path.Combine(Application.persistentDataPath, "session_p01.json"));

loader.RoomLoaded += config => StartCoroutine(RunTrial(config));  // 30 s + questionnaire
loader.LoadBatchIndex(0);
```

Export a session for the engine with:

```bash
python3 -m pipeline.cli export-unity runs/session_p01.json --out session_p01.json
```

That strips pipeline-only fields and rewrites each `id` to the unique `trial_id`, so
the id the engine loads is the same string the response log joins on.

## What the loader deliberately does not do

- **Trial timing, the questionnaire, and response logging.** Not built. `RoomLoaded`
  is the hook; the 30 s exposure and the valence–arousal form belong to your study
  runner (design-spec.md section 6).
- **Anything to the geometry**, beyond activating a shape root you assigned yourself.
  The dimensions below are yours to build by hand; the loader must never move them.
- **Colouring the light.** The light is neutral white and only its intensity varies.
  Confirmed by Mengkai - scene brief §4 and §8: hue applies to wall/floor material
  only, never to the light. `ApplyLight` is correct as written.

## Researcher-set geometry (scene brief §2, confirmed 30 Jul 2026)

Build these by hand, once per shape root. Both conditions deliberately match on
everything a standing participant could perceive, and differ only in floor area.

| | Linear | Curved |
|---|---|---|
| Floor plan | plain cuboid | straight foyer + semicircular vault |
| Entrance-wall width | 4.2 m | 4.2 m (= vault diameter) |
| Depth | 4.3 m | foyer 2.2 m + vault radius 2.1 m |
| Floor area | ≈18.1 m² | ≈16.2 m² (the one intended difference) |
| Ceiling height | 2.4 m | 2.4 m |
| Standing position | 1.3 m from entrance, centred | identical |
| → side wall | 2.1 m | 2.1 m (matched) |
| → facing wall / vault apex | 3.0 m | 3.0 m (matched) |

The area difference is intentional: width, depth and both sightlines cannot all be
matched at once with only two free parameters, so area was the one sacrificed. Do
not "fix" it. Ceiling height is settled at 2.4 m per UK residential practice.

**No windows, in either shell.** Daylight would contaminate the illuminance variable the
study manipulates (23 Jul meeting note). The curved shell's straight foyer is where a door
or window could plausibly sit, which is exactly why it must not have one.

Both shells live in **one scene** as two roots the loader toggles, not two scenes, so the
furniture set is identical across shapes by construction. The inactive shell must be
genuinely inactive: verify in the headset that it leaks neither light nor geometry.

The curved room keeps a short straight foyer rather than curving throughout, because
a participant can turn to face any direction and a half-open shape has no standable
real-world equivalent. The vault is slightly under half the total depth.

### Fixed furnishing (scene brief §3)

Identical in both shapes and in every emotion scene - never manipulated. Present so
the room does not read as an empty geometric box.

Final list, confirmed by Mengkai 2 Aug 2026:

- Three-seat sofa against the far wall, centred on width (following the curve in the
  curved condition).
- Armchair, offset to one side and angled toward the table, so the sofa keeps the
  symmetric position the brief specifies.
- Coffee table directly in front of the sofa, with a teacup on it.
- Rug under the table and the sofa's front portion.
- Bookshelf against a side wall, deliberately clear of the facing sightline the two
  shapes are matched on.
- Two wall art pieces above and behind the sofa, symmetric about centre.

Item selection is now settled, though the assets are still placeholders. The brief asks that these stay easily
swappable placeholder assets rather than having this particular furniture geometry
hard-coded into scene logic. The LLM decoration whitelist that would have added
cushions, lamps, plants and so on is **dropped** - do not build it.
