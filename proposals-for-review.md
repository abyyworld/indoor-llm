# Proposals for review

Drafted by Akbar so the build is not blocked while items are outstanding. **None of
this is decided.** Each item says who owns the final call and what it would take to
accept it.

Read the status tag on each. They are not the same:

| Tag | Meaning |
|---|---|
| **PROPOSAL** | A real suggestion, mine to make, adopt or reject on the merits. |
| **PLACEHOLDER** | Engineering stand-in so code runs. Not research-grounded. Must be replaced. |
| **TEST DATA** | Synthetic. Must never reach a participant. |

---

## 1. Illuminance ranges per emotion - PLACEHOLDER

**Owner: Mengkai.** Due Monday.

I have not invented these and they must not be cited. The earlier 45-150 / 670-780
figures were retracted on 1 Aug as exploratory, and the thesis will present the final
ranges as literature-derived, so numbers of mine appearing there would be a serious
problem regardless of how reasonable they look.

What is in `configs/pools.json` is a placeholder spanning a plausible domestic range so
the lighting rig runs and the scenes can be looked at. Basis, stated plainly so nobody
mistakes it for grounding: ordinary interior lighting practice puts residential ambient
somewhere around 50-150 lx, task lighting 300-500 lx, and bright commercial interiors
higher. That is background knowledge about buildings, **not** evidence about emotion.

    calm       placeholder, dim end
    depressed  placeholder, dimmest
    tense      placeholder, bright end
    excited    placeholder, brightest

Two things need to come with the real ranges:

- **Whether excited and depressed get bounds at all.** They have no literature range.
  Either they are bounded by design decision, or they stay unlocked and are reported as
  exploratory, never as failing to match.
- **Whether calm and tense are forced to their respective bands in the prompt.** If both
  are offered side by side without a mapping, the model may take the same band for both.

## 2. Roughness levels - PLACEHOLDER for the values, PROPOSAL for the structure

**Owner: Mengkai** for the levels and their grounding. Due Monday.

**Structure I propose**, which I do think is right: two levels, `rough` and `smooth`, as
a categorical variable rather than a continuous scalar. Reasons:

- It matches what the formative work used, so it stays comparable.
- Two levels crossed with three material types is six surface presets, which is
  buildable. A continuous scalar multiplies the asset and calibration work with no
  corresponding gain in what the analysis can resolve.
- Categorical suits the chi-square treatment already planned for the discrete variables.

**Placeholder values** in Unity smoothness terms, purely so the material system runs:
rough around 0.15, smooth around 0.75. These want checking in the headset, because
perceived roughness at 2 m under dim light is not the same as it looks on a monitor.

**What is genuinely open and worth a literature answer**: whether gloss is controlled
separately. A rough surface and a matte surface are different things, and if roughness
and specularity move together the manipulation confounds two properties at once.

## 3. Furniture list - PROPOSAL

**Owner: Mengkai**, due at the weekend, but this one I am happy to just propose because
the brief already calls the items provisional and asks that they stay swappable.

Keep the four from brief §3, and specify them tightly enough to source:

| Item | Spec | Why |
|---|---|---|
| Three-seat sofa | ~2.1 m wide, low back (under 0.85 m), plain upholstery, no pattern | Low back keeps sightlines to the wall open, which is where the manipulation lives. Pattern would compete with the hue variable. |
| Coffee table | ~1.1 x 0.6 m, simple rectangular, matte | Neutral, no reflective top; gloss would pick up the light colour. |
| Rug | ~2.4 x 1.6 m, plain, single tone | Under table and sofa front per the brief. Plain for the same reason as the sofa. |
| Wall decoration | ~0.9 x 0.6 m, abstract or monochrome, non-representational | A representational picture introduces semantic content that could carry its own affect. |

**The constraint I would put on all four**: neutral mid-grey, matte, no pattern and no
strong colour of their own. Furniture is fixed across every condition, so anything with
its own hue competes with the variable being manipulated. Sourced CC0 so nothing in the
thesis or a paper has a licensing question attached.

## 4. Aggregation method - PROPOSAL

**Owner: Mengkai.** No date given, and it gates producing any scene at all, which is why
I am proposing something concrete rather than waiting.

**Her stated constraint**, 31 Jul, and it is the right one: whatever reaches the build
must be *"one the model actually produced as a single coherent output, not something
reconstructed by combining the winning value from each variable independently"*. That
rules out per-variable majority vote, because taking the modal hue and the modal
illuminance separately can produce a pairing no sample ever generated.

**Proposal: medoid selection.** Of the N samples for a cell, keep the single real sample
that is closest to the middle of them all.

1. Encode each sample as a point: categorical variables (hue category, material type,
   roughness) contribute 0 if equal and 1 if not; continuous ones (saturation,
   illuminance) contribute normalised absolute difference.
2. Compute the distance from every sample to every other.
3. Keep the sample with the smallest total distance to the rest.

Why this fits:

- The output is always a real sample, so her constraint holds by construction.
- It is a standard, citable choice for mixed categorical and continuous data, so it
  survives a methods question.
- It yields consistency statistics for free: the medoid's mean distance to the others is
  a spread measure, and per-variable mode share still gets reported alongside.
- Ties break deterministically by sample index, so it is reproducible.

**Implemented** in `pipeline/aggregate.py` with tests, so it can be tried on real samples
rather than argued about in the abstract. Swap the distance weights if the variables
should not count equally, which is a judgement call I have deliberately left open.

**Alternative if this is rejected**: pick the sample closest to the target emotion's
coordinate on the affect grid. That is more directed, but it needs those coordinates to
exist first, which they currently do not (see 6).

## 5. The 8-cell config file - TEST DATA only

**Owner: Mengkai**, due Thursday. She has stated she wants to run the sampling herself
so she can describe it firsthand in the write-up, which is a good reason and I am not
going to pre-empt it.

`configs/handoff_SYNTHETIC_test_only.json` exists purely so the loader, the validator and
the scene switching can be exercised end to end before real values arrive. It is marked
in three places and one of its cells deliberately fails validation, so it cannot be
mistaken for the real file or quietly shipped.

## 6. Affect Grid instrument - PROPOSAL

**Owner: Mengkai.** No date, and it is the largest remaining piece of software.

Her description is Affect Grid structure with SAM pictorial anchors. That is a sound
compromise but it is not yet buildable, so here is a concrete spec to react to.

**Layout**: a 9 x 9 grid, which is the original Affect Grid resolution (Russell, Weiss
and Mendelsohn, 1989). Horizontal axis is valence, unpleasant on the left to pleasant on
the right. Vertical is arousal, sleepy at the bottom to high arousal at the top. One
click, one response.

**Anchors**: SAM-style figures rather than words, at the two ends of each axis, plus the
four corners labelled as in the original (stress top-left, excitement top-right,
depression bottom-left, relaxation bottom-right). Using pictures avoids translating
emotion words for a multilingual participant pool, which is the main reason to prefer SAM
anchors at all.

**Scoring**: a response is a coordinate pair, each on 1 to 9, stored raw. Do not collapse
to a single distance at collection time; that is an analysis decision and the raw pair
should survive in the log.

**Presentation**: world-space canvas about 1.2 m in front of the participant at eye
height, appearing after the room fades out so the room is not being rated from memory
while still visible. Controller-ray selection with a visible cursor, confirm on release,
one response per trial, no going back.

**What I need from Mengkai before building it:**

1. Confirm 9 x 9, or give the resolution you want.
2. **The target coordinates for the four emotions.** The primary analysis is distance to
   the target emotion's coordinate, and those coordinates are not defined anywhere I can
   find. Without them there is no primary analysis. This is the single most important
   item on this page.
3. Whether a practice trial is included so participants meet the instrument before it
   counts.
4. Whether response time is recorded.

---

## Summary of who owns what

| Item | Status | Owner | Blocking |
|---|---|---|---|
| Illuminance ranges | PLACEHOLDER | Mengkai | Final scenes |
| Roughness levels | PLACEHOLDER | Mengkai | Material presets |
| Furniture list | PROPOSAL | Mengkai | Final assets only |
| Aggregation method | PROPOSAL, implemented | Mengkai | Producing any scene |
| 8-cell config | TEST DATA | Mengkai | Final scenes |
| Affect Grid | PROPOSAL | Mengkai | The trial runner |
| Emotion target coordinates | **MISSING ENTIRELY** | Mengkai | **The primary analysis** |
