# Operation and protocol recovery

The stdio server supports MCP 2024-11-05 and 2025-03-26 initialization. Unknown requested revisions receive the supported 2024-11-05 revision, which a client must accept explicitly or disconnect. This does not introduce the newer stateless MCP lifecycle.

Requests retain operation IDs in structured results. `completed` describes tool execution; `verification: not-established` does not assert the target changed. Existing tool-specific verification is preserved. The operation-status tool can inspect delayed provider completion.

Cancellation notifications cancel only the matching in-flight request ID. Notifications never dispatch tools or produce responses. The input reader remains available while serialized tools run, with at most 32 admitted requests. Cancellation suppresses the eventual RPC response and requests cooperative cancellation; it does not undo input already dispatched or terminate an unresponsive UIA provider. Never replay on timeout. Observe the target and pending operation first.

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
| `steps` | For `windows_batch`: one outcome per step, numbered from 1. |
| `operationId`, `pendingPatternId`, `modalTitle`, `verification`, `message` | Unchanged. |

An outcome is pending when `provider` is `pending` or `dispatch` is `timed_out_pending`. A batch that stops at a pending step after earlier steps sent input reports `partial` with `provider: pending`, and must not be replayed. A host that tests for `completed` or `timed_out_pending` must also handle `partial`.

Operation status (`flaui/operations/status` and `windows_operation_status`) reports `status` as `ok`, `failed`, `running` or `timed_out_pending`. While an operation has no result yet, its `dispatch` is `partial` once input went out and `unknown` before that.

### Refusal codes

| Code | Raised when |
|---|---|
| `desktop-unavailable` | Windows reports no foreground window: the session is locked, disconnected or non-interactive. |
| `activation-denied` | Windows kept another window in front of the target. |
| `integrity-blocked` | The target runs at a higher integrity level, or with UIAccess, than the server, or its token cannot be read. |
| `point-not-on-target` | A pointer point is off the target window or covered, or an element's click point is obscured. `foregroundProcess` names the process under the point. |
| `click-point-unavailable` | The element is offscreen, has no bounds or clickable point, or the point is outside it or scrolled out of a containing pane. |

### Refusal text after a change

A refusal says "no input sent" only when nothing changed. When guarded input had already activated or restored the target, focused the control or scrolled the element into view, the phrase becomes, for example, "no keys or clicks were sent, but the target window was activated first". A refusal without that phrase gets a sentence such as "Before this, the control was focused." appended. A refused fallback click after Select says "No keys or clicks were sent, but Select was sent first." The possible notes are `the target window was activated`, `the target window was restored`, `the control was focused` and `the view was scrolled`.

### JSON-RPC refusals

| Code | Message | Meaning |
|---|---|---|
| -32700 | `Parse error; no action dispatched.` | The line was not valid JSON-RPC. `data.notDispatched` is true, and the recovered id is recorded as not dispatched unless a request with that id is in flight. |
| -32000 | `Request capacity reached; no action dispatched.` | 32 requests were already in flight. The same recording rule applies. |
| -32603 | The exception message, such as `Unknown method: x` | The request failed in the server. |

A `tools/call` that reuses the id of an in-flight request returns the tool error `Duplicate in-flight request ID; no action dispatched.`

### Pattern calls

A pattern call run through `ModalAwareInvoker.Execute` must count itself with `MutationGuard.Execute` (or `MutationGuard.RecordDispatch`) when it is sent. A call that is still pending when the grace period ends, and has not counted itself, is reported as `timed_out_pending`: its validation may still be waiting on a hung provider. A detected modal dialog proves that the call reached the app, so it always counts as dispatched.

## Adapting a product repo

A product repo that merges this core must check the following.

- **Outcome values.** Handle `dispatch: partial` wherever `completed`, `not-dispatched` or `timed_out_pending` is matched. Read `inputsSent` and `failure.actionDispatched` before treating a failed call as safe to retry.
- **Refusal text.** Tests and prompts that match "no input sent" must accept the rewritten forms above. The pointer refusals end in "; no input sent.", and the obscured-click text is now "Click point is obscured by another window; no input sent."
- **Exception types.** `MouseTool.RequireHit`, `ClickPoint.Choose` and the obscured-click check throw `InputRefusedException`, a subclass of `InvalidOperationException`. Tests that assert the exact type must expect it.
- **Annotations.** Every tool declares MCP annotations. Find, wait, snapshot, get-text, get-clipboard, screenshot, list-windows and operation status are read-only. The input tools are not: click, send-keys, paste, fill, mouse, batch and close are destructive, and window placement, window state and focus are idempotent.
- **Registration.** `ToolComposition.Register` adds `windows_set_window_state` (`WindowStateTool(elements, pending, sessions)`) and `windows_mouse` (`MouseTool(elements, pending, sessions)`) for every host. A host that registers its tools itself must add them.
- **Behaviour.**
  - Pointer actions press only where an element is visible (`ClickPoint`). They scroll an element into view through `ClickGeometry.ScrollIntoView`, which takes the input lease, revalidates the ref and counts. `ClickGeometry.Read` no longer scrolls.
  - The server refuses to start unless per-monitor-v2 DPI awareness is in force.
  - Launch, focus, close, window state and pointer parking take the input lease, the integrity check and the mutation-permission check, like input does.
  - Focusing a minimized window counts as a change even when activation is then denied.
  - Paste counts its clipboard write.
- **Signatures.**
  - `InputDesktop` gains `SetClipboardText`; a fake desktop must override it.
  - `PatternCallResult` gains `Dispatched`.
  - `GuardedInput` gains `Check(Action)` and `Release(Action)`.
  - `ClickGeometry.ScrollIntoView(element, processId, pending, validate)` is new.
  - `MouseTool.Drag` is internal.
  - `ClickTool` is a partial class; its replies are in `ClickToolReplies.cs`.
  - The transport calls the new `McpServer.RecordRefusedRequest`.
- **Offline tests.** A test assembly that drives input tools must install a fake `InputDesktop`, and should set `FLAUI_OFFLINE_TESTS=1` (the core test assembly does this in a module initializer). The real desktop then refuses every change if a test misses the fake.
