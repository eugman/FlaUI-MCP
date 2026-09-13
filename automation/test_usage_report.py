import unittest

from usage_report import summarize


def record(response="r1", usage=None, cumulative=None, turn="t1", thread="a1"):
    usage = usage if usage is not None else dict(input_tokens=10, cached_input_tokens=4,
        cache_write_input_tokens=0, output_tokens=3, reasoning_output_tokens=1, total_tokens=13)
    return {"type": "token_usage_record", "payload": {"thread_id": thread,
        "turn_id": turn, "response_id": response, "usage": usage,
        "turn_token_usage": usage if cumulative is None else cumulative}}


def terminal(kind="task_complete"):
    return {"type": "event_msg", "payload": {"type": kind, "turn_id": "t1"}}


class UsageReportTests(unittest.TestCase):
    def test_reconciled_completed_turn(self):
        result = summarize([record(), terminal()], "a1")
        self.assertTrue(result["reconciled"])
        self.assertEqual(result["totals"]["total_tokens"], 13)
        self.assertEqual(result["fresh_input_tokens"], 6)

    def test_duplicate_response_not_double_counted(self):
        result = summarize([record(), record(), terminal()], "a1")
        self.assertEqual(result["request_count"], 1)

    def test_conflicting_duplicate_rejected(self):
        with self.assertRaises(ValueError):
            summarize([record(), record(usage={"input_tokens": 99})], "a1")

    def test_missing_counter_is_unknown(self):
        result = summarize([record(usage={"input_tokens": 10}), terminal()], "a1")
        self.assertIsNone(result["totals"]["output_tokens"])
        self.assertFalse(result["reconciled"])

    def test_missing_record_detected_by_cumulative(self):
        result = summarize([record(cumulative={"input_tokens": 100}), terminal()], "a1")
        self.assertFalse(result["reconciled"])

    def test_foreign_thread_excluded(self):
        result = summarize([record(thread="other"), record(), terminal()], "a1")
        self.assertEqual(result["request_count"], 1)

    def test_interrupted_retains_usage_not_completion(self):
        result = summarize([record(), terminal("turn_aborted")], "a1")
        self.assertEqual(result["totals"]["input_tokens"], 10)
        self.assertFalse(result["all_turns_completed"])

    def test_multiple_turns_do_not_share_cumulative_baseline(self):
        result = summarize([record(), terminal(), record("r2", turn="t2")], "a1")
        self.assertEqual(result["totals"]["input_tokens"], 20)
        self.assertTrue(result["reconciled"])
        self.assertFalse(result["all_turns_completed"])

    def test_reset_inside_turn_flagged(self):
        result = summarize([record(), record("r2"), terminal()], "a1")
        self.assertFalse(result["reconciled"])

    def test_no_usage_is_not_zero(self):
        result = summarize([terminal()], "a1")
        self.assertIsNone(result["totals"]["input_tokens"])
        self.assertFalse(result["reconciled"])
        self.assertFalse(result["usage_observed"])
        self.assertTrue(result["all_turns_completed"])  # Lifecycle only.

    def test_consistency_does_not_establish_billing_completeness(self):
        result = summarize([record(), terminal()], "a1")
        self.assertTrue(result["reconciled"])
        self.assertEqual(result["billing_complete"], "unverified")

    def test_invalid_counter_rejected(self):
        for value in (-1, True, 1.5):
            with self.subTest(value=value), self.assertRaises(ValueError):
                summarize([record(usage={"input_tokens": value})], "a1")

    def test_context_only_turn_is_incomplete(self):
        context = {"type": "turn_context", "payload": {"turn_id": "t2"}}
        result = summarize([record(), terminal(), context], "a1")
        self.assertFalse(result["all_turns_completed"])
        self.assertFalse(result["reconciled"])

    def test_impossible_breakdowns_rejected(self):
        for usage in ({"input_tokens": 1, "output_tokens": 2, "total_tokens": 99},
                      {"output_tokens": 1, "reasoning_output_tokens": 2},
                      {"input_tokens": 1, "cached_input_tokens": 2,
                       "cache_write_input_tokens": 0}):
            with self.subTest(usage=usage), self.assertRaises(ValueError):
                summarize([record(usage=usage)], "a1")


if __name__ == "__main__":
    unittest.main()
