# Coverage and mutation testing for TE3

Run TE3 coverage through the no-focus test project:

```powershell
pwsh scripts/coverage-te3.ps1
```

The script sets `IncludeLocalRecipes=false`, selects `tests/FlaUI.Mcp.Te3.Tests` with coverlet, and writes an HTML report under ignored `local/build-background/coverage/`. It requires the `reportgenerator` command (`dotnet tool install -g dotnet-reportgenerator-globaltool`). It does not select the integration test project or launch TE3 itself; project selection cannot prove that every test body avoids the desktop.

To mutate the TE3 code listed in `tests/FlaUI.Mcp.Te3.Tests/stryker-config.json`, commit the code and tests first, then run:

```powershell
pwsh scripts/coverage-te3.ps1 -Mutation
pwsh scripts/coverage-te3.ps1 -Mutation -MutationMinutes 45
```

Coverage runs first. For mutation, the script exports `HEAD` to ignored `local/build-background/stryker-te3/`; uncommitted changes and ignored local recipes are absent from that export. It names only the TE3 no-focus test project. `scripts/stryker-guard.ps1` watches startup output and stops a run if the output reports another project, an integration project, or more tests than the offline run found. That guard checks the reported project selection and count, not the behavior of every selected test. The default wall-clock cap is 30 minutes. A capped run is stopped and may leave no report. Check the command's exit code and the report it prints; do not infer current coverage from an older report.

The shared Core has its own [mutation procedure](mutation.md). Keep dated TE3 run logs, scores, and exported reports in ignored local storage. Turn actionable survivors into focused tests and fixes.
