"""Extract native usage from one explicitly selected Codex agent rollout.

No dependencies, pricing assumptions, transcript export or desktop access.
Unknown counters stay null; reconciliation is not proof of complete billing.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any, Iterable


FIELDS = ("input_tokens", "cached_input_tokens", "cache_write_input_tokens",
          "output_tokens", "reasoning_output_tokens", "total_tokens")


def counters(value: dict[str, Any]) -> dict[str, int | None]:
    result = {}
    for field in FIELDS:
        count = value.get(field)
        if count is not None and (type(count) is not int or count < 0):
            raise ValueError(f"Invalid {field}: {count!r}")
        result[field] = count
    input_count, cache, writes, output, reasoning, total = (result[field] for field in FIELDS)
    if input_count is not None and output is not None and total is not None:
        if input_count + output != total:
            raise ValueError("Input plus output does not equal total tokens")
    if output is not None and reasoning is not None and reasoning > output:
        raise ValueError("Reasoning exceeds output tokens")
    if input_count is not None and cache is not None and writes is not None:
        if cache + writes > input_count:
            raise ValueError("Cache counters exceed input tokens")
    return result


def totals(records: list[dict[str, Any]]) -> dict[str, int | None]:
    return {field: sum(record["usage"][field] for record in records)
            if records and all(record["usage"][field] is not None for record in records)
            else None for field in FIELDS}


def summarize(events: Iterable[dict[str, Any]], thread_id: str) -> dict[str, Any]:
    """Caller supplies a single-thread rollout; usage itself is thread-filtered."""
    requests: dict[str, dict[str, Any]] = {}
    statuses: dict[str, str] = {}
    models: dict[str, dict[str, Any]] = {}
    for event in events:
        payload = event.get("payload", {})
        kind = event.get("type")
        turn = payload.get("turn_id")
        if kind == "turn_context" and turn:
            models[turn] = {"model": payload.get("model"), "effort": payload.get("effort")}
        if kind == "event_msg" and turn:
            if payload.get("type") in ("task_started", "task_complete", "turn_aborted"):
                statuses[turn] = payload["type"]
        if kind != "token_usage_record" or payload.get("thread_id") != thread_id:
            continue
        response = payload.get("response_id")
        if not response or not turn:
            raise ValueError("Usage record lacks response/turn identity")
        request = {"response_id": response, "turn_id": turn,
                   "usage": counters(payload.get("usage") or {}),
                   "turn_token_usage": counters(payload.get("turn_token_usage") or {})}
        if response in requests and requests[response] != request:
            raise ValueError(f"Conflicting usage for response {response}")
        requests[response] = request

    rows = list(requests.values())
    turns = []
    for turn in sorted(set(statuses) | set(models) | {row["turn_id"] for row in rows}):
        turn_rows = [row for row in rows if row["turn_id"] == turn]
        # Check observed prefixes. This detects interior gaps/resets, not a
        # missing trailing record or a wholly omitted turn.
        reconciled = bool(turn_rows)
        for index, row in enumerate(turn_rows):
            running = totals(turn_rows[:index + 1])
            reconciled &= all(running[field] is not None and
                              running[field] == row["turn_token_usage"][field]
                              for field in FIELDS)
        turns.append({"turn_id": turn, **models.get(turn, {}),
                      "status": statuses.get(turn, "unknown"),
                      "reconciled": reconciled, "totals": totals(turn_rows)})
    total = totals(rows)
    fresh_fields = [total[field] for field in FIELDS[:3]]
    fresh = (fresh_fields[0] - fresh_fields[1] - fresh_fields[2]
             if all(value is not None for value in fresh_fields) else None)
    if fresh is not None and fresh < 0:
        raise ValueError("Cache counters exceed input tokens")
    return {"thread_id": thread_id, "request_count": len(rows), "totals": total,
            "usage_observed": bool(rows),
            "fresh_input_tokens": fresh,
            "reconciled": bool(turns) and all(turn["reconciled"] for turn in turns),
            "all_turns_completed": bool(turns) and
                all(turn["status"] == "task_complete" for turn in turns),
            "turns": turns, "requests": rows,
            "billing_complete": "unverified", "cost_usd": None}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("rollout", type=Path)
    parser.add_argument("--thread", required=True)
    args = parser.parse_args()
    # Read a snapshot; reject partial JSON rather than silently omitting usage.
    with args.rollout.open(encoding="utf-8-sig") as source:
        events = [json.loads(line) for line in source if line.strip()]
    headers = [event["payload"] for event in events if event.get("type") == "session_meta"]
    if len(headers) != 1 or headers[0].get("id") != args.thread:
        parser.error("Expected one rollout header matching --thread")
    report = summarize(events, args.thread)
    report["agent_path"] = headers[0].get("agent_path")
    report["parent_thread_id"] = headers[0].get("parent_thread_id")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
