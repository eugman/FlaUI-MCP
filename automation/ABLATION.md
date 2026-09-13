# Agent-efficiency experiment

Status: preparation only; no scored UI trials yet. Use gpt-5.6-luna, low effort,
fresh context for each arm. Run desktop trials serially with permission. Never
run an arm against a different MCP build without recording that difference.

## Arms

| Arm | Additional task guidance |
|---|---|
| A | None: task, safety boundaries and exposed tool descriptions only |
| B | ablation/guide.txt (strategy only; no locators) |
| C | ablation/code-actions-map.txt (locators only) |
| D | Both guide and locator excerpt |
| E | Typed runner recipe; report separately, since its action interface differs |

Start with A versus D. Use identical offline fixtures, window geometry and
preferences. Reset between trials. Keep success criteria outside the agent prompt
except for the actual requested outcome. Do not coach a struggling arm. Record
failure, unnecessary screenshots, broad snapshots, stale refs, repeated failed
actions and recovery attempts. Include all attempts, not only successful ones.
Record the tool inventory and MCP binary hash alongside model/effort and arm ID.
No database creation, external writes or original-docs changes.

## First task and grading

`ablation/task.txt` is the identical base prompt for A–D. Substitute only the
owned window title and unique screenshot output path. A gets no added guidance;
B appends guide.txt, C appends code-actions-map.txt, D appends both in that order.
Do not use the full AGENT-QUICKSTART for B: it already contains locators and would
confound the guidance-versus-map comparison. E is not part of this first pair.

The operator prepares a fresh offline model, normalized settings and the
same layout/geometry before each arm, outside its measured turn. The opt-in
`ablation-code-actions` recipe now captures agent-start.png and writes
agent-ready.json, then waits without sending UI input for up to ten minutes.
This is an operator-driven pilot, not an unattended trial driver. After stopping
the agent and checking it is quiescent, the operator writes `grade` or `abort`
to a temporary file in that run directory, then atomically renames it to
agent-release.txt (destination must not already exist). Do not write directly
to the polled path: Terra identified a partial-write/locked-file race.
`grade` independently checks the
selected page, empty filter and actual prefixes, captures grader-code-actions.png,
then cancels Preferences. Either path uses the runner's existing owned-process
cleanup and durable settings restore. A timeout is a failed trial. Finish the
five-minute agent arm well before the ten-minute cleanup deadline.

Use fresh Luna-low agents with no forked conversation. Block repository/other-agent
access in the prompt; record that this is instruction-enforced, not a technical
tool allowlist. Stop the trial for a safety violation. Log tool-discovery overhead
separately; the common cap is 30 FlaUI calls/five minutes, checked by the operator.
Cancel and verify agent quiescence before grading or restoring the application.

Grade independently after the agent finishes: correct owned process/dialog,
unfiltered navigation, Code Actions selected, both reported prefixes equal the
actual UI values, and an existing readable screenshot of that page. Inspect
the PNG; a correctly named file or agent success claim is insufficient. Keep
task success separate from screenshot fidelity, safety and cost. Close the
owned process and restore settings using the same durable backup safeguards;
do not reset a live user-owned session. Exclude operator setup/grading from arm
usage but retain it as separately attributable overhead.

Run one A/D pair as a feasibility pilot, not evidence of statistical superiority.
If viable, run three fresh paired repetitions with alternating order A/D, D/A,
A/D. Report all failures and native cached/uncached counts: cache warmth and
trial order affect cost, so do not describe total-input reduction as dollars saved.
Freeze these prompts before the pilot; if a task or map changes, version it and
do not pool results across versions silently.

The initial guided failure motivated a separate D+ intervention:
append `ablation/physical-open-intervention.txt` to D without changing A–D.
It names the existing physical-click option for the modal-opening transition.
This tests whether a specific operational hint repairs the observed failure;
it is not a retest of the original guidance arm. Do not apply the intervention
to any running agent or change the MCP implementation during that comparison.

User-requested model comparison: repeat D+ with gpt-5.6-terra at low effort,
same prompt/guidance, 30-call/five-minute limits and fresh offline state. This
isolates model choice from the physical-open hint. Report model rows separately;
do not compare Terra D+ with Luna A as if guidance were controlled. Token counts
remain comparable workload measures, not equal dollar costs across models.

## Native telemetry proof (2026-09-13)

Metadata-only probe `/root/luna_probe`, thread
`01a09943-5a94-7ef2-a37b-9f74696ddd66`, turn
`01a09943-5b55-7cf1-a8b1-eebe39242378`.
The local rollout header identifies this repository and parent thread; its
turn_context records `gpt-5.6-luna` and effort `low`.

Source under the local Codex sessions directory:
`2026/09/13/rollout-2026-09-13T01-35-23-01a09943-5a94-7ef2-a37b-9f74696ddd66.jsonl`.
Six event_msg/token_count records, ordinals 15, 21, 27, 31, 37 and 43;
last observed usage timestamp `2026-09-13T05:35:44.568Z`.

| Native counter | Final cumulative | Sum of six last-usage counters |
|---|---:|---:|
| input_tokens | 167034 | 167034 |
| cached_input_tokens | 144896 | 144896 |
| cache_write_input_tokens | 0 | 0 |
| output_tokens | 505 | 505 |
| reasoning_output_tokens | 149 | 149 |
| total_tokens | 167539 | 167539 |

These are runtime-reported counts, not character/tokenizer estimates. Cached input
and reasoning are breakdowns: do not add them to input/output again. Fresh input
is 22138. This discovery-only probe is NOT an efficiency score or UI success.
Its large input count includes repeated context, not just the short task prompt.
Parent orchestration is separate. The live rollout was readable but hash access
was denied by its writer; no immutable full-log hash is claimed.

## Collection rules

Reuse normalization and pricing concepts from agentic-runner-library-v2, but its
Codex CLI turn.completed parser does not directly parse this rollout event format.
For each fresh agent, capture the matching header, model/effort, usage events and
terminal status. Deduplicate event identity, not equal token amounts. Reconcile
incremental counters with cumulative totals; do not sum cumulative values.
Missing counters remain unknown. Do not assume interrupted calls or retries have
complete accounting just because this clean probe reconciles. Detect resets and
resume boundaries before combining logs. Keep raw evidence outside committed docs;
export only experiment-scoped records, not unrelated conversations.

Report native tokens separately from any list-price-equivalent dollar estimate.
Pricing requires a verified model/rate mapping; no dollar estimate exists yet.
`usage_report.py` now reads per-response `token_usage_record` records, deduplicates
response IDs and reconciles every per-turn cumulative prefix. It deliberately does
not also sum event_msg/token_count, which reports the same usage a second time.
Fourteen no-focus tests cover duplicates/conflicts, missing counters/records, resets,
multiple turns, interrupted turns, foreign-thread usage and invalid counters.
The real six-request probe reconciles and its task_complete marker is present.
Billing completeness remains explicitly unverified; prices remain null.
Terra reviewed the extractor. `reconciled` means observed cumulative-prefix
consistency only: missing trailing records or wholly absent turns cannot be
detected this way. `all_turns_completed` describes lifecycle markers, not billing;
`usage_observed` separately says whether any native request usage was found.

```powershell
python -B -m unittest discover -s automation -p test_usage_report.py
python -B automation/usage_report.py PATH_TO_ROLLOUT --thread THREAD_ID
```

The command prints JSON with counters and request evidence only, not conversation
text. It requires a matching session header and rejects partial JSON. It reads
existing logs, does not start agents and does not change any Codex configuration.

## Connection prerequisite

Current source is published separately at `artifacts/mcp-ablation-20260913`.
With user permission, the local Codex FlaUI command was repointed there; TOML and
the executable path validated. Existing processes were not restarted. A fresh
connection still needs its inventory checked before any scored trial.
After the user restarted this session, the new process used that isolated path;
the inventory includes windows_place_window/windows_operation_status and no
windows_profile. The other session's old process was left alone. Never overwrite
either running server's binaries.
No upstream telemetry change is currently proven necessary; a stable read-only
agent-ID-to-usage export would avoid dependence on local rollout internals.
