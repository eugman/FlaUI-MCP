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
| `status` | `ok`, `failed` or `unverified`: whether the tool did what was asked, separate from how much input went out. A tool with a `proof` is `ok` only when its verdict is `verified` or `no-op`, `failed` on `mismatch` or `not-applied`, and `unverified` when it could not check (`unavailable`, `not-established`). A call with no proof is `unverified` when it sent input (`verification: not-established`: nothing checked the effect, as for a plain Invoke, typed text or a key chord) and `ok` when it sent none (a read-only tool, or a mutating one with nothing to do). `unverified` is never success: operation status reports it too, a batch with an unverified step is `unverified`, and the broker treats it as "not success, not quarantined" (observe before relying on the effect; nothing failed after input). |
| `proof` | `{verdict, observed, expected, source, independent}`: how the tool checked its effect (focus, place, expand/collapse/toggle, fill, paste, and a screenshot's park when it verified). A screenshot that parked the pointer always reports the park's proof as `details.park`. A park that left the pointer over the window makes the capture `unverified`, with a warning, and never `failed`; a refused park moved nothing, so the capture stays `ok`. `independent` is false when the read-back came through the same channel that acted (SetValue then Value). Hosts build theirs with `Proof.Compare` and never hard-code `verified`. `verification` repeats the verdict. |
| `inputsSent` | The number of changes the call made: keys, clicks, pointer moves, pattern calls, focus and restore, close, window state, clipboard writes, scroll-into-view and process starts. Only changes that happened are counted. |
| `failure` | Set when guarded input refused: `code`, `message`, `foregroundProcess` (`name`, `pid`), `target` (`pid`, `integrityLevel`, `uiAccess`) and `actionDispatched`. |
| `steps` | For `windows_batch`: one outcome per step, numbered from 1. A batch with a failed step has `status: failed` and `isError: true`, even when earlier steps sent input (`dispatch: partial`). |
| `message` | New: the tool's text, for clients that show only `structuredContent`. |
| `operationId`, `pendingPatternId`, `modalTitle`, `verification` | Unchanged. |
| host details | A host's own fields from `ToolOutcome.Details`, such as TE3's `walk`, written at the top level beside the fields above. Absent unless the host set them. |

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
| `operation-failed` | A tool failed before any recorded input without a more specific refusal, and its provider has returned. `actionDispatched: false` means this call itself did not act; inspect or correct the cause before retrying. A pending or already dispatched call never receives this fallback. |
| `invalid-arguments` | A required argument is missing or the arguments contradict each other (`Missing required argument: ref`, `verifyFocus requires an element ref`); refused before any input, `actionDispatched: false`. Fix the call. |
| `drop-point-in-scroll-band` | A `windows_mouse` drag's `toRef` drop point lies within one row (the element's own height) of the top or bottom of an ancestor's viewport whose content scrolls vertically now (Scroll pattern `VerticallyScrollable` with a view smaller than the content; a list that holds all its rows has no band), where a resting drag auto-scrolls the view and the release would land on another row; refused before input. Scroll the view so the target is away from that edge, then observe and retry. |
| `drag-interrupted` | A `windows_mouse` drag failed while the button was down for a reason that is not a refusal: a provider error re-reading the drop target, or the operation was cancelled (the cancellation is still the reply's error; this refusal is its `failure`). Escape was sent where the target still held the foreground, the button was released, and the text says what went out; `actionDispatched: true`. A step's own refusal with the button down (`target-changed`) takes the same way out and says so in its text. Observe the window, then retry. |
| `drop-target-moved` | A `windows_mouse` drag re-checked its drop point after the last move, with the button still down, and found it no longer on what it was proved on: the target window is no longer under it, the `toRef`'s re-read bounds no longer hold it (`TreeItem 'X' is now at (x,y wxh), which no longer holds the drop point (x,y)`), the hit test now reports something covering it, or its geometry could not be re-read. Escape was sent before the release while the target still held the foreground (an OLE drag ends without a drop on it; a mouse-capture drag may still drop; the text says `Escape was not sent (reason)` otherwise), so whether the app dropped is not verified: the call is `status: unverified`, `dispatch: completed`, `actionDispatched: true`, `inputsSent` counts the moves, the Escape and the release, and the text ends with what UI Automation reports under the pointer now. Observe the window, then retry. |
| `target-stale`, `window-ownership-unknown`, `focus-not-verified`, `process-not-allowed` | The guard refused before any input: the target's identity no longer holds or cannot be read, the control did not take focus, or the foreground process is off the allowlist. |
| `launch-pending` | `windows_launch` started a process that is still alive, but could not attach one unique usable window in time. The result is `status: unverified`, `dispatch: partial`, with `processId` and `observedWindows` (title and HWND) in `structuredContent`. Inspect windows and attach explicitly; never retry the launch blindly. |
| `launch-failed` | `windows_launch` could not start the app, or its started process exited before a usable window could be attached. |
| `window-not-found` | No top-level window has the title asked for. |
| `target-changed`, `focus-lost` | The target lost the foreground or was disabled, or focus left the control during a keyboard fill. Raised after input, these say `actionDispatched: true`; never replay. |
| `click-point-covered` | Just before input, UI Automation's hit test at the press point reports neither the element nor one of its descendants (a scrollbar, header, sibling or overlay inside the same window), or the hit test failed. The message names it: `The point (x,y) is covered by ScrollBar 'Name'`. A descendant that is itself a row (`TreeItem`, `ListItem`, `DataItem`: the child row of an expanded tree item, whose bounds include its children's) does not pass, except a `DataItem` inside a `DataItem` element (a grid cell inside its row: the MSAA proxy reports both as `DataItem`): `The point (x,y) is on TreeItem 'Name', a row inside the element`. An ancestor also passes when it is at most 3 levels up, is not the top-level window, the element's own bounds hold the point (`is outside the element's bounds, on List 'Name'` otherwise), and no onscreen sibling of the element, or of an ancestor between it and the hit, has bounds over the point (3 levels is a working value, pending the live check): some providers report the container for a point on its child (a WinForms PropertyGrid row through MSAA, a Chromium node, a WPF text block). UI Automation gives no z-order, so any sibling over the point refuses. A sibling is skipped as offscreen only when its IsOffscreen read succeeded and said true; one whose bounds cannot be read counts as covering ("whose bounds could not be read"). A pattern call whose window baseline cannot be read is refused before it is sent (`window-baseline-unreadable`), since modal detection would be off for it. The reply's `hitTest` says how the point was proven: `target`, `descendant` or `ancestor` (the weakest of the call). Checked for windows_click's mouse clicks and windows_mouse points derived from a ref (move, hover, scroll, click, drag start); raw window points are not hit-tested. A live check on TE3 PropertyGrid rows, PBI page tabs and the Camtasia timeline clip is pending the desktop. |

### Raw drag point guards

`windows_mouse` `drag` accepts optional `fromMustHit` with a raw `from` point and `toMustHit` with a raw `to` point. Each names an element ref from the same observed window. The tool checks that the fixed screenshot coordinate is visible inside that ref and hit-tests it before pressing the button. It never scrolls the ref or adjusts the coordinate. A guarded `to` is read and hit-tested again while the button is down, just before release; if the element moved or is covered, the drag takes the `drop-target-moved` cancellation path and reports the outcome as unverified. Supplying either guard without its raw point is an invalid argument before input. Successful drag replies include `fromPoint` in screen and window coordinates, alongside the release point and any `droppedOn` result. Existing unguarded raw drags retain their behavior.

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

A pattern call run through `ModalAwareInvoker.Execute` must count itself with `MutationGuard.Execute` (or `MutationGuard.RecordDispatch`) when it is sent. A call that is still pending when the grace period ends, and has not counted itself, is reported as `timed_out_pending`: its validation may still be waiting on a hung provider. A call made through `GuardedDesktop.CallPattern` (or `WindowMutation.Uia`) that is still in validation when its tool returns is then never sent: the input lease is released at that point, so it would otherwise go out unleased. A pattern call that fails after its tool reported it pending records the failure in the operation's `lateError`, and operation status then reports `status: failed`. A failed window read while waiting no longer ends the wait. A detected modal dialog proves that the call reached the app, so it always counts as dispatched. A modal is detected only when a window of the process that was enabled becomes disabled, as a modal disables its owner; a new tooltip, menu or tool window alone is not a modal. Its title comes from the new owned, non-tooltip window. `Win32WindowInfo` gains `Owner` and `ClassName`, and `InputDesktop` gains `TopLevelWindows`.

### What the offline switch covers

With `FLAUI_OFFLINE_TESTS=1` and no fake installed, the real `InputDesktop` refuses, before doing anything, every change made through it:

- `Dispatch`: all keyboard and mouse input sent through guarded input, including pointer parking.
- `Focus`, `FocusElement`, `PostClose`, `SetWindowState`, `PlaceWindow` and `SetClipboardText`.
- `StartProcess`: `WindowMutation.Start`, used by `windows_launch`.
- `CallPattern`: the UI Automation calls that change an app (Invoke, Toggle, Select, Expand, Collapse, SetValue, ScrollIntoView, and UI Automation focus and close).

It does not cover anything that bypasses the seam: raw FlaUI input (`element.Click()`, `Mouse`/`Keyboard`, or `GetClickablePoint` followed by a mouse call), and UI Automation reads. Product code that calls these directly is not protected by the switch, and should go through `InputDesktop` or guarded input instead.

