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
`artifacts/tasks12-cN-sonnet.config.json`. The earlier four-task study
(`ladder-20260914-*`, `study-20260914-*`) is history: it ran 4 tasks × 3 trials,
and its code-actions hold-out became contaminated once the map and `te3_navigate`
covered it. Never pool the two.

## Tasks

Twelve distinct screenshots, one run each per condition, in the same seeded order
for every condition so conditions are compared task by task. Task-to-task
differences are larger than run-to-run differences, so more tasks tell us more than
repeats. Prompts are `tasks` in `ladder-study.mjs`; rubrics are `rubrics` in
`blind-review.mjs`.

| Trained | Hold-out |
|---|---|
| `formatting`, `code-actions` (Preferences) | `dax-general` (Preferences > DAX Editor > General) |
| `column`, `measure`, `table`, `tom-tree` (TOM Explorer) | `save-to-folder` (Preferences > File Formats) |
| `script-run`, `script-source` (C# scripts) | `calc-group-menu` (Model menu open) |
| | `relationship` (relationship Properties) |

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
