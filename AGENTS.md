# Agent notes

- Code quality rules and the commands that enforce them are in [docs/code-quality.md](docs/code-quality.md). Run `pwsh scripts/check.ps1` before you push.
- Files listed in `scripts/core-paths.txt` belong to the shared core (FlaUI-MCP-Core). Change them there, then merge `core/main` here.
- Keep TE3 code in `src/FlaUI.Mcp.Te3`. Personal recipes and configs go in the gitignored `local/` folder.
- Anything that launches TE3 or a test app takes the desktop: `tests/FlaUI.Mcp.IntegrationTests`, `FlaUI.Mcp.Te3.exe run`, and the `te3_navigate` tool. Run them only when the person at the machine agrees. `scripts/check.ps1` runs only the no-focus tests.
