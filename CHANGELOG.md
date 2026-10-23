# Changelog

All notable changes to FlaUI-MCP will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

- `windows_launch` now returns a structured `launch-pending` receipt when the started process is still alive but no unique usable window could be attached. The unverified result includes `processId` and observed title/HWND pairs, so callers can attach without relaunching.
- `windows_mouse` `click` accepts `button: "right"` as well as the default left button. Raw point hit checks accept a popup only when Win32 reports it owned by the captured target window and in that same process.

### Changed for product repos
- Guard refusals are typed: `GuardedInput`, `InputTarget`, the keyboard fill and `SessionManager` throw `InputRefusedException` with a code instead of a plain `InvalidOperationException` or `Exception`, texts unchanged. New codes: `target-stale`, `window-ownership-unknown`, `focus-not-verified`, `process-not-allowed`, `target-changed`, `focus-lost`, `launch-failed` and `window-not-found`. The ones raised after input (`target-changed` after a send, `focus-lost`, a fill whose read-back was interrupted, a launch that started a process) say `actionDispatched: true`. Catch `AutomationRefusal`, not `InvalidOperationException`.
- A call that sent input without a `proof` reports `status: unverified` (`verification: not-established`) instead of `ok`; a call that sent nothing stays `ok`.
- `windows_screenshot` with `parkPointer` reports the park's proof as `details.park`. A park that left the pointer over the window makes the capture `unverified`, with a warning, not `failed`, so it no longer fires `FailedAfterInput`.
- Every core tool's schema comes from an argument record (`ToolSchema.For<T>()` with `[ToolDoc]`, `[ToolEnum]`, `[ToolRange]`, `[ToolSchemaFrom]`, `[ToolSchemaRequiredList]`, `NoArguments`), and 19 arguments gained descriptions. `tools/list` is otherwise byte-identical, which a snapshot (`tests/FlaUI.Mcp.Tests/Snapshots`) and a test against the pre-conversion baseline pin. The generator leaves an empty `required` out, treats an unmarked init property as optional and a property without `[ToolDoc]` as having no description; only records are expanded, and a self-referencing record is refused.
- Waits that a fake clock can hold still are capped: `ClickTool`'s settling reads and the modal wait poll at most their timeout over 50 ms. `InputDesktop` gains `Time` (the clock those waits, the Diagnostics budget and the batch `wait` row read) and `Pause`; a fake that holds `Time` still must make `Pause` advance it (the shared `FakeDesktop` does, via `IManualClock` or `OnPause`). The shared `FakeDesktop` also answers `ClipboardText()` and `FocusedControl()` itself, and its written-clipboard property is `ClipboardWritten`.
- `ProcessPolicy.NameOf(pid)` is the hosts' process-name seam, through `InputDesktop.ProcessNameOf`.
- A drag's drop point on its `toRef` is hit-tested like a press. A target whose siblings cannot be read refuses with `click-point-covered`. The input observer's start is bounded (10 s).
- A `windows_mouse` drag checks its drop point again before the release, with the button still down: the point must still be on the target window, and a `toRef`'s bounds, re-read then, must still hold it with the hit test still passing (`ClickPoint.Moved`, `ClickHitTest.Covering`). When something moved, Escape goes out before the release and the call is `unverified` (not failed: a mouse-capture drag may still drop on the release) with `failure.code` the new `drop-target-moved` (`actionDispatched: true`, `inputsSent` counted), naming what moved and what is under the pointer afterwards. Every other failure with the button down (a refused step, a provider error, a cancellation) takes the same way out, Escape (only while the target holds the foreground) then the release, and its refusal says what went out; a provider error or cancellation is the new code `drag-interrupted`. A `toRef` drop point within one row of the top or bottom of an ancestor's viewport whose content scrolls vertically now (`ClickGeometry.VerticallyScrollable`, from the Scroll pattern's `VerticallyScrollable` and `VerticalViewSize`) is refused before input with the new code `drop-point-in-scroll-band` (`ClickPoint.RequireOutsideScrollBand`); a list that holds all its rows has no band. A drop on a `toRef` edge sits `PointerPlan.DropInset` (2) px inside the edge, symmetric on every side (`EdgePoint(r, edge, inset)`); a `fromEdge` start keeps the edge's own pixel. The reply carries `changed` when the tool's scroll into view came first, and a drag with a ref carries `droppedOn`: the element UI Automation reports under the pointer after the release. A `resizeTo` drag reads the ref's bounds back after the release and carries a `proof` (`element-width`/`element-height`, independent); its status follows the verdict (`ok`, `failed` with `resized: {requested, reached}`, or `unverified` when unread).
- The hit test (`ClickPoint.Verify`) no longer proves a point on a row inside the element (a `TreeItem`, `ListItem` or `DataItem` descendant, such as the child row of an expanded tree item; a `DataItem` inside a `DataItem` element is a cell inside its row and still passes) and accepts a container's answer only when the element's own bounds hold the point; both refuse with `click-point-covered`.
- Element geometry and the scroll into view go through `InputDesktop` (`Geometry`, `ScrollIntoView`), pointer moves through `GuardedInput.MoveTo` and `InputDesktop.MovePointer`, and the pointer actions report `InputDesktop.PointerPosition()`. A product fake that tracked pointer moves through `Dispatch` must override `MovePointer`; the shared `FakeDesktop` answers `Geometry` from `ElementBounds`, `Offscreen` and the new `Viewports` (or `OnGeometry`), and `ScrollIntoView` with `ScrollNote`.
- `windows_mouse` reports a refusal (a covered or off-window point, a blocked scroll into view, a guard) as a failed result, `Failed to <action>: <reason>`, the way `windows_click` does, instead of throwing for the registry to type as `Error: <reason>`; `failure`, `dispatch` and `inputsSent` are unchanged. A direct caller of the tool now sees the same reply as an MCP client. Argument errors still throw.
- A completed Invoke, Toggle, Select or Expand (the calls that can open a dialog; not SetValue, ScrollIntoView, a focus or a close) is watched for `ModalAwareInvoker.LateModalGrace` (300 ms, on the desktop's clock, ended early by a cancellation without failing the counted call) more: a modal dialog that opens a moment after the call returned comes back as `ModalTitle` on the completed `PatternCallResult`, `windows_click` says so in its text (`then dialog "Title" opened`), and a toggle, expand or collapse verdict that read `verified` before it becomes the new proof verdict `unsettled` (status `unverified`).
- A `windows_fill` through the Value pattern is `unverified`, never `ok`: its proof's verdict is the new `self-reported` (read back through the pattern that set it, `independent: false`), and the text says the app taking it is not verified. A field in a common file dialog (under a `FileNameControlHost`, or in a `#32770` dialog that hosts `DirectUIHWND`/`ShellView`/`FileNameControlHost` or whose field has AutomationId `1148`/`1152`; `InputDesktop.ClassNameOf`) is filled by keyboard instead, so the dialog sees the change; other `#32770` dialogs keep the pattern. The fill tool's description says a Value-pattern fill is unverified. The keyboard route's verdicts are unchanged.
- An argument check a tool fails before any input (a missing `ref`, `text`, `value`, `app` or `actions`, `verifyFocus` without a ref, `windows_mouse` without a handle or ref) carries the typed refusal `invalid-arguments` (`failure.code`, `actionDispatched: false`, `dispatch: not-dispatched`) instead of a bare error text; `ToolBase.ErrorResult(code, message)` builds one for hosts.
- `HostPath` validates host-configured paths (`Validate`) and checks containment (`Within`), for the products' config readers to adopt.

### Changed for tests and tooling
- `GuardedInput.Foreground.cs` (the foreground target and activation failure), `MouseToolReplies.cs`, `MouseDrag.cs` (`DragRequest`, `DragPlanner`) and `RefPoints.cs` (`Press`) are split out of `GuardedInput` and `MouseTool` and are mutated with them.
- The test clock (`ManualTimeProvider`) fires due timers on the thread pool, not on the thread that moved it, with `WaitForCallbacks` and `PendingTimers`; the offline tests use it and one `Eventually` helper instead of wall-clock waits, and the integration tests use `WaitFor`.
- Mutation testing of the pointer code against the integration tests runs in two parts, `-Part clicks` and `-Part input` (`-Part pointer-integration` runs both), each with its own 3-hour cap and report.
- `ToolOutcome.Details`: a host's own fields (string to JSON element), written at the top level of `structuredContent`. Keys are camelCase and never a fixed field; see [the adapting guide](docs/operation-recovery.md#adapting-a-product-repo).
- `InteractionGrant` gains `BeginPrompt`/`Rebaseline(bounds)` (the grant keeps the prompt's counts) (atomic activation rule, `activation-denied`, 120 s prompt window), `End(code)`, a `counts` source for `Mark`, and refuses an unwatched batch; constructing one declares the host interactive (`MutationGuard.HookRequired`). Reason texts are Camtasia's again. `FLAUI_LEASE_NAME` counts only with `FLAUI_OFFLINE_TESTS=1`; a lease that fails refuses with `lease-unavailable`. `unverified` is reported by operation status and rolled up by batches.
- A failed read is never taken as "nothing there": an unreadable window baseline refuses a pattern call before it is sent (`window-baseline-unreadable`), a sibling whose bounds cannot be read counts as covering the press point, `NewModal` names only a window the newly disabled one owns, and a no-op window-state change still checks the window is alive and drivable. `ChildBox.Bounds` and `IsOffscreen` are nullable (unread).
- Hit-test opt-ins for hosts: `ClickHitTest.PassThrough(automationIds)` lets listed overlays (Camtasia's `selectionPlayhead`, `informationPopup`) pass a hit through (`hitTest: pass-through`) when the point is in the target's bounds and the overlay's ancestors reach the target's own top-level window; `PassThrough(check, automationIds)` adds the host's check (`(point, target, overlay) => reason or null`, such as "an acting part lies under the point"), whose reason refuses the press. The registration is per flow: it applies to the flow that made it and the tasks that flow starts later, so register it at startup before serving, and `ClickHitTest.RequireNotWithin(point, target, mustNotHit)` / `windows_mouse drag fromMustNotHit` refuse a drag start on named elements (TE3's pane captions). `InputDesktop` gains `AutomationIdOf` and `BoundsOf`.
- `SnapshotBuilder.BuildSnapshot` takes a handle's input identity only from that handle's own window: pass the handle's HWND (`windowHwnd`) for a scoped snapshot; without it, a root that is not a window gives no identity (it used to replace the handle's identity with the scope root's).
- Each tool's facts are one `ToolFacts` record (`ToolBase.Facts`: name, description, annotations, schema, inputFree, observation, recovery, mutatingWhenArgument) that builds its `tools/list` entry and the README table; `tools/list` annotations gain `observationHint`, `recoveryHint` and `mutatingWhenArgument`. A tool sets the new facts by overriding `Observation`, `Recovery` and `MutatingWhenArgument`. `ToolSchema.For<T>()` builds a closed schema from an argument record whose properties carry `[ToolDoc]`. Products: replace hand-kept tool tables (PBI's `InputFreeTools` and annotations dictionary, README tables) with the facts.
- Outcomes carry a `proof` (`verdict`, `observed`, `expected`, `source`, `independent`) for focus, place, expand/collapse/toggle, fill, paste and a screenshot park that verified; `status` is then `ok` only for `verified`/`no-op`, `failed` for `mismatch`/`not-applied`, and the new `unverified` otherwise. `Proof.Compare` builds one. A constrained placement carries `failure.code: placement-mismatch`.
- Refusals are one family, `AutomationRefusal` (Code, InputsSent, ActionDispatched, Disposition NoInput/Partial/Uncertain), deliberately not an `InvalidOperationException`: `InputRefusedException`, `ElementNotFoundException` and `ProviderBlockedException` derive from it. A `catch (InvalidOperationException)` no longer catches them; narrow poll catches to `ElementNotFoundException`. Codes are hyphenated (`RefusalCodes.Canonical` maps `focus_revoked` to `focus-revoked`). A host hook's refusal keeps its type and code in the outcome; any other hook exception is recorded as `permission-denied`. `ToolRegistry.FailedAfterInput` lets a host pause its grant when a call fails after input.
- The input lease is machine-wide: a named mutex (`Global\FlaUI-MCP-DesktopInput`) shared by every host, owned by a dedicated thread so a lease may be released from any thread; a lease abandoned by a host that exited is taken over. A product's offline test module initializer must set `FLAUI_LEASE_NAME` to a name of its own process, as Core's does. The desktop fixture refuses to start while another process holds the lease.
- The interaction guard is in Core: `IInputObserver`, `InputCounts`, `PhysicalInputObserver` (keys and button-downs counted apart, with the last button-down point), `Heartbeat` and `InteractionGrant` (Camtasia's permission with an injectable observer and clock, 500 ms foreground grace, `Reason`/`Code`, `Guard()`, `Pause`/`Observed`, `Mark`/`Rebaseline`). `MutationGuard.RequireHook()` makes a host fail closed when no hook is installed. Contract tests in `tests/FlaUI.Mcp.Tests/Contract/` are for each product test project to link. Delete the product copies.
- `windows_fill`'s keyboard fallback verifies keyboard focus by default and refuses before Ctrl+A without it; `verifyFocus` is in its schema. Fill says "Filled" only when the read-back matches (ccd5ba7).
- Click, fill and scroll pattern calls go through `GuardedDesktop.CallPattern`, so they run the integrity check; paste writes the clipboard through `GuardedInput.SetClipboardText`, re-checking permission and foreground first (98df458).
- Tools read the foreground, focus, window bounds and owners and the clipboard through `InputDesktop` (`FocusedControl`, `ClipboardText` are new), so product fakes drive them (7ae40c0).
- A batch step no longer receives the batch's `action` key; batch clicks use Invoke or Select again. Closed schemas are enforced by the registry for every registered tool, the product tools' included (PBI, Camtasia and TE3 already close theirs); the refusal comes before an operation is begun, so it has no operation-status entry. `windows_send_keys` with a ref verifies focus by default. A paste whose read-back shows it did not apply (`not-applied`, `mismatch`) fails; an unchanged value that already holds the text is `unchanged` and ok; line endings are ignored. `ClickPoint.Verify` accepts the element's container where the element is visible. `Diagnostics.Capture` is bounded by a deadline and records the window list and UI Automation tree. The desktop fixture closes only its own checkout's test apps and refuses to start while a FlaUI.Mcp host runs or `FLAUI_DESKTOP_BUSY` is set.
- New grant-free `Diagnostics.Capture(hwnd, path)` for failure screenshots: PrintWindow by handle, no lease, permission check, cancellation, focus or pointer move, and a `SkipReason` instead of an exception. `windows_screenshot` with `parkPointer` captures anyway, with a warning, when the guards refuse the park (revoked grant, busy lease, a window covering its screen). `InputDesktop` gains `CaptureWindow`.
- A pattern call still in validation when its tool returns as pending is never sent afterwards (the lease is gone). A late failure of a pending call is kept as `lateError` on operation status, which then reports `failed`. New `WaitOutcome<T>` (`Done`, `TimedOut(last, lastError)`, `Changed`) for polls.
- New `WindowBaseline` (`Take`, `Opened`, `OwnerDisabled`, `NewModal`, `BlockingDialog`, `CanBeDialog`) judges opened and blocking windows against a before-list, with tooltip, menu, tool-window and cloaked filters. windows_click's failure text names a modal only when a dialog is owned by a disabled window, and its "window opened" note ignores tooltips. Variants can replace their own window diffs with it.
- `SessionManager.ResolveTarget` resolves a `windows_batch` to the target of its first targeted step, so a stranded batch marks that app's provider pending like a single tool.
- Every core tool schema sets `additionalProperties: false`; `SchemaParityTests` checks each handler reads only declared arguments and every declared one. A product host can copy the test for its own tools.
- Pointer actions on an element press only where it is visible: a scrolling ancestor's viewport excludes its scrollbars and headers, a hidden clickable point or centre falls back to the centre of the element's visible part, and the press point is hit-tested (`ClickPoint.Verify`) before input, refusing with `click-point-covered` when something else is there. `InputDesktop` gains `ElementAt`, `ParentOf`, `SameElement` and `DescribeElement`.
- Modal detection during a pattern call needs a window of the process to become disabled; a tooltip, menu or tool window that appears no longer marks the provider pending.
- `windows_click` expand, collapse and toggle read the state back: "Expanded X"/"Collapsed X" only when it got there (`verification: verified`); a state that did not move fails with `verification: mismatch`, dispatch completed.
- `windows_paste` puts its read-back verdict in `verification` (`verified`, `unchanged`, `mismatch`, `unavailable` or `not-established`), not only in the text.
- `windows_place_window` fails with `verification: mismatch` and the deltas when the window did not land exactly where requested (`verified` when it did). `windows_set_window_state` sends and counts nothing when the window is already in the requested state (`verification: no-op`).
- `windows_focus` fails unless the target window is in front afterwards, and guarded input calls a foreground change an activation only when the target is in front (otherwise the note is "the foreground window changed").
- Pointer parking never lands on the window: it parks beside it, on a neighbouring screen or in a free corner, and refuses with no input when the window covers every such point. It checks the integrity of the window under the park point. `InputDesktop` gains `MovePointer`, `PointerPosition` and `Monitors`.
- A `windows_batch` with a failed step returns `isError: true` (it mirrors `status: failed`). A stranded (timed-out or cancelled) call reports `status: failed` and keeps a refusal it recorded.
- The raw Win32 mutators (`Win32Desktop.FocusWindow`, `CloseWindow`, `PlaceWindow`, `SetWindowState`, `SetClipboardText`, `PreserveAndSetClipboardText`) are internal; product code goes through the guarded calls.
- Native focus and close by handle deliberately bypass the pending tracker, so they still reach a modal dialog that blocks the provider; a host that wants them refused while a call is pending can check `PendingInvokeTracker.TryGetPending` first.
- New public guarded calls for what product code did raw: `GuardedDesktop.StartProcess`, `GuardedDesktop.CallPattern` and `GuardedDesktop.SetClipboardText`, and `GuardedInput.SetClipboardText` / `PreserveAndSetClipboardText` under a guard. Each takes the lease and permission check, counts, and is refused under `FLAUI_OFFLINE_TESTS`. [docs/operation-recovery.md](docs/operation-recovery.md#adapting-a-product-repo) lists the call sites in each product repo.
- Outcomes report `dispatch: partial` when some input went out before a call failed or stopped. They count every change in `inputsSent`, including focus, restore, clipboard writes, scroll-into-view and process starts, and carry refusals as `failure` with a code. A batch stopped at a pending step after earlier input is `partial` with `provider: pending`. Hosts that match `completed` or `timed_out_pending` must also handle `partial`. [docs/operation-recovery.md](docs/operation-recovery.md#adapting-a-product-repo) has the full list: refusal codes and texts, JSON-RPC refusals, annotations, the new tool registrations, behaviour and signature changes, and the offline-test switch `FLAUI_OFFLINE_TESTS`.
- A refusal says "no keys or clicks were sent, but ... first" when guarded input had already activated or restored the target, focused the control or scrolled the element into view.
- Pointer refusals (`point-not-on-target`, `click-point-unavailable`) are structured `InputRefusedException`s.
- `ClickGeometry.Read` no longer scrolls. `ClickGeometry.ScrollIntoView` scrolls through the input guards and counts only when the element moved; `windows_mouse` reads the point again under the input lease.
- A pattern call must count itself through `MutationGuard.Execute`, or a pending call reports `timed_out_pending`. A detected modal always counts as dispatched.
- A parse or capacity refusal no longer marks an in-flight request id as not dispatched.
- The core ownership check fails when the branch has no merge-base with `core/main`.
- Outcome values that are gone: `dispatch: failed` (now `not-dispatched` or `partial`), and in operation status `status: provider_pending`/`completed` and `dispatch: running`/`failed`. Status entries add `requestKey`, `finished`, `annotations`, `inputsSent` and `resultOutcome`. Every result carries an outcome, `McpToolResult.Outcome` is `[JsonIgnore]` behind a computed `structuredContent` with the new `message`, and several refusal texts changed. [docs/operation-recovery.md](docs/operation-recovery.md#changes-since-the-previous-core-release) lists them with the changed signatures, the variant calls that bypass the guards, and the call sites that must handle `partial`.
- Status tools are answered at once even when 32 requests are in flight or a stranded call holds the serial slot. A tool opts in with `McpToolAnnotations.Status` (read-only plus `StatusHint`); `windows_operation_status` does, and is also exempt by name.
- `windows_screenshot` is annotated as a non-destructive, idempotent input tool, because `parkPointer` moves the pointer.
- UI Automation focus and close report a pending or blocked provider in their outcome. Window state and placement check integrity first. The offline switch also covers process starts, window placement and UI Automation pattern calls, and the product test assemblies can install a fake desktop.
- Coverage (`scripts/coverage.ps1`) and mutation testing (`scripts/mutation.ps1`, `check.ps1 -Mutation`) run against the offline tests only; see [docs/mutation.md](docs/mutation.md).

### Added
- `windows_set_window_state` maximizes, restores or minimizes a window and reports the state it reached.
- `windows_mouse` parks, moves, hovers, scrolls and drags the pointer. A drag can start on an element's edge (`fromEdge`), end on another element (`toRef`, `toEdge`), or resize an element to a given size (`resizeTo`). Both ends of a drag must be on the target window.
- `windows_screenshot parkPointer` moves the pointer off the window before capturing, so no tooltip or hover state lands in the image.
- `windows_click action` picks the pattern (invoke, select, expand, collapse, toggle) or forces a mouse click. Toggle reports the state it reached.
- `windows_find` and the other selectors accept `nameContains`, a case-insensitive part of the name.
- Numpad and named punctuation keys for `windows_send_keys` (`Multiply`, `NumPad0`, `semicolon`, ...). The punctuation names map to the US-layout OEM keys.

### Changed
- Unknown selector keys are now an error instead of being ignored.
- A left click on a tree or list row defaults to Select instead of Invoke (Invoke usually toggles expansion). When Select changes nothing, the tool falls back to a mouse click. It never clicks a tree row that is already selected, because some trees start an inline rename on that click.
- `structuredContent` carries the tool's text as `message`, for clients that show it instead of `content`.
- The screenshot occlusion check covers only the area a `frame` keeps.
- `windows_launch` waits up to about 20 seconds for a window that stays the only candidate across two polls.
- This repository is now the shared core. Product automation (Tabular Editor 3, and others) lives in separate repos that merge from here.
- The namespace is `FlaUI.Mcp` (was `PlaywrightWindows.Mcp`), and the server reports itself to MCP clients as `flaui-mcp`.
- Added a clipboard lease (`Win32Desktop.PreserveAndSetClipboardText`) for hosts that can confirm a paste landed: it saves the user's text, writes ours, and restores theirs on dispose. `windows_paste` does not use it and still leaves the pasted text on the clipboard.
- Guarded input now refuses input when a different top-level window of the target process is in the foreground (for example an unowned dialog), not only when another process is. Owned dialogs and hosted children such as WebView2 still count as the target.
- Added settled-text reads (`ControlText`) and `Invoke` / `ExpandCollapse` as `windows_find` pattern filters.
- `windows_wait` accepts at most 25 seconds, so a maximum wait ends as a wait result rather than a tool timeout.
- Formatting, size and complexity limits are enforced by CSharpier, analyzers, git hooks and CI (see docs/code-quality.md).

### Fixed
- A screenshot of a window that has closed says so, instead of reporting the allowlist refusal.
- `windows_wait` failures always name a reason, using the exception type when the message is empty.
- `windows_set_window_state restore` reaches the normal state from a window that was minimized while maximized.
- Guarded input accepts a modal dialog that disables the target even when the dialog is an owned overlapped window (WinForms `ShowDialog`), which `GA_ROOTOWNER` does not follow. A modeless owned window is still treated as a separate window. When the target is disabled by a modal, activation goes to the dialog rather than the disabled window.
- `windows_wait` rejects `settleMs` equal to `timeoutMs`, which could never succeed.
- The tool timeout timer is cancelled when a tool returns.
- `scripts/check.ps1` builds with two nodes, and `scripts/setup.ps1` only configures the blame ignore list when the repo has one.
- Integration tests: an exact-ID miss expects `ElementNotFoundException`, and tab selection names the `TabItem`, since a selected WinForms `TabPage` shares its tab's AutomationId.

### Added
- `Win32Desktop.IsMinimized` and `Win32Desktop.LastActivePopup`.

### Added
- `windows_screenshot` refuses a screen-pixel capture when another application's window covers part of the target, naming each obstruction by title, HWND and PID. Such a capture previously returned the covering window's pixels and reported success with `screenFallback: false` — observed three times against a real app, once by accident during ordinary work. Native capture (`strictNative`) renders the window's own surface and is exempt, as is the blocked-provider fallback, where a modal is in front of the target by construction and refusing would leave no way to capture at all. `allowOccludedPixels: true` overrides deliberately. Windows belonging to the target's own process are allowed, so an app's own dialogs and menus are never treated as intruders.
- `windows_screenshot` reports `monitorDpi` beside `windowDpi`, with a `dpiMismatch` flag. A window that has not re-negotiated after a display-scaling change renders a native capture at its own scale into a bitmap sized for the monitor's: correctly sized, wrongly scaled, and invisible to the `frame` size assertion because the bitmap is exactly the expected size. A mismatch warns and never refuses — those pixels are what is actually on the glass, and the state is permanent for DPI-unaware apps. Warnings no longer depend on `includeMetadata`, and `occluded` is tri-state so a capture that could not be checked is never reported as clean.
- `windows_screenshot` accepts `locate: ["ref", ...]` and returns each element's rectangle as measured in the returned image, so annotation coordinates and pixels come from one observation. Converting `windows_find` bounds by hand used screen coordinates from an earlier observation and put callout boxes on the wrong control; the offset that conversion needs is not even constant, measuring 39px in one DPI state and 55px in another on the same window. It refuses rather than guessing when the capture cannot be trusted to map 1:1.
- A `text:` selector on `windows_find` and `windows_wait` matching name **or** value. Grid, tree and list rows carry a positional name (`Node0`, `Name row 87`) with the displayed label in `value`, so a name-based search silently matches nothing. The value arm mirrors the existing `IsPassword` guard.
- `windows_send_keys` names the control that actually received the keys, and accepts `expectFocusClass` to abort before sending when focus is not where the caller expects, rechecked before every chord. Ref-less and handle-only input previously reported only "focused element": two keystrokes aimed at a tree landed in a code editor holding unsaved changes.
- `windows_get_clipboard` reads the clipboard, for controls that expose no text to UI Automation at all — a Scintilla editor has no Value or Text pattern, so selecting and copying is the only way to read one. While an app allowlist is active the text must have been put there by an allowed app — the gate reads `GetClipboardOwner`, since the clipboard is shared with every application and the *focused* window says nothing about who wrote to it.
- Blocked-provider and modal-detected guidance messages now recommend capturing the dialog via its own window handle from `windows_list_windows` (which works while the provider is blocked and under an app allowlist) instead of `fullScreen: true` (refused while an allowlist is active), and mention waiting for the dialog to appear and take focus before sending keys.
- Optional app allowlist via the `FLAUI_MCP_ALLOWED_APPS` environment variable (semicolon- or comma-separated process names). When set, the server refuses to launch, list, snapshot, screenshot or send input to any other application: window handles are only issued for allowed processes (scoping every ref-based tool), ref-less keyboard input requires an allowed foreground window and rejects the Windows key, and `fullScreen` screenshots are disabled. Unset means everything is allowed, as before.
- The server now keeps the display awake while tools are actively being called, so Windows does not turn off the screen or show the lock screen in the middle of a long automation run. Implemented with a Windows power availability request (`PowerCreateRequest`/`PowerSetRequest` with `PowerRequestDisplayRequired` + `PowerRequestSystemRequired`) — the same mechanism video players and conferencing apps use, visible in `powercfg /requests`. The request is released after 5 minutes without a tool call; configure the idle period (or disable with `0`) via the `FLAUI_MCP_KEEP_AWAKE_SECONDS` environment variable.

### Fixed
- `windows_click` no longer hangs (and then times out) when the clicked element's handler opens a modal dialog. UIA pattern calls (Invoke/Toggle/Select) are synchronous cross-process calls: a WinForms/DevExpress handler that calls `ShowDialog()` does not return until the dialog closes, blocking the target app's entire UIA provider. The click now runs on a background thread while non-blocking Win32 APIs watch for the modal signature (new top-level window, or owner window disabled) and returns immediately with the dialog's title and interaction guidance.
- While an app's UIA provider is blocked by such a pending call, `windows_snapshot`, `windows_get_text`, `windows_click`, `windows_type`, `windows_fill`, `windows_send_keys` (ref-based) and `windows_batch` actions targeting that app now fail fast with guidance (use `windows_screenshot` / `windows_send_keys` without ref) instead of hanging until the 30s global timeout.
- `windows_list_windows` now enumerates windows via Win32 instead of walking the UIA desktop tree, so it keeps working even while some app's UIA provider is blocked. Window handles are also stable across calls now (previously every call registered new handles for the same windows).
- `windows_focus` and `windows_close` (by handle) now use Win32 (`SetForegroundWindow` / `WM_CLOSE`) and work while a provider is blocked.
- `windows_screenshot` with a window handle falls back to a Win32 window-bounds capture while the app's provider is blocked.

## [0.2.0] - 2026-07-08

### Fixed
- Screenshots are now correct on scaled displays (DPI > 100%). Added `SetProcessDpiAwarenessContext(PER_MONITOR_AWARE_V2)` as the first call in the process entry point so UIA coordinates match physical pixels.
- Tool execution now times out after 30 seconds instead of hanging indefinitely when UI Automation blocks.

### Added
- Solution file (`FlaUI.Mcp.slnx`)
- xUnit test project (`tests/FlaUI.Mcp.Tests`) with DPI regression test
- `windows_send_keys` MCP tool for sending key presses and key chords.
- Opt-in `background` mode for `windows_screenshot` handle captures, with blank-frame fallback to the normal capture path.
- `savePath` and `overwrite` options for `windows_screenshot`, with local PNG path validation and atomic writes.
- Desktop integration test project with WinForms and WPF test applications.

## [0.1.0] - 2024-02-02

### Added
- Initial release
- **Core MCP Tools:**
  - `windows_launch` - Launch Windows applications
  - `windows_snapshot` - Capture accessibility tree with element refs
  - `windows_click` - Click elements by ref (uses Invoke pattern when available)
  - `windows_type` - Type text into elements
  - `windows_fill` - Clear and fill text fields
  - `windows_get_text` - Get element text content
  - `windows_screenshot` - Capture window/element screenshots
  - `windows_list_windows` - List all open windows
  - `windows_focus` - Bring window to foreground
  - `windows_close` - Close windows
  - `windows_batch` - Execute multiple actions in a single call

- **Architecture:**
  - MCP protocol handler (JSON-RPC over stdio)
  - Element registry for ref ↔ AutomationElement mapping
  - Snapshot builder for agent-friendly accessibility tree format
  - Session manager for tracking launched applications

- **Documentation:**
  - README with installation and usage instructions
  - GitHub Actions for CI/CD
  - MIT License

### Technical Details
- Built on [FlaUI](https://github.com/FlaUI/FlaUI) for Windows UI Automation
- Uses UIA3 for modern app support (WPF, UWP, Win32)
- Targets .NET 8.0-windows
- Prefers control patterns (Invoke, Value, Toggle) over mouse simulation
