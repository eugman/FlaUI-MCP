# FlaUI-MCP for Tabular Editor 3

MCP tools and a screenshot runner for driving [Tabular Editor 3](https://tabulareditor.com) (TE3) on Windows. It is built on [FlaUI-MCP-Core](https://github.com/eugman/FlaUI-MCP-Core), a generic MCP server that lets agents operate desktop apps through UI Automation.

What you get:

| Piece | Use it to |
|---|---|
| `FlaUI.Mcp.Te3.exe mcp` | One MCP server with the generic `windows_*` tools (snapshot, find, click, type, screenshot and so on) plus the eight TE3 tools (`te3_interaction_begin`, `te3_interaction_end`, `te3_navigate`, `te3_model_state`, `te3_layout`, `te3_layout_read`, `te3_native_preflight`, `te3_env_check`; their facts are in the generated table in [automation/README.md](automation/README.md)). Refs and handles are shared across all of them. |
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

The TE3-specific tools still target TE3. Shared `windows_*` tools prefer TE3 and warn in the result when a call targets another app, such as Windows Terminal. Mutating tools require `te3_interaction_begin`, and physical user input revokes the grant. Set `FLAUI_MCP_ALLOWED_APPS` to enforce a strict allowlist when needed.

## Documentation

- [automation/README.md](automation/README.md): the TE3 tools, the screenshot runner, configuration and writing recipes.
- [automation/UI-MAP.md](automation/UI-MAP.md): TE3 locators and behaviour.
- [FlaUI-MCP-Core README](https://github.com/eugman/FlaUI-MCP-Core#readme): the generic `windows_*` tools.
- [docs/code-quality.md](docs/code-quality.md): formatting, size limits and the git hooks.

## Your own files

Put personal recipes, configs and notes in `local/` at the repo root. Git ignores it, and the runner compiles `local/recipes/*.cs` when it exists. For a reproducible source-only build when personal recipes are stale, pass `-p:IncludeLocalRecipes=false` to `dotnet build` and `dotnet test`. The same property can be set as the `IncludeLocalRecipes=false` environment variable for the pre-push gate; ordinary builds still compile personal recipes.

## Repository layout

| Path | Contents |
|---|---|
| `src/FlaUI.Mcp`, `tests/FlaUI.Mcp.*`, `tests/TestApps` | The shared core. Changes go to FlaUI-MCP-Core first and arrive here by merge. |
| `src/FlaUI.Mcp.Te3`, `tests/FlaUI.Mcp.Te3.Tests` | The TE3 tools and runner. |
| `automation/` | TE3 docs, the agent skill, the example config, the test model script and the engine reset script. |

## Generic tool annotations

The core `windows_*` tools this server includes carry MCP `annotations` in `tools/list`, generated from each tool's `ToolFacts` (run the offline tests with `FLAUI_APPROVE_TOOL_SCHEMAS=1` to rewrite the table). The TE3 tools are listed in [automation/README.md](automation/README.md).

<!-- tool-facts:begin -->
| Tool | readOnly | destructive | idempotent | observation | recovery | mutating when |
|---|---|---|---|---|---|---|
| `windows_batch` |  | yes |  |  |  |  |
| `windows_click` |  | yes |  |  |  |  |
| `windows_close` |  | yes |  |  | yes |  |
| `windows_fill` |  | yes |  |  |  |  |
| `windows_find` | yes |  | yes | yes |  |  |
| `windows_focus` |  |  | yes |  | yes |  |
| `windows_get_clipboard` | yes |  | yes | yes |  |  |
| `windows_get_text` | yes |  | yes | yes |  |  |
| `windows_launch` |  |  |  |  |  |  |
| `windows_list_windows` | yes |  | yes | yes | yes |  |
| `windows_mouse` |  | yes |  |  |  |  |
| `windows_operation_status` | yes |  | yes |  | yes |  |
| `windows_paste` |  | yes |  |  |  |  |
| `windows_place_window` |  |  | yes |  |  |  |
| `windows_screenshot` |  |  | yes | yes | yes | `parkPointer` |
| `windows_send_keys` |  | yes |  |  | yes |  |
| `windows_set_window_state` |  |  | yes |  |  |  |
| `windows_snapshot` | yes |  | yes | yes |  |  |
| `windows_type` |  |  |  |  |  |  |
| `windows_wait` | yes |  | yes | yes |  |  |
<!-- tool-facts:end -->

## License

MIT. See [LICENSE](LICENSE).
