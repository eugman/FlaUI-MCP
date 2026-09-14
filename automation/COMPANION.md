# TE3 MCP companion

`FlaUI.Automation mcp` is an attach-only MCP server with three TE3 tools. The generic
FlaUI MCP stays TE3-independent; the companion reuses its transport and the runner's
typed navigation. Live status is in [VERIFICATION.md](VERIFICATION.md).

| Tool | Does |
|---|---|
| `te3_inspect` | Reads windows, Preferences or object state for an explicit `processId`. No focus. |
| `te3_navigate` | Navigates to a destination and verifies arrival. Takes focus. |
| `te3_capture` | Saves a native whole-window PNG of a scene `te3_navigate` already prepared. Never navigates or resizes. |

Destinations and scenes: `tom-explorer`, `expression-editor`, `object` (with `table`,
`objectName`, `objectType` Table/Column/Measure, optional `folder`),
`preferences/auto-formatting`, `preferences/code-actions`.

```json
{"processId":1234,"topic":"windows"}
{"processId":1234,"destination":"preferences/code-actions"}
{"processId":1234,"scene":"preferences/code-actions","savePath":"C:/Temp/code_actions.png"}
```

Responses include `assessment`:

| Field | Values |
|---|---|
| `sceneIdentity` | `matched` or `not-assessed` |
| `artifact` | `saved` or `not-saved` |
| `path` | saved file path |
| `visualCompleteness` | always `not-assessed` |
| `docsApproved` | always `false` |

## Setup

Build `dotnet build FlaUI.Mcp.slnx`, then configure a stdio MCP server that runs the built
`FlaUI.Automation.exe` with the argument `mcp`. Don't overwrite a binary a connected client
is running. Agent guidance for these tools is built as skill layers; see
[the ladder study](experiments/LADDER.md).

## Boundaries

- Attach only: no launch, close, preference changes, database access or script execution.
- `te3_navigate` can open Preferences and change search or selection; it never presses OK or Cancel.
- `te3_capture` requires the actual target window. A maximized window's invisible borders are
  cropped; a partially offscreen normal window fails.
- A saved PNG is not an approved docs replacement. Look at it.
- Windows can refuse to activate TE3. Failures then report `blocker: activation-denied` (ask the
  user to click the TE3 title bar) or `desktop-unavailable` (locked or disconnected session).
- One agent owns the desktop at a time. Pending-operation tracking is per server process, so
  don't drive the generic server and the companion concurrently.
- After a timeout, inspect before continuing; a blocked UIA provider can't be interrupted.

## Tests

`FlaUI.Mcp.Tests` and `FlaUI.Automation.Tests` need no desktop. Integration tests and the
companion smoke test launch apps and take focus; run them only during a desktop handoff.
