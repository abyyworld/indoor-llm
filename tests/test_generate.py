"""Tests for the generation loop, driven by a stub client.

The point is the control flow the spec section 4 asks for -- reject, re-ask, never
ship a bad config -- which is exactly the part you cannot check by eyeballing a real
run. No API key and no network needed.

    python3 -m unittest discover -s tests -v
"""

from __future__ import annotations

import json
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from pipeline.generate import (
    GenerationError,
    duplicate_rate,
    generate_candidates,
)
from pipeline.validate import validate_batch


class FakeBlock:
    def __init__(self, text: str):
        self.type = "text"
        self.text = text


class FakeMessage:
    def __init__(self, candidates: list[dict], stop_reason: str = "end_turn"):
        self.content = [FakeBlock(json.dumps({"candidates": candidates}))]
        self.stop_reason = stop_reason
        self.stop_details = None


class FakeRefusal:
    def __init__(self, explanation: str):
        self.content = []
        self.stop_reason = "refusal"

        class Details:
            pass

        self.stop_details = Details()
        self.stop_details.explanation = explanation


class FakeStream:
    def __init__(self, message):
        self._message = message

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        return False

    def get_final_message(self):
        return self._message


class FakeMessages:
    def __init__(self, responses: list):
        self.responses = list(responses)
        self.calls: list[dict] = []

    def stream(self, **kwargs):
        # Snapshot the message list: the generator keeps appending to the same list, so
        # storing the reference would show every call the conversation's final state.
        self.calls.append({**kwargs, "messages": list(kwargs["messages"])})
        if not self.responses:
            raise AssertionError("stub client ran out of scripted responses")
        return FakeStream(self.responses.pop(0))


class FakeClient:
    def __init__(self, responses: list):
        self.messages = FakeMessages(responses)


def candidate(hue=240, saturation=0.2, brightness=300, texture="plaster", roughness="smooth", **extra) -> dict:
    base = {
        "hue": hue,
        "saturation": saturation,
        "brightness": brightness,
        "texture": texture,
        "roughness": roughness,
        "rationale": "Stub rationale.",
    }
    base.update(extra)
    return base


def n_valid(count: int, start: int = 0) -> list[dict]:
    from pipeline.pools import HUES

    return [candidate(hue=HUES[(start + i) % len(HUES)]) for i in range(count)]


class TestGenerationHappyPath(unittest.TestCase):
    def test_assembles_valid_unity_configs(self):
        client = FakeClient([FakeMessage(n_valid(3))])
        result = generate_candidates(client, "calm", 3, chunk_size=25, verbose=False)

        self.assertEqual(len(result.rooms), 3)
        self.assertEqual(result.requests, 1)
        self.assertEqual(result.rejected, [])

        accepted, rejected = validate_batch(result.rooms)
        self.assertEqual(rejected, [])
        self.assertEqual([r["id"] for r in accepted], ["calm_001", "calm_002", "calm_003"])
        self.assertTrue(all(r["source"] == "llm" for r in accepted))
        self.assertTrue(all(r["target_emotion"] == "calm" for r in accepted))

    def test_request_is_shaped_the_way_the_skill_prescribes(self):
        client = FakeClient([FakeMessage(n_valid(2))])
        generate_candidates(client, "calm", 2, verbose=False)

        call = client.messages.calls[0]
        self.assertEqual(call["model"], "claude-opus-5")
        self.assertEqual(call["thinking"], {"type": "adaptive"})
        schema = call["output_config"]["format"]
        self.assertEqual(schema["type"], "json_schema")
        item = schema["schema"]["properties"]["candidates"]["items"]
        self.assertIn("enum", item["properties"]["hue"])
        self.assertFalse(item["additionalProperties"])

    def test_schema_count_matches_what_the_prompt_asks_for(self):
        client = FakeClient([FakeMessage(n_valid(2)), FakeMessage(n_valid(2, start=2))])
        generate_candidates(client, "calm", 4, chunk_size=2, verbose=False)

        for call in client.messages.calls:
            candidates = call["output_config"]["format"]["schema"]["properties"]["candidates"]
            self.assertEqual(candidates["minItems"], 2)
            self.assertEqual(candidates["maxItems"], 2)

    def test_floats_are_snapped_onto_pool_members(self):
        client = FakeClient([FakeMessage([candidate(saturation=0.2000000001)])])
        result = generate_candidates(client, "calm", 1, verbose=False)
        self.assertEqual(result.rooms[0]["saturation"], 0.2)

    def test_neutral_arm_uses_the_neutral_prompt(self):
        client = FakeClient([FakeMessage(n_valid(2))])
        generate_candidates(client, "neutral", 2, verbose=False)

        opening = client.messages.calls[0]["messages"][0]["content"]
        self.assertIn("neutral control rooms", opening)
        self.assertIn("NOT designed to convey", opening)


class TestChunking(unittest.TestCase):
    def test_splits_into_chunks_and_tells_the_model_what_it_already_produced(self):
        client = FakeClient(
            [FakeMessage(n_valid(2)), FakeMessage(n_valid(2, start=2)), FakeMessage(n_valid(1, start=4))]
        )
        result = generate_candidates(client, "calm", 5, chunk_size=2, verbose=False)

        self.assertEqual(len(result.rooms), 5)
        self.assertEqual(result.requests, 3)

        # The last request must carry the combinations already produced, or the model
        # has no way to avoid repeating itself across chunks.
        last = client.messages.calls[-1]["messages"][-1]["content"]
        self.assertIn("already produced", last)
        self.assertIn("hue=0", last)

    def test_prior_turns_are_carried_forward(self):
        client = FakeClient([FakeMessage(n_valid(1)), FakeMessage(n_valid(1, start=1))])
        generate_candidates(client, "calm", 2, chunk_size=1, verbose=False)

        roles = [m["role"] for m in client.messages.calls[-1]["messages"]]
        self.assertEqual(roles, ["user", "assistant", "user"])


class TestRejectAndReask(unittest.TestCase):
    def test_out_of_pool_candidate_is_rejected_and_re_asked(self):
        client = FakeClient(
            [
                FakeMessage([candidate(hue=217), candidate(hue=30)]),  # spec's example failure
                FakeMessage([candidate(hue=60)]),
            ]
        )
        result = generate_candidates(client, "calm", 2, chunk_size=2, verbose=False)

        self.assertEqual(len(result.rooms), 2)
        self.assertEqual(result.requests, 2)
        self.assertEqual(len(result.rejected), 1)
        self.assertEqual(result.rejected[0]["candidate"]["hue"], 217)
        self.assertTrue(any("hue=217" in v for v in result.rejected[0]["violations"]))

        # No out-of-pool value survives into the shipped rooms.
        self.assertEqual(validate_batch(result.rooms)[1], [])

    def test_reask_states_the_violations_and_the_shortfall(self):
        client = FakeClient(
            [FakeMessage([candidate(texture="velvet"), candidate(hue=30)]), FakeMessage([candidate(hue=60)])]
        )
        generate_candidates(client, "calm", 2, chunk_size=2, verbose=False)

        reask = client.messages.calls[1]["messages"][-1]["content"]
        self.assertIn("velvet", reask)
        self.assertIn("rejected", reask)
        self.assertIn("Return 1 replacement", reask)

    def test_gives_up_loudly_rather_than_shipping_short(self):
        client = FakeClient([FakeMessage([candidate(hue=217)]) for _ in range(3)])
        with self.assertRaises(GenerationError) as caught:
            generate_candidates(client, "calm", 1, chunk_size=1, verbose=False)
        self.assertIn("still failing validation", str(caught.exception))

    def test_refusal_surfaces_as_a_generation_error(self):
        client = FakeClient([FakeRefusal("not doing that")])
        with self.assertRaises(GenerationError) as caught:
            generate_candidates(client, "calm", 1, verbose=False)
        self.assertIn("declined", str(caught.exception))

    def test_candidate_inventing_extra_fields_is_rejected(self):
        client = FakeClient(
            [FakeMessage([candidate(room_length=6.0)]), FakeMessage([candidate(hue=30)])]
        )
        result = generate_candidates(client, "calm", 1, chunk_size=1, verbose=False)
        self.assertEqual(len(result.rejected), 1)
        self.assertTrue(any("room_length" in v for v in result.rejected[0]["violations"]))
        self.assertEqual(len(result.rooms), 1)

    def test_rejection_rate_is_reported(self):
        client = FakeClient(
            [FakeMessage([candidate(hue=217), candidate(hue=30)]), FakeMessage([candidate(hue=60)])]
        )
        result = generate_candidates(client, "calm", 2, chunk_size=2, verbose=False)
        self.assertAlmostEqual(result.rejection_rate, 1 / 3)


class TestSketchMode(unittest.TestCase):
    def test_sketch_is_requested_kept_and_never_reaches_unity(self):
        from pipeline.schema import unity_config

        client = FakeClient([FakeMessage([candidate(sketch="####\n#  #")])])
        result = generate_candidates(client, "calm", 1, sketch=True, verbose=False)

        item = client.messages.calls[0]["output_config"]["format"]["schema"]
        item = item["properties"]["candidates"]["items"]
        self.assertIn("sketch", item["properties"])

        self.assertEqual(result.rooms[0]["_sketch"], "####\n#  #")
        self.assertNotIn("_sketch", unity_config(result.rooms[0]))

    def test_sketch_is_rejected_when_not_requested(self):
        client = FakeClient([FakeMessage([candidate(sketch="####")]), FakeMessage([candidate(hue=30)])])
        result = generate_candidates(client, "calm", 1, chunk_size=1, verbose=False)
        self.assertEqual(len(result.rejected), 1)


class TestDuplicateRate(unittest.TestCase):
    def test_collapsed_output_is_visible(self):
        rooms = [
            {"hue": 240, "saturation": 0.2, "brightness": 300, "texture": "plaster"}
            for _ in range(4)
        ]
        self.assertAlmostEqual(duplicate_rate(rooms), 0.75)
        self.assertEqual(duplicate_rate([]), 0.0)


if __name__ == "__main__":
    unittest.main()
