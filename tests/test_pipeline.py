"""Tests for the parts that must not break: the pools, the gate, the control arm.

    python3 -m unittest discover -s tests -v

No API key and no network needed -- nothing here calls Claude.
"""

from __future__ import annotations

import json
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from pipeline import pools
from pipeline.controls import random_rooms
from pipeline.schema import ROOM_CONFIG_KEYS, candidate_schema, room_id, unity_config
from pipeline.session import build_session
from pipeline.validate import (
    validate_batch,
    validate_candidate,
    validate_room_config,
)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

VALID_ROOM = {
    "id": "calm_007",
    "target_emotion": "calm",
    "source": "llm",
    "hue": 210,
    "saturation": 0.2,
    "brightness": 0.6,
    "texture": "plaster",
    "rationale": "Low-saturation cool blue with soft even light reads as restful.",
}


def room(**overrides) -> dict:
    merged = dict(VALID_ROOM)
    merged.update(overrides)
    return merged


class TestPools(unittest.TestCase):
    def test_design_space_matches_the_spec(self):
        # design-spec.md section 3: 12 x 3 x 5 x 4 = 720, doubling to 1440 with shape.
        self.assertEqual(pools.design_space_size(), 720)
        self.assertEqual(pools.design_space_size(include_shape=True), 1440)

    def test_enumeration_is_complete_and_distinct(self):
        rooms = list(pools.enumerate_rooms())
        self.assertEqual(len(rooms), 720)
        combos = {tuple(sorted(r.items())) for r in rooms}
        self.assertEqual(len(combos), 720)

    def test_hues_are_thirty_degrees_apart(self):
        self.assertEqual(len(pools.HUES), 12)
        self.assertEqual(pools.HUES[0], 0)
        gaps = {b - a for a, b in zip(pools.HUES, pools.HUES[1:])}
        self.assertEqual(gaps, {30})
        self.assertLess(max(pools.HUES), 360)  # no wraparound duplicate of 0

    def test_in_pool_tolerance_cannot_reach_a_neighbour(self):
        self.assertTrue(pools.in_pool(0.2, pools.SATURATIONS))
        self.assertTrue(pools.in_pool(0.20000000001, pools.SATURATIONS))
        self.assertFalse(pools.in_pool(0.35, pools.SATURATIONS))
        self.assertFalse(pools.in_pool(0.21, pools.SATURATIONS))

    def test_canonical_snaps_to_the_pool_member(self):
        self.assertEqual(pools.canonical(0.2, pools.SATURATIONS), 0.20)
        with self.assertRaises(ValueError):
            pools.canonical(0.35, pools.SATURATIONS)


class TestRoomConfigValidation(unittest.TestCase):
    def test_the_spec_example_passes(self):
        self.assertEqual(validate_room_config(room()), [])

    def test_rejects_hue_off_the_pool(self):
        # The exact failure design-spec.md section 4 predicts.
        violations = validate_room_config(room(hue=217))
        self.assertEqual([v.field for v in violations], ["hue"])

    def test_rejects_invented_texture(self):
        violations = validate_room_config(room(texture="velvet"))
        self.assertEqual([v.field for v in violations], ["texture"])

    def test_rejects_value_between_pool_members(self):
        self.assertTrue(validate_room_config(room(saturation=0.35)))
        self.assertTrue(validate_room_config(room(brightness=0.5)))

    def test_rejects_missing_required_field(self):
        incomplete = room()
        del incomplete["brightness"]
        fields = [v.field for v in validate_room_config(incomplete)]
        self.assertIn("brightness", fields)

    def test_rejects_unknown_field(self):
        fields = [v.field for v in validate_room_config(room(wall_height=3))]
        self.assertIn("wall_height", fields)

    def test_rejects_bad_id(self):
        for bad in ("Calm_007", "007", "calm 007", "", None, 7):
            with self.subTest(bad=bad):
                self.assertTrue(any(v.field == "id" for v in validate_room_config(room(id=bad))))

    def test_rejects_empty_rationale(self):
        self.assertTrue(any(v.field == "rationale" for v in validate_room_config(room(rationale="  "))))

    def test_booleans_are_not_numbers(self):
        # True == 1 in Python; brightness=True must not sneak past a pool check.
        self.assertTrue(validate_room_config(room(hue=True)))
        self.assertTrue(validate_room_config(room(brightness=True)))

    def test_shape_is_optional_but_pool_checked(self):
        self.assertEqual(validate_room_config(room(shape="curved")), [])
        self.assertTrue(validate_room_config(room(shape="spiral")))

    def test_non_object_is_rejected(self):
        self.assertTrue(validate_room_config("calm room"))
        self.assertTrue(validate_room_config(None))

    def test_every_point_in_the_design_space_validates(self):
        for index, point in enumerate(pools.enumerate_rooms(include_shape=True), start=1):
            candidate = room(id=room_id("calm", index % 1000), **point)
            self.assertEqual(validate_room_config(candidate), [], msg=str(point))


class TestCandidateValidation(unittest.TestCase):
    def test_accepts_a_bare_candidate(self):
        candidate = {
            "hue": 210,
            "saturation": 0.2,
            "brightness": 0.6,
            "texture": "plaster",
            "rationale": "Cool and soft.",
        }
        self.assertEqual(validate_candidate(candidate), [])

    def test_rejects_a_candidate_that_sets_its_own_id(self):
        candidate = {
            "id": "calm_001",
            "hue": 210,
            "saturation": 0.2,
            "brightness": 0.6,
            "texture": "plaster",
            "rationale": "Cool and soft.",
        }
        fields = [v.field for v in validate_candidate(candidate)]
        self.assertIn("id", fields)

    def test_sketch_only_allowed_when_requested(self):
        candidate = {
            "hue": 210,
            "saturation": 0.2,
            "brightness": 0.6,
            "texture": "plaster",
            "rationale": "Cool and soft.",
            "sketch": "####",
        }
        self.assertTrue(validate_candidate(candidate, allow_sketch=False))
        self.assertEqual(validate_candidate(candidate, allow_sketch=True), [])


class TestBatchValidation(unittest.TestCase):
    def test_duplicate_ids_are_rejected(self):
        accepted, rejected = validate_batch([room(), room()])
        self.assertEqual(len(accepted), 1)
        self.assertEqual(len(rejected), 1)
        self.assertTrue(any(v.reason == "duplicate id in batch" for v in rejected[0][1]))

    def test_valid_batch_passes(self):
        rooms = [room(id=room_id("calm", i)) for i in range(1, 6)]
        accepted, rejected = validate_batch(rooms)
        self.assertEqual(len(accepted), 5)
        self.assertEqual(rejected, [])


class TestShippedConfigFiles(unittest.TestCase):
    """The files in configs/ are the loader's fixtures; they must stay honest."""

    def load(self, name: str) -> dict:
        with open(os.path.join(ROOT, "configs", name), encoding="utf-8") as handle:
            return json.load(handle)

    def test_handwritten_single_config_is_valid(self):
        self.assertEqual(validate_room_config(self.load("handwritten_calm_001.json")), [])

    def test_smoke_batch_is_valid_and_covers_the_pools(self):
        rooms = self.load("handwritten_smoke_batch.json")["rooms"]
        accepted, rejected = validate_batch([unity_config(r) for r in rooms])
        self.assertEqual(rejected, [])
        self.assertEqual(set(r["texture"] for r in accepted), set(pools.TEXTURES))
        self.assertEqual(set(r["shape"] for r in accepted), set(pools.SHAPES))
        self.assertIn(min(pools.BRIGHTNESSES), [r["brightness"] for r in accepted])
        self.assertIn(max(pools.BRIGHTNESSES), [r["brightness"] for r in accepted])

    def test_every_room_in_the_invalid_file_is_rejected(self):
        rooms = self.load("INVALID_do_not_ship.json")["rooms"]
        accepted, rejected = validate_batch(rooms)
        self.assertEqual(accepted, [], "a room in the INVALID file passed validation")
        self.assertEqual(len(rejected), len(rooms))


class TestRandomControl(unittest.TestCase):
    def test_draws_are_valid_and_reproducible(self):
        first = random_rooms(40, seed=7)
        second = random_rooms(40, seed=7)
        self.assertEqual(first, second)
        self.assertNotEqual(first, random_rooms(40, seed=8))

        accepted, rejected = validate_batch(first)
        self.assertEqual(rejected, [])
        self.assertEqual(len(accepted), 40)
        self.assertTrue(all(r["source"] == "random" for r in accepted))
        self.assertTrue(all(r["target_emotion"] == pools.UNASSIGNED_LABEL for r in accepted))

    def test_unique_mode_removes_collisions(self):
        rooms = random_rooms(200, seed=3, unique=True)
        combos = {(r["hue"], r["saturation"], r["brightness"], r["texture"]) for r in rooms}
        self.assertEqual(len(combos), 200)

    def test_draws_spread_across_the_pools(self):
        # A "random" arm that only ever picked one hue would silently invalidate the
        # falsifiability argument in design-spec.md section 5.
        rooms = random_rooms(400, seed=11)
        self.assertEqual(len({r["hue"] for r in rooms}), len(pools.HUES))
        self.assertEqual(len({r["texture"] for r in rooms}), len(pools.TEXTURES))


class TestSession(unittest.TestCase):
    def batch(self) -> list[dict]:
        rooms = []
        for emotion in pools.EMOTIONS + (pools.NEUTRAL_LABEL,):
            for index in range(1, 11):
                rooms.append(
                    room(
                        id=room_id(emotion, index),
                        target_emotion=emotion,
                        hue=pools.HUES[index % len(pools.HUES)],
                    )
                )
        rooms.extend(random_rooms(10, seed=99))
        return rooms

    def test_spec_session_is_sixteen_trials_inside_budget(self):
        # design-spec.md section 6: 4 emotions x 2 shapes x 2 variants = 16.
        session = build_session(self.batch(), participant="p01", seed=1)
        self.assertEqual(len(session.trials), 16)
        self.assertAlmostEqual(session.minutes, 24.0)
        self.assertFalse(session.over_budget)

    def test_each_room_appears_in_both_shapes(self):
        session = build_session(self.batch(), participant="p01", seed=1)
        by_room: dict[str, set[str]] = {}
        for trial in session.trials:
            by_room.setdefault(trial["id"], set()).add(trial["shape"])
        self.assertTrue(all(shapes == set(pools.SHAPES) for shapes in by_room.values()))

    def test_trial_ids_are_unique(self):
        session = build_session(self.batch(), participant="p01", seed=1)
        ids = [t["trial_id"] for t in session.trials]
        self.assertEqual(len(ids), len(set(ids)))

    def test_controls_push_the_session_over_budget(self):
        # The tension worth flagging to Mengkai: the spec's 16 trials are already fully
        # spent on the emotion conditions, so control rooms cost emotion variants.
        session = build_session(
            self.batch(), participant="p01", seed=1, neutral_trials=4, random_trials=4
        )
        self.assertEqual(len(session.trials), 24)
        self.assertTrue(session.over_budget)

    def test_trials_are_reproducible_and_participant_seeded(self):
        a = build_session(self.batch(), participant="p01", seed=5)
        b = build_session(self.batch(), participant="p01", seed=5)
        c = build_session(self.batch(), participant="p02", seed=6)
        self.assertEqual(a.trials, b.trials)
        self.assertNotEqual(a.trials, c.trials)

    def test_refuses_to_build_from_too_few_rooms(self):
        with self.assertRaises(ValueError):
            build_session([room()], participant="p01", seed=1)

    def test_trials_carry_only_contract_fields_plus_trial_keys(self):
        session = build_session(self.batch(), participant="p01", seed=1)
        allowed = set(ROOM_CONFIG_KEYS) | {"trial_index", "trial_id"}
        for trial in session.trials:
            self.assertEqual(set(trial) - allowed, set())


class TestSchema(unittest.TestCase):
    def test_candidate_schema_enumerates_every_pool(self):
        schema = candidate_schema()
        props = schema["properties"]
        self.assertEqual(props["hue"]["enum"], list(pools.HUES))
        self.assertEqual(props["saturation"]["enum"], list(pools.SATURATIONS))
        self.assertEqual(props["brightness"]["enum"], list(pools.BRIGHTNESSES))
        self.assertEqual(props["texture"]["enum"], list(pools.TEXTURES))
        self.assertFalse(schema["additionalProperties"])

    def test_candidate_schema_does_not_let_the_model_set_ids(self):
        self.assertNotIn("id", candidate_schema()["properties"])
        self.assertNotIn("target_emotion", candidate_schema()["properties"])

    def test_unity_config_strips_pipeline_fields(self):
        stripped = unity_config({**room(), "_sketch": "###", "trial_index": 3})
        self.assertEqual(set(stripped), set(room()))


class TestGeneratedUnityConstants(unittest.TestCase):
    def test_the_committed_cs_file_is_up_to_date(self):
        # If this fails, someone changed pools.py without regenerating:
        #   python3 -m pipeline.cli emit-unity-pools
        from pipeline.emit_unity import render

        path = os.path.join(ROOT, "unity", "PoolConstants.cs")
        with open(path, encoding="utf-8") as handle:
            self.assertEqual(handle.read(), render(), "unity/PoolConstants.cs is stale")


class TestPoolFileIsData(unittest.TestCase):
    """configs/pools.json must be the only place the values live.

    Scene brief section 7 step 4 wants filling in the literature-derived values to
    "only touch data". These tests are what makes that claim checkable.
    """

    def _write(self, tmpdir, mutate):
        with open(os.path.join(ROOT, "configs", "pools.json"), encoding="utf-8") as handle:
            data = json.load(handle)
        mutate(data)
        path = os.path.join(tmpdir, "pools.json")
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(data, handle)
        return path

    def test_committed_pool_file_loads(self):
        self.assertEqual(pools._load(pools.POOL_FILE)["manipulated"]["hue"], list(pools.HUES))

    def test_values_flow_all_the_way_to_the_generated_cs(self):
        # The whole point: swap the data, and the prompt, schema, validator and the
        # C# constants all follow, with no code edited.
        import subprocess
        import tempfile

        def mutate(data):
            data["manipulated"]["hue"] = [0, 120, 240]
            data["manipulated"]["texture"] = ["smooth_plaster", "rough_render"]

        with tempfile.TemporaryDirectory() as tmpdir:
            path = self._write(tmpdir, mutate)
            env = {**os.environ, "EMOTION_ROOMS_POOLS": path}
            code = (
                "from pipeline import pools;"
                "from pipeline.emit_unity import render;"
                "print(list(pools.HUES));"
                "print(pools.design_space_size());"
                "print('smooth_plaster' in render())"
            )
            out = subprocess.run(
                [sys.executable, "-c", code],
                cwd=ROOT, env=env, capture_output=True, text=True, check=True,
            ).stdout.split("\n")

        self.assertEqual(out[0], "[0, 120, 240]")
        self.assertEqual(out[1], str(3 * len(pools.SATURATIONS) * len(pools.BRIGHTNESSES) * 2))
        self.assertEqual(out[2], "True", "generated C# did not follow the pool file")

    def test_a_malformed_pool_file_fails_loudly(self):
        # A pool file that quietly loses a constraint would widen the gate, so every
        # one of these must raise rather than degrade.
        import tempfile

        cases = {
            "dropped variable": lambda d: d["manipulated"].pop("saturation"),
            "empty pool": lambda d: d["manipulated"].update(hue=[]),
            "duplicate values": lambda d: d["manipulated"].update(hue=[0, 0, 30]),
            "pool is not a list": lambda d: d["manipulated"].update(hue=42),
            "wrong emotion count": lambda d: d.update(emotions=["calm", "tense"]),
            "no researcher_set": lambda d: d.pop("researcher_set"),
        }
        for name, mutate in cases.items():
            with self.subTest(name), tempfile.TemporaryDirectory() as tmpdir:
                path = self._write(tmpdir, mutate)
                with self.assertRaises(RuntimeError):
                    pools._load(__import__("pathlib").Path(path))

    def test_missing_and_unparseable_files_fail_loudly(self):
        import pathlib
        import tempfile

        with self.assertRaises(RuntimeError):
            pools._load(pathlib.Path("/nonexistent/pools.json"))
        with tempfile.TemporaryDirectory() as tmpdir:
            bad = pathlib.Path(tmpdir) / "pools.json"
            bad.write_text("{not json", encoding="utf-8")
            with self.assertRaises(RuntimeError):
                pools._load(bad)

    def test_provisional_flag_is_still_true(self):
        # Fails the day someone flips it. That should be a deliberate act taken only
        # when Mengkai confirms the values are locked -- scene brief section 4 asks
        # that nothing be built against specific values before then.
        self.assertTrue(
            pools.PROVISIONAL,
            "pools.json says the values are final -- confirm Mengkai locked them, "
            "then update README.md and delete this assertion",
        )


if __name__ == "__main__":
    unittest.main()
