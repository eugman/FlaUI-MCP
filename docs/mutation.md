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

Stryker mutates committed code only. It runs on an export of `HEAD` in `local/build-background/stryker-te3`, so its builds never land in the working tree. It uses one test runner and is stopped after `-MutationMinutes` (30 by default). The report is at `local/build-background/stryker-te3/tests/FlaUI.Mcp.Te3.Tests/StrykerOutput/run/reports/mutation-report.html`.

- `tests/FlaUI.Mcp.Te3.Tests/stryker-config.json` lists the files that are mutated: the TE3 guard and protocol files in `src/FlaUI.Mcp.Te3`. The shared core, generated code and `local/` recipes are never mutated.
- `tests/FlaUI.Mcp.Te3.Tests/.config/dotnet-tools.json` pins `dotnet-stryker`. It is a non-root manifest, so the root one (owned by the core) still supplies CSharpier. When the core's root manifest gains `dotnet-stryker`, delete this one.
- `scripts/check.ps1` is owned by the core, so the mutation switch lives here until the core's check script gains one.

A surviving mutant is either a missing test, which you fix with a red-green test, or an equivalent mutant, which you list below with the reason.

## Equivalent and accepted survivors

To be filled in from the latest run.
