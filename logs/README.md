# Event logs

Written by `unity/EventLog.cs` to `Application.persistentDataPath/logs/` on the headset.
Pull them off with:

    adb pull /sdcard/Android/data/<bundle-id>/files/logs ./logs

## Two kinds of file, both wanted

`*_events.csv` is this. One row per thing that happened, from before the participant
does anything. It is deliberately verbose and wide.

`responses.csv` and `oversight_responses.csv` are the summaries, one row per trial,
answer only. Those are what the analysis joins on.

The summary tells you what they answered. The event log tells you what happened on the
way to it, which is what you need when a number looks wrong months later.

## Rules

- **First row is `session_start`, before anything happens.** It carries the pool values,
  Unity version and device the session ran under. That is the thing you most want and
  least often have when a result looks strange later.
- **Every row has both `t_utc` and `t_ms`.** Analyse on `t_ms` (milliseconds since
  session start); use `t_utc` to line up against anything external.
- **One row per discrete change.** `grid_hover` fires when the hovered cell changes, not
  every frame, so hesitation is visible without flooding the file.
- **Continuous things are sampled at a fixed rate AND on significant change.**
  `head_pose` at 10 Hz plus an extra row when the head turns more than 5 degrees between
  samples, so a still head is cheap and a fast turn is not smoothed away.
- **Wide and sparse.** Most columns are empty on most rows. A wide CSV that never loses
  a field beats a tidy one that cannot represent an event.
- **Append and flush as it goes.** A crashed session keeps everything up to the crash.

## Joining

`trial_id` is the join key, not `room_id`. Room ids repeat: the same room appears in
Phase A and again, possibly swapped, in Phase B.

`phase` is `A` for the VR trials and `B` for the end-of-session oversight block.

## The example file

`EXAMPLE_p01_20260802_140311_events.csv` is **simulated, not recorded** - there is no
headset attached to the machine that generated it. Its column order and event vocabulary
are read directly out of `EventLog.cs`, so the shape is exact even though the values are
invented. Delete it once you have a real one.
