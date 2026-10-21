# FlaUI-MCP for Tabular Editor 3

MCP tools and a screenshot runner for driving [Tabular Editor 3](https://tabulareditor.com) (TE3) on Windows. It is built on [FlaUI-MCP-Core](https://github.com/eugman/FlaUI-MCP-Core), a generic MCP server that lets agents operate desktop apps through UI Automation.

What you get:

| Piece | Use it to |
|---|---|
| `FlaUI.Mcp.Te3.exe mcp` | One MCP server with the generic `windows_*` tools (snapshot, find, click, type, screenshot and so on) plus `te3_navigate`, `te3_model_state`, `te3_layout` and `te3_layout_read`, which know TE3's layout. Refs and handles are shared across all of them. |
| `FlaUI.Mcp.exe` | The generic `windows_*` server on its own, for apps other than TE3. |
| `FlaUI.Mcp.Te3.exe run` | Script repeatable TE3 screenshots with typed C# recipes. |
| `automation/skills/te3-ui` | Agent guidance for driving TE3. |

## Quick start

You need:

- Windows 10 or 11 and PowerShell 7.
- The .NET 9 SDK (9.0.300 or later; pinned in `global.json`) and the .NET 8 Desktop Runtime.
- [Tabular Editor 3](https://tabulareditor.com), installed and licensed.
- For the screenshot runner: the Tabular Editor command-line tool `te.exe` (the runner looks for it at `%LOCALAPPDATA%Programs	e	e.exe` by default), and a local SQL Server Analysis Services instance for recipes that need a database engine.

```powershell
git clone https://github.com/eugman/FlaUI-MCP.git
cd FlaUI-MCP
pwsh scripts/setup.ps1
dotnet build FlaUI.Mcp.slnx -c Release
```

Add the server to your MCP client:

```json
{
  "mcpServers": {
    "te3": {
      "command": "C:/path/to/FlaUI-MCP/src/FlaUI.Mcp.Te3/bin/Release/net8.0-windows/FlaUI.Mcp.Te3.exe",
      "args": ["mcp"]
    }
  }
}
```

It only touches TE3: the app allowlist is built in.

## Documentation

- [automation/README.md](automation/README.md): the TE3 tools, the screenshot runner, configuration and writing recipes.
- [automation/UI-MAP.md](automation/UI-MAP.md): TE3 locators and behaviour.
- [FlaUI-MCP-Core README](https://github.com/eugman/FlaUI-MCP-Core#readme): the generic `windows_*` tools.
- [docs/code-quality.md](docs/code-quality.md): formatting, size limits and the git hooks.

## Your own files

Put personal recipes, configs and notes in `local/` at the repo root. Git ignores it, and the runner compiles `local/recipes/*.cs` when it exists.

## Repository layout

| Path | Contents |
|---|---|
| `src/FlaUI.Mcp`, `tests/FlaUI.Mcp.*`, `tests/TestApps` | The shared core. Changes go to FlaUI-MCP-Core first and arrive here by merge. |
| `src/FlaUI.Mcp.Te3`, `tests/FlaUI.Mcp.Te3.Tests` | The TE3 tools and runner. |
| `automation/` | TE3 docs, the agent skill, the example config, the test model script and the engine reset script. |

## License

MIT. See [LICENSE](LICENSE).
