# Running the study

**Everything is in one window: `Emotion Rooms > Study Control Panel`, or Cmd-Shift-E.**

Five numbered steps, each turning green as it completes. Work down the list and you have
run the study correctly; you do not need the rest of this file for a normal session. The
sections below explain what each step does and what to do when one of them complains.

```
● 1. Scene                    build it, check it, see whether real furniture is loaded
● 2. Participant and stimuli  auto-suggests the next id, builds their session + block
○ 3. Consent                  opens the web form, then records that consent was taken
○ 4. Run                      Begin Study, and the withdraw button
○ 5. After                    questionnaire, then bundle every log into one file
```

Two parts below: a one-time project setup, then the per-participant routine.

---

## 1. Project setup, once

**Unity 6000.3.19f1**, URP template, platform Android. Record the exact editor version
somewhere; the write-up needs it and "Unity 6" is not reproducible.

The scripts already live in the right place:

```
unity/Assets/Scripts/EmotionRooms/   the runtime code
unity/Assets/Editor/                 the editor commands
```

Editor scripts must sit under a folder literally named `Editor`, or the player build
fails on the `UnityEditor` reference. That is already the case; do not move them.

Open `unity/` as the project in Unity Hub, then:

```
Emotion Rooms > Set Up Study Scene
```

That builds both room shells, the light, the affect grid with its markers, and wires
`RoomLoader`, `EventLog`, `TrialRunner`, `OversightReview` and `StudyBootstrap` to each
other. Doing it by hand is about forty inspector fields and a mis-wired reference does
not fail loudly: it fails as a grid that never responds, mid-session.

Both are step 1 in the control panel. `Check Scene` confirms the wiring and that a
session file exists; `Report Dimensions` prints the matched sightlines and both floor
areas. If either reports a problem, stop and fix it before going further.

### Furniture

The fixed furnishing ships as procedural placeholders, so the study runs on a clean
checkout. To use real models, import a CC0 set -- [Kenney's Furniture
Kit](https://kenney.nl/assets/furniture-kit) covers every slot and needs no attribution
-- then `Assets > Create > Emotion Rooms > Furniture Set` and drop a prefab into each
slot. Rebuild the scene and the panel's step 1 will say how many slots are still on
placeholders.

Models land on the same anchors as the placeholders and are scaled to the same
footprint, so swapping them cannot move furniture between conditions. Keep exactly one
FurnitureSet asset in the project, or participants could see different furnishing.

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

`OversightReview`: wire `loader`, `grid`, `events`, `telemetry`, and the three panels
(2.6). Set `Block File Name` to `oversight.json`. Leave `Re Rate Corrections` on.

`StudyBootstrap`: wire `trialRunner`, `oversightReview`, `grid` and the three panels. Set
`Pointer Origin` to the controller ray transform. Leave `Auto Start` **off** so a
researcher starts each session deliberately. Leave `Chain Oversight Block` on. Set
`Researcher Initials`, and `Consent Form Version` to whichever paper form was approved.

### 2.6 The three review panels

Generated by **Set Up Study Scene** -- nothing to build by hand. Each is a `QuestionPanel`:
world-space collider cells driven by the same pointer ray as the affect grid, inactive
until the block asks the question.

| Panel | Content | Field it writes |
|---|---|---|
| Detection | "This room was built to feel **[emotion]**. Does anything about it look wrong for that?" -- yes / no, plus a 5-step confidence strip | `pendingDetected`, `pendingDetectionConfidence` |
| Attribution | one cell per attributable variable (`hue`, `saturation`, `texture`, `roughness`, `brightness`) plus `nothing_wrong`; confidence strip | `pendingAttributedField`, `pendingAttributionConfidence` |
| Correction | every pool value, narrowed at show time to the attributed variable's values | `pendingCorrectedValue` |

The cell labels are the field names and pool values verbatim, so a chosen answer is
scored and applied without a translation step. They come from the generated
`PoolConstants`, so a pool edit moves the buttons too.

Attribution and correction are only shown when the participant says something looks off.
That is deliberate: forcing an attribution from someone who noticed nothing manufactures
data and destroys the false-alarm rate, which is half of detection sensitivity. Picking
`nothing_wrong` after saying something looked off is recorded as such and skips the
correction step rather than forcing a value.

Confidence is set by touching the strip; that does not submit. Only an option cell
submits, and both panels ignore input for 0.4 s after appearing so a click aimed at the
previous screen cannot fall through into an answer.

### 2.7 Consent and withdrawal

**The consent form stays on paper, outside the app.** A headset is the wrong place to
read one: the participant cannot keep a copy, cannot re-read it later, and cannot ask a
question without taking the headset off. What the software owes the ethics record is a
timestamped statement that consent was taken before any stimulus was shown.

So, with the headset still off: take the paper form, then
**Emotion Rooms > Confirm Consent Taken**. That appends a row to `consent_log.csv` and
unlocks `Begin Study` -- which refuses to run until it is done.

To end a session early, hold **F12** (`Withdraw Key`) for 1.5 s, or use the
`StudyBootstrap` context menu. Everything recorded so far is kept and marked
`withdrawn`; nothing is deleted, because whether a partial session is usable is an
analysis decision. If the participant asks for their data to be destroyed, that is a
separate deliberate act on the files.

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

The control panel does all of the above: it suggests the next unused id, runs the three
commands, and sets the id everywhere it is needed. `StudyBootstrap` owns the id and
pushes it into `TrialRunner`, `OversightReview`, `EventLog` and `StudyTelemetry`, so
there is no longer a field to type it into four times.

**Never reuse an id.** A second session under the same id appends to the first one's
files and neither is recoverable afterwards. The panel suggests one past the highest it
finds, which is why you should let it.

### During

Take consent on paper, then **Emotion Rooms > Confirm Consent Taken** before the headset
goes on. Fit the headset, then trigger `Begin Study` on `StudyBootstrap`. Eight rooms run
at 20 s each, then the review block starts automatically. Hold **F12** for 1.5 s if the
participant wants to stop.

If the app is closed mid-session, everything up to the last completed trial is already on
disk. Responses are appended per trial, not held in memory.

### After

```bash
adb pull /sdcard/Android/data/<bundle-id>/files/ ./data/p01/
```

Four files:

| File | Grain |
|---|---|
| `responses.csv` | one row per trial |
| `oversight_responses.csv` | one row per review trial |
| `logs/*_events.csv` | one row per discrete event |
| `logs/*_telemetry.csv` | one row per 20 Hz sample **and** per event, every column on every row |
| `consent_log.csv` | one row per consent confirmation or withdrawal, across participants |

The first two are what the analysis joins on. The telemetry file is the dense record --
head pose, phase, trial, segment and the current response state at every sample, so any
question about what someone was looking at when they answered is answerable after the
fact. `consent_log.csv` is the ethics audit trail and is the one file that is **not**
per-participant, so pull it once and keep it.

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
