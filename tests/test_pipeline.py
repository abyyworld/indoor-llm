"""Tests for the parts that must not break: the pools, the gate, the control arm.

    python3 -m unittest discover -s tests -v

No API key and no network needed -- nothing here calls Claude.
"""

from __future__ import annotations

import json
from collections import Counter
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
    "hue": 240,
    "saturation": 0.2,
    "brightness": 300,
    "texture": "plaster",
"roughness": "smooth",
    "rationale": "Low-saturation cool blue with soft even light reads as restful.",
}


def room(**overrides) -> dict:
    merged = dict(VALID_ROOM)
    merged.update(overrides)
    return merged


EXPECTED_DESIGN_SPACE = (
    len(pools.HUES) * len(pools.SATURATIONS) * len(pools.BRIGHTNESSES)
    * len(pools.TEXTURES) * max(len(pools.ROUGHNESSES), 1)
)


class TestPools(unittest.TestCase):
    def test_design_space_matches_the_pools(self):
        # 10 hues x 2 saturations x 5 lux x 3 materials x 2 roughness = 600, doubling
        # with shape. Roughness joined the count on 3 Aug when she confirmed the levels;
        # before that the design space was silently understated by a factor of two.
        self.assertEqual(pools.design_space_size(), EXPECTED_DESIGN_SPACE)
        self.assertEqual(pools.design_space_size(include_shape=True), EXPECTED_DESIGN_SPACE * 2)

    def test_enumeration_is_complete_and_distinct(self):
        rooms = list(pools.enumerate_rooms())
        self.assertEqual(len(rooms), EXPECTED_DESIGN_SPACE)
        combos = {tuple(sorted(r.items())) for r in rooms}
        self.assertEqual(len(combos), EXPECTED_DESIGN_SPACE)

    def test_hues_are_the_munsell_calibrated_ten(self):
        # research/variable-pool/color_pool_diagram.png (draft v5). Adopted from the
        # Munsell-to-HSV mapping in Febbraio et al. (2025). Deliberately NOT an even
        # step: 150 and 210 are absent. A test that demanded uniform spacing would be
        # asserting the old pool, so it asserts the actual categories instead.
        self.assertEqual(
            list(pools.HUES), [0, 30, 60, 90, 120, 180, 240, 270, 300, 330]
        )
        self.assertNotIn(150, pools.HUES)
        self.assertNotIn(210, pools.HUES)
        self.assertLess(max(pools.HUES), 360)  # no wraparound duplicate of 0

    def test_warm_and_cool_sets_partition_the_hue_pool(self):
        # Song et al. (2025) valence split, per the same diagram.
        warm = {60, 30, 0, 330, 300}
        cool = {270, 240, 180, 120, 90}
        self.assertEqual(warm | cool, set(pools.HUES))
        self.assertEqual(warm & cool, set())

    def test_saturations_sit_inside_the_ecological_ceiling(self):
        # Yi and Kang (2020): 92% of real surfaces are below 35% saturation. Both of
        # her fixed points are meant to sit in the plausible range.
        self.assertEqual(list(pools.SATURATIONS), [0.2, 0.4])
        self.assertLessEqual(max(pools.SATURATIONS), 0.4)

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
            "hue": 240,
            "saturation": 0.2,
            "brightness": 300,
            "texture": "plaster",
"roughness": "smooth",
            "rationale": "Cool and soft.",
        }
        self.assertEqual(validate_candidate(candidate), [])

    def test_rejects_a_candidate_that_sets_its_own_id(self):
        candidate = {
            "id": "calm_001",
            "hue": 240,
            "saturation": 0.2,
            "brightness": 300,
            "texture": "plaster",
"roughness": "smooth",
            "rationale": "Cool and soft.",
        }
        fields = [v.field for v in validate_candidate(candidate)]
        self.assertIn("id", fields)

    def test_sketch_only_allowed_when_requested(self):
        candidate = {
            "hue": 240,
            "saturation": 0.2,
            "brightness": 300,
            "texture": "plaster",
"roughness": "smooth",
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
        # Keyed on every manipulated field. Keying on four of five would let two rooms
        # differing only in roughness collapse into one and report a false collision.
        rooms = random_rooms(200, seed=3, unique=True)
        combos = {
            (r["hue"], r["saturation"], r["brightness"], r["texture"], r.get("roughness"))
            for r in rooms
        }
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

    def test_default_session_is_the_eight_trial_design(self):
        # 4 emotions x 2 shapes = 8, Mengkai 2 Aug. Supersedes the spec's 16, which
        # assumed 2 variants per emotion and a between-subjects shape factor.
        session = build_session(self.batch(), participant="p01", seed=1)
        self.assertEqual(len(session.trials), 8)
        # 8 x (20 s exposure + 45 s form + 15 s transition) = 10.67 min.
        self.assertAlmostEqual(session.minutes, 8 * (80 / 60), places=4)
        self.assertFalse(session.over_budget)

    def test_the_old_sixteen_trial_design_is_still_reachable(self):
        # Kept explicit rather than as a default, so nobody gets 16 trials by accident.
        session = build_session(
            self.batch(), participant="p01", seed=1, variants_per_emotion=2
        )
        self.assertEqual(len(session.trials), 16)

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

    def test_controls_add_trials_and_the_budget_still_binds_eventually(self):
        # Both control arms are out of the participant design (neutral dropped, random
        # cancelled), but they stay reachable. At 8 emotion trials the budget no longer
        # bites, so the check is that controls add trials and that the budget still
        # fires when enough are added.
        session = build_session(
            self.batch(), participant="p01", seed=1, neutral_trials=4, random_trials=4
        )
        self.assertEqual(len(session.trials), 16)
        self.assertFalse(session.over_budget)

        loaded = build_session(
            self.batch(), participant="p01", seed=1, neutral_trials=8, random_trials=8
        )
        self.assertEqual(len(loaded.trials), 24)
        self.assertTrue(loaded.over_budget)

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

        path = os.path.join(ROOT, "unity", "Assets", "Scripts", "EmotionRooms",
                            "PoolConstants.cs")
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
        expected = (3 * len(pools.SATURATIONS) * len(pools.BRIGHTNESSES) * 2
                    * max(len(pools.ROUGHNESSES), 1))
        self.assertEqual(out[1], str(expected))
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

    def test_values_are_no_longer_provisional(self):
        # Flipped on 3 Aug 2026, deliberately, once every manipulated pool was
        # Mengkai's final value: hue and saturation 30 Jul, roughness and material type
        # 3 Aug, illuminance 3 Aug. The guard existed to make this a considered act
        # rather than a drift, which is what it was.
        self.assertFalse(pools.PROVISIONAL)


class TestHandoffFile(unittest.TestCase):
    """The gate on Mengkai's finalised 8-cell file.

    She runs the sampling and aggregation herself, so this is the only automated check
    between her values and a participant's headset.
    """

    def _doc(self, **overrides):
        from pipeline.pools import EMOTIONS, SHAPES

        doc = {
            "format": "emotion-rooms-handoff/v2",
            "variables": {
                "hue_category": {"type": "enum", "values": ["warm", "cool", "neutral"]},
                "saturation_pct": {"type": "bands", "unit": "%", "bands": [[10, 20], [30, 40]]},
                "material": {"type": "enum", "values": ["rough", "smooth"]},
                "brightness_lux": {
                    "type": "per_emotion_bands",
                    "unit": "lx",
                    "bands": {"calm": [45, 150], "tense": [670, 780], "excited": None, "depressed": None},
                },
            },
            "cells": [
                {
                    "target_emotion": emotion,
                    "shape": shape,
                    "hue_category": "cool",
                    "saturation_pct": 15,
                    "material": "smooth",
                    "brightness_lux": {"calm": 95, "tense": 720}.get(emotion, 500),
                }
                for emotion in EMOTIONS
                for shape in SHAPES
            ],
        }
        doc.update(overrides)
        return doc

    def test_a_complete_file_passes(self):
        from pipeline.handoff import validate_handoff

        self.assertEqual(validate_handoff(self._doc()), [])

    def test_shipped_template_is_parseable_and_fails_while_blank(self):
        from pipeline.handoff import validate_handoff

        path = os.path.join(ROOT, "configs", "handoff_TEMPLATE.json")
        with open(path, encoding="utf-8") as handle:
            template = json.load(handle)
        self.assertTrue(validate_handoff(template))

    def test_saturation_between_the_two_bands_is_caught(self):
        # 25% sits in the gap between 10-20 and 30-40 and must not be accepted.
        from pipeline.handoff import validate_handoff

        doc = self._doc()
        doc["cells"][0]["saturation_pct"] = 25
        errors = validate_handoff(doc)
        self.assertTrue(any("outside every declared band" in e for e in errors), errors)

    def test_out_of_band_brightness_is_caught(self):
        # The real case: tense taking calm's low band, which her batch-2 samples did.
        from pipeline.handoff import validate_handoff

        doc = self._doc()
        for cell in doc["cells"]:
            if cell["target_emotion"] == "tense":
                cell["brightness_lux"] = 95
        errors = validate_handoff(doc)
        self.assertEqual(len(errors), 2)
        self.assertTrue(all("outside declared band" in e for e in errors))

    def test_unlocked_emotions_accept_any_positive_value(self):
        from pipeline.handoff import exploratory_cells, validate_handoff

        doc = self._doc()
        for cell in doc["cells"]:
            if cell["target_emotion"] in ("excited", "depressed"):
                cell["brightness_lux"] = 3000
        self.assertEqual(validate_handoff(doc), [])
        self.assertEqual(len(exploratory_cells(doc)), 4)

    def test_nonsense_values_are_still_caught_when_unlocked(self):
        from pipeline.handoff import validate_handoff

        for bad in (0, -5, "dim", True):
            with self.subTest(bad):
                doc = self._doc()
                doc["cells"][4]["brightness_lux"] = bad
                self.assertTrue(validate_handoff(doc))

    def test_material_works_as_one_variable_or_two(self):
        # She is deciding between keeping material as roughness alone, or splitting it
        # into roughness plus a type. Both must validate without a code change.
        from pipeline.handoff import validate_handoff

        split = self._doc()
        split["variables"]["material_type"] = {
            "type": "enum",
            "values": ["plaster", "concrete", "textile"],
        }
        for cell in split["cells"]:
            cell["material_type"] = "plaster"
        self.assertEqual(validate_handoff(split), [])

        # Declared optional, absent from cells: still valid while undecided.
        undecided = self._doc()
        undecided["variables"]["material_type"] = {
            "type": "enum",
            "values": ["plaster", "concrete", "textile"],
            "optional": True,
        }
        self.assertEqual(validate_handoff(undecided), [])

        # Declared required but missing from cells: caught.
        missing = self._doc()
        missing["variables"]["material_type"] = {"type": "enum", "values": ["plaster"]}
        self.assertTrue(any("missing material_type" in e for e in validate_handoff(missing)))

    def test_missing_and_duplicate_cells_are_caught(self):
        from pipeline.handoff import validate_handoff

        short = self._doc()
        short["cells"].pop()
        self.assertTrue(any("missing cell" in e for e in validate_handoff(short)))

        duped = self._doc()
        duped["cells"].append(dict(duped["cells"][0]))
        self.assertTrue(any("duplicate cell" in e for e in validate_handoff(duped)))

    def test_off_pool_values_are_caught(self):
        from pipeline.handoff import validate_handoff

        for key, bad in (("hue_category", "teal"), ("material", "velvet")):
            with self.subTest(key):
                doc = self._doc()
                doc["cells"][0][key] = bad
                self.assertTrue(any("not in declared pool" in e for e in validate_handoff(doc)))

    def test_a_malformed_contract_is_caught(self):
        from pipeline.handoff import validate_handoff

        cases = {
            "no variables block": lambda d: d.pop("variables"),
            "unknown type": lambda d: d["variables"].update(material={"type": "slider"}),
            "empty enum": lambda d: d["variables"].update(material={"type": "enum", "values": []}),
            "reversed band": lambda d: d["variables"].update(
                saturation_pct={"type": "bands", "bands": [[40, 30]]}
            ),
            "emotion missing a band": lambda d: d["variables"]["brightness_lux"]["bands"].pop("calm"),
        }
        for name, mutate in cases.items():
            with self.subTest(name):
                doc = self._doc()
                mutate(doc)
                self.assertTrue(validate_handoff(doc), f"{name} was not caught")

    def test_a_wrong_format_tag_is_caught(self):
        from pipeline.handoff import validate_handoff

        self.assertTrue(any("format" in e for e in validate_handoff(self._doc(format="v1"))))


if __name__ == "__main__":
    unittest.main()


class TestAggregation(unittest.TestCase):
    """PROPOSAL under test -- proposals-for-review.md section 4.

    Mengkai owns the method. These lock in the property she asked for: the chosen
    config is always a combination the model actually produced.
    """

    def test_medoid_output_is_always_a_real_sample(self):
        from pipeline.aggregate import medoid

        samples = [
            {"hue_category": "warm", "material": "rough", "saturation_pct": 40},
            {"hue_category": "cool", "material": "smooth", "saturation_pct": 20},
            {"hue_category": "warm", "material": "rough", "saturation_pct": 40},
        ]
        chosen, _ = medoid(samples)
        self.assertIn(chosen, samples)

    def test_modal_reconstruction_can_invent_a_pairing_the_medoid_cannot(self):
        # Her 31 Jul constraint, made mechanical. Per-variable modal here yields
        # warm+smooth, which no sample generated. The medoid cannot do that.
        from pipeline.aggregate import medoid, modal_reconstruction

        samples = (
            [{"hue_category": "warm", "material": "rough"}] * 3
            + [{"hue_category": "cool", "material": "smooth"}] * 2
            + [{"hue_category": "neutral", "material": "smooth"}] * 2
        )
        real = {(s["hue_category"], s["material"]) for s in samples}

        modal = modal_reconstruction(samples)
        self.assertNotIn((modal["hue_category"], modal["material"]), real)

        chosen, _ = medoid(samples)
        self.assertIn((chosen["hue_category"], chosen["material"]), real)

    def test_continuous_fields_are_normalised_so_lux_cannot_dominate(self):
        # Without normalising, a lux span of ~900 would swamp a saturation span of 20
        # and decide the medoid alone.
        from pipeline.aggregate import medoid

        samples = [
            {"saturation_pct": 20, "brightness_lux": 40},
            {"saturation_pct": 20, "brightness_lux": 45},
            {"saturation_pct": 40, "brightness_lux": 900},
        ]
        chosen, stats = medoid(samples)
        self.assertEqual(chosen["saturation_pct"], 20)
        self.assertEqual(stats["n_samples"], 3)

    def test_ties_are_deterministic(self):
        from pipeline.aggregate import medoid

        samples = [{"hue_category": "warm"}, {"hue_category": "cool"}]
        first, _ = medoid(samples)
        for _ in range(5):
            again, _ = medoid(samples)
            self.assertEqual(first, again)

    def test_identical_samples_report_full_consistency(self):
        from pipeline.aggregate import medoid

        samples = [{"hue_category": "warm", "material": "rough"}] * 4
        _, stats = medoid(samples)
        self.assertEqual(stats["consistency"], 1.0)
        self.assertEqual(stats["mode_share"]["hue_category"], 1.0)

    def test_empty_input_raises(self):
        from pipeline.aggregate import AggregationError, medoid

        with self.assertRaises(AggregationError):
            medoid([])


class TestSyntheticFixture(unittest.TestCase):
    def test_it_must_fail_validation(self):
        # It carries a deliberately out-of-band cell. If this ever passes, the gate
        # has broken and synthetic data could reach a participant.
        from pipeline.handoff import validate_handoff

        path = os.path.join(ROOT, "configs", "handoff_SYNTHETIC_test_only.json")
        with open(path, encoding="utf-8") as handle:
            doc = json.load(handle)
        self.assertTrue(validate_handoff(doc), "synthetic fixture unexpectedly passed")
        self.assertIn("_DO_NOT_SHIP", doc)


class TestOversightTrials(unittest.TestCase):
    """Phase B trial construction -- study-design-v2.md section 3. Not yet approved."""

    CONFIGS = [
        {"target_emotion": "calm", "hue": 240, "saturation": 0.2, "brightness": 150,
         "texture": "plaster", "rationale": "Cool blue, soft light."},
        {"target_emotion": "excited", "hue": 30, "saturation": 0.4, "brightness": 750,
         "texture": "plaster", "rationale": "Warm orange, bright."},
        {"target_emotion": "tense", "hue": 0, "saturation": 0.4, "brightness": 750,
         "texture": "concrete", "rationale": "Hard red, harsh light."},
    ]

    def test_swap_injects_a_value_the_agent_chose_elsewhere(self):
        from pipeline.oversight import swap

        calm, excited = self.CONFIGS[0], self.CONFIGS[1]
        out = swap(calm, excited, "hue")
        self.assertEqual(out["hue"], excited["hue"])
        self.assertEqual(out["saturation"], calm["saturation"])  # nothing else moved
        self.assertNotEqual(out["hue"], calm["hue"])

    def test_every_llm_controlled_variable_can_actually_be_swapped(self):
        # Regression. ATTRIBUTABLE spelled the material axis "material" while configs
        # spell it "texture", and _attributable_fields keeps only keys the config has,
        # so texture was silently unswappable: one of the five variables never appeared
        # in the block and could never be attributed to. A distribution test would not
        # have caught it -- the field just never showed up.
        from pipeline.oversight import swappable_fields

        calm, tense = self.CONFIGS[0], self.CONFIGS[2]
        self.assertIn("texture", swappable_fields(calm, tense))

    def test_attributable_covers_both_field_vocabularies(self):
        from pipeline.oversight import ATTRIBUTABLE

        self.assertIn("texture", ATTRIBUTABLE)   # this repo's configs
        self.assertIn("material", ATTRIBUTABLE)  # Mengkai's

    def test_swaping_with_an_identical_donor_value_is_refused(self):
        # Otherwise the trial would claim a fault that is not visible, and every
        # participant would be scored wrong for not seeing it.
        from pipeline.oversight import OversightError, swap

        a = {"hue": 240, "target_emotion": "calm"}
        b = {"hue": 240, "target_emotion": "depressed"}
        with self.assertRaises(OversightError):
            swap(a, b, "hue")

    def test_emotions_the_pool_cannot_separate_are_surfaced_not_hidden(self):
        # The tense/depressed worry, mechanically. If two emotions converge on identical
        # parameters there is nothing to swap between them, and that should be
        # visible rather than silently producing a degenerate trial.
        from pipeline.oversight import swappable_fields

        a = {"target_emotion": "tense", "hue": 240, "saturation": 0.2,
             "brightness": 150, "texture": "concrete"}
        b = {"target_emotion": "depressed", "hue": 240, "saturation": 0.2,
             "brightness": 150, "texture": "concrete"}
        self.assertEqual(swappable_fields(a, b), [])

        c = dict(b, brightness=750)
        self.assertIn("brightness", swappable_fields(a, c))

    def test_ground_truth_records_exactly_what_was_broken(self):
        import random as _r
        from pipeline.oversight import SWAPPED, make_trial

        trial = make_trial(self.CONFIGS[0], SWAPPED, rng=_r.Random(1), donors=self.CONFIGS)
        truth = trial["ground_truth"]
        field = truth["swapped_field"]

        self.assertIn(field, ("hue", "saturation", "brightness", "texture"))
        self.assertEqual(truth["original_value"], self.CONFIGS[0][field])
        self.assertEqual(trial["stimulus"][field], truth["swapped_in_value"])
        self.assertNotEqual(truth["original_value"], truth["swapped_in_value"])

    def test_rationale_mismatch_leaves_the_room_untouched(self):
        import random as _r
        from pipeline.oversight import RATIONALE_MISMATCHED, make_trial

        trial = make_trial(
            self.CONFIGS[0], RATIONALE_MISMATCHED, rng=_r.Random(3), donors=self.CONFIGS
        )
        self.assertEqual(trial["stimulus"], self.CONFIGS[0])  # artifact is genuine
        self.assertNotEqual(trial["rationale_shown"], self.CONFIGS[0]["rationale"])
        self.assertTrue(trial["ground_truth"]["rationale_is_wrong"])

    def test_faithful_trials_carry_no_fault(self):
        import random as _r
        from pipeline.oversight import FAITHFUL, make_trial, score_response

        trial = make_trial(self.CONFIGS[0], FAITHFUL, rng=_r.Random(0), donors=self.CONFIGS)
        self.assertIsNone(trial["ground_truth"]["swapped_field"])

        # Saying nothing is wrong is a correct rejection, not a miss.
        scored = score_response(trial, {"detected": False})
        self.assertTrue(scored["correct_rejection"])
        self.assertFalse(scored["miss"])

        # Flagging a faithful trial is a false alarm, which is how criterion gets measured.
        self.assertTrue(score_response(trial, {"detected": True})["false_alarm"])

    def test_blocks_are_balanced_and_deterministic(self):
        from pipeline.oversight import build_oversight_block

        a = build_oversight_block(self.CONFIGS, seed=7, participant="P01", trials_total=8)
        b = build_oversight_block(self.CONFIGS, seed=7, participant="P01", trials_total=8)
        self.assertEqual(a["trials"], b["trials"])

        counts = Counter(t["condition"] for t in a["trials"])
        # The detection contrast stays balanced: equally many genuinely-altered rooms
        # as genuinely-faithful ones. If altered rooms dominated, a participant would
        # work out that most rooms are broken and shift criterion toward "something is
        # wrong", and criterion would then measure the base rate rather than the
        # person. Mismatch trials sit outside this contrast by design.
        from pipeline.oversight import FAITHFUL, SWAPPED
        corrupted = counts[SWAPPED]
        self.assertEqual(counts[FAITHFUL], corrupted,
                         "an uneven base rate makes criterion a property of the block")

    def test_random_condition_needs_a_sampler(self):
        from pipeline.oversight import build_oversight_block

        block = build_oversight_block(self.CONFIGS, seed=1, participant="P01", trials_total=6)
        self.assertNotIn("random", block["conditions"])


class TestCounterbalancing(unittest.TestCase):
    """Trial ordering. Shape is within-subjects (Mengkai, 2 Aug), so every participant
    meets each emotion twice and ordering stops being a detail."""

    ROOMS = [
        {"id": f"r_{e}", "target_emotion": e, "source": "llm", "hue": 240,
         "saturation": 0.2, "brightness": 300, "texture": "plaster",
         "roughness": "smooth"}
        for e in ("calm", "excited", "depressed", "tense")
    ]

    def _session(self, mode, index):
        kwargs = {} if mode == "random" else {"participant_index": index}
        return build_session(
            self.ROOMS, participant=f"P{index:02d}", seed=1000 + index,
            variants_per_emotion=1, counterbalance=mode, **kwargs
        )

    def test_default_is_within_subjects_eight_trials(self):
        # The 2 Aug design: both shapes crossed within every emotion.
        session = build_session(self.ROOMS, participant="P01", seed=1, variants_per_emotion=1)
        self.assertEqual(len(session.trials), 8)
        by_emotion = Counter(t["target_emotion"] for t in session.trials)
        self.assertEqual(set(by_emotion.values()), {2})

    def test_between_subjects_path_still_works(self):
        session = build_session(
            self.ROOMS, participant="P01", seed=1, shapes=("curved",), variants_per_emotion=1
        )
        self.assertEqual(len(session.trials), 4)
        self.assertEqual({t["shape"] for t in session.trials}, {"curved"})

    def test_separated_ordering_maximises_the_gap_between_paired_trials(self):
        from pipeline.session import pair_separations

        for index in range(24):
            with self.subTest(index=index):
                gaps = pair_separations(self._session("separated", index).trials)
                # Four emotions over eight trials: four is the maximum achievable.
                self.assertEqual(set(gaps.values()), {4})

    def test_separated_never_places_a_pair_adjacently(self):
        from pipeline.session import pair_separations

        adjacent = sum(
            1
            for index in range(24)
            for gap in pair_separations(self._session("separated", index).trials).values()
            if gap == 1
        )
        self.assertEqual(adjacent, 0)

    def test_williams_would_place_pairs_adjacently_here(self):
        # Documents WHY separated exists. Williams balances first-order carryover, which
        # is a different property, and it happily puts an emotion pair side by side.
        from pipeline.session import pair_separations

        adjacent = sum(
            1
            for index in range(24)
            for gap in pair_separations(self._session("williams", index).trials).values()
            if gap == 1
        )
        self.assertGreater(adjacent, 0)

    def test_separated_does_not_confound_shape_with_session_half(self):
        # Blocking all of one shape into the first half would separate pairs just as
        # well while loading session drift onto the shape contrast.
        counts = Counter()
        for index in range(24):
            trials = self._session("separated", index).trials
            for position, trial in enumerate(trials):
                counts[(trial["shape"], "first" if position < 4 else "second")] += 1
        self.assertEqual(len(set(counts.values())), 1, f"shape is uneven across halves: {counts}")

    def test_separated_balances_first_position(self):
        firsts = Counter(self._session("separated", i).trials[0]["target_emotion"] for i in range(24))
        self.assertEqual(set(firsts.values()), {6})
        shapes = Counter(self._session("separated", i).trials[0]["shape"] for i in range(24))
        self.assertEqual(set(shapes.values()), {12})

    def test_williams_square_is_balanced_both_ways(self):
        from pipeline.session import _check_williams, williams_square

        for n in (2, 4, 6, 8):
            with self.subTest(n=n):
                self.assertEqual(_check_williams(williams_square(n)), [])

    def test_odd_williams_is_refused_rather_than_silently_wrong(self):
        from pipeline.session import williams_square

        with self.assertRaises(NotImplementedError):
            williams_square(5)

    def test_counterbalance_choice_is_recorded_on_the_session(self):
        session = self._session("separated", 3)
        self.assertEqual(session.counterbalance, "separated")
        self.assertEqual(session.participant_index, 3)

    def test_counterbalanced_modes_need_a_participant_index(self):
        for mode in ("separated", "williams"):
            with self.subTest(mode), self.assertRaises(ValueError):
                build_session(self.ROOMS, participant="P", seed=1,
                              variants_per_emotion=1, counterbalance=mode)

    def test_unknown_counterbalance_is_refused(self):
        with self.assertRaises(ValueError):
            build_session(self.ROOMS, participant="P", seed=1,
                          variants_per_emotion=1, counterbalance="latin")


class TestHandoffImport(unittest.TestCase):
    """Her vocabulary into ours. The two sides named the same five variables
    differently, and this translation is the only place that can get it wrong."""

    DOC = {
        "format": "emotion-rooms-handoff-v1",
        "variables": {
            "hue": {"type": "enum", "values": ["R", "Y", "B", "BG"]},
            "hue_category": {"type": "enum", "values": ["warm", "cool", "neutral"]},
            "saturation_pct": {"type": "enum", "values": [0, 20, 40]},
            "material": {"type": "enum", "values": ["rough", "smooth"]},
            "material_type": {"type": "enum", "values": ["plaster", "concrete", "textile"]},
            "brightness_lux": {"type": "enum", "values": [150, 300, 500, 750]},
        },
        "cells": [
            {"target_emotion": "calm", "shape": shape, "hue": "Y", "hue_category": "warm",
             "saturation_pct": 20, "material": "smooth", "material_type": "plaster",
             "brightness_lux": 300, "hue_detail": "pale straw plaster"}
            for shape in ("linear", "curved")
        ],
    }

    def test_units_and_names_are_translated(self):
        from pipeline.handoff import to_room_configs

        room = to_room_configs(self.DOC)[0]
        self.assertEqual(room["hue"], 60)          # Munsell Y -> 60 degrees
        self.assertEqual(room["saturation"], 0.2)  # percent -> fraction
        self.assertEqual(room["texture"], "plaster")   # material_type -> texture
        self.assertEqual(room["roughness"], "smooth")  # material -> roughness
        self.assertEqual(room["brightness"], 300.0)

    def test_converted_rooms_pass_the_same_validator_as_any_stimulus(self):
        from pipeline.handoff import to_room_configs
        from pipeline.validate import format_violations, validate_batch

        accepted, rejected = validate_batch(to_room_configs(self.DOC))
        self.assertEqual(rejected, [],
                         "\n".join(format_violations(v) for _, v in rejected))
        self.assertEqual(len(accepted), 2)

    def test_the_two_shapes_of_an_emotion_stay_identical(self):
        # The whole point of her resample. If the translation broke it, the shape
        # contrast would compare unrelated rooms again.
        from pipeline.handoff import to_room_configs

        linear, curved = to_room_configs(self.DOC)
        for field in ("hue", "saturation", "brightness", "texture", "roughness"):
            self.assertEqual(linear[field], curved[field], field)
        self.assertNotEqual(linear["shape"], curved["shape"])

    def test_an_achromatic_cell_is_refused_rather_than_given_a_colour(self):
        # Her achromatic rule says hue is meaningless at zero saturation, and this
        # repo's pool has no zero. Inventing a hue would put colour in a room
        # specified as having none.
        from pipeline.handoff import HandoffError, to_room_configs

        doc = json.loads(json.dumps(self.DOC))
        doc["cells"][0]["hue"] = "black"
        doc["cells"][0]["saturation_pct"] = 0
        with self.assertRaises(HandoffError):
            to_room_configs(doc)

    def test_provenance_travels_beside_the_rooms_not_inside_them(self):
        # An unknown key on a room would weaken the validator that keeps unvalidated
        # fields out of a stimulus.
        from pipeline.handoff import provenance, to_room_configs

        for room in to_room_configs(self.DOC):
            self.assertNotIn("_source_cell", room)
        self.assertEqual(len(provenance(self.DOC)), 2)


class TestPhaseBMeasurement(unittest.TestCase):
    """The four things that made Phase B uninterpretable, as invariants.

    Each of these is a measurement defect rather than a matter of taste: a reviewer who
    knows signal detection theory finds them mechanically, and any one of them is a
    reject rather than a revision.
    """

    CONFIGS = [
        {"target_emotion": "calm", "hue": 240, "saturation": 0.2, "brightness": 150,
         "texture": "plaster", "roughness": "smooth", "rationale": "Cool, soft."},
        {"target_emotion": "excited", "hue": 30, "saturation": 0.4, "brightness": 750,
         "texture": "plaster", "roughness": "rough", "rationale": "Warm, bright."},
        {"target_emotion": "tense", "hue": 0, "saturation": 0.4, "brightness": 750,
         "texture": "concrete", "roughness": "rough", "rationale": "Hard, harsh."},
        {"target_emotion": "depressed", "hue": 240, "saturation": 0.2, "brightness": 150,
         "texture": "textile", "roughness": "rough", "rationale": "Dim, muted."},
    ]

    def _block(self, **kwargs):
        from pipeline.controls import random_rooms
        from pipeline.oversight import build_oversight_block

        def sampler(rng):
            room = random_rooms(1, seed=rng.randrange(1 << 30))[0]
            return {k: room[k] for k in ("hue", "saturation", "brightness", "texture")}

        return build_oversight_block(self.CONFIGS, participant="P01", seed=5,
                                     pool_sampler=sampler, **kwargs)

    def test_the_block_is_long_enough_for_a_usable_d_prime(self):
        """Enough trials on each side of the detection contrast to estimate a rate.

        Three faithful trials give a false-alarm rate that can only be 0, .33, .67 or
        1, and a d-prime computed from that is not an estimate. The bar is twelve a
        side rather than sixteen because a third of the block now carries the
        rationale-mismatch condition, which answers a different question; twelve
        still gives a false-alarm rate on a usable grid, and the analysis pools
        partially across participants.
        """
        from pipeline.oversight import FAITHFUL, SWAPPED

        trials = self._block()["trials"]
        faithful = sum(1 for t in trials if t["condition"] == FAITHFUL)
        swapped = sum(1 for t in trials if t["condition"] == SWAPPED)
        self.assertGreaterEqual(len(trials), 32)
        self.assertGreaterEqual(faithful, 12)
        self.assertGreaterEqual(swapped, 12)

    def test_the_detection_contrast_has_an_even_base_rate(self):
        """Even across the trials d-prime is computed FROM, which is not all of them.

        The block also carries rationale_mismatched trials, whose rooms are unaltered
        and which are analysed as their own contrast. The invariant that matters is
        that the trials the detection measure is built from -- rooms genuinely altered
        against rooms genuinely not -- are balanced, so criterion stays a property of
        the participant rather than of the block.
        """
        from pipeline.oversight import FAITHFUL, SWAPPED

        trials = self._block()["trials"]
        faithful = sum(1 for t in trials if t["condition"] == FAITHFUL)
        swapped = sum(1 for t in trials if t["condition"] == SWAPPED)
        self.assertEqual(faithful, swapped,
                         "the faithful/altered contrast must stay balanced")

    def test_rationale_mismatch_is_present_but_never_scoreable_as_an_alteration(self):
        """It is in the block now, and its room is still unaltered.

        These trials are the point of the study: the room is genuine and only the
        stated reasoning is wrong, so flagging one is evidence the participant is
        checking the account rather than the artifact. The old invariant kept them
        out of the block entirely, for a reason that has not gone away -- counting
        one as signal would mark a correct "nothing is wrong" as a miss and
        contaminate d-prime. That reason is now enforced in the data rather than by
        exclusion: the ground truth names no altered field, so any analysis that
        selects on swapped_field cannot sweep these into the detection contrast.
        """
        from pipeline.oversight import RATIONALE_MISMATCHED

        trials = self._block()["trials"]
        mismatched = [t for t in trials if t["condition"] == RATIONALE_MISMATCHED]
        self.assertTrue(mismatched, "the mismatch condition carries the main contrast")

        for trial in mismatched:
            truth = trial["ground_truth"]
            self.assertIsNone(truth["swapped_field"],
                              "a mismatched room is not altered; only its story is")
            self.assertTrue(truth["rationale_is_wrong"])
            self.assertTrue(trial["explanation_shown"],
                            "a mismatched rationale nobody sees is not a condition")

    def test_the_rationale_block_asks_its_own_question(self):
        from pipeline.oversight import build_rationale_block

        block = build_rationale_block(self.CONFIGS, seed=5, participant="P01")
        self.assertIn("reasoning", block["question"])

        kinds = Counter(t["condition"] for t in block["trials"])
        self.assertEqual(kinds["rationale_matched"], kinds["rationale_mismatched"],
                         "its own detection task needs its own even base rate")

    def test_corrected_trials_are_split_between_own_and_yoked(self):
        # Without a yoked comparison the correction effect cannot be told apart from
        # self-consistency, and that is the study's central measure.
        from pipeline.oversight import FAITHFUL, OWN, YOKED

        trials = self._block()["trials"]
        corrupted = [t for t in trials if t["condition"] != FAITHFUL]
        sources = Counter(t["correction_source"] for t in corrupted)

        self.assertEqual(sources[OWN], sources[YOKED])
        self.assertEqual(sources[OWN] + sources[YOKED], len(corrupted))

    def test_faithful_trials_carry_no_correction_source(self):
        # There is nothing to correct in a room that is not broken.
        from pipeline.oversight import FAITHFUL

        for trial in self._block()["trials"]:
            if trial["condition"] == FAITHFUL:
                self.assertEqual(trial["correction_source"], "")

    def test_every_yoked_trial_can_reconstruct_its_substitution(self):
        # The substituted value depends on what the participant chose, so it cannot be
        # written out in advance -- but the draw can still be deterministic. Seeding it
        # from the block makes a yoked trial reproducible from the trial file alone and
        # identical on both platforms, rather than a runtime coin flip nobody can
        # reconstruct afterwards.
        from pipeline.oversight import YOKED

        for trial in self._block()["trials"]:
            if trial["correction_source"] == YOKED:
                self.assertIsInstance(trial["sham_seed"], int)

    def test_the_sham_rule_is_stated_in_the_block(self):
        # The write-up and the ethics application have to describe the same procedure.
        block = self._block()
        self.assertIn("excluding", block["sham_rule"])

    def test_swapped_trials_record_the_value_that_would_repair_them(self):
        # A substitution that happened to be correct would read as a successful yoked
        # correction, so the runtime needs to know which value to avoid.
        from pipeline.oversight import SWAPPED

        for trial in self._block()["trials"]:
            if trial["condition"] == SWAPPED:
                self.assertIsNotNone(trial["ground_truth"]["original_value"])

    def test_the_split_is_deterministic_for_a_participant(self):
        a = self._block()["trials"]
        b = self._block()["trials"]
        self.assertEqual([t["correction_source"] for t in a],
                         [t["correction_source"] for t in b])


class TestPracticeRooms(unittest.TestCase):
    """The warm-up rooms have to pass the same validator as everything else.

    Regression: build-practice emitted target_emotion='practice' and source='practice'
    while neither was a legal pool value, so every practice room failed validation in
    Python and again in C#. The warm-up could never load, and nothing said so until a
    session was started and the runner refused it.
    """

    def test_practice_is_a_legal_label_in_both_pools(self):
        from pipeline import pools

        self.assertIn("practice", pools.TARGET_LABELS)
        self.assertIn("practice", pools.SOURCES)

    def test_generated_practice_rooms_validate(self):
        from pipeline.cli import _practice_rooms
        from pipeline.validate import format_violations, validate_room_config

        for room in _practice_rooms():
            violations = validate_room_config(room)
            self.assertEqual(violations, [], format_violations(violations))

    def test_practice_is_not_one_of_the_studied_emotions(self):
        # It must never be counted as a condition or reach the analysis as one.
        from pipeline import pools

        self.assertNotIn("practice", pools.EMOTIONS)


class TestInstruments(unittest.TestCase):
    """The published scales, at their published scoring. A transcription slip here makes
    every score incomparable to the literature the instrument was chosen for."""

    def test_ssq_maximum_matches_kennedy(self):
        from pipeline.instruments import SSQ_SYMPTOMS, score_ssq

        worst = score_ssq({key: 3 for key, _ in SSQ_SYMPTOMS})
        # Kennedy et al. (1993): total score maxes at 235.62 with every item "severe".
        self.assertAlmostEqual(worst["total"], 235.62, places=1)

    def test_ssq_zero_when_nothing_is_reported(self):
        from pipeline.instruments import score_ssq

        self.assertEqual(score_ssq({})["total"], 0.0)

    def test_every_ssq_subscale_item_is_a_real_symptom(self):
        from pipeline.instruments import SSQ_SUBSCALES, SSQ_SYMPTOMS

        known = {key for key, _ in SSQ_SYMPTOMS}
        for name, (items, _) in SSQ_SUBSCALES.items():
            for item in items:
                self.assertIn(item, known, f"{name} scores a symptom that does not exist")

    def test_raw_tlx_is_the_unweighted_mean(self):
        from pipeline.instruments import TLX_ITEMS, score_tlx

        answers = {key: 60 for key, *_ in TLX_ITEMS}
        self.assertAlmostEqual(score_tlx(answers)["raw_tlx"], 60.0)

    def test_trust_reverses_the_five_distrust_items(self):
        from pipeline.instruments import TRUST_ITEMS, score_trust

        # All 7s: the five distrust items become 1, the seven trust items stay 7.
        self.assertAlmostEqual(score_trust({k: 7 for k, _, _ in TRUST_ITEMS})["trust_mean"],
                               (5 * 1 + 7 * 7) / 12)
        self.assertEqual(sum(1 for _, _, rev in TRUST_ITEMS if rev), 5)

    def test_presence_reversed_items_are_flipped(self):
        from pipeline.instruments import IPQ_ITEMS, score_presence

        answers = {key: 7 for key, *_ in IPQ_ITEMS}
        scored = score_presence(answers)
        # A blanket 7 cannot come out as 7 overall, because six items are reverse-keyed.
        self.assertLess(scored["presence_mean"], 7.0)
        self.assertGreater(scored["presence_mean"], 1.0)

    def test_nothing_is_scheduled_during_the_trials(self):
        # The affect rating is the only thing collected inside a room. A questionnaire
        # mid-session would measure the interruption as much as the room.
        from pipeline.instruments import FORMS

        for form in FORMS:
            self.assertIn(form["when"], ("before", "after"), form["id"])

    def test_consent_and_debrief_sit_on_the_right_side_of_the_session(self):
        from pipeline.instruments import FORMS

        when = {f["id"]: f["when"] for f in FORMS}
        self.assertEqual(when["consent"], "before")
        self.assertEqual(when["ssq_before"], "before")
        # A debrief before the study would tell them what it is about.
        self.assertEqual(when["debrief"], "after")
        # TLX asks about the review block, so it cannot precede it.
        self.assertEqual(when["nasa_tlx"], "after")

    def test_form_and_item_ids_are_unique(self):
        from pipeline.instruments import FORMS

        self.assertEqual(len({f["id"] for f in FORMS}), len(FORMS))
        for form in FORMS:
            ids = [i["id"] for i in form["items"]]
            self.assertEqual(len(set(ids)), len(ids), f"{form['id']} repeats an item id")

    def test_awareness_asks_openly_before_it_prompts(self):
        # The checklist names the manipulated variables, so a free-text answer collected
        # after it is worthless. The open questions have to come first in item order.
        from pipeline.instruments import AWARENESS

        ids = [i["id"] for i in AWARENESS["items"]]
        self.assertLess(ids.index("guessed_purpose"), ids.index("noticed_colour"))
        self.assertLess(ids.index("noticed_varying"), ids.index("noticed_colour"))

    def test_attention_check_names_a_real_item(self):
        from pipeline.instruments import ATTENTION_CHECK, FORMS

        form_id, item_id, expected = ATTENTION_CHECK
        form = next(f for f in FORMS if f["id"] == form_id)
        item = next(i for i in form["items"] if i["id"] == item_id)
        self.assertIn(expected, item["options"])

    def test_unanswered_attention_check_is_not_a_failure(self):
        from pipeline.instruments import passed_attention_check

        self.assertIsNone(passed_attention_check({}))
        self.assertIsNone(passed_attention_check({"attention_check": ""}))
        self.assertTrue(passed_attention_check({"attention_check": "Disagree"}))
        self.assertFalse(passed_attention_check({"attention_check": "Agree"}))

    def test_baseline_mood_is_collected_before_any_room(self):
        from pipeline.instruments import FORMS

        when = {f["id"]: f["when"] for f in FORMS}
        self.assertEqual(when["baseline_mood"], "before")
        # Awareness must not be: naming the manipulated variables beforehand would
        # create the demand characteristics it exists to detect.
        self.assertEqual(when["awareness"], "after")

    def test_each_phase_gets_only_the_instruments_it_can_answer(self):
        # A Phase B participant never rated rooms, so asking which shape they preferred
        # or how present they felt is burden that produces noise.
        from pipeline.instruments import due

        b = {f["id"] for f in due("after", "B")}
        self.assertNotIn("preference", b)
        self.assertNotIn("presence", b)
        self.assertIn("nasa_tlx", b)
        self.assertIn("trust", b)

        a = {f["id"] for f in due("after", "A")}
        self.assertIn("presence", a)
        # TLX asks about the oversight task, which a Phase A participant never did.
        self.assertNotIn("nasa_tlx", a)
        self.assertNotIn("trust", a)

    def test_safety_and_consent_reach_everyone(self):
        from pipeline.instruments import due

        for phase in ("A", "B"):
            ids = {f["id"] for f in due("before", phase)} | {f["id"] for f in due("after", phase)}
            for essential in ("consent", "ssq_before", "ssq_after", "debrief"):
                self.assertIn(essential, ids, f"{essential} missing for phase {phase}")

    def test_no_phase_filter_returns_everything(self):
        # Somebody doing both halves answers the lot.
        from pipeline.instruments import FORMS, due

        self.assertEqual(len(due("before")) + len(due("after")), len(FORMS))

    def test_every_form_declares_its_phases(self):
        from pipeline.instruments import FORMS

        for form in FORMS:
            self.assertTrue(form.get("phases"), f"{form['id']} has no phases")

    def test_generated_json_carries_every_form(self):
        from pipeline.instruments import FORMS, as_dict

        self.assertEqual(len(as_dict()["forms"]), len(FORMS))


class TestParticipantBundle(unittest.TestCase):
    """One participant, one file. The bundle replaces five files, so anything it drops
    is information that no longer exists anywhere an analyst will look."""

    def _write(self, directory, name, header, rows):
        path = directory / name
        path.parent.mkdir(parents=True, exist_ok=True)
        lines = [",".join(header)] + [",".join(str(v) for v in r) for r in rows]
        path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    def _fixture(self, tmp):
        self._write(tmp, "responses.csv",
                    ["participant", "trial_index", "valence", "utc_ms"],
                    [["p01", 1, 7, 1000], ["p01", 2, 3, 3000], ["p99", 1, 5, 2000]])
        self._write(tmp, "consent_log.csv",
                    ["participant", "event", "utc"],
                    [["p01", "consent_taken", "2026-08-05T10:00:00Z"]])
        self._write(tmp / "logs", "telemetry_p01_x.csv",
                    ["participant", "t_session", "lux", "utc_ms"],
                    [["p01", 0.1, 300, 2000]])

    def test_every_row_survives_and_is_tagged_by_source(self):
        import tempfile
        from pathlib import Path

        from pipeline.bundle import collect

        with tempfile.TemporaryDirectory() as name:
            tmp = Path(name)
            self._fixture(tmp)
            rows = collect(tmp, "p01")

        self.assertEqual(len(rows), 4)   # 2 trials + 1 consent + 1 telemetry
        self.assertEqual({r["source"] for r in rows},
                         {"trial", "consent", "telemetry"})

    def test_another_participants_rows_are_never_pulled_in(self):
        # Sharing one responses.csv across participants makes this the easy mistake,
        # and it would silently attribute p99's ratings to p01.
        import tempfile
        from pathlib import Path

        from pipeline.bundle import collect

        with tempfile.TemporaryDirectory() as name:
            tmp = Path(name)
            self._fixture(tmp)
            rows = collect(tmp, "p01")

        self.assertTrue(all(r.get("participant") == "p01" for r in rows))

    def test_columns_are_the_union_of_every_source(self):
        import tempfile
        from pathlib import Path

        from pipeline.bundle import collect, to_csv

        with tempfile.TemporaryDirectory() as name:
            tmp = Path(name)
            self._fixture(tmp)
            header = to_csv(collect(tmp, "p01")).splitlines()[0]

        for column in ("source", "trial_index", "valence", "event", "lux", "t_session"):
            self.assertIn(column, header)

    def test_questionnaire_rows_are_scored_into_the_summary(self):
        import tempfile
        from pathlib import Path

        from pipeline.bundle import collect, summarise

        with tempfile.TemporaryDirectory() as name:
            tmp = Path(name)
            self._fixture(tmp)
            self._write(tmp, "questionnaire_responses.csv",
                        ["participant", "form", "item", "answer", "state"],
                        [["p01", "nasa_tlx", "mental_demand", 60, "Completed"],
                         ["p01", "nasa_tlx", "effort", 40, "Completed"],
                         ["p01", "awareness", "noticed_colour", "Yes", "Completed"],
                         ["p01", "awareness", "noticed_shape", "Yes", "Completed"],
                         ["p01", "preference", "shape_preference",
                          "The curved rooms", "Completed"],
                         ["p01", "baseline_mood", "valence", 7, "Completed"]])
            report = summarise(collect(tmp, "p01"), "p01")

        scores = report["scores"]
        self.assertAlmostEqual(scores["nasa_tlx"]["raw_tlx"], 50.0)
        self.assertEqual(scores["awareness"]["noticed_count"], 2)
        self.assertTrue(scores["preference"]["prefers_curved"])
        self.assertEqual(scores["baseline_mood"]["baseline_valence"], 7.0)

    def test_ssq_change_needs_both_administrations(self):
        # A post-exposure score on its own cannot separate sickness the study caused
        # from a headache someone arrived with, so the change is only reported when
        # there is a baseline to change from.
        import tempfile
        from pathlib import Path

        from pipeline.bundle import collect, summarise

        with tempfile.TemporaryDirectory() as name:
            tmp = Path(name)
            self._fixture(tmp)
            self._write(tmp, "questionnaire_responses.csv",
                        ["participant", "form", "item", "answer", "state"],
                        [["p01", "ssq_after", "nausea", "Severe", "Completed"]])
            report = summarise(collect(tmp, "p01"), "p01")

        self.assertIn("ssq_after", report["scores"])
        self.assertNotIn("ssq_change", report["scores"])

    def test_a_withdrawn_session_is_flagged_not_silently_partial(self):
        import tempfile
        from pathlib import Path

        from pipeline.bundle import collect, summarise

        with tempfile.TemporaryDirectory() as name:
            tmp = Path(name)
            self._fixture(tmp)
            self._write(tmp, "consent_log.csv",
                        ["participant", "event", "utc"],
                        [["p01", "consent_taken", "2026-08-05T10:00:00Z"],
                         ["p01", "withdrawn", "2026-08-05T10:20:00Z"]])
            report = summarise(collect(tmp, "p01"), "p01")

        self.assertTrue(report["withdrew"])
        self.assertFalse(report["complete"])


class TestUnityCorrectionValues(unittest.TestCase):
    """The correction panel narrows to the attributed field, so the generated C# has to
    know the values of every field a participant can attribute to. A field missing from
    ValuesFor shows an empty correction screen mid-session."""

    def test_values_for_covers_every_attributable_field(self):
        from pipeline.emit_unity import render
        from pipeline.oversight import ATTRIBUTABLE

        cs = render()
        for field in ATTRIBUTABLE:
            self.assertIn(f'case "{field}"', cs, f"ValuesFor has no case for {field!r}")

    def test_attributable_list_is_mirrored_into_csharp(self):
        from pipeline.emit_unity import render
        from pipeline.oversight import ATTRIBUTABLE

        cs = render()
        block = cs.split("Attributable =", 1)[1].split(";", 1)[0]
        # The alias is deliberately absent on the C# side: it is one panel, one vocabulary.
        for field in ATTRIBUTABLE:
            if field == "material":
                continue
            self.assertIn(f'"{field}"', block)


class TestOversightConfidenceAndTiming(unittest.TestCase):
    """Confidence on attribution, not only detection, plus time per trial."""

    def _swapped(self):
        return {"condition": "swapped",
                "ground_truth": {"swapped_field": "hue", "original_value": 240,
                                 "swapped_in_value": 30, "rationale_is_wrong": False}}

    def test_confidently_wrong_is_distinguishable_from_calibrated(self):
        # Design section 3.3's most interesting outcome. Invisible without confidence
        # attached to the attribution itself rather than only to detection.
        from pipeline.oversight import score_response, summarise

        wrong = [score_response(self._swapped(),
                                {"detected": True, "attributed_field": "brightness",
                                 "attribution_confidence": 0.9, "duration_ms": 4000})
                 for _ in range(6)]
        calibrated = [score_response(self._swapped(),
                                     {"detected": True, "attributed_field": "hue",
                                      "attribution_confidence": 0.6, "duration_ms": 4000})
                      for _ in range(6)]

        self.assertGreater(summarise(wrong)["overconfidence"], 0.5)
        self.assertLess(abs(summarise(calibrated)["overconfidence"]), 0.5)
        self.assertGreater(summarise(wrong)["attribution_brier"],
                           summarise(calibrated)["attribution_brier"])

    def test_detection_and_attribution_confidence_are_separate(self):
        # Someone can be sure a room is wrong and have no idea which variable did it.
        from pipeline.oversight import score_response

        scored = score_response(self._swapped(),
                                {"detected": True, "detection_confidence": 0.95,
                                 "attributed_field": "material", "attribution_confidence": 0.2})
        self.assertEqual(scored["detection_confidence"], 0.95)
        self.assertEqual(scored["attribution_confidence"], 0.2)
        self.assertFalse(scored["attribution_correct"])

    def test_timing_is_split_by_attribution_correctness(self):
        # Oversight cost as a dependent variable: if being right takes far longer,
        # that says something about whether this kind of supervision scales.
        from pipeline.oversight import score_response, summarise

        scored = [score_response(self._swapped(),
                                 {"detected": True, "attributed_field": "hue",
                                  "attribution_confidence": 0.8, "duration_ms": 8000})
                  for _ in range(3)]
        scored += [score_response(self._swapped(),
                                  {"detected": True, "attributed_field": "material",
                                   "attribution_confidence": 0.8, "duration_ms": 2000})
                   for _ in range(3)]
        summary = summarise(scored)
        self.assertEqual(summary["median_ms_correct_attribution"], 8000)
        self.assertEqual(summary["median_ms_wrong_attribution"], 2000)

    def test_dprime_survives_perfect_performance(self):
        # A short block easily yields a hit rate of 1.0, which is infinite without the
        # log-linear correction.
        from pipeline.oversight import score_response, summarise

        scored = [score_response(self._swapped(), {"detected": True}) for _ in range(5)]
        scored += [score_response({"condition": "faithful", "ground_truth": {}},
                                  {"detected": False}) for _ in range(5)]
        d = summarise(scored)["d_prime"]
        self.assertTrue(d == d and d != float("inf"), f"d-prime is not finite: {d}")
        self.assertGreater(d, 1.0)


class TestDesignLevelRegressions(unittest.TestCase):
    """Guards for confounds that would survive into a paper if they slipped through."""

    ROOMS = [
        {"id": f"r_{e}", "target_emotion": e, "source": "llm", "hue": 240,
         "saturation": 0.2, "brightness": 300, "texture": "plaster",
         "roughness": "smooth"}
        for e in ("calm", "excited", "depressed", "tense")
    ]
    CONFIGS = [
        {"target_emotion": e, "hue": h, "saturation": 0.2, "brightness": 300,
         "texture": "plaster", "rationale": f"{e} room"}
        for e, h in zip(("calm", "excited", "depressed", "tense"), (240, 30, 0, 180))
    ]

    def test_the_default_session_can_be_counterbalanced(self):
        # A default that cannot express the study design is a trap. The old default of
        # 2 variants produced 16 trials and made "separated" impossible.
        build_session(self.ROOMS, participant="P", seed=1,
                      counterbalance="separated", participant_index=0)

    def test_minutes_per_room_follows_the_component_durations(self):
        # Was a hardcoded 1.5 that did not follow exposure dropping 30 s -> 20 s.
        from pipeline import session as S

        expected = (S.EXPOSURE_SECONDS + S.QUESTIONNAIRE_SECONDS + S.TRANSITION_SECONDS) / 60
        self.assertAlmostEqual(S.MINUTES_PER_ROOM, expected)
        self.assertAlmostEqual(S.EXPOSURE_SECONDS, 20.0)

    def test_oversight_conditions_are_not_confounded_with_emotion(self):
        # Drawing configs with replacement let a condition cluster on one emotion, so
        # attribution accuracy for "swapped" would partly be an accuracy figure for
        # whichever emotion happened to dominate it.
        from pipeline.oversight import build_oversight_block

        def sampler(rng):
            return {"hue": rng.choice([0, 30, 240]), "saturation": 0.2,
                    "brightness": 300, "texture": "plaster"}

        worst = 0
        for seed in range(30):
            block = build_oversight_block(
                self.CONFIGS, seed=seed, participant="P",
                trials_total=12, pool_sampler=sampler,
            )
            for condition in {t["condition"] for t in block["trials"]}:
                counts = Counter(
                    t["target_emotion_shown"]
                    for t in block["trials"]
                    if t["condition"] == condition
                )
                worst = max(worst, max(counts.values()) - min(counts.values()))

        # At most one trial apart. Cycling a reshuffled list is what caps it; drawing
        # with replacement would let a condition cluster on one emotion, and attribution
        # accuracy for "swapped" would partly be an accuracy figure for whichever
        # emotion dominated it. Exact evenness is only possible when the trial count
        # divides by the number of configs, which 16 faithful over 4 emotions does not.
        self.assertLessEqual(worst, 1, "condition is not evenly spread over emotions")


class TestAffectGrid(unittest.TestCase):
    """Target coordinates and the primary measure. Closes the gap where the analysis
    was defined as 'distance to target' with no targets existing."""

    def test_targets_sit_in_the_published_corners(self):
        from pipeline.affect import GRID_CENTRE, TARGETS

        # Affect Grid corners: stress top-left, excitement top-right, depression
        # bottom-left, relaxation bottom-right. The four emotions map definitionally.
        self.assertLess(TARGETS["tense"][0], GRID_CENTRE)
        self.assertGreater(TARGETS["tense"][1], GRID_CENTRE)
        self.assertGreater(TARGETS["excited"][0], GRID_CENTRE)
        self.assertGreater(TARGETS["excited"][1], GRID_CENTRE)
        self.assertLess(TARGETS["depressed"][0], GRID_CENTRE)
        self.assertLess(TARGETS["depressed"][1], GRID_CENTRE)
        self.assertGreater(TARGETS["calm"][0], GRID_CENTRE)
        self.assertLess(TARGETS["calm"][1], GRID_CENTRE)

    def test_every_target_is_equidistant_from_neutral(self):
        # Otherwise the emotions are not symmetric and one is easier to hit than
        # another purely by where its target was placed.
        from math import hypot
        from pipeline.affect import GRID_CENTRE, TARGETS

        distances = {
            round(hypot(x - GRID_CENTRE, y - GRID_CENTRE), 6) for x, y in TARGETS.values()
        }
        self.assertEqual(len(distances), 1, f"targets are not symmetric: {distances}")

    def test_a_perfect_response_scores_one(self):
        from pipeline.affect import TARGETS, congruence

        for emotion, (x, y) in TARGETS.items():
            with self.subTest(emotion):
                self.assertEqual(congruence(emotion, x, y)["congruence"], 1.0)
                self.assertEqual(congruence(emotion, x, y)["distance"], 0.0)

    def test_axis_errors_separate_the_two_failure_modes(self):
        from pipeline.affect import congruence

        pleasant_not_calming = congruence("calm", 7, 7)
        self.assertEqual(pleasant_not_calming["valence_error"], 0.0)
        self.assertEqual(pleasant_not_calming["arousal_error"], 4.0)

        calming_not_pleasant = congruence("calm", 3, 3)
        self.assertEqual(calming_not_pleasant["valence_error"], -4.0)
        self.assertEqual(calming_not_pleasant["arousal_error"], 0.0)

    def test_off_grid_responses_are_refused_not_clamped(self):
        # A silently clamped response is a fabricated one.
        from pipeline.affect import AffectError, congruence

        for bad in ((0, 5), (10, 5), (5, 0), (5, 10), ("x", 5), (True, 5)):
            with self.subTest(bad), self.assertRaises(AffectError):
                congruence("calm", bad[0], bad[1])

    def test_confusion_matrix_surfaces_the_tense_collapse(self):
        # If tense responses land nearest the depressed target, that is the collapse
        # appearing in participant data rather than in the parameters.
        from pipeline.affect import confusion_matrix

        trials = [{"target_emotion": "tense", "valence": 3, "arousal": 4}] * 5
        matrix = confusion_matrix(trials)
        self.assertEqual(matrix["tense"]["depressed"], 5)
        self.assertEqual(matrix["tense"]["tense"], 0)

    def test_hit_rate_is_interpretable_against_chance(self):
        from pipeline.affect import summarise_congruence

        trials = [{"target_emotion": "calm", "valence": 7, "arousal": 3}] * 4
        summary = summarise_congruence(trials)
        self.assertEqual(summary["calm"]["hit_rate"], 1.0)  # chance is 0.25
        self.assertEqual(summary["calm"]["mean_distance"], 0.0)


class TestManipulationCheck(unittest.TestCase):
    """Illuminance is now ONE shared pool across all four emotions (Mengkai, 3 Aug),
    not a band per emotion. These assert what that means rather than what the earlier
    per-emotion design meant."""

    def test_no_emotion_has_a_locked_illuminance_band(self):
        from pipeline.affect import illuminance_bands

        bands = illuminance_bands()
        self.assertEqual(set(bands), set(pools.EMOTIONS))
        self.assertTrue(all(b is None for b in bands.values()), bands)

    def test_every_emotion_reports_no_locked_range(self):
        # Not a pass and not a fail. There is no expectation to compare against.
        from pipeline.affect import check_illuminance

        for emotion in pools.EMOTIONS:
            with self.subTest(emotion):
                result = check_illuminance(emotion, 500)
                self.assertEqual(result["status"], "no_locked_range")
                self.assertIsNone(result["matches"])

    def test_there_is_effectively_no_illuminance_manipulation_check(self):
        # The consequence of a shared pool, asserted so it cannot be forgotten at
        # write-up: whether the model chose a lux level appropriate to its target is a
        # question this design cannot answer, because no level is designated
        # appropriate to any emotion. Every cell is unscoreable, and match_rate is None
        # rather than 1.0, so an absent check can never be mistaken for a passing one.
        from pipeline.affect import manipulation_check

        rooms = [{"target_emotion": e, "brightness": lux}
                 for e, lux in zip(pools.EMOTIONS, pools.BRIGHTNESSES)]
        summary = manipulation_check(rooms)
        self.assertEqual(summary["no_locked_range"], len(rooms))
        self.assertEqual(summary["matched"], 0)
        self.assertEqual(summary["missed"], 0)
        self.assertIsNone(summary["match_rate"])

    def test_a_band_still_bites_if_one_is_restored(self):
        # The machinery is kept because per-emotion bands may return. If one does, it
        # must actually constrain rather than having quietly become a no-op.
        from pipeline import affect

        original = affect.illuminance_bands
        affect.illuminance_bands = lambda: {**original(), "calm": (100.0, 200.0)}
        try:
            self.assertTrue(affect.check_illuminance("calm", 150)["matches"])
            self.assertFalse(affect.check_illuminance("calm", 750)["matches"])
        finally:
            affect.illuminance_bands = original

    def test_brightness_pool_is_lux_not_normalised(self):
        # Guards the unit. Normalised values would silently be read as about 1 lux.
        self.assertTrue(all(v >= 1 for v in pools.BRIGHTNESSES), pools.BRIGHTNESSES)
        self.assertEqual(list(pools.BRIGHTNESSES), [150, 300, 500, 750])


class TestOversightBlockContract(unittest.TestCase):
    """The block file is consumed by C# JsonUtility, which fails silently on a shape
    mismatch. These assert the contract the Unity side depends on."""

    CONFIGS = [
        {"id": f"{e}_demo_001", "target_emotion": e, "source": "handwritten",
         "hue": h, "saturation": s, "brightness": b, "texture": t,
         "roughness": "smooth" if t == "plaster" else "rough",
         "rationale": f"{e} room."}
        for e, h, s, b, t in [
            ("calm", 240, 0.2, 150, "plaster"),
            ("excited", 30, 0.4, 750, "plaster"),
            ("tense", 240, 0.4, 500, "concrete"),
            ("depressed", 240, 0.2, 150, "textile"),
        ]
    ]

    def _block(self):
        from pipeline.oversight import build_oversight_block

        return build_oversight_block(
            self.CONFIGS, seed=7, participant="b01", trials_total=8
        )

    def test_every_stimulus_would_pass_the_load_time_gate(self):
        # The review screen validates each stimulus before showing it, same as the
        # headset. A block containing an invalid stimulus would silently lose trials.
        block = self._block()
        for trial in block["trials"]:
            with self.subTest(trial["trial_id"]):
                self.assertEqual(validate_room_config(unity_config(trial["stimulus"])), [])

    def test_required_fields_are_present_on_every_trial(self):
        for trial in self._block()["trials"]:
            for key in ("trial_id", "condition", "target_emotion_shown",
                        "stimulus", "ground_truth"):
                self.assertIn(key, trial)
            self.assertIn("swapped_field", trial["ground_truth"])
            self.assertIn("rationale_is_wrong", trial["ground_truth"])

    def test_trial_id_is_the_join_key_not_stimulus_id(self):
        # Stimulus ids repeat across trials by design: the same room appears faithful
        # in one trial and swapped in another. Joining on id would merge them.
        block = self._block()
        trial_ids = [t["trial_id"] for t in block["trials"]]
        stimulus_ids = [t["stimulus"]["id"] for t in block["trials"]]
        self.assertEqual(len(set(trial_ids)), len(trial_ids))
        self.assertLess(len(set(stimulus_ids)), len(stimulus_ids))

    def test_only_swapped_trials_carry_a_scoreable_fault_location(self):
        for trial in self._block()["trials"]:
            field = trial["ground_truth"]["swapped_field"]
            if trial["condition"] == "swapped":
                self.assertIsNotNone(field)
            else:
                self.assertIsNone(field)


class TestConstrainedOrdering(unittest.TestCase):
    """Mengkai's alternative, 2 Aug. Reshuffle until spacing holds, rather than forcing
    a fixed mirrored structure."""

    ROOMS = [
        {"id": f"r_{e}", "target_emotion": e, "source": "llm", "hue": 240,
         "saturation": 0.2, "brightness": 300, "texture": "plaster",
         "roughness": "smooth"}
        for e in ("calm", "excited", "depressed", "tense")
    ]

    def _sessions(self, n=60, **kwargs):
        return [
            build_session(self.ROOMS, participant=f"P{i:03d}", seed=7000 + i,
                          counterbalance="constrained", **kwargs)
            for i in range(n)
        ]

    def test_no_pair_falls_below_the_minimum(self):
        from pipeline.session import pair_separations

        for separation in (2, 3):
            with self.subTest(separation):
                for session in self._sessions(min_separation=separation):
                    gaps = pair_separations(session.trials)
                    self.assertGreaterEqual(min(gaps.values()), separation)

    def test_it_is_the_default_ordering(self):
        # Because "separated" produces only 8 distinct orders across any sample size,
        # which is a detectable session structure in its own right.
        import inspect
        from pipeline.session import build_session as build

        self.assertEqual(
            inspect.signature(build).parameters["counterbalance"].default, "constrained"
        )

    def test_orders_vary_between_participants(self):
        # The whole point. "separated" has only 8 possible rows; this should be close to
        # one distinct order per participant.
        orders = {
            tuple(f"{t['target_emotion']}/{t['shape']}" for t in s.trials)
            for s in self._sessions(60)
        }
        self.assertGreater(len(orders), 50, f"only {len(orders)} distinct orders in 60")

    def test_separated_really_does_have_only_eight_orders(self):
        # Documents the weakness that motivated the switch, so nobody re-adopts it
        # without knowing.
        orders = {
            tuple(f"{t['target_emotion']}/{t['shape']}" for t in
                  build_session(self.ROOMS, participant=f"P{i}", seed=8000 + i,
                                counterbalance="separated", participant_index=i).trials)
            for i in range(60)
        }
        self.assertEqual(len(orders), 8)

    def test_an_impossible_constraint_fails_loudly(self):
        # Rather than looping forever or silently returning an unshuffled list.
        with self.assertRaises(ValueError):
            build_session(self.ROOMS, participant="P", seed=1,
                          counterbalance="constrained", min_separation=8)


class TestCorrectionLoop(unittest.TestCase):
    """Re-rating a corrected room. This is what makes the correction question
    analysable without the reference point Mengkai said was missing."""

    def test_the_reference_is_the_participants_own_first_rating(self):
        # No external "right value" is needed, which is exactly the objection this
        # answers: the comparison is within-participant, within-room.
        from pipeline.affect import correction_effect

        result = correction_effect("depressed", 4, 5, 3, 3)
        self.assertEqual(result["distance_after"], 0.0)
        self.assertTrue(result["improved"])
        self.assertGreater(result["improvement"], 0)

    def test_a_correction_that_makes_it_worse_is_recorded_as_such(self):
        from pipeline.affect import correction_effect

        result = correction_effect("calm", 7, 3, 3, 7)
        self.assertFalse(result["improved"])
        self.assertLess(result["improvement"], 0)

    def test_unapplied_corrections_are_counted_not_dropped(self):
        # "Did not help" and "never happened" look identical in the outcome column and
        # mean opposite things, so they must not be pooled.
        from pipeline.affect import summarise_corrections

        summary = summarise_corrections([
            {"target_emotion": "calm", "valence_before": 5, "arousal_before": 5,
             "valence_after": 7, "arousal_after": 3, "correction_applied": True},
            {"target_emotion": "calm", "valence_before": 7, "arousal_before": 3,
             "valence_after": 7, "arousal_after": 3, "correction_applied": False},
        ])
        self.assertEqual(summary["n"], 1)
        self.assertEqual(summary["not_applied"], 1)

    def test_missing_ratings_are_counted_separately_from_failures(self):
        from pipeline.affect import summarise_corrections

        summary = summarise_corrections([
            {"target_emotion": "calm", "valence_before": -1, "arousal_before": -1,
             "valence_after": -1, "arousal_after": -1},
        ])
        self.assertEqual(summary["n"], 0)
        self.assertEqual(summary["incomplete"], 1)
        self.assertIsNone(summary["improvement_rate"])

    def test_improvement_rate_is_interpretable_against_chance(self):
        from pipeline.affect import summarise_corrections

        records = [
            {"target_emotion": "calm", "valence_before": 5, "arousal_before": 5,
             "valence_after": 7, "arousal_after": 3, "correction_applied": True}
        ] * 4
        # Chance is 0.5 if corrections were unrelated to congruence, so this needs no
        # separate control condition to interpret.
        self.assertEqual(summarise_corrections(records)["improvement_rate"], 1.0)


class TestSeparability(unittest.TestCase):
    """Can the cells be told apart? The largest scientific risk, made checkable."""

    def _cells(self, spec):
        return [
            {"id": f"{e}_x", "target_emotion": e, "source": "llm", "hue": h,
             "saturation": s, "brightness": b, "texture": t,
             "roughness": "smooth" if t == "plaster" else "rough", "rationale": "x"}
            for e, h, s, b, t in spec
        ]

    WELL_SEPARATED = [("calm", 240, 0.2, 150, "plaster"),
                      ("tense", 240, 0.4, 500, "concrete"),
                      ("excited", 30, 0.4, 750, "plaster"),
                      ("depressed", 240, 0.2, 150, "textile")]

    def test_the_distance_fields_cover_this_repos_vocabulary(self):
        # The bug this exists to prevent: aggregate.py's field lists originally held
        # only Mengkai's template names, so brightness and texture were ignored by every
        # distance calculation. Two rooms differing ONLY on those read as identical,
        # which silently inverts the whole check.
        from pipeline.aggregate import CATEGORICAL, CONTINUOUS

        for field in ("hue", "saturation", "brightness", "texture"):
            self.assertIn(field, CATEGORICAL + CONTINUOUS, f"{field} is invisible to the distance")

    def test_a_well_separated_design_passes(self):
        from pipeline.separability import check

        report = check(self._cells(self.WELL_SEPARATED))
        self.assertTrue(report["safe"], report)
        self.assertEqual(report["identical_pairs"], [])

    def test_the_real_collapse_pattern_is_caught(self):
        # Her 4a batch-2 shape: calm/tense/depressed all cool, tense and depressed both
        # rough, separated by illuminance alone.
        from pipeline.separability import check

        report = check(self._cells([("calm", 240, 0.2, 150, "plaster"),
                                    ("tense", 240, 0.2, 300, "concrete"),
                                    ("excited", 30, 0.4, 750, "plaster"),
                                    ("depressed", 240, 0.2, 150, "concrete")]))
        self.assertFalse(report["safe"])
        close = report["close_pairs"][0]
        self.assertEqual({close["a"][0], close["b"][0]}, {"tense", "depressed"})
        self.assertEqual(close["differing_fields"], ["brightness"])

    def test_identical_cells_are_reported_as_identical_not_merely_close(self):
        from pipeline.separability import check

        # calm and tense share every manipulated value, which is the case this exists
        # to catch: two target emotions the design cannot tell apart at all.
        report = check(self._cells([("calm", 240, 0.2, 150, "plaster"),
                                    ("tense", 240, 0.2, 150, "plaster"),
                                    ("excited", 30, 0.4, 750, "concrete"),
                                    ("depressed", 180, 0.4, 300, "textile")]))
        self.assertTrue(report["identical_pairs"])
        self.assertFalse(report["safe"])

    def test_a_variable_that_never_varies_is_flagged_as_inert(self):
        # Manipulated in name only. Worth knowing before, not after, collection.
        from pipeline.separability import check

        report = check(self._cells([("calm", 240, 0.2, 150, "plaster"),
                                    ("tense", 240, 0.2, 500, "plaster"),
                                    ("excited", 240, 0.2, 750, "plaster"),
                                    ("depressed", 240, 0.2, 30, "plaster")]))
        self.assertIn("hue", report["inert_variables"])
        self.assertIn("texture", report["inert_variables"])
        self.assertFalse(report["safe"])

    def test_same_emotion_across_shapes_is_not_treated_as_a_collision(self):
        # Two shapes of the same emotion are SUPPOSED to be similar. Flagging that
        # would bury the real signal under noise.
        from pipeline.separability import check

        cells = self._cells(self.WELL_SEPARATED)
        for cell in cells:
            cell["shape"] = "linear"
        twins = [dict(c, shape="curved", id=c["id"] + "_c") for c in cells]
        report = check(cells + twins)
        self.assertTrue(report["safe"], report)


class TestConditionComparison(unittest.TestCase):
    """LLM-designed rooms against uniformly-drawn ones, from the review block's
    before-ratings. The falsifiability comparison the main study does not make."""

    def _records(self, faithful_offset, random_offset, n_participants=20):
        out = []
        for p in range(n_participants):
            for _ in range(3):
                out.append({"participant": f"p{p}", "condition": "faithful",
                            "target_emotion_shown": "calm",
                            "valence_before": 7 - faithful_offset, "arousal_before": 3})
                out.append({"participant": f"p{p}", "condition": "random",
                            "target_emotion_shown": "calm",
                            "valence_before": 7 - random_offset, "arousal_before": 3})
        return out

    def test_it_is_paired_within_participant(self):
        # Mengkai's power objection was about a BETWEEN-groups comparison. This one is
        # paired, which is where the design's power actually comes from.
        from pipeline.affect import compare_conditions

        result = compare_conditions(self._records(0, 4))
        self.assertEqual(result["n_participants_paired"], 20)
        self.assertLess(result["mean_paired_difference"], 0)
        self.assertEqual(result["participants_favouring_target"], 20)

    def test_it_detects_no_difference_when_there_is_none(self):
        from pipeline.affect import compare_conditions

        result = compare_conditions(self._records(2, 2))
        self.assertEqual(result["mean_paired_difference"], 0.0)

    def test_it_reports_the_wrong_direction_honestly(self):
        # If random rooms score BETTER, that has to be visible, not absorbed.
        from pipeline.affect import compare_conditions

        result = compare_conditions(self._records(4, 0))
        self.assertGreater(result["mean_paired_difference"], 0)
        self.assertEqual(result["participants_favouring_target"], 0)

    def test_uncollected_ratings_are_skipped_not_counted_as_zero(self):
        from pipeline.affect import compare_conditions

        records = self._records(0, 4)
        records.append({"participant": "pX", "condition": "faithful",
                        "target_emotion_shown": "calm",
                        "valence_before": -1, "arousal_before": -1})
        self.assertEqual(compare_conditions(records)["n_faithful"], 60)

    def test_random_rooms_are_redrawn_per_participant(self):
        # This is what answers her sparse-sampling objection: the pool is sampled
        # broadly because the draw is not fixed across participants.
        from pipeline.controls import random_rooms
        from pipeline.oversight import build_oversight_block

        configs = [
            {"id": f"{e}_x", "target_emotion": e, "source": "llm", "hue": h,
             "saturation": 0.2, "brightness": 300, "texture": "plaster",
             "roughness": "smooth", "rationale": "x"}
            for e, h in (("calm", 240), ("excited", 30), ("tense", 0), ("depressed", 180))
        ]

        def sampler(rng):
            room = random_rooms(1, seed=rng.randrange(1 << 30))[0]
            return {k: room[k] for k in ("hue", "saturation", "brightness", "texture")}

        # RANDOM is no longer in the study's default composition: a randomly drawn
        # room contradicts its stated reasoning on every variable at once, which makes
        # detection trivial and is not the manipulation under test. The condition
        # itself still exists and still has to sample broadly wherever it is used, so
        # the block is asked for it explicitly here.
        from pipeline.oversight import RANDOM

        seen = set()
        for p in range(24):
            block = build_oversight_block(configs, seed=9000 + p, participant=f"p{p}",
                                          trials_total=12, pool_sampler=sampler,
                                          composition=[(RANDOM, False, 12)])
            for trial in block["trials"]:
                if trial["condition"] == "random":
                    s = trial["stimulus"]
                    seen.add((s["hue"], s["saturation"], s["brightness"], s["texture"]))
        self.assertGreater(len(seen), 40, f"only {len(seen)} distinct random rooms in 72 draws")


class TestCorrectionConvergence(unittest.TestCase):
    """Do independent people correct the same way? This is the training-signal
    question, and it is distinct from whether a correction helped its author."""

    def _rows(self, values, emotion="depressed", field="brightness", original="30"):
        return [
            {"participant": f"p{i}", "target_emotion_shown": emotion,
             "swapped_field": field, "attributed_field": field,
             "corrected_value": str(v), "original_value": original}
            for i, v in enumerate(values)
        ]

    def test_unanimous_corrections_score_one(self):
        from pipeline.affect import correction_convergence

        result = correction_convergence(self._rows(["30"] * 8))
        group = result["groups"]["depressed/brightness"]
        self.assertEqual(group["mode_share"], 1.0)
        self.assertEqual(group["recovery_rate"], 1.0)

    def test_scattered_corrections_score_near_chance(self):
        from pipeline.affect import correction_convergence

        result = correction_convergence(self._rows([30, 100, 300, 700, 900, 30]))
        self.assertLess(result["groups"]["depressed/brightness"]["mode_share"], 0.4)

    def test_recovery_is_stricter_than_mode_share(self):
        # Everyone agreeing on the WRONG value gives a high mode share and zero
        # recovery. Reporting only mode share would call shared error consensus.
        from pipeline.affect import correction_convergence

        result = correction_convergence(self._rows(["900"] * 8, original="30"))
        group = result["groups"]["depressed/brightness"]
        self.assertEqual(group["mode_share"], 1.0)
        self.assertEqual(group["recovery_rate"], 0.0)

    def test_trials_with_no_swap_are_excluded(self):
        # A correction on an unmodified room has no original value to recover, so it
        # cannot speak to convergence and must not dilute it.
        from pipeline.affect import correction_convergence

        rows = self._rows(["30"] * 4)
        rows.append({"participant": "pX", "target_emotion_shown": "depressed",
                     "swapped_field": None, "attributed_field": "brightness",
                     "corrected_value": "900", "original_value": None})
        self.assertEqual(correction_convergence(rows)["groups"]["depressed/brightness"]["n"], 4)

    def test_groups_are_keyed_by_emotion_and_variable(self):
        # That is the unit a training signal would aggregate over.
        from pipeline.affect import correction_convergence

        rows = self._rows(["30"] * 3) + self._rows(["0"] * 3, emotion="tense", field="hue", original="0")
        result = correction_convergence(rows)
        self.assertEqual(result["n_groups"], 2)
        self.assertIn("tense/hue", result["groups"])


class TestRoughnessVariable(unittest.TestCase):
    """The fifth variable. Mengkai confirmed the split on 1 Aug; levels are pending, so
    it is optional until she confirms them."""

    BASE = dict(id="calm_007", target_emotion="calm", source="llm", hue=240,
                saturation=0.2, brightness=300, texture="plaster", roughness="smooth", rationale="x")

    def test_a_valid_roughness_validates(self):
        self.assertEqual(validate_room_config(dict(self.BASE, roughness="rough")), [])

    def test_an_off_pool_roughness_is_caught(self):
        # Optional must not mean unchecked. An unknown roughness would be a surface the
        # material system cannot render.
        violations = validate_room_config(dict(self.BASE, roughness="velvety"))
        self.assertEqual([v.field for v in violations], ["roughness"])

    def test_it_reaches_the_schema_the_model_sees(self):
        schema = candidate_schema()

        def find(node):
            if isinstance(node, dict):
                if isinstance(node.get("roughness"), dict):
                    return node["roughness"]
                for value in node.values():
                    found = find(value)
                    if found:
                        return found
            return None

        prop = find(schema)
        self.assertIsNotNone(prop, "roughness never reaches the model's schema")
        self.assertEqual(prop["enum"], list(pools.ROUGHNESSES))

    def test_it_reaches_the_generated_c_sharp(self):
        from pipeline.emit_unity import render

        self.assertIn("Roughnesses", render())

    def test_it_is_now_required(self):
        # Mengkai confirmed the levels on 3 Aug, so roughness_required was flipped in
        # pools.json. It was a data edit, which was the point of putting it there.
        self.assertTrue(pools.ROUGHNESS_IS_REQUIRED)
        self.assertEqual(list(pools.ROUGHNESSES), ["rough", "smooth"])

    def test_a_config_without_roughness_is_now_rejected(self):
        without = {k: v for k, v in self.BASE.items() if k != "roughness"}
        violations = validate_room_config(without)
        self.assertEqual([v.field for v in violations], ["roughness"])
