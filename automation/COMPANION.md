# TE3 MCP companion

`FlaUI.Automation mcp` is an attach-only MCP server with one TE3 tool. The generic FlaUI
MCP stays TE3-independent and owns capture; the companion reuses its transport and the
runner's typed navigation. Live status is in [VERIFICATION.md](VERIFICATION.md).

| Tool | Does |
|---|---|
| `te3_navigate` | Navigates to a destination and verifies arrival. Takes focus. |

`te3_inspect` and `te3_capture` were removed after the five-condition study: agents never
called `te3_capture`, and `te3_inspect` only repeated `windows_list_windows`.

Destinations: `tom-explorer`, `expression-editor`, `object` (with `table`, `objectName`,
`objectType` Table/Column/Measure, optional `folder`), `preferences/auto-formatting`,
`preferences/code-actions`.

```json
{"processId":1234,"destination":"preferences/code-actions"}
{"processId":1234,"destination":"object","table":"Comparison","objectName":"Amount","objectType":"Column"}
```

Responses include `assessment`:

| Field | Values |
|---|---|
| `sceneIdentity` | `matched` or `not-assessed` |
| `visualCompleteness` | always `not-assessed` |
| `docsApproved` | always `false` |

## Setup

Build `dotnet build FlaUI.Mcp.slnx`, then configure a stdio MCP server that runs the built
`FlaUI.Automation.exe` with the argument `mcp`. Don't overwrite a binary a connected client
is running. Agent guidance for this tool is the `te3-tools` skill layer; see
[the study](experiments/LADDER.md).

## Boundaries

- Attach only: no launch, close, preference changes, database access or script execution.
- `te3_navigate` can open Preferences and change search or selection; it never presses OK or Cancel.
- Arrival is not a visual review. Capture with the generic `windows_screenshot` and look at it.
- Windows can refuse to activate TE3. Failures then report `blocker: activation-denied` (ask the
  user to click the TE3 title bar) or `desktop-unavailable` (locked or disconnected session).
- One agent owns the desktop at a time. Pending-operation tracking is per server process, so
  don't drive the generic server and the companion concurrently.
- After a timeout, inspect before continuing; a blocked UIA provider can't be interrupted.

## Tests

`FlaUI.Mcp.Tests` and `FlaUI.Automation.Tests` need no desktop. Integration tests and the
companion smoke test launch apps and take focus; run them only during a desktop handoff.
