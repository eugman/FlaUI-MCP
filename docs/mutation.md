# Coverage and mutation testing (TE3)

Both run only the no-focus tests in `tests/FlaUI.Mcp.Te3.Tests`. They never launch TE3 and never run the integration tests.

## Coverage

```powershell
pwsh scripts/coverage.ps1
```

This runs the tests once with coverlet (`--collect:"XPlat Code Coverage"`), builds into `local/build-background` with one MSBuild node, and renders `local/build-background/coverage/index.html` with the global `reportgenerator` (`dotnet tool install -g dotnet-reportgenerator-globaltool`).

## Mutation

```powershell
pwsh scripts/coverage.ps1 -Mutation             # coverage first, then Stryker
pwsh scripts/coverage.ps1 -Mutation -MutationMinutes 45
```

Mutation testing never runs desktop tests. Stryker runs from `tests/FlaUI.Mcp.Te3.Tests` with that one test project named in its config, and no solution, and `MutationScopeTests` checks that. While it starts, `scripts/stryker-guard.ps1` reads its output and stops it before any test runs if it finds a second test project, any `*IntegrationTests*` project, or more tests than the offline run found.

Stryker mutates committed code only. It runs on an export of `HEAD` in `local/build-background/stryker-te3`, so its builds never land in the working tree. It uses one test runner and is stopped after `-MutationMinutes` (30 by default). The report is at `local/build-background/stryker-te3/tests/FlaUI.Mcp.Te3.Tests/StrykerOutput/run/reports/mutation-report.html`.

- `tests/FlaUI.Mcp.Te3.Tests/stryker-config.json` lists the files that are mutated: the TE3 guard and protocol files in `src/FlaUI.Mcp.Te3`. The shared core and generated code are never mutated, and the export holds no `local/` recipes. Patterns match the full path, which runs through `local/`, so never exclude `**/local/**`.
- `tests/FlaUI.Mcp.Te3.Tests/.config/dotnet-tools.json` pins `dotnet-stryker`. It is a non-root manifest, so the root one (owned by the core) still supplies CSharpier. When the core's root manifest gains `dotnet-stryker`, delete this one.
- `scripts/check.ps1` is owned by the core, so the mutation switch lives here until the core's check script gains one.

A surviving mutant is either a missing test, which you fix with a red-green test, or an equivalent mutant, which you list below with the reason.

## Latest run

On 2026-10-20, run at `f463ab9`, before the input-accounting, journal, retry and cancel fixes that followed it (`93a3e6b` onwards). The scores and the survivor list below describe that commit; rerun `pwsh scripts/coverage.ps1 -Mutation` for the current code. That run tested 871 mutants, 757 killed, 5 timed out and 109 survived. The covered-mutant score is 87.5%; the score over all mutants is 28.5%, because 1801 mutants have no covering test. Almost all of the uncovered ones are in `Te3Page.cs`, `Te3Layout.cs` and `Te3Companion.cs` and drive the live TE3 desktop, so only the integration tests can reach them. The first run scored 75.6% covered, with 208 survivors; tests killed 99 of them, and `e84d006` kills 6 more.

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

**Accepted: needs the desktop or a live TE3**

- `Te3Interaction.Begin` (`physicalInput == null`) and `Dispose` (`observer?.Dispose()`): only the real keyboard and mouse hook takes these branches, and offline tests never install it.
- `Te3Companion.ExecuteAsync`, the pending-call check: it runs after attaching to a TE3 window.
- `Te3Page` selector literals (`OpenFileNameTarget`, `PreferencesTarget`, `McpServerMarkerTarget`, `TomChangesTarget`, `IsPreferencesLookup`) and `WaitForAbsenceAsync` timing: the integration tests check these against TE3.
- `Te3Page.ParkPoint` and `ScalarSceneBounds` at a few pixels from the edge: the offline tests check that the point stays inside the window, not its exact offset.

**Accepted: size-cap off-by-ones**

`NativeMcpProbeSession` `AppendData`, `ReadJson`, `ReadLine` and `ReadSse` compare with `>` or `>=` against `MaxResponseChars` and `MaxLineChars`. A one-character shift moves a 256K or 64K cap by one character, and the offline tests already check that oversized responses are refused.

**Accepted: text that does not change behaviour**

Tool descriptions, `InputSchema` strings, and diagnostic messages that are not a guard's instruction to the agent or user: for example `VerifyArrival`'s detail, `ObjectNotFoundMessage` and `ModelStateReadFailure`. A guard's refusal message is tested when it says what to do next ("Ask the user", "--force", "retry when it finishes").

**Accepted: deliberate behaviour**

`Te3NativePreflightTool.BearerToken` and `Optional`, `&&` changed to `||`: the mutant throws on a non-string `tokenEnv` or `compatibleServer`, while the code ignores it. Refusing a non-string value may be the better behaviour, but that is a product decision, not a test gap.
