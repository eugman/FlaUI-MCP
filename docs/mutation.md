# Mutation testing

Stryker mutates the guard and protocol files listed in `stryker-config.json` and runs `tests/FlaUI.Mcp.Tests` against each mutant. Run it with `pwsh scripts/mutation.ps1` (see [code-quality.md](code-quality.md)).

A surviving mutant needs either a test that kills it or a line here saying why no offline test can tell it apart from the original. Mutants in code that only runs against a real desktop (UI Automation elements, Win32 calls, process starts) are listed under "Not reachable offline": the integration tests cover them, and mutation testing never runs those.

## Never the desktop tests

Stryker runs every test once per mutant. The integration tests launch test apps and take keyboard focus, so a mutation run that included them would fight whoever is using the machine for hundreds of runs. `scripts/mutation.ps1` therefore:

- runs Stryker inside `tests/FlaUI.Mcp.Tests` and names that project with `--test-project`, so Stryker never opens `FlaUI.Mcp.slnx`;
- stops Stryker when its options, which it logs before it builds anything, show a solution context or any test project but the offline one, or when it starts building before those options were checked; and stops it again if test discovery finds more tests than `dotnet test --list-tests` finds in the offline project. The discovery check comes as the initial test run starts, so the options check is the one that keeps desktop tests from ever running.

Separately, the offline test assembly sets `FLAUI_OFFLINE_TESTS=1` before any test runs (and the script sets it too). With it set, the real desktop refuses every change made through it: input, focus, close, window state and placement, the clipboard, process starts and UI Automation pattern calls (see [operation-recovery.md](operation-recovery.md#what-the-offline-switch-covers) for the exact list and what it does not cover). A test that forgot to install the fake desktop, or a mutant that skips a guard, then fails instead of typing into the window in front.

`MutationConfigTests` fails if the config names another test project, the script loses these checks, the variable is not set, or a test constructs an input tool without installing the fake desktop.

### The one deliberate exception

The pointer code (ClickTool, MouseTool, PointerActions, ClickPoint, GuardedInput.Click) mostly runs against real UI Automation elements, so offline tests cannot reach most of its mutants. `pwsh scripts/mutation.ps1 -Part pointer-integration` mutates those files against `tests/FlaUI.Mcp.IntegrationTests` instead, using `stryker-config.pointer-integration.json`. It waives the offline-only rule for that part alone, and only when asked for by name: it is never part of `-Part all`. Run it only with the desktop free and the person at the machine agreeing. It runs one test session at a time, gives each mutant 180 s beyond the normal timeout, stops after 3 hours, and runs with the real desktop (`FLAUI_OFFLINE_TESTS` unset). The guard still checks Stryker's options, here for the integration project alone. Every test session must leave no test app or other stray window behind: the fixture closes apps a killed test host left running before it launches its own, and the script closes any that remain when the part ends.

## Run log

- 2026-10-21, about 05:40: a Windows Run dialog appeared on the desktop while automated runs were active. Its origin is unknown; none of the core's tests send Win+R, and the Windows key is refused under an allowlist. Integration and mutation runs must leave no stray windows: check the desktop after each run.
- 2026-10-20: the first runs started Stryker from the repository root. Stryker found `FlaUI.Mcp.slnx` and ran every test project in it, including `FlaUI.Mcp.IntegrationTests`, although `test-projects` named only the offline project. Those runs launched the WinForms and WPF test apps and took focus, once while the machine was in use. Two test apps were left running after a run was stopped. The script now runs Stryker inside the offline project and checks its log before any test runs (see above). Scores from those runs are discarded.

## Latest run

Run on 2026-10-21 at commit 2352395, concurrency 1, 667 offline tests and no desktop tests. The score counts mutants with no covering test as survivors. One survivor from this run, the cross-process activation text, was killed afterwards with a test in `FinalMutationTests`.

| Part | Files | Score | Killed | Timed out | Survived | No coverage |
|---|---|---|---|---|---|---|
| guards | GuardedInput, GuardedDesktop, ProcessIntegrity, WindowMutation, ClickPoint, InputRefusal | 82.43% | 258 | 0 | 16 | 39 |
| state | ElementRegistry, PendingInvokeTracker, OperationContext | 87.16% | 128 | 1 | 7 | 12 |
| protocol | McpServer, McpLineTransport, ToolRegistry, ToolBase | 90.51% | 233 | 15 | 8 | 18 |
| pointer | ClickTool, ClickToolReplies, MouseTool, PointerActions | 49.36% | 192 | 1 | 14 | 184 |
| keyboard | SendKeysTool, TypeTools, BatchTool | 88.34% | 405 | 4 | 17 | 37 |

The first run with only the offline tests scored 77.74%, 73.29%, 72.38%, 24.87% and 62.97% for the same parts. The tests that killed the difference are in `GuardMutationTests`, `StateMutationTests`, `ProtocolMutationTests`, `StrandedCallMutationTests`, `PointerMutationTests`, `KeyboardMutationTests`, `FinalMutationTests` and `ToolSchemaTests`.

## Coverage

Line and branch coverage of the mutated files at the same commit, from `pwsh scripts/coverage.ps1` (whole assembly: 60.7% of lines, 49.8% of branches). The low tool numbers are the UI Automation paths listed under "Not reachable offline".

| File | Line | Branch |
|---|---|---|
| Core/GuardedInput.cs | 85.1% (246/289) | 84.3% (86/102) |
| Core/ProcessIntegrity.cs | 88.1% (59/67) | 90.5% (38/42) |
| Core/WindowMutation.cs | 100.0% (80/80) | 100.0% (16/16) |
| Core/ClickPoint.cs | 73.1% (57/78) | 57.1% (16/28) |
| Core/InputRefusal.cs | 100.0% (22/22) | 100.0% (2/2) |
| Core/ElementRegistry.cs | 81.2% (121/149) | 70.5% (31/44) |
| Core/PendingInvokeTracker.cs | 96.7% (87/90) | 94.4% (17/18) |
| Core/OperationContext.cs | 100.0% (77/77) | 100.0% (30/30) |
| Mcp/McpServer.cs | 96.6% (168/174) | 98.5% (67/68) |
| Mcp/McpLineTransport.cs | 86.4% (108/125) | 90.0% (45/50) |
| Mcp/ToolRegistry.cs | 98.4% (187/190) | 97.4% (76/78) |
| Tools/ClickTool.cs | 42.0% (126/300) | 29.5% (43/146) |
| Tools/MouseTool.cs | 33.2% (62/187) | 12.5% (8/64) |
| Tools/SendKeysTool.cs | 87.4% (222/254) | 90.9% (80/88) |
| Tools/TypeTools.cs | 64.3% (191/297) | 71.8% (56/78) |
| Tools/BatchTool.cs | 100.0% (153/153) | 96.2% (50/52) |
| Core/GuardedDesktop.cs | 100.0% (12/12) | 100.0% (2/2) |
| Mcp/ToolBase.cs | 86.9% (73/84) | 66.7% (12/18) |
| Tools/ClickToolReplies.cs | 100.0% (42/42) | 100.0% (30/30) |
| Tools/PointerActions.cs | 72.3% (47/65) | 75.0% (9/12) |

## Equivalent mutants

No test can tell these from the original.

- `GuardedInput.Capture`, `hwnd == 0` changed to `hwnd != 0` or to `&&`: a zero handle still fails the ownership check, because `GetProcessId(0)` is 0.
- `GuardedInput` constructor, `WindowMutation.Start` and `WindowMutation.Native`, the `OperationContext.Check()` right after the lease is taken: acquiring the lease already waits on the operation's token, so a cancelled operation never gets past it.
- `SendKeysTool.ExecuteAsync` and `TypeTool.ExecuteAsync`, the `OperationContext.Check()` before the target is chosen: `GuardedInput` checks again before anything is focused or sent.
- `GuardedInput.RequireActivated`, the foreground PID and name lookups when there is no foreground window: that refusal is desktop-unavailable, which uses neither.
- `ProcessIntegrity.Read` and `ReadToken`, `OpenProcess` inheritance and the `CloseHandle`/`FreeHGlobal` calls: only handle and memory leaks, which no result shows.
- `ElementRegistry.BeginSnapshot`, generation `+ 1` changed to `- 1`: consecutive generations still differ, and that is all they are compared for.
- `ElementRegistry.BeginSnapshot`, removing `_refOrder.Remove`: stale queue entries are dequeued harmlessly before any new ref is evicted.
- `PendingInvokeTracker.ReadProcessStart`, the `catch` bodies and `HasExited`: a missing or exited process reads as null either way, and an unreadable one as 0, which the tracker treats like null.
- `McpLineTransport.Dispatch`, the `return` after a parse error; `Parse`, the `catch` body; `RecoverId`, the `reader.Read()` check: each path ends with the same reply, and only a line on stderr differs.
- `McpServer.HandleInitialize`, `new ToolsCapability {}`: `ListChanged` already defaults to false.
- `ToolRegistry.ExecuteToolAsync`, `await timer.CancelAsync()`: the timer only stops early.
- `ToolRegistry.RunOperation`, the `finally` that clears `OperationContext.Current`: the operation runs in its own `Task.Run` flow, whose context is discarded.
- `ToolRegistry.SettledOrTimedOut`, `remaining > 0` changed to `>=` or dropped: `Task.Delay(TimeSpan.Zero)` and a cancel that arrives after the timeout cannot be told apart.
- `ClickTool.ObserveSelection`, the general `catch` body: removing `return null` returns null.
- `ClickTool.Settled`, `waited < timeoutMs` changed to `<=`: one more 50 ms poll.
- `MouseTool.Drag` `Thread.Sleep(StepDelay)` and `SendKeysTool` `Thread.Sleep(30)` with its `!hasChord` condition: pacing only.
- `SendKeysTool.SplitChord`, the blank-input early return: splitting a blank string yields no parts either way.
- `ClickGeometry.Scroll`, `GuardedDesktop.SetClipboardText`, the `OperationContext.Check()` calls before the lease and inside the pattern call: the lease waits on the operation's token, and `ModalAwareInvoker` checks it again before the call runs.
- `PointerActions.Move`, the `hoverMs > 0` comparison: the hover is clamped to 0 or more, and a 0 ms sleep is no sleep. `PointerActions.Drag`'s step delay is pacing only.

## Not reachable offline

These need a real window, element, process or input device. The integration tests cover them, and mutation testing never runs those.

- `GuardedInput.Click`, `InputTarget.EnsureAliveOnWindows`, `GuardedInput.ForegroundTarget()`, `ClickGeometry.Read` and `ClickGeometry.ScrollIntoView`'s pattern lookup: read UI Automation geometry, real window ownership and the real foreground window. The guarded scroll itself (`ClickGeometry.Scroll`) is tested offline.
- `ProcessIntegrity.Read` failure paths: need a process whose token cannot be opened.
- `WindowMutation.Start` after a successful start: would start a process.
- `ElementRegistry.SetWindowIdentity`, `InputForRef` and the identity check in `ResolveRef`: the identity comes from a real window handle.
- `PendingInvokeTracker.ReadProcessStart` reading a real start time differently: needs a process that exits and is replaced under the same PID.
- `McpLineTransport.RunConsoleAsync` and its stderr logging: process standard streams.
- `ClickTool` after the ref resolves (`Click`, `MouseClick`, `RunPattern`, `Supports`, `IsSelected`) and the `button`, `doubleClick` and `physical` arguments read for them: need a UI Automation element.
- `MouseTool` after the blocked-provider check (`WindowOrigin`, `ResolvePoint`, `Move`, `Scroll`, `Click`, `DragFromArguments`, `Ticks`, `HoverMs`): need the window's real bounds.
- `SendKeysTool` and `TypeTool` with a ref that has an identity (the element passed to `GuardedInput`, the ref named in the reply), and ref-less, handle-less input (`ForegroundTarget`): need a real window.
- `SendKeysTool`'s "focus afterwards" note: compares live focus before and after, which another process can move.
- `SendKeysTool.PressKeys` and `FillTool.FillByValuePattern`: send real keys or call a real Value pattern.
