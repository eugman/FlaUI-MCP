# Five-condition study

This study measures what each addition to the TE3 agent setup is worth. Agents run
in Claude Code headless mode with Sonnet or Opus, one trial at a time, against an
offline TE3 fixture that the controller launches and restores. The config field
`rung` holds the condition number.

| Condition | MCP server build | Injected skill | TE3 tools |
|---|---|---|---|
| 1 | Original (TabularEditor main 6a39906) | none | no |
| 2 | New (current) | none | no |
| 3 | New | generic skill (`layers/1-mcp-basics.md`) | no |
| 4 | New | generic skill + map (`layers/map.md`) | no |
| 5 | New | generic skill + map + TE3 tool line (`layers/te3-tools.md`) | `te3_navigate` |

The skill text is appended to the prompt under `Guidance:`. The map is
`automation/skills/te3-notes` concatenated as is, plus lines added from condition 2
failures on trained tasks.

Current studies are `artifacts/study-tasks12-cN-sonnet`, prepared from
`artifacts/tasks12-cN-sonnet.config.json`. An earlier four-task study (4 tasks ×
3 trials) shaped the tool fixes, the generic skill and the map edits; its run
folders were deleted on 2026-09-14 and its results are not comparable with the
12-task suite.

## Tasks

Twelve distinct screenshots, one run each per condition, in the same seeded order
for every condition so conditions are compared task by task. Task-to-task
differences are larger than run-to-run differences, so more tasks tell us more than
repeats. Prompts are `tasks` in `ladder-study.mjs`; rubrics are `rubrics` in
`blind-review.mjs`.

| Trained | Hold-out |
|---|---|
| `formatting`, `code-actions` (Preferences) | `bpa-view` (Best Practice Analyzer view) |
| `column`, `measure`, `table`, `tom-tree` (TOM Explorer) | `dax-query` (new DAX Query document) |
| `script-run`, `script-edit` (C# scripts) | `model-properties` (model root node Properties) |
| `calc-group-menu` (Model menu open) | |

Round 1 used `script-source`, `dax-general` and `save-to-folder` in place of
`script-edit`, `bpa-view` and `dax-query`. Guidance was written after reading the
`dax-general` and `save-to-folder` transcripts (the Preferences search-scope line,
added in 2ff9d1c and removed before round 2), so those two results are contaminated
for conditions 4 and 5 from the `c4b`/`c5b` reruns on. Round 2 replaced them.
Likewise, the generic skill's open-menu exception to background captures (2ff9d1c)
came from a `calc-group-menu` transcript, so that task is contaminated for conditions
3–5 from `c4b`/`c5b` on and counts as trained in round 2.
`summarize` takes each trial's hold-out status from its own `study.json`, so round-1
rows keep their original classification.

The fixture has no relationships. The first hold-out list had a `relationship` task,
which no agent could complete; it was replaced by `model-properties`. Trials of
`relationship` in conditions 1–3 are excluded from scoring, and those conditions run
`model-properties` in a sibling study `study-tasks12-cN-sonnet-model`.

**Hold-outs** get no map topic, no skill line and no `te3_navigate` destination,
ever. Guidance can be tuned until the practiced tasks pass; the hold-outs check
that it also works on screenshots nobody wrote it around, which is what matters
for the rest of the backlog. `automation/skills/holdout-guard.test.mjs` fails if a
skill layer names hold-out UI.

## Trials

- Sonnet: 12 trials per condition. Run condition 2 first: its trained-task
  transcripts decide any map lines. Then conditions 1, 3 and 4, then the companion
  smoke test and condition 5.
- Trial ids are `CONDITION-MODEL-TASK-01`. `study.json` lists them in a seeded
  shuffled order; run them in that order.
- Keep every attempted trial, including failures and interruptions. Never replace
  one with a retry.

### Round 2

- **Question:** do the call-count differences between conditions 3, 4 and 5
  replicate on the 9 unchanged tasks, and did anything regress? It also gives first
  results on the 3 new tasks. It does not estimate the effect of the generic MCP
  fixes made between rounds (key names, newlines, `windows_paste`, lookup
  candidates, launch error).
- **Conditions:** 5, 4, 3, then 1, one run per task (48 trials).
  - Condition 1 is unchanged and checks for drift in Claude Code, Sonnet or TE3.
  - Condition 2 is not rerun.
- **Setup:** configs `artifacts/tasks12-r2-cN-sonnet.config.json`, with
  `label: "r2"` and seed 20260915, prepared into `artifacts/study-tasks12-r2-cN-sonnet`.
- **Server task:** TE3 needs an engine connection for DAX queries, so `dax-query`
  opens the existing `fla_te3_small` database on localhost (`controller.server.config.json`)
  and its prompt says so. Every other task uses the offline fixture.
- **Guidance is frozen:** the generic skill, map and `te3-tools` line don't change
  during or after round 2. The round-2 transcript review classifies failures only.
  Round-1 condition 3 ran before the open-menu exception was added, so its
  round-to-round change mixes that guidance change with the MCP fixes.
- **Grading:** all 48 trials are packed into one blind review.
- **Report:** rounds side by side per task, with no averaging across rounds and median
  calls as the cost measure.

**Pre-registered targets**
- Conditions 3–5 pass 12/12 with 0 false claims. A pass also requires settings to
  be restored, so check a failure for a restoration fault before blaming the agent.
- No "Unsupported key" error in any trial (search each trial's `agent.jsonl`;
  `calls.jsonl` records result sizes only).
- Median calls on the 9 unchanged tasks: condition 5 ≤ condition 4 ≤ condition 3.
  `summarize` reports one median over all 12 tasks, so compute this one from the
  per-trial dispatched call counts of those 9 tasks.
- `script-edit` shows the exact two lines in conditions 3–5, and at least one trial
  uses `windows_paste`.

**Round 2 results** (2026-09-15, blind review `artifacts/review-tasks12-r2`)

| Condition | Passes r1 → r2 | False claims r1 → r2 | Median calls, 9 unchanged tasks r1 → r2 | Total calls r1 → r2 |
|---|---|---|---|---|
| 1 | 8 → 11 | 2 → 0 | 25 → 30 | 353 → 353 |
| 3 | 12 → 12 | 0 → 0 | 24 → 16 | 280 → 272 |
| 4 | 12 → 12 | 0 → 0 | 17 → 17 | 209 → 189 |
| 5 | 12 → 12 | 0 → 0 | 9 → 9 | 164 → 130 |

Round-1 condition 1's 8 passes include dax-general and save-to-folder failures that round 2 no longer has.

- **Targets:** three of four met.
  - Met: conditions 3–5 passed 12/12 with no false claims.
  - Met: no trial hit "Unsupported key".
  - Met: `script-edit` passed in conditions 3–5, each using `windows_paste`.
    Condition 1 typed it with 11 `windows_type` calls.
  - Missed by one call: the median on the 9 unchanged tasks was 9 for condition 5,
    17 for condition 4 and 16 for condition 3, so condition 4 did not beat
    condition 3. Condition 5 is still clearly cheapest.
- **Only failure:** condition 1 formatting ran out of its 60 calls and reported failure.
- **Outliers (classified, not acted on):** condition 3 formatting (56 calls) closed
  Preferences with Escape after resizing it and started again; condition 1 script-run
  (42 calls) hit repeated UIA timeouts on the original MCP's file dialog; condition 5
  dax-query (14) navigated menus, since hold-outs have no `te3_navigate` destination.

### Extended-budget reruns

A trial that hits the call limit or the time limit may be rerun once in a sibling
study with a larger budget. The config adds `label: "extended"`, `onlyTrials`
(e.g. `["formatting-01"]`), `maxCalls: 120` and `agentMinutes: 15`. Rerun ids end in
`-extended`, and `summarize` groups them separately. They show whether a task is
solvable with more room; rung comparisons still use the standard 60-call,
7-minute trials.

## Grading

Grade each trial from its final `result.png`, `agent.jsonl` transcript and
controller manifest. Grade blind to rung: hide the trial directory name while
judging the image. Write `<trial>/review.json`:

```json
{ "passed": true, "claimedSuccess": true, "rubric": { "section selected": true }, "notes": "" }
```

`passed` is true only when every rubric item holds and settings restoration was
verified. `claimedSuccess` is whether the agent's final message claimed the task
succeeded. The per-task rubrics are `rubrics` in `blind-review.mjs`.

## Controller assemblies

The controller bundles its own copy of the TE3 runner to restore settings. At
condition 5, `prepare` requires `FlaUI.Mcp.dll` and `FlaUI.Automation.dll` in
`controller/` to match `companionBuild`. At other rungs the agent never uses the
runner, so the check runs only when the config supplies `currentBuild`.

## Runbook

1. Build the controller: `dotnet build automation/experiments/controller/Controller.csproj`.
2. Check the skill for the rung: `node automation/skills/build-skill.mjs RUNG`.
3. Write a config:

   ```json
   {
     "rung": "1", "model": "sonnet", "seed": 20260914,
     "genericBuild": "PATH/TO/current/build",
     "genericCommand": ["FlaUI.Mcp.exe", "mcp"],
     "controller": "PATH/TO/Controller.exe",
     "baseline": "PATH/TO/fixture.bim",
     "te3": "PATH/TO/TabularEditor3.exe", "te": "PATH/TO/TabularEditor.exe",
     "claudeEntry": "%USERPROFILE%/.local/bin/claude.exe"
   }
   ```

   Optional: `trialsPerTask` (default 1), `tasks` (default all twelve),
   `currentBuild`. Condition 5 requires `companionBuild`; other conditions reject it.
   For condition 1, point `genericBuild` at the original build and set
   `genericCommand` to its executable and arguments.
4. Prepare: `node automation/experiments/ladder-study.mjs prepare CONFIG NEW_STUDY_DIRECTORY`.
5. Check the tool surface without dispatching tools:
   `node automation/experiments/tool-preflight.mjs STUDY NEW_OUTPUT_DIRECTORY`.
6. With explicit desktop handoff from the user, run one trial:
   `node STUDY/ladder-study.mjs run STUDY TRIAL_ID --handoff`.
   After the first trial of a study, confirm `visibleTools` in `usage.json` lists
   only `mcp__study__*` tools.
7. Grade blind: `node automation/experiments/blind-review.mjs pack STUDY... NEW_REVIEW_DIRECTORY`
   copies each attempted trial's image, rubric and redacted final message under a
   random code. Write `grades.json` there (`{ CODE: { rubric, claimedSuccess, notes } }`),
   then `blind-review.mjs apply REVIEW_DIRECTORY` writes each trial's `review.json`.
8. Summarize: `node automation/experiments/ladder-study.mjs summarize STUDY`.
   It prints one JSON line per trial, one per condition × task × model, and one
   per condition with passes (overall, trained, hold-out), false claims, median
   calls and mean tokens.

## Agent invocation

`run` launches Claude Code in a fresh empty temp directory with the prompt on
stdin. The flags keep the session to the study gateway: `-p`, `--model`,
`--output-format stream-json --verbose`, `--no-session-persistence`,
`--setting-sources local` (skips user and project settings, so no user hooks,
plugins or permissions), `--disable-slash-commands` (no skills),
`--strict-mcp-config --mcp-config TRIAL/mcp.json`, `--tools ""` (no built-in
tools), `--allowedTools mcp__study__*` and `--permission-mode dontAsk` (anything
else is denied). The full command is saved in `invocation.json`.

The gateway allows 60 dispatched actions per trial; each batch step counts once,
and the count survives a gateway restart. `windows_launch` and `windows_close`
are rejected. The agent deadline is seven minutes.

## History

The Codex four-arm studies under `artifacts/` are historical. Never pool their
results with ladder results.
