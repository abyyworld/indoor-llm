#!/usr/bin/env bash
# Build one participant's session + oversight block and drop them where the Unity
# editor will find them. Editor only -- on the headset use adb push (RUNBOOK section 3).
#
#   ./test-participant.sh p01 42 0
#
set -euo pipefail

PARTICIPANT="${1:-p00}"
SEED="${2:-42}"
INDEX="${3:-0}"
BATCH="${BATCH:-configs/study_8cell.json}"

# Where the app reads. Passed in by the control panel, which knows Unity's
# persistentDataPath; the fallback is only for running this by hand.
#
# This is not cosmetic: the folder is named after the product, so renaming the product
# moved it, and the stimuli kept being written to a folder nothing read any more. The
# study then reported "no session file" with the files sitting right there under the
# old name.
DEST="${EMOTION_ROOMS_DATA:-$HOME/Library/Application Support/DefaultCompany/Emotion Rooms}"
mkdir -p "$DEST" runs

python3 -m pipeline.cli build-session \
  --batch "$BATCH" --participant "$PARTICIPANT" --seed "$SEED" \
  --participant-index "$INDEX" --out "runs/session_$PARTICIPANT.json"

python3 -m pipeline.cli export-unity "runs/session_$PARTICIPANT.json" \
  --out "runs/unity_$PARTICIPANT.json"

python3 -m pipeline.cli oversight-block \
  --batch "$BATCH" --participant "$PARTICIPANT" --seed "$SEED" \
  --out "runs/oversight_$PARTICIPANT.json"

python3 -m pipeline.cli build-practice --out runs/practice.json

cp "runs/practice.json"               "$DEST/practice.json"
python3 -m pipeline.cli build-practice --out "runs/practice.json"

cp "runs/unity_$PARTICIPANT.json"     "$DEST/session.json"
cp "runs/practice.json"               "$DEST/practice.json"
cp "runs/oversight_$PARTICIPANT.json"           "$DEST/oversight.json"
cp "runs/oversight_${PARTICIPANT}_rationale.json" "$DEST/rationale.json"

echo
echo "Ready for $PARTICIPANT in: $DEST"
python3 - "$PARTICIPANT" <<'PY'
import collections, json, sys
p = sys.argv[1]
s = json.load(open(f"runs/unity_{p}.json"))
b = json.load(open(f"runs/oversight_{p}.json"))
rooms = s.get("rooms") or s.get("trials") or []
print(f"  session.json    {len(rooms)} trials")
print(f"  oversight.json  {len(b['trials'])} review trials")
print("    conditions:", dict(collections.Counter(t["condition"] for t in b["trials"])))
print("    swapped:   ", dict(collections.Counter(
    t["ground_truth"].get("swapped_field") or "-" for t in b["trials"])))
print("    corrections:", dict(collections.Counter(
    t.get("correction_source") or "n/a" for t in b["trials"])))
r = json.load(open(f"runs/oversight_{p}_rationale.json"))
print(f"  rationale.json  {len(r['trials'])} trials  ({r['question']})")
PY
echo
echo "In Unity: Emotion Rooms > Study Control Panel (Cmd-Shift-E) sets the id and runs"
echo "the session. Nothing to type into the inspector."
