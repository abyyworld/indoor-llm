# Running the study

Two parts: a one-time project setup, then a short routine per participant.

---

## 1. Project setup, once

**Unity 6000.3.19f1**, URP template, platform Android. Record the exact editor version
somewhere; the write-up needs it and "Unity 6" is not reproducible.

Copy the scripts in:

```
Assets/Scripts/EmotionRooms/     all of unity/*.cs
Assets/Editor/                   unity/Editor/RoomBuilderMenu.cs
```

`RoomBuilderMenu.cs` **must** sit under a folder literally named `Editor`, or the player
build fails on the `UnityEditor` reference.

Then:

```
Emotion Rooms > Build Both Shells
Emotion Rooms > Report Dimensions
```

The second prints the matched sightlines and both floor areas. If any constraint fails
it says so in red; do not carry on past that.

---

## 2. Scene setup, once

### 2.1 The rooms

`Build Both Shells` creates `EmotionRooms` holding `Linear Room Root`, `Curved Room
Root`, `Standing Position`, and the fixed furnishing under each. Linear starts active,
curved inactive. Leave it that way; the loader toggles them.

Put the XR rig at `Standing Position`, eye height around 1.6 m.

### 2.2 The lighting rig

One light the participant reads as the room's light source. Colour **4500K neutral
white**, fixed. Position fixed. Only intensity ever changes.

### 2.3 `RoomLoader`

Empty GameObject, add `RoomLoader`, then wire:

| Field | Value |
|---|---|
| `Wall Renderers` | every surface carrying `TintableSurface`. Furniture is deliberately excluded. |
| `Room Light` | the light above |
| `Wall Textures` | one entry per material: `plaster`, `concrete`, `textile`. Name must match **exactly**. Each `greyscaleMap` must genuinely be greyscale; a coloured map fights the hue and the manipulation stops being clean. |
| `Min Intensity` / `Max Intensity` | the real intensities that 150 lx and 750 lx map to. Tune in the headset, not on a monitor. Record what you settle on: it is a study parameter. |
| `Linear Room Root` / `Curved Room Root` | the two roots |

The lux to intensity mapping is logarithmic, not linear. Perceived brightness is roughly
logarithmic in illuminance, so a linear mapping would squash 150, 300 and 500 into the
bottom of the range and make three of the four levels nearly indistinguishable.

### 2.4 The affect grid

1. Quad, 1.2 m in front of the standing position, at eye height, facing the participant.
2. Add `BoxCollider` and `AffectGrid`.
3. Two small spheres as `Hover Marker` and `Selection Marker`.
4. Grid texture: 9x9 lattice, valence left to right, arousal bottom to top, with the SAM
   anchors at the four edges. `OnDrawGizmosSelected` draws the cell lattice while the
   object is selected, so you can align the texture to the actual cells.

Leave `cells` at 9. Changing it makes the data non-comparable with anything else
measured on an affect grid.

### 2.5 The runners

Empty GameObject, add `EventLog`, `TrialRunner`, `OversightReview`, `StudyBootstrap`.

`EventLog`: set `Head Transform` to the XR camera. That is what makes head pose land in
the log.

`TrialRunner`: wire `loader`, `grid`, `events`. Exposure 20 s, transition 15 s. Set
`Session File Name` to `session.json`.

`OversightReview`: wire `loader`, `grid`, `events`, and the three panels below. Set
`Block File Name` to `oversight.json`. Leave `Re Rate Corrections` on.

`StudyBootstrap`: wire `trialRunner`, `oversightReview`, `grid`. Set `Pointer Origin` to
the controller ray transform. Leave `Auto Start` **off** so a researcher starts each
session deliberately. Leave `Chain Oversight Block` on.

### 2.6 The three review panels

World-space UI, same place as the grid, all three inactive by default.

| Panel | Content | Buttons call |
|---|---|---|
| Detection | "Does this room look consistent with **[emotion]**?" plus a confidence slider | `CommitDetection(true/false)`, slider writes `pendingDetectionConfidence` |
| Attribution | one button per variable: hue, saturation, material, roughness, lighting, plus "nothing looks off"; confidence slider | `CommitAttribution("hue")` etc., slider writes `pendingAttributionConfidence` |
| Correction | the pool values for whichever variable they picked | `CommitCorrection("240")` etc. |

Attribution and correction are only shown when the participant says something looks off.
That is deliberate: forcing an attribution from someone who noticed nothing manufactures
data and destroys the false-alarm rate, which is half of detection sensitivity.

---

## 3. Per participant

### Before

```bash
python3 -m pipeline.cli build-session \
  --batch configs/pilot_8cell.json \
  --participant p01 --seed 42 --participant-index 0 \
  --out runs/session_p01.json

python3 -m pipeline.cli export-unity runs/session_p01.json --out runs/unity_p01.json

python3 -m pipeline.cli oversight-block \
  --batch configs/pilot_8cell.json \
  --participant p01 --seed 42 --per-condition 3 \
  --out runs/oversight_p01.json
```

`--participant-index` is their position in the recruitment order, counting from 0. It
selects their counterbalancing row, so the balance holds across the sample rather than
per person. **Increment it for every participant.** Reusing 0 gives everyone the same
ordering and quietly wastes the counterbalancing.

Push both to the headset:

```bash
adb push runs/unity_p01.json /sdcard/Android/data/<bundle-id>/files/session.json
adb push runs/oversight_p01.json /sdcard/Android/data/<bundle-id>/files/oversight.json
```

Set `participantId` on `TrialRunner`, `OversightReview` and `EventLog` to `p01`.

### During

Start the app, fit the headset, then trigger `Begin Study` on `StudyBootstrap`. Eight
rooms run at 20 s each, then the review block starts automatically.

If the app is closed mid-session, everything up to the last completed trial is already on
disk. Responses are appended per trial, not held in memory.

### After

```bash
adb pull /sdcard/Android/data/<bundle-id>/files/ ./data/p01/
```

Three files: `responses.csv` (one row per trial), `oversight_responses.csv` (one row per
review trial), and `logs/*_events.csv` (one row per event). The first two are what the
analysis joins on; the third is what you go back to when a number looks wrong.

---

## 4. Check before the first real participant

**Verify in the headset, not the editor.** Everything below has an editor appearance that
differs from the device.

- [ ] The **inactive shell leaks nothing.** Stand in the linear room and look around for
      light or geometry bleeding from the curved one. They share a scene.
- [ ] **The vault reads as curved** from the standing position. If it reads as a dome or
      as a flat wall, the shape manipulation is not doing what the design assumes.
- [ ] **Walls do not clip to white at 750 lx.** If they do, lower the albedo ceiling.
      Clipping destroys the hue manipulation in exactly the brightest conditions.
- [ ] **The four lux levels are distinguishable.** Step through 150, 300, 500, 750. If
      two look the same, the intensity mapping needs retuning.
- [ ] **The grid is reachable and readable.** Every one of the 81 cells selectable
      without leaning. Check the corners.
- [ ] **20 seconds feels right.** Long enough to look around, short enough not to drag.
- [ ] **A full session end to end**, including the review block, with you as the
      participant. Time it. Then open the CSVs and confirm eight trial rows, twelve review
      rows and a plausible event log.
- [ ] **The event log's first row is `session_start`** and carries the pool values.

## 5. Two things that are easy to get wrong

**Reusing `--participant-index`.** Nothing breaks and nothing warns you. The
counterbalancing silently stops working and you find out during analysis.

**Forgetting to change `participantId` in the inspector.** The CSV appends, so a second
participant's rows land under the first one's ID and the two are unseparable afterwards.
Worth a sticky note on the laptop.
