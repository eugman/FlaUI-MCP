# Four-arm study — implementation in progress; desktop paused

This replaces THREE-WAY.md for new trials. Historical three-arm results are not
observations in this study. This is an instruction-controlled study, not enforced
tool isolation. Extra built-in tools may remain visible. Audit attempted violations
and retain contaminated trials and their costs without automatic reruns.
The live command runs one trial per explicit handoff; no automatic pilot loop.

| Arm | Tools and guidance |
|---|---|
| A | Generic MCP; no skill or map |
| B | Generic MCP + frozen generic skill |
| C | B + TE3 catalog/map and identical map-access bootstrap |
| D | C + companion inspect/navigate/capture |

Pilot: 36 fresh-context trials. Models are Luna low, Terra medium, Sol low.
Tasks are Auto Formatting completeness, Comparison[Amount] identity/properties,
and supplied Hello World C# file → verified source → output dialog.
Skill content is injected explicitly, not discovered automatically. This tests
instruction effectiveness, not skill discovery. The three admitted paragraphs
each have three distinct trace references in generic-skill-evidence.json.
Revision four-arm-2 adds final-image review using three failures from the first
12 formatting runs. That block is now training evidence for the revised skill.
Do not edit its frozen directory. Prepare a new study, select its 12 formatting
trials in their existing order, and compare the combined changes before/after;
do not attribute differences to the skill alone. Other prepared tasks are not
part of this repeat block. Live regression checks must pass before freezing the
live candidate; preparing files is not permission to run the desktop.

Use the same generic binary, offline fixture, normalized layout, 120 DPI,
task wording and native Windows source path in every arm. Freeze builds, maps,
skill, source, prompts and environment observations. Orders rotate ABDC, BCAD,
CDBA, DACB across model/task blocks. Keep failed/interrupted costs; never replace
them silently with a successful retry. One trial per cell is exploratory.

Revision four-arm-3 (2026-09-14) applies to studies prepared after that date.
Earlier results stay historical and aren't pooled with later ones.
- `view_image` is enabled in every arm.
- Batch steps and `resources/read` count against the budget, and the budget
  survives a gateway restart.
- `windows_launch`/`windows_close` are rejected.
- Gateway logs summarize results.
- Restoration is verified separately from whether the hold passed.
- `study.json` is sealed with a sidecar hash.
- The controller's assemblies must match `build/`.
- The tool preflight compares the embedded map resources against `map/`.

The experiment gateway is separate from the production MCP. Its budget is 60
dispatched actions across both backends: each tool call, each batch step, and each
map resource read counts once. Batch steps are recorded as requested unless
completion evidence proves execution. The agent deadline is
seven minutes. No uncertain input is replayed. User stop ends the current run
and blocks automatic progression. Original docs and database servers are out of scope.

## Before live use

- Backend-only preflight passed on 2026-09-13: 16 generic tools, six map resources,
  and exactly the planned A/B/C/D differences. Reproduce without desktop input:
  `node automation/experiments/tool-preflight.mjs BUILD_DIRECTORY NEW_OUTPUT_DIRECTORY`.
  This does not establish the CLI's model-visible tool surface.
- The readable controller/agent lifecycle now has fake-process tests for exit,
  timeout, cancellation, restoration validation, and failure-preserving results.
  The controller reuses RunExecutor's shared runner lock and restoration. Each
  agent gets a fresh empty working directory. Fake-process coverage does not
  replace a live smoke trial.
- Review model/effort and instruction compliance from each full rollout.
  Run `node automation/experiments/compliance-audit.mjs EXACT_ROLLOUT ARM` for
  direct-call violations and orchestration lines needing manual review. Inspect
  nested calls and access to other-arm guidance; the checker does not parse JavaScript.
  Record compliance separately from task success and costs. Hook enforcement is
  not a prerequisite and no further hook experiments are planned.
- Preparation now records input hashes and the resolved Node, Codex launcher/native,
  TE, and TE3 runtimes. Recheck them before each trial. These detect changes;
  they do not freeze the OS, display, hosted model, or authorize desktop access.
- Re-run no-focus tests after live wiring, including all four tool surfaces.
- Obtain fresh desktop handoff. A command-line flag alone is not user permission.

Build the small controller without launching TE3:
`dotnet build automation/experiments/controller/Controller.csproj`.
Use its built `Controller.exe` as `controller` in the preparation config, alongside
`buildDirectory`, `baseline`, `te3`, `te`, and `codexEntry` paths. Prepare a fresh
study with `node automation/experiments/four-arm-study.mjs prepare CONFIG NEW_DIRECTORY`.
After permission, run one scheduled trial with
`node STUDY/four-arm-study.mjs run STUDY TRIAL_ID --handoff`.
The controller checks 120 DPI before handing over. Review image, restoration,
rollout compliance and model/token accounting before moving to the next trial.

### CLI preflight finding (2026-09-13)

Codex CLI 0.154.0, Luna/low, metadata-only Arm A probes used an empty working
directory, ignored user config/rules, disabled MCP actions, and emitted actual
`ALL_TOOLS` metadata (verified in the rollout, not just the final response).
Adding `web_search="disabled"`, `tools.view_image=false`, and disabling goals
removed those tools. `apply_patch` and all five `multi_agent_v1` tools remained,
despite `features.multi_agent=false`. No non-metadata tool was invoked.
The [official configuration reference](https://learn.chatgpt.com/docs/config-file/config-reference)
documents the web/image controls and the multi-agent feature toggle.

| Probe | Input | Cached input | Output | Total |
|---|---:|---:|---:|---:|
| r4 | 22,026 | 19,968 | 284 | 22,310 |
| r5, additional controls | 15,687 | 6,912 | 274 | 15,961 |

Evidence: `artifacts/four-arm-cli-preflight-luna-A-20260913-r4` and `-r5`.
Both standalone rollouts verify Luna/low. These costs are preflight overhead,
not pilot observations or an estimate of a tool's isolated token cost.

Upstream/helper request: expose a per-run enforced tool allowlist, the effective
model-visible tool inventory, and effective model/effort metadata. Include hosted
and orchestration tools, not only configured MCP tools. A process-tree/job helper
would also avoid adding custom Windows containment to this small research tool:
normal launcher exit currently proves only launcher exit, not descendant exit.
Until resolved, strict isolation is unverified. An instruction-controlled study
with contamination reporting is a different design choice requiring review.

### Hook enforcement check (2026-09-13)

The disposable `hook-probe.mjs` separates hook evaluation, the inert MCP server,
argument construction, execution, and observation reporting. It shares bounded
process execution with the CLI preflight. All 54 experiment tests pass without
models or desktop access.

Two harmless probes (`artifacts/hook-probe-20260913-01` and `-02`) dispatched
both marker tools, created their scratch sentinel, and spawned the authorized
trivial child. Neither produced a hook startup record. App-server `hooks/list`
recognized the inline configuration; that separate check did not establish
execution-time trust or activation. Thus enforcement is not established.

A single-marker control (`artifacts/hook-activation-control-20260913-03`)
disabled code-mode hosting. Codex reported that code mode failed closed and the
marker was unavailable. No marker dispatch occurred: this control is inconclusive,
not evidence of hook denial. Stop paid retries pending an upstream explanation
or a known-working hook invocation for this installed CLI.

| Probe | Parent input | Cached input | Parent output | Parent total |
|---|---:|---:|---:|---:|
| Hook 01 | 40,827 | 28,672 | 643 | 41,470 |
| Hook 02 | 73,137 | 56,832 | 576 | 73,713 |
| Activation control 03 | 15,923 | 8,704 | 382 | 16,305 |

These are parent CLI counters, not complete experiment costs: the first two
child usages remain unaudited, and reviewer costs are separate. None is pilot
evidence. No TE3 launch, desktop input, or global configuration change occurred.

## Scoring

Inspect images independently and verify task evidence in the trace. Scene
identity, image completeness, model preservation, and restoration are separate
checks. A controller finishing is not an agent pass. Report exact input, cached
input, uncached input and output; reasoning is a subset of output. Missing final
counters stay unknown/partial. Parent/controller/reviewer overhead is separate.
For a standalone CLI rollout, use
`node automation/experiments/rollout-audit.mjs EXACT_ROLLOUT EXPECTED_THREAD MODEL EFFORT`.
This checks thread identity, every model/effort context, and cumulative counters;
resumed/multi-turn logs cannot be charged as a single trial. The older
`read-arm-usage.ps1` is for historical collaboration subagent logs only.
`usage.json` holds lifecycle, accounting, `threadId` and `rolloutPath`, but no
score placeholders; write the score to `<trial>/review.json` (`taskOutcome`).
`node automation/experiments/four-arm-study.mjs summarize STUDY` prints one JSON
line per attempted trial: tokens, dispatched calls, rejections and review outcome
(`unreviewed` without review.json). Older studies report missing fields as null.
Compare within task/model first: success, then resources among successful runs.
Review pilot validity/cost before deciding on repetitions or skill-rule removal.
