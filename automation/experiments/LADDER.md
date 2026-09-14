# Evaluation ladder

This study measures what each addition to the TE3 agent setup is worth. Each rung
adds one thing to the rung below it. Agents run in Claude Code headless mode with
Sonnet or Opus, one trial at a time, against an offline TE3 fixture that the
controller launches and restores.

| Rung | MCP server build | Injected skill | Companion TE3 tools |
|---|---|---|---|
| 0a | Original build (TabularEditor main 6a39906) | none | no |
| 0b | Build at 31f8d56 (hardened generic tools) | none | no |
| 0c | Current build (0b plus fixes from 0b transcripts) | none | no |
| 1 | Current build | `composeSkill('1')` | no |
| 2 | Current build | `composeSkill('2')` | no |
| 3 | Current build | `composeSkill('3')` | `te3_inspect`, `te3_navigate`, `te3_capture` |

The skill text comes from `automation/skills/layers/` and is appended to the prompt
under `Guidance:`. Rungs 0a, 0b and 0c inject nothing. Rung 1 builds on 0c. No rung serves the TE3 map
through MCP.

## Rules for adding layers

- Write a rung's layer lines only after reading the previous rung's transcripts.
- Each line must fix a failure seen in at least two trials.
- Never derive a line from the hold-out task (`code-actions`) or its transcripts.
- Stop adding rungs once a rung passes all 3 trials of every training task with
  no false claims.
- Keep a rung only if passes rise, false claims fall, or mean dispatched calls drop
  by at least 30% without fewer passes. Otherwise remove its layer.

## Trials

- Sonnet: all 4 tasks × 3 trials at every rung (12 trials per rung).
- Opus: the same 12 trials at rung 0c and at the final rung.
- Trial ids are `RUNG-MODEL-TASK-NN`. `study.json` lists them in a seeded shuffled
  order; run them in that order.
- Keep every attempted trial, including failures and interruptions. Never replace
  one with a retry.

### Extended-budget reruns

A trial that hits the call limit or the time limit may be rerun once in a sibling
study with a larger budget. The config adds `label: "extended"`, `onlyTrials`
(e.g. `["formatting-01"]`), `maxCalls: 120` and `agentMinutes: 15`. Rerun ids end in
`-extended`, and `summarize` groups them separately. They show whether a task is
solvable with more room; rung comparisons still use the standard 60-call,
7-minute trials.

Tasks: `formatting`, `object` and `script` are training tasks. `code-actions` is
the hold-out.

## Grading

Grade each trial from its final `result.png`, `agent.jsonl` transcript and
controller manifest. Grade blind to rung: hide the trial directory name while
judging the image. Write `<trial>/review.json`:

```json
{ "passed": true, "claimedSuccess": true, "rubric": { "section selected": true }, "notes": "" }
```

`passed` is true only when every rubric item holds. `claimedSuccess` is whether
the agent's final message claimed the task succeeded.

**formatting** and **code-actions**
- The section (Auto Formatting or Code Actions) is selected in the tree.
- The search box is empty.
- All controls of the section are visible, including the bottom control
  (formatting: Use default formatting settings).
- The Preferences title and the OK/Cancel buttons are readable.
- Settings are unchanged (controller restoration verified, no edits in transcript).

**object**
- The Comparison table context is visible.
- The Amount row is selected.
- Properties are readable, with Name `Amount`.
- Object Type is shown as a column type.
- The DAX identifier reads `'Comparison'[Amount]`.
- The model is unchanged.

**script**
- The output dialog title is visible.
- The `Hello World` text is visible.
- The Close button is visible.
- The source file was not modified.

## Controller assemblies

The controller bundles its own copy of the TE3 runner to restore settings. At
rung 3, `prepare` requires `FlaUI.Mcp.dll` and `FlaUI.Automation.dll` in
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

   Optional: `trialsPerTask` (default 3), `tasks` (default all four),
   `currentBuild`. Rung 3 requires `companionBuild`; other rungs reject it. For
   0a, point `genericBuild` at the original build and set `genericCommand` to its
   executable and arguments.
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
   It prints one JSON line per trial, then one per rung × task × model with
   passes, pass-all, false claims, mean calls and mean tokens.

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
