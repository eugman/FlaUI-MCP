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

## Equivalent mutants

Pending the first run with the guard in place.

## Not reachable offline

Pending the first run with the guard in place.
