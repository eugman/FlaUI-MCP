# Mutation testing

Stryker mutates the guard and protocol files listed in `stryker-config.json` and runs `tests/FlaUI.Mcp.Tests` against each mutant. Run it with `pwsh scripts/mutation.ps1` (see [code-quality.md](code-quality.md)).

A surviving mutant needs either a test that kills it or a line here saying why no test can tell it apart from the original. Mutants in code that only runs against a real desktop (UI Automation elements, Win32 calls) are listed under "Not reachable offline": the integration tests cover them.

## Equivalent mutants

Pending the first full run.

## Not reachable offline

Pending the first full run.
