#!/usr/bin/env bash
# Scripted walkthrough of the system, for filming.
#
#   ./demo.sh            pause between sections, press enter to advance
#   ./demo.sh --auto     run straight through, for a screen recording with no keypresses
#   ./demo.sh 3          run one section only
#
# Everything here runs offline: no API key, no network, no headset. The Unity part is
# separate and has to be filmed in the editor; see demo-plan.md.
#
# Ordered by how well each one reads on camera, not by how the pipeline runs.

set -uo pipefail
cd "$(dirname "$0")"

AUTO=0
ONLY=""
for arg in "$@"; do
  case "$arg" in
    --auto) AUTO=1 ;;
    [0-9]) ONLY="$arg" ;;
  esac
done

BOLD=$'\033[1m'; DIM=$'\033[2m'; RESET=$'\033[0m'

section() {
  [ -n "$ONLY" ] && [ "$ONLY" != "$1" ] && return 1
  echo
  echo "${BOLD}────────────────────────────────────────────────────────────${RESET}"
  echo "${BOLD} $1. $2${RESET}"
  echo "${DIM} $3${RESET}"
  echo "${BOLD}────────────────────────────────────────────────────────────${RESET}"
  echo
  return 0
}

pause() {
  [ "$AUTO" = "1" ] && { sleep 2; return; }
  echo
  read -rsp "$(printf '%s' "${DIM}enter to continue${RESET}")" -n 1
  echo
}

run() { echo "${DIM}\$ $*${RESET}"; echo; eval "$@"; echo; }

# ---------------------------------------------------------------------------

if section 1 "The gate" "Nothing unvalidated reaches a participant. Exit codes tell the story."; then
  run "python3 -m pipeline.cli validate configs/pilot_8cell.json; echo \"exit=\$?\""
  pause
  run "python3 -m pipeline.cli validate configs/INVALID_do_not_ship.json; echo \"exit=\$?\""
  pause
fi

if section 2 "Pool values are data" "Change one number; the prompt, schema, validator and generated C# all follow."; then
  run "python3 -m pipeline.cli pools | head -8"
  echo "${DIM}# swap the illuminance pool in a scratch copy and re-read it${RESET}"
  echo
  run "python3 -c \"
import json
d=json.load(open('configs/pools.json'))
d['manipulated']['brightness']=[10,20]
json.dump(d,open('/tmp/demo_pools.json','w'))
print('wrote /tmp/demo_pools.json with brightness [10, 20]')\""
  run "EMOTION_ROOMS_POOLS=/tmp/demo_pools.json python3 -m pipeline.cli pools | head -6"
  run "EMOTION_ROOMS_POOLS=/tmp/demo_pools.json python3 -m pipeline.cli emit-unity-pools --out /tmp/demo.cs >/dev/null && grep Brightnesses /tmp/demo.cs"
  echo "${DIM}# no code changed. one data file did.${RESET}"
  pause
fi

if section 3 "Separability" "Can the eight cells be told apart? The study's largest risk, as one command."; then
  run "python3 -m pipeline.cli check-separability configs/pilot_8cell.json; echo \"exit=\$?\""
  pause
  echo "${DIM}# now a design where tense and depressed collapse onto each other${RESET}"
  echo
  run "python3 -c \"
import json
rows=[('calm',240,0.2,150,'plaster','smooth'),('tense',240,0.2,150,'concrete','rough'),
      ('excited',30,0.4,750,'plaster','smooth'),('depressed',240,0.2,150,'concrete','rough')]
json.dump({'rooms':[{'id':f'{e}_x','target_emotion':e,'source':'llm','hue':h,'saturation':s,
  'brightness':b,'texture':t,'roughness':r,'rationale':'x'} for e,h,s,b,t,r in rows]},
  open('/tmp/demo_collapse.json','w'))
print('wrote a design where tense and depressed share every value')\""
  run "python3 -m pipeline.cli check-separability /tmp/demo_collapse.json; echo \"exit=\$?\""
  pause
fi

if section 4 "Aggregation" "Why the selected room must be one the model actually produced."; then
  run "python3 -c \"
from pipeline.aggregate import medoid, modal_reconstruction, differs_from_modal
samples = ([{'hue_category':'warm','material':'rough'}]*3
         + [{'hue_category':'cool','material':'smooth'}]*2
         + [{'hue_category':'neutral','material':'smooth'}]*2)
real = {(s['hue_category'], s['material']) for s in samples}
print('samples the model produced:', sorted(real))
print()
modal = modal_reconstruction(samples)
print('per-variable winners ->', modal, ' exists in the data?',
      (modal['hue_category'], modal['material']) in real)
chosen, stats = medoid(samples)
print('medoid              ->', chosen, ' exists in the data?',
      (chosen['hue_category'], chosen['material']) in real)
print()
print('taking each variable\\'s winner invents a room nobody generated.')
print('the medoid cannot: it returns one of the actual samples.')\""
  pause
fi

if section 5 "Counterbalancing" "Each participant meets every emotion twice. Those two trials must not sit together."; then
  run "python3 -c \"
from collections import Counter
from pipeline.session import build_session, pair_separations
import json
rooms=json.load(open('configs/pilot_8cell.json'))['rooms']
for mode in ('random','constrained'):
    seps=[]
    for i in range(200):
        kw={} if mode=='random' else {}
        s=build_session(rooms, participant=f'p{i}', seed=900+i, counterbalance=mode)
        seps+=list(pair_separations(s.trials).values())
    adj=sum(1 for x in seps if x==1)
    print(f'{mode:12s} adjacent same-emotion pairs: {adj:3d} of {len(seps)}')
print()
s=build_session(rooms, participant='p01', seed=42, counterbalance='constrained')
print('one participant:', [f\\\"{t['target_emotion'][:4]}/{t['shape'][:3]}\\\" for t in s.trials])\""
  pause
fi

if section 6 "Oversight trials" "Errors injected on purpose, so what a participant notices is scoreable."; then
  run "python3 -m pipeline.cli oversight-block --batch configs/pilot_8cell.json --participant demo --seed 7 --per-condition 3 --out /tmp/demo_block.json"
  pause
  run "python3 -c \"
import json
b=json.load(open('/tmp/demo_block.json'))
for t in b['trials'][:6]:
    g=t['ground_truth']
    fault = g['swapped_field'] or ('rationale' if g['rationale_is_wrong'] else
            ('everything' if g.get('all_variables_random') else '-'))
    print(f\\\"  {t['condition']:21s} shown as {t['target_emotion_shown']:10s} broke: {fault}\\\")
print()
p=[t for t in b['trials'] if t['condition']=='swapped'][0]
g=p['ground_truth']
print('one swapped trial, ground truth recorded:')
print('   shown as ', p['target_emotion_shown'])
print('   swapped  ', g['swapped_field'], ':', g['original_value'], '->', g['swapped_in_value'])
print('   borrowed from the', g['donor_emotion'], 'room, so it is a value the model')
print('   genuinely chose, just in the wrong place')\""
  pause
fi

if section 7 "A session, end to end" "Config in, participant trial list out, re-validated."; then
  run "python3 -m pipeline.cli build-session --batch configs/pilot_8cell.json --participant p01 --seed 42 --out /tmp/demo_session.json"
  run "python3 -m pipeline.cli export-unity /tmp/demo_session.json --out /tmp/demo_unity.json"
  run "python3 -m pipeline.cli validate /tmp/demo_unity.json"
  pause
fi

if section 8 "The suite" "No API key, no network, no headset."; then
  run "python3 -m unittest discover -s tests 2>&1 | tail -4"
fi

echo
echo "${BOLD}Done.${RESET} Unity sections are filmed separately: see demo-plan.md."
echo
