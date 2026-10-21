# Mutation testing

Stryker mutates the guard and protocol files listed in `stryker-config.json` and runs `tests/FlaUI.Mcp.Tests` against each mutant. Run it with `pwsh scripts/mutation.ps1` (see [code-quality.md](code-quality.md)).

A surviving mutant needs either a test that kills it or a line here saying why no offline test can tell it apart from the original. Mutants in code that only runs against a real desktop (UI Automation elements, Win32 calls, process starts) are listed under "Not reachable offline": the integration tests cover them, and mutation testing never runs those.

## Never the desktop tests

Stryker runs every test once per mutant. The integration tests launch test apps and take keyboard focus, so a mutation run that included them would fight whoever is using the machine for hundreds of runs. `scripts/mutation.ps1` therefore:

- runs Stryker inside `tests/FlaUI.Mcp.Tests` and names that project with `--test-project`, so Stryker never opens `FlaUI.Mcp.slnx`;
- stops the run before its first test if Stryker's log shows a solution context, the integration test project, or more tests than `dotnet test --list-tests` finds in the offline project.

`MutationConfigTests` fails if the config names another test project or the script loses these checks.

## Run log

- 2026-10-20: the first runs started Stryker from the repository root. Stryker found `FlaUI.Mcp.slnx` and ran every test project in it, including `FlaUI.Mcp.IntegrationTests`, although `test-projects` named only the offline project. Those runs launched the WinForms and WPF test apps and took focus, once while the machine was in use. Two test apps were left running after a run was stopped. The script now runs Stryker inside the offline project and checks its log before any test runs (see above). Scores from those runs are discarded.

## Results

Run on 2026-10-21 at commit 7fa11b4, concurrency 1, 608 offline tests and no desktop tests. The score counts mutants with no covering test as survivors.

| Part | Files | Score | Killed | Timed out | Survived | No coverage |
|---|---|---|---|---|---|---|
| guards | GuardedInput, ProcessIntegrity, WindowMutation, ClickPoint, InputRefusal | 81.51% | 213 | 3 | 9 | 40 |
| state | ElementRegistry, PendingInvokeTracker, OperationContext | 86.99% | 126 | 1 | 7 | 12 |
| protocol | McpServer, McpLineTransport, ToolRegistry | 91.63% | 210 | 9 | 8 | 12 |
| pointer | ClickTool, MouseTool | 50.27% | 186 | 2 | 11 | 175 |
| keyboard | SendKeysTool, TypeTools, BatchTool | 88.03% | 396 | 1 | 17 | 37 |

The first run with only the offline tests scored 77.74%, 73.29%, 72.38%, 24.87% and 62.97% for the same parts. The tests that killed the difference are in `GuardMutationTests`, `StateMutationTests`, `ProtocolMutationTests`, `StrandedCallMutationTests`, `PointerMutationTests`, `KeyboardMutationTests` and `ToolSchemaTests`.

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

## Not reachable offline

These need a real window, element, process or input device. The integration tests cover them, and mutation testing never runs those.

- `GuardedInput.Click`, `InputTarget.EnsureAliveOnWindows`, `GuardedInput.ForegroundTarget()` and `ClickPoint.Read`: read UI Automation geometry, real window ownership and the real foreground window.
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
