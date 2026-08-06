"""Command line entry point.

    python3 -m pipeline.cli <command> --help

Commands, in the order design-spec.md section 8 says to use them:

    pools              show the frozen pools and the design space size
    validate           gate a config, batch or session file
    build-practice     practice rooms shown before the real trials
    build-participants pre-build every participant's stimuli into the app
    bundle-participant join one participant's files into a single record
    emit-questionnaires write the in-app questionnaires for Unity
    emit-unity-pools   regenerate unity/PoolConstants.cs from pools.py
    generate           ask Claude for candidates for one target label
    generate-all       every emotion plus the neutral control arm
    random-control     the uniform-draw control arm (no API calls)
    merge              combine run files into one pool of rooms
    build-session      draw one participant's trial list
    export-unity       strip pipeline-only fields, write an engine-ready batch
"""

from __future__ import annotations

import argparse
import json
import sys

from .pools import (
    BRIGHTNESSES,
    EMOTIONS,
    HUES,
    NEUTRAL_LABEL,
    SATURATIONS,
    SHAPES,
    TEXTURES,
    UNASSIGNED_LABEL,
    WALL_VALUE,
    design_space_size,
)
from .schema import unity_config
from .validate import format_violations, validate_batch


def _load(path: str) -> dict | list:
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def _rooms_of(payload: dict | list) -> list[dict]:
    """Accept a single config, a `{"rooms": [...]}` batch, a session, or a bare list."""
    if isinstance(payload, list):
        return payload
    for key in ("rooms", "trials"):
        if key in payload:
            return payload[key]
    return [payload]


def _is_session(payload: dict | list) -> bool:
    return isinstance(payload, dict) and "trials" in payload


def _write(path: str | None, payload: dict) -> None:
    text = json.dumps(payload, indent=2, ensure_ascii=False) + "\n"
    if path is None or path == "-":
        sys.stdout.write(text)
        return
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(text)
    print(f"wrote {path}")


# ---------------------------------------------------------------- commands


def cmd_pools(args: argparse.Namespace) -> int:
    print("LLM-controlled pools (design-spec.md section 3)")
    print(f"  hue        ({len(HUES):2d}) {', '.join(str(v) for v in HUES)}")
    print(f"  saturation ({len(SATURATIONS):2d}) {', '.join(str(v) for v in SATURATIONS)}")
    print(f"  brightness ({len(BRIGHTNESSES):2d}) {', '.join(str(v) for v in BRIGHTNESSES)}")
    print(f"  texture    ({len(TEXTURES):2d}) {', '.join(TEXTURES)}")
    print()
    print("Researcher-set, never an LLM output")
    print(f"  shape      ({len(SHAPES):2d}) {', '.join(SHAPES)}")
    print(f"  wall HSV value  {WALL_VALUE} (fixed -- brightness lives on the light)")
    print()
    print(f"design space          {design_space_size()} rooms")
    print(f"design space x shape  {design_space_size(include_shape=True)} rooms")
    return 0


def cmd_validate(args: argparse.Namespace) -> int:
    exit_code = 0
    for path in args.files:
        payload = _load(path)
        trials = _rooms_of(payload)
        session = _is_session(payload)

        accepted, rejected = validate_batch(
            [unity_config(room) for room in trials],
            check_duplicate_ids=not session,
        )
        kind = "trials" if session else "rooms"
        print(f"{path}: {len(accepted)} valid {kind}, {len(rejected)} rejected")
        for room, violations in rejected:
            room_id = room.get("id", "<no id>") if isinstance(room, dict) else "<not an object>"
            print(f"  {room_id}")
            print(format_violations(violations))
        if rejected:
            exit_code = 1

        if session:
            # Room ids repeat across shapes here; trial_id is the unique key the
            # response log joins on, so that is what has to be checked.
            trial_ids = [t.get("trial_id") for t in trials if isinstance(t, dict)]
            duplicates = {i for i in trial_ids if trial_ids.count(i) > 1}
            missing = sum(1 for i in trial_ids if not isinstance(i, str))
            if duplicates or missing:
                print(f"  duplicate trial_ids: {sorted(duplicates)}; without a trial_id: {missing}")
                exit_code = 1
    return exit_code


def cmd_validate_handoff(args: argparse.Namespace) -> int:
    from .handoff import exploratory_cells, validate_handoff

    exit_code = 0
    for path in args.files:
        doc = _load(path)
        errors = validate_handoff(doc)

        if errors:
            print(f"{path}: NOT safe to build against, {len(errors)} problem(s)")
            for error in errors:
                print(f"  - {error}")
            exit_code = 1
            continue

        cells = doc.get("cells") or []
        print(f"{path}: OK, {len(cells)} cells, safe to build against")

        exploratory = exploratory_cells(doc)
        if exploratory:
            print(
                f"  {len(exploratory)} value(s) have no locked band for their emotion and "
                f"are exploratory by design, not failures:"
            )
            for emotion, shape, name, value in exploratory:
                print(f"    {emotion}/{shape}: {name}={value}")
    return exit_code


def cmd_oversight_block(args: argparse.Namespace) -> int:
    """Phase B: one participant's detection / attribution / correction block."""
    from .controls import random_rooms
    from .oversight import build_oversight_block

    payload = _load(args.batch)
    configs = _rooms_of(payload)
    if len(configs) < 2:
        print("error: need at least two configs so there is a donor to swap from",
              file=sys.stderr)
        return 1

    def pool_sampler(rng):
        room = random_rooms(1, seed=rng.randrange(1 << 30))[0]
        return {k: room[k] for k in ("hue", "saturation", "brightness", "texture") if k in room}

    block = build_oversight_block(
        configs,
        seed=args.seed,
        participant=args.participant,
        per_condition=args.per_condition,
        pool_sampler=None if args.no_random_arm else pool_sampler,
    )

    counts: dict[str, int] = {}
    for trial in block["trials"]:
        counts[trial["condition"]] = counts.get(trial["condition"], 0) + 1
    print(f"{len(block['trials'])} trials: " +
          ", ".join(f"{k}={v}" for k, v in sorted(counts.items())))
    swapped = [t for t in block["trials"] if t["ground_truth"]["swapped_field"]]
    if swapped:
        broken: dict[str, int] = {}
        for trial in swapped:
            field = trial["ground_truth"]["swapped_field"]
            broken[field] = broken.get(field, 0) + 1
        print("  injected faults by variable: " +
              ", ".join(f"{k}={v}" for k, v in sorted(broken.items())))

    _write(args.out, block)
    return 0


def cmd_check_separability(args: argparse.Namespace) -> int:
    """Are the cells actually distinguishable? Run this the moment a config arrives."""
    from .separability import check, format_report

    exit_code = 0
    for path in args.files:
        configs = _rooms_of(_load(path))
        report = check(configs, too_close=args.too_close)
        print(f"{path}:")
        print(format_report(report, too_close=args.too_close))
        if not report["safe"]:
            exit_code = 1
    return exit_code


def cmd_build_practice(args: argparse.Namespace) -> int:
    """Practice rooms, deliberately outside the eight study cells.

    The first rating anyone gives measures the interface, not the room: where the pointer
    is, what the grid means, how hard to press. Somewhere that noise has to land, and
    without practice it lands on whichever emotion came first. Counterbalancing then
    spreads it evenly across conditions instead of removing it.

    These use mid-pool values and no target emotion, so a participant never rates a study
    stimulus twice and nothing here can be confused for data. One of each shape, so the
    curved shell is not a surprise on the first real trial.
    """
    import json
    from pathlib import Path

    from . import pools

    rooms = _practice_rooms()

    for room in rooms:
        for field, pool in (
            ("hue", pools.HUES),
            ("saturation", pools.SATURATIONS),
            ("brightness", pools.BRIGHTNESSES),
            ("texture", pools.TEXTURES),
            ("roughness", pools.ROUGHNESSES),
        ):
            if room[field] not in pool:
                print(f"error: practice {field}={room[field]!r} is not in the pool")
                return 1

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps({"rooms": rooms}, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {out} ({len(rooms)} practice rooms, one per shape)")
    return 0


def _practice_rooms() -> list[dict]:
    """The warm-up rooms. Separate from the command so a test can validate them.

    Both carry target_emotion and source of "practice", which are legal pool labels
    precisely so these pass the same validator as a real stimulus -- they are shown to
    a participant, so nothing about them should be exempt from that check.
    """
    return [
        {
            "id": "practice_linear",
            "target_emotion": "practice",
            "source": "practice",
            "hue": 60,
            "saturation": 0.2,
            "brightness": 300.0,
            "texture": "plaster",
            "roughness": "smooth",
            "shape": "linear",
            "rationale": "Practice room. Not a study stimulus.",
        },
        {
            "id": "practice_curved",
            "target_emotion": "practice",
            "source": "practice",
            "hue": 90,
            "saturation": 0.2,
            "brightness": 500.0,
            "texture": "textile",
            "roughness": "rough",
            "shape": "curved",
            "rationale": "Practice room. Not a study stimulus.",
        },
    ]


def cmd_build_participants(args: argparse.Namespace) -> int:
    """Pre-build every participant's stimuli into the app.

    So a session can be run by someone with no copy of this repo, no Python and no
    Unity: a standalone build carries the rooms for the whole sample and the researcher
    picks a participant number. Generating stimuli on the machine that runs the session
    was fine while that was also the machine holding the pipeline, and stops being fine
    the moment a second person collects data.

    Counterbalancing still comes from the participant index, so participant 7 gets the
    same trial order whoever runs them and on whichever machine.
    """
    import json
    import shutil
    from pathlib import Path

    from .oversight import OversightError, build_oversight_block
    from .session import build_session

    rooms = _rooms_of(_load(args.batch))
    practice = _practice_rooms()

    out = Path(args.out)
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)

    built = []
    for i in range(args.count):
        participant = f"p{i + 1:02d}"
        folder = out / participant
        folder.mkdir()

        try:
            session = build_session(
                rooms,
                participant=participant,
                seed=args.seed + i,
                participant_index=i,
            )
            block = build_oversight_block(
                rooms, participant=participant, seed=args.seed + i, per_condition=3
            )
        except (ValueError, OversightError) as exc:
            print(f"error building {participant}: {exc}", file=sys.stderr)
            return 1

        trials = [
            {**trial, "id": trial.get("trial_id", trial.get("id"))}
            for trial in session.trials
        ]
        exported = [unity_config(room) for room in trials]

        accepted, rejected = validate_batch(exported)
        if rejected:
            print(f"error: {participant} produced invalid rooms", file=sys.stderr)
            for room, violations in rejected:
                print(f"  {room.get('id', '<no id>')}", file=sys.stderr)
                print(format_violations(violations), file=sys.stderr)
            return 1

        (folder / "session.json").write_text(
            json.dumps({"rooms": exported}, indent=2) + "\n", encoding="utf-8"
        )
        (folder / "oversight.json").write_text(
            json.dumps(block, indent=2) + "\n", encoding="utf-8"
        )
        (folder / "practice.json").write_text(
            json.dumps({"rooms": practice}, indent=2) + "\n", encoding="utf-8"
        )
        built.append(participant)

    (out / "index.json").write_text(
        json.dumps({"participants": built}, indent=2) + "\n", encoding="utf-8"
    )
    print(f"wrote {len(built)} participants to {out}")
    print("  each folder holds session.json, oversight.json and practice.json")
    print("  these ship inside the built app, so no Python is needed to run a session")
    return 0


def cmd_bundle_participant(args: argparse.Namespace) -> int:
    from pathlib import Path

    from .bundle import BundleError, bundle

    try:
        report = bundle(Path(args.data), args.participant, Path(args.out))
    except BundleError as exc:
        print(f"error: {exc}")
        return 1

    print(f"wrote {report['file']}")
    print(f"  {report['rows']} rows: " + ", ".join(
        f"{name} {count}" for name, count in sorted(report["by_source"].items())
    ))
    print(f"  consent recorded: {report['consent_recorded']}")
    if report["forms"]:
        print(f"  questionnaires: {len(report['forms'])} returned")
    if report["incomplete_forms"]:
        print("  NOT COMPLETED: " + ", ".join(report["incomplete_forms"]))

    scores = report.get("scores") or {}
    if scores.get("ssq_change"):
        print(f"  SSQ total change: {scores['ssq_change'].get('total', 0):+.1f}")
    if scores.get("nasa_tlx"):
        print(f"  raw NASA-TLX:     {scores['nasa_tlx'].get('raw_tlx', 0):.1f}")
    if scores.get("trust"):
        print(f"  trust (1-7):      {scores['trust'].get('trust_mean', 0):.2f}")
    if scores.get("presence"):
        print(f"  presence (1-7):   {scores['presence'].get('presence_mean', 0):.2f}")
    if scores.get("awareness"):
        aware = scores["awareness"]
        print(f"  noticed {aware.get('noticed_count', 0)} of 4 manipulated variables")
    if scores.get("preference"):
        check = scores["preference"].get("attention_check_passed")
        if check is False:
            print("  ATTENTION CHECK FAILED -- decide whether to keep this participant")
    if report["withdrew"]:
        print("  WITHDREW -- partial session, decide whether to keep it")
    if not report["complete"]:
        print("  INCOMPLETE -- fewer than 8 trials, no consent row, or withdrawn")
    return 0


def cmd_emit_questionnaires(args: argparse.Namespace) -> int:
    import json
    from pathlib import Path

    from .instruments import FORMS, as_dict

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(as_dict(), indent=2) + "\n", encoding="utf-8")

    print(f"wrote {out}")
    for form in FORMS:
        citation = form["citation"] or "study-specific, not a validated instrument"
        print(f"  {form['when']:<7} {form['id']:<14} {len(form['items']):>2} items   {citation}")
    return 0


def cmd_emit_unity_pools(args: argparse.Namespace) -> int:
    from .emit_unity import render

    text = render()
    if args.out == "-":
        sys.stdout.write(text)
    else:
        with open(args.out, "w", encoding="utf-8") as handle:
            handle.write(text)
        print(f"wrote {args.out}")
    return 0


def _generate_one(client, label: str, args: argparse.Namespace):
    from .generate import generate_candidates

    print(f"generating {args.count} candidates for '{label}' with {args.model}")
    return generate_candidates(
        client,
        label,
        args.count,
        model=args.model,
        sketch=args.sketch,
        chunk_size=args.chunk_size,
    )


def _report(result) -> None:
    from .generate import duplicate_rate

    print(
        f"  {label_summary(result)} | requests: {result.requests} | "
        f"rejected: {len(result.rejected)} ({result.rejection_rate:.0%}) | "
        f"duplicate combinations: {duplicate_rate(result.rooms):.0%}"
    )


def label_summary(result) -> str:
    return f"{result.target_label}: {len(result.rooms)} rooms"


def cmd_generate(args: argparse.Namespace) -> int:
    from .generate import GenerationError, make_client, run_metadata, save_batch

    try:
        client = make_client()
        result = _generate_one(client, args.emotion, args)
    except GenerationError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1

    _report(result)
    save_batch(
        args.out,
        run_metadata(args.model, {"target_emotion": args.emotion, "requested": args.count}),
        result.rooms,
        result.rejected,
    )
    print(f"wrote {args.out}")
    return 0


def cmd_generate_all(args: argparse.Namespace) -> int:
    from .generate import GenerationError, make_client, run_metadata, save_batch

    labels = list(EMOTIONS) + ([NEUTRAL_LABEL] if not args.no_neutral else [])
    rooms: list[dict] = []
    rejected: list[dict] = []

    try:
        client = make_client()
        for label in labels:
            result = _generate_one(client, label, args)
            _report(result)
            rooms.extend(result.rooms)
            rejected.extend(result.rejected)
    except GenerationError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1

    save_batch(
        args.out,
        run_metadata(args.model, {"labels": labels, "requested_per_label": args.count}),
        rooms,
        rejected,
    )
    print(f"wrote {args.out} ({len(rooms)} rooms)")
    return 0


def cmd_random_control(args: argparse.Namespace) -> int:
    from .controls import random_rooms
    from .generate import run_metadata, save_batch

    rooms = random_rooms(args.count, seed=args.seed, unique=args.unique)
    save_batch(
        args.out,
        run_metadata(
            "none (uniform random draw)",
            {"target_emotion": UNASSIGNED_LABEL, "seed": args.seed, "unique": args.unique},
        ),
        rooms,
    )
    print(f"wrote {args.out} ({len(rooms)} control rooms, seed {args.seed})")
    return 0


def cmd_merge(args: argparse.Namespace) -> int:
    rooms: list[dict] = []
    sources: list[str] = []
    for path in args.files:
        payload = _load(path)
        rooms.extend(_rooms_of(payload))
        sources.append(path)

    accepted, rejected = validate_batch([unity_config(room) for room in rooms])
    if rejected:
        print(f"error: {len(rejected)} rooms failed validation; not merging", file=sys.stderr)
        for room, violations in rejected:
            print(f"  {room.get('id', '<no id>')}", file=sys.stderr)
            print(format_violations(violations), file=sys.stderr)
        return 1

    _write(args.out, {"meta": {"merged_from": sources}, "rooms": accepted})
    return 0


def cmd_build_session(args: argparse.Namespace) -> int:
    from .session import MINUTES_PER_ROOM, TRIAL_BUDGET_MINUTES, build_session

    rooms = _rooms_of(_load(args.batch))
    try:
        session = build_session(
            rooms,
            participant=args.participant,
            seed=args.seed,
            variants_per_emotion=args.variants,
            neutral_trials=args.neutral,
            random_trials=args.random,
            counterbalance=args.counterbalance,
            participant_index=args.participant_index,
            min_separation=args.min_separation,
        )
    except ValueError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1

    print(
        f"{len(session.trials)} trials x {MINUTES_PER_ROOM} min = "
        f"{session.minutes:.1f} min of trial time"
    )
    if session.over_budget:
        print(
            f"WARNING: over the {TRIAL_BUDGET_MINUTES:.0f} min trial budget in "
            f"design-spec.md section 6. Drop variants or control rooms.",
            file=sys.stderr,
        )

    _write(
        args.out,
        {
            "meta": {
                "participant": session.participant,
                "seed": session.seed,
                "batch": args.batch,
                "trial_minutes": session.minutes,
            },
            "trials": session.trials,
        },
    )
    return 0


def cmd_export_unity(args: argparse.Namespace) -> int:
    payload = _load(args.file)
    trials = _rooms_of(payload)

    if _is_session(payload):
        # Exporting a session: the engine looks rooms up by id, and the response log
        # joins on trial_id, so those two keys have to be the same string. Room ids
        # repeat across shapes, trial_ids do not. Presentation order is preserved.
        trials = [{**trial, "id": trial.get("trial_id", trial.get("id"))} for trial in trials]

    rooms = [unity_config(room) for room in trials]
    accepted, rejected = validate_batch(rooms)
    if rejected:
        print(
            f"error: {len(rejected)} rooms failed validation; nothing exported",
            file=sys.stderr,
        )
        for room, violations in rejected:
            print(f"  {room.get('id', '<no id>')}", file=sys.stderr)
            print(format_violations(violations), file=sys.stderr)
        return 1

    _write(args.out, {"rooms": accepted})
    return 0


# ---------------------------------------------------------------- parser


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="pipeline", description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)

    sub.add_parser("pools", help="show the frozen pools").set_defaults(func=cmd_pools)

    p = sub.add_parser("validate", help="validate config, batch or session files")
    p.add_argument("files", nargs="+")
    p.set_defaults(func=cmd_validate)

    p = sub.add_parser(
        "validate-handoff",
        help="check Mengkai's finalised 8-cell config file before building against it",
    )
    p.add_argument("files", nargs="+")
    p.set_defaults(func=cmd_validate_handoff)

    p = sub.add_parser(
        "oversight-block",
        help="Phase B: build one participant's detection/attribution/correction trials",
    )
    p.add_argument("--batch", required=True, help="configs to draw stimuli and donors from")
    p.add_argument("--participant", required=True)
    p.add_argument("--seed", type=int, required=True)
    p.add_argument("--per-condition", type=int, default=4)
    p.add_argument("--no-random-arm", action="store_true",
                   help="omit the uniform-random condition")
    p.add_argument("--out", default="runs/oversight_block.json")
    p.set_defaults(func=cmd_oversight_block)

    p = sub.add_parser(
        "check-separability",
        help="can the cells be told apart? exits 1 if any two emotions collide",
    )
    p.add_argument("files", nargs="+")
    p.add_argument("--too-close", type=float, default=0.25,
                   help="Gower distance below which two cells count as the same room")
    p.set_defaults(func=cmd_check_separability)

    p = sub.add_parser(
        "build-practice",
        help="practice rooms shown before the real trials",
    )
    p.add_argument("--out", default="runs/practice.json")
    p.set_defaults(func=cmd_build_practice)

    p = sub.add_parser(
        "build-participants",
        help="pre-build N participants' stimuli into the Unity app itself",
    )
    p.add_argument("--batch", default="configs/pilot_8cell.json")
    p.add_argument("--count", type=int, default=30)
    p.add_argument("--seed", type=int, default=40)
    p.add_argument("--out", default="unity/Assets/StreamingAssets/participants")
    p.set_defaults(func=cmd_build_participants)

    p = sub.add_parser(
        "bundle-participant",
        help="join one participant's files into a single long-format record",
    )
    p.add_argument("--participant", required=True)
    p.add_argument(
        "--data",
        required=True,
        help="the app's data folder (Emotion Rooms > Reveal Data Folder)",
    )
    p.add_argument("--out", default="runs/bundles")
    p.set_defaults(func=cmd_bundle_participant)

    p = sub.add_parser(
        "emit-questionnaires",
        help="write questionnaires.json for the Unity forms",
    )
    p.add_argument("--out", default="unity/Assets/StreamingAssets/questionnaires.json")
    p.set_defaults(func=cmd_emit_questionnaires)

    p = sub.add_parser("emit-unity-pools", help="regenerate unity/PoolConstants.cs")
    p.add_argument("--out", default="unity/Assets/Scripts/EmotionRooms/PoolConstants.cs")
    p.set_defaults(func=cmd_emit_unity_pools)

    def add_generation_args(sp: argparse.ArgumentParser) -> None:
        sp.add_argument("--count", type=int, default=50, help="candidates per label (spec: 50-100)")
        sp.add_argument("--model", default="claude-opus-5")
        sp.add_argument("--chunk-size", type=int, default=25, help="candidates per request")
        sp.add_argument(
            "--sketch",
            action="store_true",
            help="also ask for a 2D ASCII sanity check (spec section 7)",
        )

    p = sub.add_parser("generate", help="candidates for one target label")
    p.add_argument("--emotion", required=True, choices=list(EMOTIONS) + [NEUTRAL_LABEL])
    p.add_argument("--out", required=True)
    add_generation_args(p)
    p.set_defaults(func=cmd_generate)

    p = sub.add_parser("generate-all", help="every emotion plus the neutral arm")
    p.add_argument("--out", default="runs/llm_rooms.json")
    p.add_argument("--no-neutral", action="store_true", help="skip the neutral control arm")
    add_generation_args(p)
    p.set_defaults(func=cmd_generate_all)

    p = sub.add_parser("random-control", help="uniform-draw control arm, no API calls")
    p.add_argument("--count", type=int, default=16)
    p.add_argument("--seed", type=int, required=True, help="record this in the paper")
    p.add_argument("--unique", action="store_true", help="reject repeat draws (biases the sample)")
    p.add_argument("--out", default="runs/random_control.json")
    p.set_defaults(func=cmd_random_control)

    p = sub.add_parser("merge", help="combine run files into one pool of rooms")
    p.add_argument("files", nargs="+")
    p.add_argument("--out", default="-")
    p.set_defaults(func=cmd_merge)

    p = sub.add_parser("build-session", help="draw one participant's trial list")
    p.add_argument("--batch", required=True)
    p.add_argument("--participant", required=True)
    p.add_argument("--seed", type=int, required=True)
    p.add_argument("--variants", type=int, default=1, help="rooms per emotion")
    p.add_argument(
        "--counterbalance",
        choices=("constrained", "separated", "williams", "random"),
        default="constrained",
        help="trial ordering. 'constrained' (default) reshuffles until no two trials "
             "sharing an emotion are closer than --min-separation, giving a different "
             "order to every participant. 'separated' instead holds the two trials sharing an "
             "emotion as far apart as possible, which matters because shape is "
             "within-subjects and those two trials are the same room in a different "
             "geometry. 'random' shuffles by seed and leaves ~24%% of pairs adjacent.",
    )
    p.add_argument("--min-separation", type=int, default=2,
                   help="constrained ordering only: minimum gap between the two trials "
                        "sharing an emotion")
    p.add_argument(
        "--participant-index",
        type=int,
        default=None,
        help="0-based position in the recruitment order. Required by 'separated' and "
             "'williams': it selects which counterbalancing row this participant gets, "
             "so the balance holds across the sample rather than per person.",
    )
    p.add_argument("--neutral", type=int, default=0, help="neutral control trials")
    p.add_argument("--random", type=int, default=0, help="random control trials")
    p.add_argument("--out", default="-")
    p.set_defaults(func=cmd_build_session)

    p = sub.add_parser("export-unity", help="write an engine-ready batch")
    p.add_argument("file")
    p.add_argument("--out", default="-")
    p.set_defaults(func=cmd_export_unity)

    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
