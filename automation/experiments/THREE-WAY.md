# Three-way TE3 agent comparison

Historical design only. New trials follow [FOUR-ARM.md](FOUR-ARM.md); arm letters
have different meanings and these results must not be pooled.

Prepared, not executed. Every desktop batch requires explicit handoff permission.

Freeze the generic/companion executable commit or build hash, map revision,
fixture hash, TE3 version, DPI/monitor/window geometry and task prompts. Live-check
the new helpers first; if their code changes, start a new comparison revision.

## Arms and schedule

| Arm | Exposed capability |
|---|---|
| A | Generic MCP only; no map, skill, runner or companion |
| B | Same generic MCP plus map via `mcp --guidance-only` |
| C | Same generic MCP plus identical map delivery and full companion |

All arms use the improved generic build. This compares guidance and companion
value, not old vs new generic server. Keep generic descriptions TE3-free.
Use fresh Luna-low and Terra-low contexts, no inherited conversation. Arm B/C
get the same instruction to read their relevant catalog topic. Do not install a
skill into A's discovery environment. Verify tools actually exposed in each arm;
prompt-only prohibition is not equivalent to tool isolation. Record any leakage.

Tasks, identically worded across arms:

1. Qualified objects: capture Sales/Amount, Comparison/Amount, and Sales/Smoke tests/
   Total Amount, verifying type and table. Leave the last object selected; no saves.
   Budget: 45 dispatched calls, six minutes.
2. Preferences Auto Formatting: capture the selected section with empty search,
   full dialog chrome and readable controls; preserve settings. Budget: 60 calls,
   seven minutes.
3. Preferences Code Actions: same requirements for Code Actions. Budget: 60 calls,
   seven minutes.

The controller supplies actual PID and absolute output paths equally to all arms.
It stages the same fresh offline fixture and normalized settings before each trial;
agents may not invoke the runner or use CLI model shortcuts. Prepare Preferences
geometry identically before each trial if needed, and restore the same start view.
Do not let one arm benefit from a pre-opened destination another must discover.

Pilot: one trial per model/arm/task (18 trials). If setup is sound, repeat each cell
twice more (54 total). For each task/model, use ABC, BCA, CAB across repetitions;
reverse these orders for Terra to reduce simple ordering effects. Do not report
single pilots as a causal model ranking.

## Controller and clean-state gate

One desktop owner; no parallel UI subagents. The controller, not the agent's
self-report, enforces the call/time cap and records interventions. Count dispatched
MCP calls and executed batch steps separately. A helper's internal calls are a
separate diagnostic, not additional agent calls. All arms share the same counting rule.

Before launch verify: restored preferences, fresh fixture, expected TE3 identity,
expected initial view, no file picker/output dialog/external app chooser, unchanged
display environment. External state can survive TE3 shutdown. If an unrelated
chooser or obstruction appears, stop and request user intervention; no allowlist bypass.
Keep contaminated runs and their token costs, but exclude them from clean success-rate
and paired efficiency summaries. Log why and repeat only after a verified reset.

## Evidence and token accounting

Store one trial row: model/effort, arm, task, repetition, build/map/fixture revisions,
start/end, budget/interventions, outcome, visual correctness, contamination reason,
calls/batch steps, screenshot calls/image returns, artifact paths, rollout path.
Inspect PNGs independently; control state or an agent saying done is not visual proof.

Run `read-arm-usage.ps1 -LogPath EXACT_ROLLOUT -ExpectedAgent EXACT_AGENT_PATH`
only after that arm finishes. It uses the last cumulative token counter, never the
sum of cumulative counters; guidance reads and reasoning costs are included.
Cached input is a subset of input; reasoning output a subset of output. Report
uncached input separately. Controller/evaluator tokens are separate overhead.
Unavailable counters are unknown, never zero. Token counters are not a dollar bill
and do not isolate image-token cost. Cheaper failed runs are not efficiency wins.

Acceptance: all required state/visual assertions, no unauthorized edits, fixture
unchanged, within budget. Publish per-trial data and ranges/medians for clean runs;
do not hide setup failures or replace them with selected successful screenshots.

## Skill ablation: evidence before additional instructions

The packaged `te3-ui` skill is a minimal map-discovery bootstrap, not a second UI
guide. Its efficacy has not been tested. Earlier generic-vs-reference runs are
evidence of UI/map failures, not a controlled evaluation of this skill.

Keep the main three-way comparison skill-free: B/C receive the same direct map
retrieval instruction. Separately compare S0 (no skill) against S1 (the minimal
packaged skill) with identical map/catalog availability, generic tools, task
prompt, model/effort, fixture and budgets. Remove direct map-retrieval instructions
from both S0/S1 task prompts, so the skill is the only guidance difference.
Do not expose the companion action tools in either skill condition initially.
Use fresh contexts and alternate S0/S1 order; include skill discovery/read tokens.

Observe whether the agent discovers the map, retrieves the appropriate topic,
successfully reads it, and applies it. Audit the log, not its claimed skill use.
If no-skill already succeeds equally well, do not expand the skill. If content
was never retrieved, fix discovery before adding more content. If the retrieved
map is wrong, fix the map rather than duplicating a workaround in the skill.

For any proposed new skill sentence, record the failing trial/log reference,
the precise observed mistake, and the expected behavioral change in the trial
notes. Compare the shorter version with the expanded version on that failure
and one unaffected task. Retain additions only when they improve verified
completion or prevent the targeted mistake without disproportionate token cost.
Keep UI procedures in the shared map; do not prefill speculative safeguards.

This subtest requires its own desktop handoff; no skill-ablation trials have run yet.
