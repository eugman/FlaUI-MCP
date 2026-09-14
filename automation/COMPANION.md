# TE3 MCP companion

Live status is in [VERIFICATION.md](VERIFICATION.md). The generic FlaUI MCP stays
TE3-independent. This executable reuses its transport and the runner's typed navigation.

Responses include `assessment` with these fields:

| Field | Values |
|---|---|
| `sceneIdentity` | `matched` or `not-assessed` |
| `artifact` | `saved` or `not-saved` |
| `path` | saved file path |
| `visualCompleteness` | always `not-assessed` |
| `docsApproved` | always `false` |

Navigation saves no file. If a step after capture fails, the response still reports
the saved path.

Build `dotnet build FlaUI.Mcp.slnx`. Configure a separate stdio MCP command pointing
to the built `FlaUI.Automation.exe` with arguments `mcp`. For map-only access use
`mcp --guidance-only`. Do not overwrite a connected MCP binary; coordinate publish
and refresh separately. No global client configuration or skill installation is
performed by the build.

The four tools are `te3_catalog`, `te3_inspect`, `te3_navigate`, `te3_capture`.
Catalog/resources are usable with TE3 closed. The other tools require an explicit
running TabularEditor3 `processId`; obtain it through permitted process discovery.
Handles/refs belong to their originating MCP process and are not interchangeable.

Examples of tool arguments:

```json
{"topic":"preferences-code-actions"}
{"processId":1234,"topic":"windows"}
{"processId":1234,"destination":"preferences/code-actions"}
{"processId":1234,"scene":"preferences/code-actions","savePath":"C:/Temp/fla_code_actions.png"}
```

Supported destinations/scenes: `tom-explorer`, `expression-editor`, `object`,
`preferences/auto-formatting`, `preferences/code-actions`. Object requests also
require table, objectName, objectType (Table/Column/Measure), with optional folder.
`te3_inspect` topics are windows (default, native observation), preferences, object.
It reports unknown/unassessed readiness explicitly rather than guessing a page.

Map topics under [map/](map/start.md) are embedded once and served verbatim through
catalog and explicit `te3://map/TOPIC` resources. No arbitrary filesystem reads,
dynamic templates, subscriptions, web server, or vector store. The map revision is a hash
of the topic content, so editing a topic changes it. The packaged optional
[skill](skills/te3-ui/SKILL.md) directs agents to relevant topics; it is not globally
installed. Keep the same topic delivery in guided and companion experiment arms.

## Boundaries

- Attach only: no launch, close, preference normalization, DB reset, or script execution.
- Navigation/capture require desktop handoff permission. Navigation can open Preferences
  and change search/selection; it never accepts/cancels settings for the caller.
- Capture does not navigate, resize, or overwrite. Native capture requires the actual
  target window, not its owner. Maximized invisible borders are cropped explicitly and
  recorded; partially offscreen normal windows fail. Multi-window scenes stay runner recipes.
- Saved PNGs are not approved replacements. A post-capture failure may leave an
  unverified artifact; do not promote it. Inspect clipping and all desired controls visually.
- Only one agent may own the desktop at a time. Pending-operation tracking and input
  locks are local to a server process; do not drive generic and companion concurrently.
- Windows can refuse to activate TE3 (foreground lock), for example after the user
  clicks elsewhere. Failures then report `blocker: activation-denied` (ask the user to
  click the TE3 title bar) or `desktop-unavailable` (locked/disconnected session).
  Neither is retried or forced; unattended runs need an uncontested interactive session.
- After timeout, inspect before continuing. A blocked UIA provider cannot be reliably
  interrupted; cancellation prevents subsequent cooperative mutations, not the COM read.

## Tests

`FlaUI.Mcp.Tests` and `FlaUI.Automation.Tests` need no desktop. Integration tests launch
apps and take focus, so run them only during a desktop handoff.

Before a study block, live-check these on the current TE3 build:
- Preferences arrival
- Object Type/DAX identifier values
- tab selection
- title chrome
- DPI
- keyboard focus

See the [four-arm runbook](experiments/FOUR-ARM.md).
