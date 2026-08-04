# Filming plan

Two recordings. The terminal one is scripted end to end; the Unity one has to be driven
by hand in the editor.

Ordered by how well each reads on camera rather than by how the pipeline runs, so if you
only film the first three you still have the strongest material.

---

## Recording 1: the pipeline (about 4 minutes)

```bash
./demo.sh          # pauses between sections, press enter to advance
./demo.sh --auto   # runs straight through, no keypresses
./demo.sh 3        # one section only
```

Runs offline. No API key, no network, no headset.

Record with `Cmd+Shift+5`, or `asciinema rec` if you want something embeddable on a page.
Use a large font and a plain terminal profile; the output is dense and defaults are too
small to read once the video is compressed.

| # | Section | What lands |
|---|---|---|
| 1 | The gate | A valid config passes; a broken one fails with the exact field, the legal pool, and exit code 1. |
| 2 | Pools are data | One number changes in a JSON file and the generated C# follows. No code edited. |
| 3 | Separability | A good design passes; a design where two emotions collapse is named and rejected. |
| 4 | Aggregation | Per-variable winners invent a room nobody generated. The medoid cannot. |
| 5 | Counterbalancing | 210 adjacent pairs under plain randomisation, 0 under the constrained scheme, over 200 participants. |
| 6 | Oversight trials | Errors injected on purpose, with the swapped variable and where it was borrowed from recorded. |
| 7 | End to end | Config in, participant trial list out, re-validated. |
| 8 | The suite | 156 tests, offline. |

**The three that carry the most weight**, if you have to cut: 3, 4 and 6. They each show
a decision that a reviewer would otherwise have to take on trust, and each is under
twenty seconds of screen time.

---

## Recording 2: Unity (about 2 minutes)

Not scriptable. Drive it in the editor.

1. **`Emotion Rooms > Build Both Shells`.** Both rooms appear, generated rather than
   modelled. Show the hierarchy so the furniture and the two shape roots are visible.
2. **`Emotion Rooms > Report Dimensions`.** Prints the matched sightlines and the two
   floor areas with the constraint checks passing. This is the one that shows geometry
   is verified rather than eyeballed.
3. **Toggle between the shape roots** so the linear and curved shells are seen back to
   back from the standing position.
4. **Press play with `configs/pilot_8cell.json`** and step through a few rooms so the
   colour, material and lighting changes are visible.

If a headset is available, record from the device rather than the editor. Editor footage
of VR always reads flat.

---

## What the pilot config is, and is not

`configs/pilot_8cell.json` is eight cells covering every emotion and shape, all inside
the final pools, checked for separability. It exists so scenes, headset checks and demos
can proceed without waiting.

**It is not study data.** The values are hand-chosen, not the output of Mengkai's
sampling and medoid aggregation. She is running that herself because her thesis has to
describe a process she executed. Nothing collected against this file is a result, and it
must not stand in for her config. The file says so in its own header, so a stray copy
cannot be mistaken later.

---

## If the demos are going in a submission

The pipeline recording is the stronger artefact, because every section shows something
being *caught* rather than something merely working. A validator that rejects a bad
config, a check that names two emotions the design cannot separate, an aggregation that
refuses to invent a room: those are claims a reviewer can verify in seconds.

The Unity recording is context. It shows the study is real rather than a diagram, which
matters, but it does not carry an argument on its own.

Worth stating plainly in any caption: the illuminance pool is shared across all four
emotions, so there is no per-emotion illuminance manipulation check. The system reports
those cells as "no locked range" rather than as passes, which is visible in section 1.
Better to say it than have someone notice.
