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
| `status` | `ok`, `failed` or `unverified`: whether the tool did what was asked, separate from how much input went out. A tool with a `proof` is `ok` only when its verdict is `verified` or `no-op`, `failed` on `mismatch` or `not-applied`, and `unverified` when it could not check (`unavailable`, `not-established`). `unverified` is never success: operation status reports it too, a batch with an unverified step is `unverified`, and the broker treats it as "not success, not quarantined" (observe before relying on the effect; nothing failed after input). |
| `proof` | `{verdict, observed, expected, source, independent}`: how the tool checked its effect (focus, place, expand/collapse/toggle, fill, paste, screenshot park). `independent` is false when the read-back came through the same channel that acted (SetValue then Value). Hosts build theirs with `Proof.Compare` and never hard-code `verified`. `verification` repeats the verdict. |
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
| `click-point-covered` | Just before input, UI Automation's hit test at the press point reports neither the element nor one of its descendants (a scrollbar, header, sibling or overlay inside the same window), or the hit test failed. The message names it: `The point (x,y) is covered by ScrollBar 'Name'`. An ancestor also passes when it is at most 3 levels up, is not the top-level window, and no onscreen sibling of the element, or of an ancestor between it and the hit, has bounds over the point (3 levels is a working value, pending the live check): some providers report the container for a point on its child (a WinForms PropertyGrid row through MSAA, a Chromium node, a WPF text block). UI Automation gives no z-order, so any sibling over the point refuses. A sibling is skipped as offscreen only when its IsOffscreen read succeeded and said true; one whose bounds cannot be read counts as covering ("whose bounds could not be read"). A pattern call whose window baseline cannot be read is refused before it is sent (`window-baseline-unreadable`), since modal detection would be off for it. The reply's `hitTest` says how the point was proven: `target`, `descendant` or `ancestor` (the weakest of the call). Checked for windows_click's mouse clicks and windows_mouse points derived from a ref (move, hover, scroll, click, drag start); raw window points are not hit-tested. A live check on TE3 PropertyGrid rows, PBI page tabs and the Camtasia timeline clip is pending the desktop. |

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

## Adapting a product repo

A product repo that merges this core must check the following.

- **Outcome values.** Handle `dispatch: partial` wherever `completed`, `not-dispatched` or `timed_out_pending` is matched. Read `inputsSent` and `failure.actionDispatched` before treating a failed call as safe to retry.
- **Refusal text.** Tests and prompts that match "no input sent" must accept the rewritten forms above. The pointer refusals end in "; no input sent.", and the obscured-click text is now "Click point is obscured by another window; no input sent."
- **Exception types.** `MouseTool.RequireHit`, `ClickPoint.Choose` and the obscured-click check throw `InputRefusedException`, a subclass of `InvalidOperationException`. Tests that assert the exact type must expect it.
- **Annotations.** Every tool declares MCP annotations. Find, wait, snapshot, get-text, get-clipboard, list-windows and operation status are read-only. The input tools are not: click, send-keys, paste, fill, mouse, batch and close are destructive, and window placement, window state and focus are idempotent. Screenshot is not read-only either, because `parkPointer` moves the pointer before capturing and counts that as input; it is idempotent and not destructive.
- **Host fields in structuredContent.** Put a host's own structured result on `ToolOutcome.Details` (`with { Details = new Dictionary<string, JsonElement> { ["walk"] = JsonSerializer.SerializeToElement(walk) } }`) instead of folding it into the text, where it ends up inside `message`. The registry writes each entry at the top level of `structuredContent`. A key must be a camelCase name (`^[a-z][A-Za-z0-9]*$`) and must not be one of the fixed fields (`status`, `dispatch`, `message` and the rest of the table above, compared without case); either mistake throws `ArgumentException` when the outcome is built, so a detail can never replace a fixed field. The dictionary is copied, and `with` copies keep it.
- **The shared FakeDesktop.** It now answers `ClipboardText()` and `FocusedControl()` itself, so offline tests never read the machine's clipboard or keyboard focus. Its written-clipboard property is renamed `ClipboardText` to `ClipboardWritten` (a property cannot share the method's name); tests that assert `fake.ClipboardText`, such as PBI's `EditorClipboardTests`, must use `ClipboardWritten`. Focus defaults to the fake target window (`Focused`); set `Focused` to move it, or to null for "Windows reports none".
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

### Stage B: what each product changes at the rebase

These come with the shared guard, refusal, proof and tool-fact work. Each item names the product files to delete or change.

- **Delete the guard copies and use Core's.**
  - PBI: `PowerBi/Desktop/PhysicalInputObserver.cs`, `Heartbeat.cs` and `InteractionPermission.cs` (its `ChangeStamp` has no caller and is not carried over).
  - Camtasia: `Camtasia/Desktop/PhysicalInputObserver.cs` (with its inline `IInputObserver`), `Heartbeat.cs` and `InteractionPermission.cs`.
  - TE3: `IInputObserver.cs`, `PhysicalInputObserver.cs`; rebuild `Te3Interaction` on `InteractionGrant` (`Begin`, `End`, `Require`, `Guard`, `Pause`/`Observed`, `Mark`/`Rebaseline`), keeping only its activation check (`ActivationProblem`) on top of `Mark`.
  - Map product failure types to `InteractionGrant` codes: `approval-required`, `focus-revoked`, `focus-unavailable`, `paused-after-failure` (hyphenated; the TE3 underscore spellings are mapped by `RefusalCodes.Canonical`).
- **Fail closed.** Each interactive host calls `MutationGuard.RequireHook()` at startup, so a path that forgot to install the grant (TE3 `run`, Camtasia Recorder and FFmpeg starts, PBI recovery and close_all) is refused instead of allowed.
- **Machine-wide lease.** PBI removes its own desktop lease (a lock file or mutex of its own, where the rebased branch has one) in favour of Core's `DesktopLease` mutex, which every host now takes. Each product's offline test module initializer sets `FLAUI_OFFLINE_TESTS=1` and `DesktopLease.NameVariable` (`FLAUI_LEASE_NAME`) to a name of its own process, as Core's `OfflineDesktopTests` does: the override is honoured only with `FLAUI_OFFLINE_TESTS=1`, so a host cannot be pointed at a private lease. A lease that cannot be opened refuses input with `lease-unavailable`.
- **Reason texts and codes.** `InteractionGrant` keeps Camtasia's reason texts word for word ("Interaction approval required: Non-injected keyboard or mouse input observed; ask the user before resuming", "Foreground is transitioning; no input permitted", "Foreground unavailable for more than 500 ms", "Focus/input observation failed"), so the broker's text fallback and the contract's safety reasons still match; the typed code (`focus-revoked`, `focus-unavailable`, `approval-required`, `activation-denied`, `activation-pending`, `paused-after-failure`) travels in `failure.code` beside them. Prefer the code.
- **Activation prompt (TE3).** `grant.BeginPrompt()` before asking the user to click (the grant stores the prompt's starting counts itself; a second prompt while one is open, and a launch during it, are refused), then `grant.Rebaseline(windowBounds)` at the first foreground poll (every 250 ms) that sees the window in front; a rebaseline with no open prompt is refused, and one after the prompt expired revokes; the rule (no keys, a click, the last one inside the window) is applied atomically, failure revokes with `activation-denied`, and an unanswered prompt is revoked after 120 s. Constructing a grant declares the host interactive (`MutationGuard.HookRequired`).
- **Refusal types.** Derive product refusal types (PBI `PbiFailure`, Camtasia `AutomationFailureException`, TE3 `CoreCallException`) from `AutomationRefusal`, with `Code`, `InputsSent` and `ActionDispatched`. Narrow poll catches from `InvalidOperationException` to `ElementNotFoundException`: refusals, not-found and blocked are no longer `InvalidOperationException`. Pause only when `Disposition` is not `NoInput`; wire `ToolRegistry.FailedAfterInput` to the grant's `Pause` (TE3 `FailurePause`).
- **Proofs.** Build host proofs with `Proof.Compare` (PBI `pbi_navigate` `uiVerified`, open/close pane `acted`, Camtasia clip select, export duration, TE3 layout presets) and set them on the outcome with `ToolOutcome.WithProof`; never hard-code `verified`. Read `status` (now `ok`, `failed` or `unverified`) and `proof.verdict` instead of text.
- **Tool facts.** Override `Observation`, `Recovery` and `MutatingWhenArgument` on product tools and drop the hand-kept tables (PBI `DemoToolAnnotations.ByTool`, `InputFreeTools`, `ScopedDesktop.IsMutation`; Camtasia `CamSessionInput.cs:53`'s non-mutating list; README tables). New argument records can use `ToolSchema.For<T>()` with `[ToolDoc]`.
- **Hit test.** Camtasia: `ClickHitTest.PassThrough("selectionPlayhead", "informationPopup")` at startup before moving clip clicks to refs. TE3: pass the neighbouring captions to `ClickHitTest.RequireNotWithin` (or `windows_mouse drag fromMustNotHit`) for splitter drags.
- **Snapshots.** Pass the handle's HWND (`windowHwnd`) to `SnapshotBuilder.BuildSnapshot` for a scoped snapshot (Camtasia `SessionSnapshot`, PBI `RegionSnapshot` and `ScopedDesktop`).
- **Placement.** TE3 `PlacementReadBack` reads `failure.code: placement-mismatch` and `proof` instead of comparing the text.
- **Contract tests.** Link Core's `tests/FlaUI.Mcp.Tests/Contract/*.cs` into each product test project, as `FakeDesktop.cs` is linked; the product's fake must override the `InputDesktop` virtuals they use.

### Changes from the 2026-10-21 quality review

A product repo that merges this core meets these on top of the list below.

- **New failure cases (`isError: true`, `status: failed`).**
  - `windows_batch` with a failed step (it mirrors `status: failed`; earlier input makes it `partial`).
  - A stranded (timed-out or cancelled) call; it also keeps a refusal it recorded in `failure`.
  - `windows_fill` whose read-back differs from the value (`verification: mismatch`, dispatch completed).
  - `windows_focus` when the target is not in front afterwards.
  - `windows_place_window` when the window did not land exactly where requested (`mismatch`, with deltas).
  - `windows_click` expand, collapse or toggle whose state did not move (`mismatch`).
  - `windows_paste` whose read-back is `not-applied` or `mismatch`.
  - A call with an argument its closed schema does not declare: `Unknown argument 'x' for tool; no input sent. Declared arguments: ...`, not-dispatched.
- **New outcome fields and values.** `verification` is set by fill (`verified`, `mismatch`, `unavailable`), focus (`verified`), placement (`verified`, `mismatch`), window state (`no-op`), expand/collapse/toggle (`verified`, `mismatch`, `unavailable`) and paste (`verified`; `unchanged` when the value did not change but already holds the text, as a re-paste of identical text does, which is ok; `not-applied` when it did not change and lacks the text; `mismatch`; `unavailable`; `not-established`; line endings are ignored). Operation status adds `lateError` (a pattern call that failed after it was reported pending; `status` is then `failed`). `failure.actionDispatched` keeps a recorded `true`. New refusal code `click-point-covered`.
- **Behaviour.**
  - `windows_send_keys` with a ref verifies focus by default under the new `FocusRule.SelfOrDescendant` (`verifyFocus: "self-or-descendant"`): the ref or one of its descendants must hold keyboard focus, so an editable ComboBox, whose Edit child takes focus, passes. Fill's keyboard fallback, whose Ctrl+A selects everything wherever focus is, defaults to the stricter `FocusRule.EditInComposite`: the exact control, a descendant when the ref is a ComboBox or Spinner, or a focused Edit inside the ref with no other Value-pattern control in between. `verifyFocus: true` demands the exact control and `false` turns the check off. Fill checks focus again before Backspace or typing. Call sites that send keys with a ref and must now hold focus on the ref or inside it: TE3 `Te3Page.cs:1234`, `:2202` (Alt+Down on a ComboBox), `:2326` (`OpenDropdown`) and `Te3Page.Walk.cs:120`. If one of these keeps focus outside the ref (on a sibling or the pane), pass `verifyFocus: false` deliberately or target the focused child. `GuardedInput` gains a `FocusRule` constructor, and `InputDesktop` gains `FocusedElement`.
  - Fill says "Filled" only when the read-back matches.
  - Click, fill and scroll pattern calls run the integrity check; paste re-checks permission and foreground just before the clipboard write.
  - Focus is an activation only when the target is in front; another foreground change is noted as "the foreground window changed".
  - A window-state request that matches the current state sends nothing (`inputsSent: 0`).
  - Parking never lands on the window; a park the guards refuse still lets `windows_screenshot` capture, with a warning.
  - Modal detection needs an owner to become disabled; tooltips, menus and tool windows are not modals.
  - Pointer points press the visible part of an element and are hit-tested before input.
  - A batch step no longer receives the batch's `action` key, so batch clicks use Invoke or Select again instead of a forced mouse click.
  - Every core tool schema sets `additionalProperties: false`, and the registry enforces every closed schema it registers, the product tools' included: PBI (`ToolDefinition.cs:80`, `UiToolContracts.cs`), Camtasia (`ToolDefinition.cs:127`) and TE3 (`Te3InteractionTool.cs`, `Te3Layout.Schema.cs`, `Te3NativePreflightTool.cs`, `Te3Companion.cs`) already close theirs, so after the rebase an argument those schemas do not declare is refused. Check each host's callers (recipes, the broker, cam-CLI) for undeclared keys. The refusal returns before `Operations.Begin`, so it leaves no operation-status entry; nested batch steps report it as a failed step.
  - A stranded batch blocks the app of its first targeted step.
- **New types.** `WindowBaseline`, `WaitOutcome<T>`, `Diagnostics.Capture` and `DiagnosticCapture`, `PatternCallResult.WaitError`. `ModalAwareInvoker.Execute` is internal; use `GuardedDesktop.CallPattern`.
- **New `InputDesktop` virtuals.** `MovePointer`, `PointerPosition`, `Monitors`, `TopLevelWindows`, `ElementAt`, `ParentOf`, `SameElement`, `DescribeElement`, `ControlTypeOf`, `ChildBoxes`, `FocusedControl`, `ClipboardText`, `CaptureWindow` and `ElementTree`. Tools now read the foreground, focus, window bounds and owners through the seam, so a product fake that does not override these reads the real desktop for them (TE3's `FakeDesktop` overrides none of them yet). Override at least `Foreground`, `ProcessOf`, `WindowBounds`, `TopLevelWindows`, `FocusedControl` and, for pointer tests, `ElementAt`/`ParentOf`/`SameElement`.

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

