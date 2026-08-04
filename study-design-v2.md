# Study design v2: affect induction plus agent oversight

> **Revised 2 Aug 2026.** Phase B now runs as an end-of-session block inside the same
> VR study, not as a separate online study. Rationale in section 3. This removes the
> second participant pool, the separate ethics amendment and the web front end, and it
> is a better fit for the research question because the participant has actually stood
> in the rooms before being asked to diagnose them.

Draft for Mengkai and Daniele. Supersedes nothing until approved.

The change: keep the existing study intact as Phase A, and add Phase B, in which people
judge and correct the agent's decisions. The two are not merely compatible. Phase A
produces the ground truth that makes Phase B's central question answerable, and Phase B
produces a finding Phase A cannot reach alone.

---

## 1. Why add a second phase

The current design measures whether LLM-designed rooms induce a target emotion. It does
not ask anything about the LLM as a decision-maker that people supervise. Yet the system
already logs everything an oversight study needs: a stated rationale per room, retained
rejected candidates with their specific violations, a small discrete parameter pool, and
seeded reproducible generation.

The question Phase B adds:

> When an agent making design decisions on your behalf gets one wrong, do people notice,
> can they identify which decision was wrong, and are their corrections good enough to
> use as training signal?

That is a systems-and-evaluation contribution rather than a psychology result, which is
also the framing the 23 July supervision discussion was pushing toward.

## 2. Phase A: affect induction (unchanged)

Exactly the current design. VR, four target emotions within subjects, two room shapes
between subjects, 20 second exposure, affect grid self-report.

**What it contributes to Phase B:** a measured congruence score for every configuration,
which is the ground truth against which corrections can be judged. Without it, Phase B
could only report that people agree with each other, not that they are right.

## 3. Phase B: oversight and correction

**Run at the END of the same session, after all eight VR trials are complete.**

The ordering is the entire reason this works. Asking "which variable is not consistent with the target emotion?" between
trials would tell the participant the study is about whether rooms are consistent, and every
affect rating after that would stop being a naive affective response and become an
evaluation. Priming someone to be critical also pushes valence down, so the
contamination would be directional rather than noise. By the time this block starts,
all eight ratings are collected and safe.

It also needs different stimuli from Phase A, which is the second reason the two cannot
be interleaved. Phase A needs the agent's genuine output, because the question is
whether those rooms work. Phase B needs deliberately broken rooms, because without an
injected fault there is no ground truth and attribution is unmeasurable. The same eight
rooms cannot serve both.

Rooms are shown live through the loader rather than as pre-rendered stills, since the
participant has just stood in them and a still would be a different stimulus. An
offline renderer exists (`unity/SceneRenderer.cs`) if the block is ever run online at
larger scale, but it is not on the critical path.

Each trial shows a participant the target emotion, a rendered room, and **the agent's
stated rationale for its choices**. Then three questions:

1. **Detection.** Does this room convey the target emotion? Confidence on a scale.
2. **Attribution.** If not, which variable is not consistent with the target emotion? They select a variable: hue,
   saturation, material, roughness, lighting, or nothing is wrong.
3. **Correction.** What should it have been? They pick a replacement from the same pool
   the agent chose from.

Because the correction is a pool value rather than free text, it is structured data. That
is what makes it usable as training signal, and it is a direct consequence of the
constrained-pool design already built.

### 3.1 Conditions, which is where the ground truth comes from

| Condition | What the participant sees | Ground truth |
|---|---|---|
| **Faithful** | The agent's actual output | No injected error |
| **Swapped** | One variable swapped for the value the agent chose for a *different* emotion | Error location known exactly |
| **Random** | All variables drawn uniformly from the pool | Unreasoned, though it may look plausible |
| **Rationale-mismatched** | Room from emotion X, rationale from emotion Y | Explanation is wrong, artifact is not |

The swapped condition is what makes attribution scoreable: the experiment knows which
variable was broken, so accuracy is measurable rather than inferred.

The rationale-mismatched condition is cheap to run and asks something worth knowing on
its own: **do people judge the artifact, or the explanation attached to it?** If
detection collapses when only the rationale is wrong, explanation-based oversight is
weaker than it looks, which is a result that generalises well beyond rooms.

The random condition resurrects the control arm that was built and then cancelled. Here
it earns its place: it is the floor for detection.

### 3.2 Measures

- **Detection sensitivity.** Signal detection over faithful versus swapped trials,
  giving d-prime and criterion. Criterion matters: a person who flags everything is not
  a good overseer.
- **Attribution accuracy.** Proportion of swapped trials where the participant names
  the swapped variable. Reportable per variable, so "people spot lighting errors but
  miss roughness errors" becomes a finding.
- **Correction quality.** Does the corrected configuration score better on Phase A's
  measured congruence than the swapped one did?
- **Correction convergence.** Do independent participants converge on the same
  correction? Low convergence means the signal is too noisy to train on regardless of
  whether any individual is right.

### 3.3 The question only both phases together can answer

Aggregate the corrections per cell and compare them against two references: the agent's
original choice, and the configuration Phase A measured as most congruent.

- Corrections converge **and** move toward Phase A's best: human correction is usable
  training signal. Direct, positive result.
- Corrections converge but move **away**: people are confidently and consistently wrong,
  which is a more interesting result and a genuine caution about human feedback.
- Corrections do not converge: the signal is unusable at this granularity, which is worth
  knowing before anyone builds a correction-driven training loop.

All three outcomes are publishable. The design does not depend on the result going a
particular way, which is the property a study should have before it runs.

## 4. What this does to existing problems

**The tense and depressed overlap stops being only a threat.** If those two conditions
really do produce near-identical rooms, Phase B predicts detection between them should
fail, and that becomes a measured result about the limits of the parameter pool rather
than a flaw discovered late. It still needs addressing for Phase A, but it is no longer
purely a liability.

**Excited and depressed having no literature illuminance range matters less in Phase B,**
because swap supplies ground truth by construction. You do not need a literature
band to know that a variable was swapped.

**The cancelled random arm becomes useful again** rather than being dropped work.

## 5. Cost, stated honestly

- About 10 to 15 minutes added to each session.
- No second participant pool, no separate ethics amendment, no web front end. The block
  runs in the same app immediately after the VR section.
- Statistical power is the real cost. With 20 to 30 participants at roughly 12 review
  trials each, that is 240 to 360 observations, which is workable for detection and
  attribution overall but thin for breaking attribution accuracy down per variable. If
  that analysis matters, the online version remains available as a scale-up using the
  same generator and renderer.

Nothing here requires changing Phase A's design, its literature, or its analysis plan.

## 6. Division

Phase A remains Mengkai's, unchanged: research question, literature grounding, variable
pool, sampling and aggregation, affect measurement, analysis.

Phase B would be a separate contribution: oversight design, error injection, detection
and attribution measures, the correction interface, and the training-signal analysis.

The joint analysis in 3.3 belongs to both, and is the natural shared paper.

## 7. What needs deciding before anything is built

1. Does Daniele approve adding Phase B at all.
2. Does Phase B run on rendered stills, which is the recommendation, or in VR.
3. Whether the rationale-mismatched condition is in scope. It is the cheapest condition
   and arguably the most interesting, but it adds a factor.
4. The target coordinates for the four emotions on the affect grid, which Phase A already
   needs and Phase B needs for 3.3. Still undefined anywhere.
