# Coverage and mutation testing (TE3)

Both run only the no-focus tests in `tests/FlaUI.Mcp.Te3.Tests`. They never launch TE3 and never run the integration tests.

## Coverage

```powershell
pwsh scripts/coverage-te3.ps1
```

This runs the tests once with coverlet (`--collect:"XPlat Code Coverage"`), builds into `local/build-background` with one MSBuild node, and renders `local/build-background/coverage/index.html` with the global `reportgenerator` (`dotnet tool install -g dotnet-reportgenerator-globaltool`).

## Mutation

```powershell
pwsh scripts/coverage-te3.ps1 -Mutation             # coverage first, then Stryker
pwsh scripts/coverage-te3.ps1 -Mutation -MutationMinutes 45
```

Mutation testing never runs desktop tests. Stryker runs from `tests/FlaUI.Mcp.Te3.Tests` with that one test project named in its config, and no solution, and `MutationScopeTests` checks that. While it starts, `scripts/stryker-guard.ps1` reads its output and stops it before any test runs if it finds a second test project, any `*IntegrationTests*` project, or more tests than the offline run found.

Stryker mutates committed code only. It runs on an export of `HEAD` in `local/build-background/stryker-te3`, so its builds never land in the working tree. It uses one test runner and is stopped after `-MutationMinutes` (30 by default). The report is at `local/build-background/stryker-te3/tests/FlaUI.Mcp.Te3.Tests/StrykerOutput/run/reports/mutation-report.html`.

- `tests/FlaUI.Mcp.Te3.Tests/stryker-config.json` lists the files that are mutated: the TE3 guard and protocol files in `src/FlaUI.Mcp.Te3`. The shared core and generated code are never mutated, and the export holds no `local/` recipes. Patterns match the full path, which runs through `local/`, so never exclude `**/local/**`.
- `dotnet-stryker` comes from the root `.config/dotnet-tools.json`, which the core owns and pins.
- The core's own `scripts/coverage.ps1`, `scripts/mutation.ps1` and [mutation.md](mutation.md) cover the shared core and run `tests/FlaUI.Mcp.Tests`; this page and `scripts/coverage-te3.ps1` are the TE3 equivalents.

Files added to the mutated set since the latest run: `RunGrant.cs`, `Te3Page.Walk.cs` (moved out of `Te3Page.cs`), `WalkPlace.cs`, `LayoutChecks.cs`, `SceneCheck.cs` and `Absence.cs`. `PhysicalInputObserver.IsPhysical` was mutated but had no covering test, so its mutants counted as uncovered and were missing from the covered score; `RunGrantTests.OnlyInputWithoutAnInjectedFlagCountsAsTheUsers` now covers each flag.

A surviving mutant is either a missing test, which you fix with a red-green test, or an equivalent mutant, which you list below with the reason.

## Latest run

On 2026-10-21, run at `4df0df5` (the Core `abb17ea` merge and its adaptation, plus the tests below), concurrency 1, 491 offline tests and no desktop tests. Coverage: 36.0% of lines, 30.2% of branches (1561 of 5155). Stryker tested 1161 mutants: 994 killed, 5 timed out and 162 survived, the same survivors as at `e75f4df`; the later changes (the TE CLI's guarded start, the status-read annotations and their revert) added none. The covered-mutant score is 86.0%; the score over all mutants is 34.2%, because 1761 mutants have no covering test, almost all in `Te3Page.cs`, `Te3Layout.cs` and `Te3Companion.cs`, which drive the live TE3 desktop.

The first run on the adapted code, at `daeb643`, tested 1153 mutants: 936 killed, 6 timed out, 211 survived (81.7% covered, 32.2% overall). `e75f4df` (`AdaptationMutationTests`, plus a backlog-in-a-worktree test and a workspace schema whose `properties` is not an object) kills 49 of those. The survivors that remain are listed below, or are tool descriptions and `InputSchema` text (most of `Te3Layout.Schema.cs`, `Te3InteractionTool.cs`, `Te3NativePreflightTool.cs` and `Te3Companion.cs`'s schema), or need TE3 (`Te3Page.cs`).

The run before the merge, at `f463ab9` on 2026-10-20, scored 87.5% covered and 28.5% overall over 871 mutants.

## Equivalent and accepted survivors

**Equivalent: no input changes behaviour**

- `PathWalker.Matches`, `objectType == "Table"` forced false or `""`: a table row sets `currentTable` to its own value just before the match, so both branches compare the same text.
- `NativeMcpPreflight.TryProperty` and `TryTextProperty`, the initial `value = ""`: callers read `value` only when the method returns true, and every true return assigns it.
- `NativeMcpProbeSession.Send`, `SingleOrDefault()` changed to `Single()`: the header is read only inside `TryGetValues`, so it has a value, and two values throw either way.
- `NativeMcpProbeSession.ReadResult`, `detectEncodingFromByteOrderMarks` and `leaveOpen`: responses are UTF-8, and the stream is disposed with the response.
- `NativeMcpProbeSession.AppendData`, the newline (`'\n'`) between `data:` lines: the payload is JSON, so a newline between lines is whitespace.
- `SettingsLease.WriteAtomically`, the temporary name's parts: any unique name next to the target works. (Since `ef7ba61`, a failed write deletes its temporary file quietly and keeps the write's own error; that path postdates the run.)
- `SettingsLease.Validate`, the `JsonException` text: it is the inner exception, and only the wrapping message is shown.
- `SettingsLease.Recover`, the `", "` separator: the tests change one file, and the separator only appears between names.
- `Te3Interaction.Begin`, `reason = "approved."`: the reason is shown only once the batch has ended or been revoked, and both overwrite it.
- `Te3NativePreflightTool.Create`, the `HttpClient.Timeout`: each request already has a 10 s deadline and the whole probe 25 s, so the client timeout never fires first.
- `Te3Page`'s `Indented` options: the text is used only for the human-readable output.
- `CoreCallException.Outcome`, the `"unknown"` fallback: Core's registry always sets an outcome, and the fallback is only compared with `not-dispatched` and `pending`, which `""` does not match either.
- `CoreFailure.Outcome` for an `InputRefusedException`, the `"not-dispatched"` and `"not-tracked"` texts: that outcome is read only for its refusal; `Reply` takes dispatch from the count and `MayStillGoOut` is false for both values.
- `InputTally.Dispatch`, the counter's `Tool` name: the operation it names is never registered or shown.
- `DialogCancel.TryResolve`, the catch body removed: the method then falls through to the same `null`.
- `NavigationRetry.Once`, the plural `s` in "sent N inputs": text only.

**Accepted: needs the desktop or a live TE3**

- `Te3Interaction.Begin` (`physicalInput == null`) and `Dispose` (`observer?.Dispose()`): only the real keyboard and mouse hook takes these branches, and offline tests never install it.
- `Te3Companion.ExecuteAsync` and `Attach`, the pending-call check and the `TabularEditor3` process-name check: they run after attaching to a TE3 window. The blocked outcome they produce is tested through `Te3Companion.Failure`.
- `Te3Page` selector literals (`OpenFileNameTarget`, `PreferencesTarget`, `McpServerMarkerTarget`, `TomChangesTarget`, `IsPreferencesLookup`): no test checks these against TE3. `tests/FlaUI.Mcp.IntegrationTests` drives only the WinForms and WPF test apps, and the offline tests that name the literals (`CompanionTests`, `AdaptationMutationTests`) only restate the constants, so a control TE3 renames is caught by a live run and nothing else. Accepted until a live selector smoke recipe exists.
- `WaitForAbsenceAsync` timing: since this run the wait is `Absence.WaitFor` on an injected clock, and `RunnerTests` and `StabilizationTests` assert its exact times, so the next run should kill these.
- `Te3Page.ParkPoint` and `ScalarSceneBounds` at a few pixels from the edge: the offline tests check that the point stays inside the window, not its exact offset.

**Accepted: size-cap off-by-ones**

`NativeMcpProbeSession` `AppendData`, `ReadJson`, `ReadLine` and `ReadSse` compare with `>` or `>=` against `MaxResponseChars` and `MaxLineChars`. A one-character shift moves a 256K or 64K cap by one character, and the offline tests already check that oversized responses are refused.

**Accepted: text that does not change behaviour**

Tool descriptions, `InputSchema` strings, and diagnostic messages that are not a guard's instruction to the agent or user: for example `VerifyArrival`'s detail, `ObjectNotFoundMessage` and `ModelStateReadFailure`. A guard's refusal message is tested when it says what to do next ("Ask the user", "--force", "retry when it finishes").

**Accepted: deliberate behaviour**

`Te3NativePreflightTool.BearerToken` and `Optional`, `&&` changed to `||`: the mutant throws on a non-string `tokenEnv` or `compatibleServer`, while the code ignores it. Refusing a non-string value may be the better behaviour, but that is a product decision, not a test gap.
