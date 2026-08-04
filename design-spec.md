# Emotion-Conveying Interior Appearance via LLM - Design Spec

Working notes reconstructed from the supervisor meeting. Sections marked
**[MEETING]** are what was actually said; **[PROPOSED]** are my defaults to be
confirmed; **[OPEN]** needs a decision from Mengkai / the supervisor.

---

## 1. Core method

**[MEETING]** Do *not* ask the LLM to "generate a sad room" freely. Instead:

1. You (the researchers) define a **finite, discrete pool of legal values** for
   each scene parameter.
2. The LLM's only job is to **select one value per parameter** from those pools
   to hit a target emotion.
3. The LLM emits a **structured config file** (JSON preferred over CSV).
4. A **small Unity loader** reads that config, looks up the corresponding
   assets, and builds the room.
5. The participant sees the built room in VR and self-reports affect.

Rationale from the meeting: unconstrained generation gives an effectively
infinite space you can never sample or replicate. Discretising creates a finite
set you can enumerate, sample from, and re-run. Prompt constraints are the
mechanism for restricting the LLM's freedom ("with a prompt you can restrict
this freedom").

---

## 2. What the LLM controls vs. what is fixed

### LLM-controlled (5 variables, final 3 Aug 2026)

| Variable | Applies to | Notes |
|---|---|---|
| **Hue** | Wall colour | HSV space. Vary **hue** and **saturation** only - do not expose full RGB. **[MEETING]** |
| **Material / texture** | **Walls only**, and the textures are **greyscale (black & white)** so the hue tints them. **[MEETING]** | 1–2 texture choices was the suggestion; a small handful is fine. |
| **Brightness** | The **light source**, not the surfaces. Luminosity/intensity. **[MEETING]** | Discretise, e.g. 5 levels, instead of a 0–255 / 0–1 continuum. **[MEETING]** |

### Fixed by you, never touched by the LLM **[MEETING]**

- Furniture layout, object positions, room dimensions and shape.
- Camera / participant spawn point.
- Everything not in the table above.

**[RESOLVED 30 Jul 2026]** The abstract claims **room shape (linear vs. curved)** is a
manipulated moderator. The meeting said shape is *fixed by you*. Mengkai confirms both
readings are compatible: shape is a researcher-fixed experimental factor, never an LLM
output, and does not enter the variable pool. It is **within-subjects** as of 2 Aug 2026:
every participant sees all 8 scenes (4 emotions x 2 shapes). Mengkai moved it there for
power, since within-subjects needs roughly half the N, and an interaction is the right
thing to test within subjects. So the abstract's "manipulated moderator" is real in the
sense that the researchers manipulate it, not the LLM.

**[RESOLVED 30 Jul 2026]** Does hue apply to the wall albedo, the light colour, or both?
The cleanest reading was the right one: **walls carry hue+saturation, lights carry
intensity only** (neutral white). Confirmed in scene brief §4 and §8 - hue is applied to
wall/floor material only, fixture positions are fixed, and colour temperature must not be
tied to the intensity parameter.

---

## 3. Parameter pools **[FINAL 3 Aug 2026]**

Every value below is Mengkai's. `configs/pools.json` is the source; this is a copy.

```
hue          : 10 values, (Munsell-calibrated), (0/30/60/90/120/180/240/270/300/330)
saturation   : 2 values  {0.20, 0.40}
value        : fixed 1.00  (HSB "Value" channel of wall colour - distinct from the "brightness" row below,
               which is light-source intensity, and distinct from "texture" below, which is material/roughnes
brightness   : 4 values  {150, 300, 500, 750} LUX (light intensity). FINAL, 3 Aug 2026.
               ONE pool shared by all four emotions, not a band per emotion, so there is
               no per-emotion illuminance expectation and no manipulation check on it.
texture      : 3 values  {plaster, concrete, textile}  (greyscale maps, hue-neutral)
roughness    : 2 values  {rough, smooth}. FINAL, 3 Aug 2026.
```

Design space = 10 x 2 x 4 x 3 x 2 = **480 distinct rooms**. Finite, enumerable,
reproducible - exactly the property the supervisor was after. Shape, if it is
a factor, doubles this to 960 but you will only ever *run* a tiny subset.

---

## 4. LLM output format

JSON. One record per candidate room. Sketch:

```json
{
  "id": "calm_007",
  "target_emotion": "calm",
  "hue": 240,
  "saturation": 0.2,
  "brightness": 100,
  "texture": "plaster",
  "rationale": "Low-saturation cool blue with soft even light reads as restful."
}
```

Keep `rationale` - it is free to collect, it is qualitative data for the paper,
and it lets you check whether the LLM is reasoning or pattern-matching.

**Validate every field against the pools before it reaches Unity.** An LLM will
eventually emit `"hue": 217` or `"texture": "velvet"`. Reject and re-ask; do not
let malformed configs silently reach a participant.

---

## 5. Generating and filtering candidates **[MEETING]**

- Ask the LLM for **many** candidates per target emotion - the numbers floated
  were on the order of 50–100. There is no single correct "excited" room, so
  sample the distribution rather than trusting one answer.
- Then **rank / filter** them. The supervisor explicitly raised weighting
  solutions and discriminating good from bad, including a **human-in-the-loop**
  or community/crowd judgement step, rather than assuming every LLM output is
  usable.
- **Include non-emotional / neutral control rooms.** **[MEETING]** Explicitly
  ask the LLM for rooms that are *not* designed to convey an emotion. Then
  participants have something to rate as neutral, and "no emotion here" becomes
  a valid, recordable response instead of forced choice. This is your baseline.

**[PROPOSED]** Add a **random-selection control**: rooms whose parameters are
drawn uniformly from the pools. Without it, "the LLM steers emotion" is not a
falsifiable claim - you cannot tell LLM competence apart from "any blue dim room
feels calm". This is cheap to add and is probably the difference between a
descriptive paper and one with a result.

---

## 6. Study protocol **[MEETING]**

- **20 s** exposure per room (Mengkai, 1 Aug; was 30 s). Participant looks around / moves a little, then
  stops.
- Questionnaire after each room: **valence–arousal** self-report, plus which
  emotion (with a neutral / none option).
- **Total session: 30–45 minutes maximum**, including intro, consent, VR
  training, and debrief.
- **Calibrate the number of rooms to that budget.** Rough arithmetic:
  20 s exposure + ~45 s questionnaire + ~15 s transition = **1.33 min/room**.
  The design is 4 emotions x 2 shapes = **8 rooms per participant**, about 11 min
  of trial time, comfortably inside the budget.

---

## 7. Prompting notes **[MEETING]**

- Tell the LLM it is authoring for **Unity / a 3D environment**, so it knows the
  output becomes a real 3D scene.
- You can additionally request a **2D representation** (e.g. a floor-plan or
  swatch sketch) as a sanity check on what the model thinks it is describing.
- Use the prompt to forbid everything outside the pools.

---

## 8. Immediate next steps

1. Confirm the **[OPEN]** items above with Mengkai (especially room shape).
2. Freeze the parameter pools and the JSON schema - everything downstream
   depends on them.
3. Build the Unity loader against a **hand-written** config file first. Prove
   the room builds before any LLM is involved.
4. Only then wire up generation, validation, and the filtering pass.
5. Strip the template boilerplate out of the Overleaf draft and rewrite the
   abstract to match the design that actually got agreed.
