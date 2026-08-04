# Build decisions (Akbar)

Decisions that are mine to make, so nothing sits waiting on the wrong person. Each one
records what was chosen, why, and what it costs or unblocks. Where a decision rests on an
assumption someone else has to confirm, that is called out rather than buried.

Settled 30 Jul 2026. Change any of these deliberately, not incidentally, and note the date.

---

## 1. Engine: Unity 6 LTS, exact version pinned

Pin the precise version in the project and record it here once the project is created,
because "Unity 6" is not reproducible and the thesis needs to state the build environment.
LTS rather than the current tech stream: this project runs participants, so a mid-study
editor upgrade is a risk with no upside.

## 2. Render pipeline: URP

Not a real choice, but worth stating the reasoning. HDRP is not viable on standalone
headset hardware, and the built-in pipeline is effectively deprecated for new XR work. URP
also exposes the per-material properties the loader manipulates.

`RoomLoader.cs` already resolves shader property names at runtime rather than hardcoding
`_Color` or `_BaseColor`, so it works under URP without modification. That was written
before the pipeline was chosen and turned out to be the right call.

## 3. Headset: Quest 3, standalone build

**This carries an assumption that needs the supervisor's sign-off.** The illuminance bands
come from Mostafavi et al., who ran on a Quest 2. Peak display luminance differs between
Quest 2 and Quest 3, so a scene authored to "150 lx" does not necessarily present the same
retinal illuminance on both. Borrowing their bands across devices is a stated assumption,
not a free move.

Two ways out, and the choice is the supervisor's:

- run on Quest 2 to match the reference study, accepting the older display, or
- run on Quest 3 and treat the lux figures as nominal scene-authoring targets rather than
  measured photometric quantities, and say so in the limitations.

Defaulting to Quest 3 with the nominal reading, since the second option is honest and does
not depend on hardware availability. Revisit if the supervisor prefers device matching.

## 4. Illuminance is treated as nominal until proven otherwise

A Unity light intensity is not a photometric quantity and a lux meter cannot be held up to
a headset lens in any straightforward way. Until Mostafavi's methods section is read and
shows otherwise, the pipeline treats a lux value as **an authoring target**: the light is
configured so the rendered scene corresponds to that band, and the mapping actually used
is recorded per scene.

Consequence for the write-up: the manipulation check can report that the intended band was
applied, but not that a physical illuminance was achieved. That is a limitation to state,
not a hole to hide. If Mostafavi did calibrate photometrically, this decision reverses and
the calibration table becomes a measurement exercise instead.

## 5. Scene structure: one scene, two shell roots

Both room shells live in a single scene as two roots that the loader toggles, rather than
two separate scenes.

- The loader already supports `Linear Room Root` / `Curved Room Root`.
- The fixed furnishing set is identical across shapes by construction rather than by
  discipline, which is what brief §3 asks for.
- No scene load between trials, so transitions stay inside the ~15 s budget.

Cost: one heavier scene, and the inactive shell has to be genuinely inactive so it cannot
leak light or geometry into the active one. Verify this in the headset, not the editor.

## 6. Config delivery: `adb push` to `Application.persistentDataPath`

Already what `unity/README.md` documents and what `LoadBatchFromFile` expects, so nothing
changes. Sideloading a session file beats embedding configs in the build, because a
mis-generated session is then a file swap rather than a rebuild.

## 7. Trial runner: in Unity, C#, hanging off `RoomLoaded`

The Affect Grid has to be answered inside the headset, so the runner belongs in the engine
rather than in an external process. An external runner would need clock synchronisation
with the scene and would force the participant to remove the headset between trials, which
would wreck the affect measurement it exists to collect.

`RoomLoader` already fires `RoomLoaded` as the hook and deliberately does nothing else, so
this attaches without touching the loader.

Blocked on: the instrument's exact layout, and on ethics approval before it can be run on
anyone. Not blocked on Mengkai's pool values, so it can be built in parallel with them.

## 8. Furniture placeholders: CC0 assets only

Poly Haven, Kenney, or similar CC0 sources rather than paid Asset Store models. Brief §3
wants these treated as easily swappable placeholders, and CC0 means any figure in the
thesis or a paper is redistributable without a licence question. Paid assets would create
a permissions problem at exactly the wrong moment.

---

## What these do not resolve

- The lux to light-intensity numbers, which wait on decision 4 above and on Mengkai's
  final illuminance bands.
- The material system's roughness tiers, which wait on her confirming `material` is
  `{rough, smooth}` and nothing finer.
- Ethics approval, which gates running any participant and gates nothing else, so it
  should be in flight before the build is finished rather than after.
