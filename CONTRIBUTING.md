# Contributing

## Setup

You need Windows 10 or 11, the .NET 9 SDK (9.0.300 or later; see `global.json`), the .NET 8 Desktop Runtime and PowerShell 7.

```powershell
pwsh scripts/setup.ps1
```

This installs CSharpier, turns on the git hooks and adds the shared core as a fetch-only `core` remote. The pre-commit hook checks formatting; the pre-push hook runs `pwsh scripts/check.ps1`. Hosted Build runs that full offline gate for pull requests and manual dispatches; a push to `main` runs a production Release build. The rules are described in [docs/code-quality.md](docs/code-quality.md).

## Tests

| Project | Needs the desktop |
|---|---|
| `tests/FlaUI.Mcp.Tests` | No |
| `tests/FlaUI.Mcp.Te3.Tests` | No |
| `tests/FlaUI.Mcp.IntegrationTests` | Yes: launches the WinForms and WPF apps in `tests/TestApps` and takes focus |

`FlaUI.Mcp.Te3.exe run` and the `te3_navigate` tool also take the desktop. Run them only when the person at the machine agrees.

## Where changes go

| Change | Where |
|---|---|
| TE3 tools, runner, smoke recipes | `src/FlaUI.Mcp.Te3`, `tests/FlaUI.Mcp.Te3.Tests` |
| TE3 docs, skill, example config, test model | `automation/` |
| Your own recipes, configs and notes | `local/` (gitignored) |
| The generic server, scripts and analyzer settings | [FlaUI-MCP-Core](https://github.com/eugman/FlaUI-MCP-Core), then `git fetch core; git merge core/main` here |

Files owned by the core are listed in `scripts/core-paths.txt`; the hooks and CI refuse changes to them that didn't come from a core merge.

## Releases

The **Release** workflow builds x64 and ARM64 ZIPs containing `FlaUI.Mcp.exe` and `FlaUI.Mcp.Te3.exe`. Push a version tag such as `v0.2.0`, or run the workflow by hand with a tag name; manual runs create a draft release.
