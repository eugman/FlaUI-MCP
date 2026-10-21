# Agent notes

- Code quality rules and the commands that enforce them are in [docs/code-quality.md](docs/code-quality.md). Run `pwsh scripts/check.ps1` before you push.
- Files listed in `scripts/core-paths.txt` belong to the shared core (FlaUI-MCP-Core). Change them there, then merge `core/main` here.
- Coverage and mutation testing for the TE3 guard files: [docs/mutation-te3.md](docs/mutation-te3.md) (`pwsh scripts/coverage-te3.ps1 [-Mutation]`).
- Keep TE3 code in `src/FlaUI.Mcp.Te3`. Personal recipes and configs go in the gitignored `local/` folder.
- Anything that launches TE3 or a test app takes the desktop: `tests/FlaUI.Mcp.IntegrationTests` and `FlaUI.Mcp.Te3.exe run`. Run them only when the person at the machine agrees. In the TE3 MCP server, every tool that changes the desktop (`windows_*` input and window tools, `te3_navigate`, `te3_layout` changes) refuses to run without the user's `te3_interaction_begin` grant. The user's own input revokes it. The caller, or the machine-grant broker, supplies `userApproved`; see `automation/skills/te3-ui/SKILL.md`. `scripts/check.ps1` runs only the no-focus tests.
