# Verification status

Current simplification branch: unit tests and builds do not establish live UI behavior.

| Check | Latest result |
|---|---|
| `dotnet build FlaUI.Mcp.slnx` | Passed, zero warnings/errors |
| MCP unit tests, Debug | 117 passed |
| Runner unit tests, Debug | 61 passed |
| Integration tests | 30 passed live, 2026-09-13; see artifacts/live-refresh-96aa6c2/integration.trx |
| Screenshot approval/promotion | None approved or promoted |
| Original docs checkout | Untouched |

Live rerun on TE3 3.26.3: all 15 default offline recipes plus language choices
and Preferences mapping passed with settings restored (17/17, 2026-09-13).
The menu run exposed a pending Invoke result; the runner now opens Model and
Tools menus using guarded physical clicks. No generic modal guard was weakened.
Current main-window display observation is 120 DPI (125%). This is not a
verification of other scaling variants or approval of screenshot replacements.

## Historical screenshot verification

These captures predate the simplification. They are not verification of the current code.

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

Next live gate, with explicit desktop permission: screenshot fallback/modal capture,
hosted-control input, same-process popup focus, and the offline screenshot recipes.
Inspect actual PNGs before approving replacements. See [known issues](KNOWN-ISSUES.md).
Engine recipes remain optional against an existing configured database; their lifecycle
machinery is being removed. The [backlog](screenshot-backlog.json) remains in this fork.
