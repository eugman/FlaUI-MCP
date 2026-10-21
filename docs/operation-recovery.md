# Operation and protocol recovery

The stdio server supports MCP 2024-11-05 and 2025-03-26 initialization. Unknown requested revisions receive the supported 2024-11-05 revision, which a client must accept explicitly or disconnect. This does not introduce the newer stateless MCP lifecycle.

Requests retain operation IDs in structured results. `completed` describes tool execution; `verification: not-established` does not assert the target changed. Existing tool-specific verification is preserved. The operation-status tool can inspect delayed provider completion.

Cancellation notifications cancel only the matching in-flight request ID. Notifications never dispatch tools or produce responses. The input reader remains available while serialized tools run, with at most 32 admitted requests. `ping`, the `flaui/operations/status` method and a `tools/call` of a status tool are never refused for capacity, and a status tool does not wait for the serial slot, so status stays readable while a stranded call still holds it. A status tool is one whose annotations are `McpToolAnnotations.Status` (read-only with `StatusHint`, which is not sent to clients), or `windows_operation_status` by name. Cancellation suppresses the eventual RPC response and requests cooperative cancellation; it does not undo input already dispatched or terminate an unresponsive UIA provider. Never replay on timeout. Observe the target and pending operation first.

No-focus checks use fake tools, signals, and timers. Foreground, UIPI, docking behavior and real screenshots still require a fresh machine grant.

For a locally merged, unpublished Core commit, set `CORE_REVISION` to that exact commit when running product gates. The ownership gate requires that commit to be an ancestor and compares shared paths with its tree. The default remains `core/main`; no remote-tracking ref is rewritten to impersonate a published commit.

The optional `flaui/operations/status` JSON-RPC extension is advertised as `capabilities.experimental.flauiOperationStatus.version = 1`. Supply `requestKey`, the JSON representation of the original request ID (for numeric ID 7, use string "7"). The response echoes that key and returns matching operations plus `notDispatched` for a cancelled queued request. `finished` and provider status describe execution lifetime, not verified product state; `resultOutcome` preserves tool-specific evidence. Unknown or evicted requests never imply safe retry.

## Outcome contract

Every `tools/call` result carries `structuredContent` with these fields. Product hosts and brokers read them instead of parsing the text.

| Field | Values and meaning |
|---|---|
| `dispatch` | `completed`: the call's input or pattern call went out. `partial`: some input went out, then the call failed or stopped. `not-dispatched`: nothing went out, so the call may be retried. `timed_out_pending`: the call is still running (it timed out, was cancelled, or is a pattern call that has not yet counted its dispatch); never replay it. |
| `provider` | `returned`, `pending` (a pattern call still holds the app's UI Automation provider), `not-tracked`, `unknown`. |
| `status` | `ok` or `failed`: whether the tool did what was asked, separate from how much input went out. |
| `inputsSent` | The number of changes the call made: keys, clicks, pointer moves, pattern calls, focus and restore, close, window state, clipboard writes, scroll-into-view and process starts. Only changes that happened are counted. |
| `failure` | Set when guarded input refused: `code`, `message`, `foregroundProcess` (`name`, `pid`), `target` (`pid`, `integrityLevel`, `uiAccess`) and `actionDispatched`. |
| `steps` | For `windows_batch`: one outcome per step, numbered from 1. A batch with a failed step has `status: failed` and `isError: true`, even when earlier steps sent input (`dispatch: partial`). |
| `message` | New: the tool's text, for clients that show only `structuredContent`. |
| `operationId`, `pendingPatternId`, `modalTitle`, `verification` | Unchanged. |

An outcome is pending when `provider` is `pending` or `dispatch` is `timed_out_pending`. A call stranded by its timeout or a cancel reports `timed_out_pending` with `status: failed`, and keeps any refusal it recorded before it stranded in `failure`. A batch that stops at a pending step after earlier steps sent input reports `partial` with `provider: pending`, and must not be replayed. A host that tests for `completed` or `timed_out_pending` must also handle `partial`.

Operation status (`flaui/operations/status` and `windows_operation_status`) reports `status` as `ok`, `failed`, `running` or `timed_out_pending`. While an operation has no result yet, its `dispatch` is `partial` once input went out and `unknown` before that.

### Refusal codes

| Code | Raised when |
|---|---|
| `desktop-unavailable` | Windows reports no foreground window: the session is locked, disconnected or non-interactive. |
| `activation-denied` | Windows kept another window in front of the target. |
| `integrity-blocked` | The target runs at a higher integrity level, or with UIAccess, than the server, or its token cannot be read. |
| `point-not-on-target` | A pointer point is off the target window or covered, or an element's click point is obscured. `foregroundProcess` names the process under the point. |
| `click-point-unavailable` | The element is offscreen or has no bounds, or the point (its clickable point, or the centre of its bounds when it reports none) is outside it or scrolled out of a containing pane, and no part of the element (at least 4 px each way) is visible. A containing pane's viewport excludes its own scrollbars and headers; when the element's own point is hidden, the centre of its visible part is pressed instead. A requested point (an edge) is never moved. |
| `click-point-covered` | Just before input, UI Automation's hit test at the press point reports neither the element nor one of its descendants (a scrollbar, header, sibling or overlay inside the same window), or the hit test failed. The message names it: `The point (x,y) is covered by ScrollBar 'Name'`. Checked for windows_click's mouse clicks and windows_mouse points derived from a ref (move, hover, scroll, click, drag start); raw window points are not hit-tested. |

### Refusal text after a change

A refusal says "no input sent" only when nothing changed. When guarded input had already activated or restored the target, focused the control or scrolled the element into view, the phrase becomes, for example, "no keys or clicks were sent, but the target window was activated first". A refusal without that phrase gets a sentence such as "Before this, the control was focused." appended. A refused fallback click after Select says "No keys or clicks were sent, but Select was sent first." The possible notes are `the target window was activated` (only when the target, or its modal dialog, is then in front), `the target window was restored`, `the foreground window changed` (focus moved another window in front; the activation check then refuses), `the control was focused` and `the view was scrolled`.

`windows_focus` by handle is ok (`verification: verified`) only when the target window is in front afterwards; otherwise it fails, with `partial` when the foreground still changed. Focus of a window known only through UI Automation cannot be observed and stays unverified.

### JSON-RPC refusals

| Code | Message | Meaning |
|---|---|---|
| -32700 | `Parse error; no action dispatched.` | The line was not valid JSON-RPC. `data.notDispatched` is true, and the recovered id is recorded as not dispatched unless a request with that id is in flight or already ran (while its operation is retained). |
| -32000 | `Request capacity reached; no action dispatched.` | 32 requests were already in flight. The same recording rule applies. |
| -32603 | The exception message, such as `Unknown method: x` | The request failed in the server. |

A `tools/call` that reuses the id of an in-flight request returns the tool error `Duplicate in-flight request ID; no action dispatched.`

### Pattern calls

A pattern call run through `ModalAwareInvoker.Execute` must count itself with `MutationGuard.Execute` (or `MutationGuard.RecordDispatch`) when it is sent. A call that is still pending when the grace period ends, and has not counted itself, is reported as `timed_out_pending`: its validation may still be waiting on a hung provider. A detected modal dialog proves that the call reached the app, so it always counts as dispatched. A modal is detected only when a window of the process that was enabled becomes disabled, as a modal disables its owner; a new tooltip, menu or tool window alone is not a modal. Its title comes from the new owned, non-tooltip window. `Win32WindowInfo` gains `Owner` and `ClassName`, and `InputDesktop` gains `TopLevelWindows`.

## Adapting a product repo

A product repo that merges this core must check the following.

- **Outcome values.** Handle `dispatch: partial` wherever `completed`, `not-dispatched` or `timed_out_pending` is matched. Read `inputsSent` and `failure.actionDispatched` before treating a failed call as safe to retry.
- **Refusal text.** Tests and prompts that match "no input sent" must accept the rewritten forms above. The pointer refusals end in "; no input sent.", and the obscured-click text is now "Click point is obscured by another window; no input sent."
- **Exception types.** `MouseTool.RequireHit`, `ClickPoint.Choose` and the obscured-click check throw `InputRefusedException`, a subclass of `InvalidOperationException`. Tests that assert the exact type must expect it.
- **Annotations.** Every tool declares MCP annotations. Find, wait, snapshot, get-text, get-clipboard, list-windows and operation status are read-only. The input tools are not: click, send-keys, paste, fill, mouse, batch and close are destructive, and window placement, window state and focus are idempotent. Screenshot is not read-only either, because `parkPointer` moves the pointer before capturing and counts that as input; it is idempotent and not destructive.
- **Status tools.** A host's own status tool (such as `cam_operation_status`, like `windows_operation_status`) returns `McpToolAnnotations.Status` from `Annotations` to be answered while other calls block. Use `Status` only for instant, in-memory reads that may bypass the host's own gate; a tool that reads UI Automation or takes the host gate, such as `cam_session_status`, stays plain `ReadOnly`, because it runs beside a call that may still be sending input.
- **Registration.** `ToolComposition.Register` adds `windows_set_window_state` (`WindowStateTool(elements, pending, sessions)`) and `windows_mouse` (`MouseTool(elements, pending, sessions)`) for every host. A host that registers its tools itself must add them.
- **Behaviour.**
  - Pointer actions press only where an element is visible (`ClickPoint`). They scroll an element into view through `ClickGeometry.ScrollIntoView`, which takes the input lease, revalidates the ref and counts only when the element moved (a refusal then says "the view was scrolled first", or "a scroll into view was requested first" when nothing moved). The point is read again under the input lease just before the press. `ClickGeometry.Read` no longer scrolls.
  - The server refuses to start unless per-monitor-v2 DPI awareness is in force.
  - Launch, focus, close, window state and pointer parking take the input lease, the integrity check and the mutation-permission check, like input does.
  - Focusing a minimized window counts as a change even when activation is then denied.
  - Paste counts its clipboard write.
- **Signatures.**
  - `InputDesktop` gains `SetClipboardText`, `PreserveAndSetClipboardText`, `StartProcess`, `CallPattern`, `IsNormalWindow`, `WindowBounds` and `PlaceWindow`; a fake desktop overrides the ones its tests reach.
  - `PatternCallResult` gains `Dispatched`.
  - `GuardedInput` gains `Check(Action)` and `Release(Action)`.
  - `ClickGeometry.ScrollIntoView(element, processId, pending, validate)` is new; it returns the note for a later refusal, or null when the element cannot scroll.
  - `MouseTool.Drag` is internal.
  - `ClickTool` is a partial class; its replies are in `ClickToolReplies.cs`.
  - The transport calls the new `McpServer.RecordRefusedRequest`.
- **Native focus and close.** `SessionManager.FocusWindow` and `CloseWindow` on a window with a native handle deliberately bypass the pending tracker: Win32 focus and `WM_CLOSE` never wait on UI Automation, and they are the documented way to reach or close the modal dialog that is blocking the provider. Hosts that want the stricter behaviour can check `PendingInvokeTracker.TryGetPending` for the window's process themselves before calling them, as TE3's `RunExecutor` does.
- **Raw calls to switch.** Product code must not start processes, make pattern calls or write the clipboard raw. Use the public guarded calls instead; each takes the lease and the permission check, counts, and is refused under `FLAUI_OFFLINE_TESTS`:
  - `GuardedDesktop.StartProcess(startInfo)` for `Process.Start`.
  - `GuardedDesktop.CallPattern(processId, description, call, pending, validate, gracePeriod)` for a UI Automation call that changes the app. It refuses while the provider is blocked, runs `validate` and the integrity check, counts, and runs modal-aware, returning the `PatternCallResult` (pass it to `ToolOutcome.FromPattern`). It takes the lease itself, so do not hold a `GuardedInput` around it.
  - `GuardedDesktop.SetClipboardText(text)` without a guard, or `GuardedInput.SetClipboardText(text)` and `GuardedInput.PreserveAndSetClipboardText(text)` under one.

  The call sites to switch:
  - FlaUI-MCP-PBI: `SessionLifecycle.cs:150` (`Process.Start` of Desktop) → `GuardedDesktop.StartProcess`; `ScopedExpand.ExpandByPattern` (its own lease, `ModalAwareInvoker` and `MutationGuard.Execute` around `Expand()`) → `GuardedDesktop.CallPattern`; `DesktopEditor.cs:57` (`Win32Desktop.PreserveAndSetClipboardText` under a `GuardedInput`) → `input.PreserveAndSetClipboardText`. `CommandClient.cs:65` and `RecordingEncoder.cs:68` start helper processes, not the app under test; switch them too if they should be refused offline.
  - FlaUI-MCP-Camtasia: `SessionWorkflow.cs:69` (Camtasia launch), `RecorderSystem.cs:135` and `RecorderCommandLine.cs:21` (recorder), `CalibrationFiles.cs:223` (cam-CLI) and `RecordingRun.cs:50` (encoder) → `GuardedDesktop.StartProcess`.
  - FlaUI-MCP-TE3: `CliRunner.cs:24` (TE CLI) and `ArtifactFiles.cs:338` (git) → `GuardedDesktop.StartProcess` where the process changes the desktop or the model; a pure helper such as git may stay raw.
- **Offline tests.** `InputDesktop` is internal and visible to `FlaUI.Mcp.Tests`, `FlaUI.Mcp.PowerBi.Tests`, `FlaUI.Mcp.Camtasia.Tests` and `FlaUI.Mcp.Te3.Tests`. A product test assembly that drives input tools must derive a fake from it and install it through `InputDesktop.Override` (see `FakeDesktop` in the core tests), and must set `FLAUI_OFFLINE_TESTS=1` before any test runs, as the core test assembly does in a module initializer. The real desktop then refuses the changes it covers (listed below) if a test misses the fake.

### Changes since the previous core release

These are the differences a product repo meets when it merges this core over the previous `core/main`.

**Outcome values that are gone.**

- `dispatch: failed` is gone. Registry error results, a failed batch and a batch step that throws now report `not-dispatched` when nothing went out, or `partial` when some input did. `BlockedResult` reports `not-dispatched` with `provider: pending`.
- In operation status, `status: provider_pending` and `status: completed` are gone (`status` is now `ok`, `failed`, `running` or `timed_out_pending`, and a pending provider shows in `provider: pending`). `dispatch: running` and `dispatch: failed` are gone too.

**Status fields that are new.** Operation status entries add `requestKey`, `finished`, `annotations`, `inputsSent` and `resultOutcome`.

**Every result carries an outcome.** A tool that sets none gets one from the registry: `dispatch` from whether it failed and how much input went out, `provider: not-tracked` (or `pending` while a pattern call it started is still held) and `verification: not-established`. `McpToolResult.Outcome` is now `[JsonIgnore]`; `structuredContent` is computed from it and adds `message`, so deserializing a result no longer fills `Outcome`. `status`, `inputsSent`, `failure` and `message` are new fields; `verification`, `operationId`, `pendingPatternId`, `modalTitle` and `steps` were already there.

**What inputsSent counts.** One per `GuardedInput.Send` call, so a 64-character chunk of typed text is one input, plus each counted change listed in the outcome contract above.

**Texts that are new or changed.**

- `Ref <ref> has no process identity; no action dispatched. Refresh it with windows_snapshot or windows_find on a window handle.`
- Click, pending and not yet sent: `Not sent yet: the request is still waiting on the app, whose UI Automation provider has not answered. ...`
- Fill, pending: `SetValue has not been sent yet: ...` or `SetValue dispatched; the provider call is still pending. ...`
- Focus and close by UI Automation, pending: `<Focus|Close> on <handle> was sent; the app's UI Automation provider has not returned. ...` or `... is still waiting on the app's UI Automation provider and may not have been sent. ...`
- Close: `WM_CLOSE was not posted to window <handle> (Win32 error <n>); it was not asked to close.` (`Win32Desktop.CloseWindow` now returns whether the message was posted.)
- Paste: `The clipboard is in use by another process, so nothing was pasted.`, plus `Before this, the target window was activated, so the desktop foreground changed.` only when it was. A failure after the clipboard write ends with `The clipboard now holds that text.`
- `windows_wait` failures name the exception type when the message is empty: `windows_wait: <type> No input sent.`
- `ScrollIntoView did not return; the app's UI Automation provider may be blocked. Nothing was pressed. Check windows_operation_status before retrying.`
- Startup: `Windows refused to make this process per-monitor-v2 DPI aware, ... so FlaUI-MCP will not start. ...`
- Offline tests: `The real desktop takes no input while FLAUI_OFFLINE_TESTS=1; install a fake desktop. No input sent.`
- Launch, focus, close and other lease holders while another call holds the lease: `Desktop input is busy; no input sent.`

**Mixed phrasing.** Not every refusal says "no input sent". Product code that matches `\bno input sent\b` to decide that nothing went out must also handle "no action dispatched" (ref without identity, JSON-RPC refusals, duplicate request ids), "nothing was pasted" (paste), "No keyboard input was dispatched" (send-keys), "no further input sent" (focus moved during a call) and the rewritten "no keys or clicks were sent, but ... first". Prefer `dispatch` and `inputsSent` over text.

**Signatures.**

- `OperationCoordinator.Begin(tool, requestKey, annotations)` and `Status(id, pending, requestKey)`, plus `Ran(requestKey)`.
- `ToolRegistry.ExecuteToolAsync(name, arguments, cancellationToken, requestKey, whenStranded)`.
- `ToolBase.Annotations` and `McpTool.Annotations`, with `McpToolAnnotations.ReadOnly` and `McpToolAnnotations.Input(destructive, idempotent)`.
- `SessionManager.FocusWindow(handle, pending)` and `CloseWindow(handle, pending)`, plus `UiaWindowFor(handle, pending)`; the window tools take a `PendingInvokeTracker`.
- `Win32Desktop.CloseWindow` returns `bool`.
- `MutationGuard.RecordDispatch` counts one input against the running operation, and `MutationGuard.Execute` counts before its mutation runs.
- New public types: `InputRefusal`, `ProcessRef`, `ProcessIntegrity`, `InputRefusedException`.
- `ToolOutcome.DispatchFor(failed, inputsSent)`.
- `ElementSelector.NameContains`.

**Raw Win32 mutators are internal.** `Win32Desktop.FocusWindow`, `CloseWindow`, `PlaceWindow`, `SetWindowState`, `SetClipboardText` and `PreserveAndSetClipboardText` are `internal` (visible to the test assemblies), so product code that calls them no longer compiles. Use `SessionManager.FocusWindow`/`CloseWindow`, `windows_place_window`, `windows_set_window_state`, `GuardedDesktop.SetClipboardText`, or `GuardedInput.SetClipboardText`/`PreserveAndSetClipboardText` under a guard. Known call site: FlaUI-MCP-PBI `NativeRecoveryDesktop.cs:233` (`Win32Desktop.CloseWindow` inside `GuardedInput.Send`).

**Variant code that bypasses the guards.** These calls skip the input lease, the integrity check, the counting and the offline switch, and should go through `SessionManager`, `WindowMutation` or `GuardedInput` instead:

- FlaUI-MCP-PBI: `DemoSession.cs:720` (`Win32Desktop.FocusWindow`), `SessionLifecycle.cs:559` (`Win32Desktop.CloseWindow`), `ForegroundOps.cs:16` (`Win32Desktop.FocusWindow`).
- All three variants: raw `GetClickablePoint` followed by a `GuardedInput` click, which skips `ClickPoint`'s visibility checks and the guarded scroll.

**Call sites to fix for `partial`.** Both treat only `completed` or `timed_out_pending` as "input went out", so a `partial` call would be read as safe to retry:

- FlaUI-MCP-PBI `ProviderEvidence.cs:73` and `:81`.
- FlaUI-MCP-Camtasia `CamSessionInput.cs:110`.

### What the offline switch covers

With `FLAUI_OFFLINE_TESTS=1` and no fake installed, the real `InputDesktop` refuses, before doing anything, every change made through it:

- `Dispatch`: all keyboard and mouse input sent through guarded input, including pointer parking.
- `Focus`, `FocusElement`, `PostClose`, `SetWindowState`, `PlaceWindow` and `SetClipboardText`.
- `StartProcess`: `WindowMutation.Start`, used by `windows_launch`.
- `CallPattern`: the UI Automation calls that change an app (Invoke, Toggle, Select, Expand, Collapse, SetValue, ScrollIntoView, and UI Automation focus and close).

It does not cover anything that bypasses the seam: raw FlaUI input (`element.Click()`, `Mouse`/`Keyboard`, or `GetClickablePoint` followed by a mouse call), and UI Automation reads. Product code that calls these directly is not protected by the switch, and should go through `InputDesktop` or guarded input instead.

