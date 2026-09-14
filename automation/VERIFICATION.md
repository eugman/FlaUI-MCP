# Verification status

This is the one place for live status; other docs link here. Builds and unit tests
don't establish live UI behavior.

| Check | Latest result |
|---|---|
| `dotnet build FlaUI.Mcp.slnx` | Passed, 0 warnings, 2026-09-14 |
| MCP unit tests (Debug) | 148 passed, 2026-09-14 |
| Runner unit tests (Debug) | 110 passed, 2026-09-14 |
| Experiment harness tests (`node --test "automation/experiments/*.test.mjs"`) | 65 passed, 2026-09-14 |
| Integration tests | 34/34 passed 2026-09-13 (`artifacts/live-companion-20260913/integration-fixed.trx`); not rerun since |
| Offline recipes | Batch of 15 plus language choices and Preferences map passed on TE3 3.26.3, 2026-09-13, settings restored |
| Companion navigation/capture | Live smoke passed 2026-09-13 |
| Four-arm study | Formatting: 16 trials scored (12 in r1, plus a 4-trial Luna repeat). Object: none scored yet (2 model-capacity failures, 1 interrupted Terra run, whose settings were recovered 2026-09-14). `instruction-study-20260914-object-r3` was prepared and preflighted from the build before the generic MCP hardening; no trials run yet. See [FOUR-ARM.md](experiments/FOUR-ARM.md) |
| Screenshot approval/promotion | None approved or promoted |
| Original docs checkout | Untouched |

**Not yet live-verified:**
- activation blocker results
- screenshot handle/ref checks
- `recover` and the crash-safe `needsRecovery` flag
- the batch-wide runner lock
- study harness revision four-arm-3
- generic MCP hardening:
  - one ref resolver and a capped ref table
  - timeout quarantine and a single blocked-result shape
  - pattern calls under the input lease
  - Win32 title and foreground lookups
  - shorter responses (20k-character snapshot default, nulls omitted)
- companion destination registry and content-hash map revision
- runner:
  - polling for the serialization dropdown and menu focus
  - failure window list
  - PNG hashes checked at promotion

The main window was observed at 120 DPI (125%). Other scaling levels are unverified.

## Last captured recipes

These captures predate the current branch changes, so they don't verify the current code.

| Recipe | Last captured TE3 version | Date |
|---|---|---|
| model-open | 3.26.3 | 2026-09-13 |
| qualified-navigation | 3.26.3 | 2026-09-13 |
| csharp-preview-page | 3.26.3 | 2026-09-13 |
| csharp-output-scalar | 3.26.3 | 2026-09-13 |
| csharp-scalar-scene | 3.26.3 | 2026-09-13 |
| csharp-output-object | 3.26.3 | 2026-09-13 |
| csharp-output-values | 3.26.3 | 2026-09-13 |
| file-load-three-cycles | 3.26.3 | 2026-09-13 |
| csharp-auto-rollback-source | 3.26.3 | 2026-09-13 |
| model-calculation-group-menu | 3.26.3 | 2026-09-13 |
| preferences-map | 3.26.3 | 2026-09-13 |
| preferences-auto-formatting | 3.26.3 | 2026-09-13 |
| preferences-code-actions | 3.26.3 | 2026-09-13 |
| preferences-dax-general | 3.26.3 | 2026-09-13 |
| preferences-file-formats | 3.26.3 | 2026-09-13 |
| preferences-save-to-folder | 3.26.3 | 2026-09-13 |
| preferences-language-choices | 3.26.3 | 2026-09-13 |

## Findings from the last live runs

- The integration rerun found a missing Toggle pattern in the query cache and an
  ambiguous test tab selector. Both were fixed before the 34/34 run.
- The menu run hit an Invoke call that never returned. The runner now opens the Model and
  Tools menus with guarded physical clicks.
- The companion smoke run showed `Process.MainWindowHandle` picking an untitled popup.
  The companion now selects the unique TE3 document window.

Look at the actual PNGs before approving a replacement. See [known issues](KNOWN-ISSUES.md)
and the [backlog](screenshot-backlog.json).
