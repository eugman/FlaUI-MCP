# Mutation testing

Run mutation tests through `scripts/mutation.ps1` from a committed Core revision. The script creates a temporary worktree of `HEAD` and writes reports under the ignored `local/build-background/StrykerOutput/` directory. Commit source and tests before running it; uncommitted changes are not in the worktree.

```powershell
pwsh scripts/mutation.ps1 -Part guards
pwsh scripts/mutation.ps1 -Part state
pwsh scripts/mutation.ps1 -Part protocol
pwsh scripts/mutation.ps1 -Part pointer
pwsh scripts/mutation.ps1 -Part keyboard
```

`-Part all` runs these five offline parts in sequence. `-Concurrency N` overrides Stryker's configured concurrency. `-Minutes N` sets a cap for each part. An interrupted or capped part may have no report; use the command's exit code and the report it names, not an older report, to assess the run.

The offline parts run only `tests/FlaUI.Mcp.Tests`. The script checks Stryker's selected project and discovered test count; `FLAUI_OFFLINE_TESTS=1` makes Core's real desktop input seam refuse mutations if a test omitted its fake desktop. These checks do not cover code that bypasses the seam. See [operation and protocol recovery](operation-recovery.md#what-the-offline-switch-covers) for the exact boundary.

Pointer integration mutation tests deliberately use real UI Automation and take the desktop. Run them only with an active machine grant and a free desktop:

```powershell
pwsh scripts/mutation.ps1 -Part clicks
pwsh scripts/mutation.ps1 -Part input
```

`-Part pointer-integration` runs both integration parts. They are excluded from `-Part all`. The script runs one test session at a time, clears `FLAUI_OFFLINE_TESTS` for these parts, and closes only test apps built under this checkout after each part. Inspect the desktop when the run ends.

The source file lists are in `stryker-config.json` and `stryker-config.pointer-integration.json`; `tests/FlaUI.Mcp.Tests/MutationConfigTests.cs` checks the offline project and script safety setup. Record new mutation findings as focused tests and code fixes. Keep dated run logs and report exports in ignored local storage rather than this guide.
