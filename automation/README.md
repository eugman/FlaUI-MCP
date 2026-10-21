# Tabular Editor 3 automation

Everything here builds on the generic FlaUI MCP server and adds what is specific to Tabular Editor 3 (TE3):

| Piece | What it is |
|---|---|
| `FlaUI.Mcp.Te3 mcp` | The MCP server: the generic `windows_*` tools plus the TE3 tools `te3_navigate`, `te3_model_state`, `te3_layout`, `te3_layout_read` and the `te3_interaction_begin`/`te3_interaction_end` grant, sharing one set of refs and handles. Limited to TE3. |
| `FlaUI.Mcp.Te3 run` | A runner that launches its own TE3, stages a disposable model, runs typed C# recipes and saves screenshots. |
| [skills/te3-ui](skills/te3-ui/SKILL.md) | Agent guidance for driving TE3 with the MCP tools. |
| [UI-MAP.md](UI-MAP.md) | Locators and behaviour observed in TE3 3.26 and 3.27. |
| [KNOWN-ISSUES.md](KNOWN-ISSUES.md) | TE3 behaviour that trips up automation. |

Tested against TE3 3.26.3 and 3.27.0 at 125% display scaling. Other versions and scaling levels may need locator updates.

## Build

```powershell
dotnet build FlaUI.Mcp.slnx -c Release
$runner = "./src/FlaUI.Mcp.Te3/bin/Release/net8.0-windows/FlaUI.Mcp.Te3.exe"
```

## TE3 MCP tools

Add a second stdio MCP server that runs `FlaUI.Mcp.Te3.exe` with the argument `mcp`:

```json
{
  "mcpServers": {
    "te3": { "command": "C:/path/to/FlaUI.Mcp.Te3.exe", "args": ["mcp"] }
  }
}
```

| Tool | Does |
|---|---|
| `te3_interaction_begin` / `te3_interaction_end` | Start and end the user's grant. In this server it covers every tool that changes the desktop: the `windows_*` input and window tools (`windows_click`, `windows_type`, `windows_fill`, `windows_send_keys`, `windows_paste`, `windows_mouse`, `windows_place_window`, `windows_set_window_state`, `windows_focus`, `windows_close`, `windows_launch`), `te3_navigate` and `te3_layout`'s changes. Begin only after the user says the machine is clear; their own keyboard or mouse input ends it. |
| `te3_navigate` | Moves to a destination and checks it arrived. Takes focus; needs the grant. |
| `te3_model_state` | Reads the status bar: whether a model is loaded, its connection, the BPA count, the selected object. Sends no input. |
| `te3_layout_read` | Lists docked panes and document tabs (`list`), confirms a named pane is visible (`show-pane`) and plans the `assistant-model-review` geometry offline (`plan-preset`). Sends no input and needs no grant. |
| `te3_layout` | Hides, maximizes, restores and resizes docked panes for a screenshot (`close`, `keep`, `hide-pane`, `apply-preset`, `maximize-pane`, `restore-pane`, `resize`). Takes focus and needs the grant; no model edits. |

Every TE3 tool carries MCP annotations: `te3_model_state`, `te3_layout_read` and `te3_native_preflight` are read-only (plain reads, not Core status tools: they walk TE3's UI Automation tree or open a session in TE3, so they wait their turn like other calls); `te3_navigate` and `te3_layout` change the desktop (`te3_layout` is marked destructive, since it closes panes); `te3_interaction_begin` and `te3_interaction_end` are not read-only either, because the grant unlocks input. A failed `te3_navigate` or `te3_layout` call reports how much input went out in `structuredContent` (`dispatch`, `inputsSent`, `failure`), as Core's tools do.

Destinations: `native-mcp-server` (opens and verifies the native MCP Server dialog without starting it), `tom-explorer`, `tom-explorer/changes` (turns on the TOM Explorer Show changes view filter), `expression-editor`, `object` (with `table`, `objectName`, `objectType` Table/Column/Measure and optional `folder`), `preferences/auto-formatting`, `preferences/code-actions`, `preferences/ai-provider`, `preferences/ai-assistant`, `preferences/mcp-server`, `preferences/permissions`.

```json
{"destination": "object", "table": "Sales", "objectName": "Amount", "objectType": "Column"}
```

Rules:
- The tools attach to the running TE3. Pass `processId` only when more than one TE3 is open. They never launch or close TE3, change preferences, touch databases or run scripts.
- They take a `processId`, not a window handle or element ref. The `windows_*` tools on the same server share refs and handles with each other.
- Capture with `windows_screenshot`, and look at the image: arrival is not visual proof.
- Only one agent should drive the desktop at a time.
- If Windows refuses to activate TE3, the result says `blocker: activation-denied`. Click the TE3 title bar and retry.

## Screenshot runner

The runner launches its own TE3 instance, so close yours first. It stages a model, runs each recipe, and restores your TE3 preferences afterwards. Every run writes a manifest, the PNGs and an `index.html` under the configured output folder.

### Configure

Your configs, recipes and other personal files go in `local/` at the repo root. Git ignores it.

```powershell
mkdir local
copy automation/example.config.json local/te3.config.json   # then edit paths
& $runner validate local/te3.config.json --no-focus             # checks inputs only
& $runner list local/te3.config.json
```

Config paths are relative to the config file and expand environment variables.

| Setting | Meaning |
|---|---|
| `te3` | Path to `TabularEditor3.exe`. |
| `te` | Path to the Tabular Editor command-line tool (`te.exe`), which the runner uses to build and inspect models. |
| `fixtureMode` | `offline` builds a model file for each recipe; `fixed` uses the SSAS database `fixedSlot`; `auto` uses offline where it can and the database for engine recipes. |
| `fixtureScript` | The script that builds the test model. Relative to the config file; the example sets `../automation/fixture.csx`, which works from `local/`. |
| `offlineBaseline` | Optional model file to start offline runs from, instead of an empty model. |
| `server`, `fixedSlot` | The SSAS server (default `localhost`) and database (default `fla_te3_small`) for engine recipes. The name must start with `fla_`. |
| `output` | Where runs are written. |
| `docsRoot` | A docs checkout that `promote` may copy approved images into. |
| `scenarios` | Recipe IDs to run by default. |
| `repeat` | How many times to run each recipe (1-100). |
| `maximizeWindow` | Maximize TE3 before capturing (default true). |
| `captureVariant` | A label for the run, such as `150pct`. |
| `expectedDpi` | Fail if TE3 isn't at this DPI (e.g. 144 for 150%). |

### The test model

Recipes run against a small synthetic model that `automation/fixture.csx` builds: a `Sales` table with three amounts and the measures `Total Amount` and `UI Revenue`, a `Comparison` table, and one pinned Best Practice Analyzer rule. Offline recipes build it fresh each time.

Engine recipes (`measure-edit`, `dax-query`, `expression-recovery`, `bpa-correction`, `properties`, `csharp-preview`) need it deployed to SSAS, and they save changes into it. Create it once. The runner then resets it to the test model before each engine recipe, with the same steps as `reset-engine.ps1`:

```powershell
pwsh automation/reset-engine.ps1 -Create     # deploys fla_te3_small to localhost
pwsh automation/reset-engine.ps1             # puts the test objects back by hand, e.g. after using the companion tools
```

### Run

```powershell
& $runner run local/te3.config.json --scenario model-open
& $runner run local/te3.config.json --scenario model-open,dax-query --repeat 2
& $runner run local/te3.config.json --continue          # keep going after a failed recipe
```

| Command | Does |
|---|---|
| `list CONFIG` | List the recipes, whether each needs the engine, and its docs page. |
| `validate CONFIG [--no-focus]` | Check the config and that its files exist. |
| `run CONFIG [--scenario IDS] [--repeat N] [--continue]` | Run recipes. Takes the desktop. |
| `compose MANIFEST SPEC OUT.png` | Crop and arrange checkpoints from a run into one image. |
| `annotate MANIFEST SPEC` | Add boxes and arrows to a checkpoint. |
| `promote RUN_DIR CHECKPOINT DEST.png [--overwrite]` | Copy an approved checkpoint into `docsRoot`. |
| `promote RUN_DIR --item ITEM_ID [--overwrite]` | Copy a backlog item's checkpoint to the docs image it replaces. |
| `recover BACKUP_DIR [--force]` | Restore TE3 settings after an interrupted run and mark its manifest recovered. Refuses settings changed since the run without `--force`. |
| `mcp` | Start the TE3 MCP tools. |

A passing run is not visual approval. Look at every PNG before using it.

### Recipes

The repo ships the smoke recipes in `src/FlaUI.Mcp.Te3/RecipeCatalog.cs`: `model-open`, `measure-edit`, `dax-query`, `expression-recovery`, `qualified-navigation`, `bpa-correction`, `properties` and `csharp-preview`. Use them as templates.

Add your own in `local/recipes/*.cs`. The project compiles that folder when it exists. Register all of them in one file, since a partial method can be implemented only once:

```csharp
public static partial class RecipeCatalog
{
    static partial void AddLocal(List<Recipe> recipes)
    {
        recipes.Add(new("my-dialog", "content/my-page.md", false, MyDialog));
    }

    private static async Task MyDialog(RecipeContext c)
    {
        await c.Unchanged(async () =>
        {
            await c.Page.OpenPreferences();
            await c.Capture("my-dialog", Te3Page.PreferencesTarget());
            await c.Page.CancelPreferences();
        });
    }
}
```

Tests in `local/tests/*.cs` are compiled into the runner test project in the same way.

### Safety

- Settings backups are kept under `%LOCALAPPDATA%/FlaUI-MCP/settings-backups`. If a run can't restore them, it prints the backup path; run `recover` on it with TE3 closed before the next run. `recover` refuses when a settings file is neither its pre-run original nor what the run wrote, because restoring would discard later changes; the run's own TE3 can also cause this. Check the files, then pass `--force`.
- On a CLI timeout the runner kills the process tree it launched. If it can't confirm the exit, check the reported PID before retrying.
- `promote` only copies into the configured `docsRoot`. Overwriting needs `--overwrite` and a clean destination.
- Checkpoints named after a backlog item (`L026`, `R013`) are checked against the docs screenshot backlog, `local/screenshot-backlog.json`, found by searching up from the runner to the repository root. It is gitignored, so a worktree has none: set `FLAUI_TE3_BACKLOG` to the main checkout's copy (for this repo, `FlaUI-MCP/local/screenshot-backlog.json`) instead of copying it.

## Tests

```powershell
dotnet test tests/FlaUI.Mcp.Tests
dotnet test tests/FlaUI.Mcp.Te3.Tests
```

Neither suite touches the desktop. `tests/FlaUI.Mcp.IntegrationTests` launches test apps and takes focus.

## Native MCP read-only preflight

`te3_native_preflight` exposes `NativeMcpPreflight` through the MCP server. Supply an explicit
loopback `endpoint`, `identityTool`, `identityProperty`, and `expectedIdentity`; optional `tokenEnv` names an environment variable holding the bearer token. It performs initialize, tools/list, and exactly one configured identity
tool call. The identity tool must advertise `annotations.readOnlyHint=true`, except TE3 3.27.1's exact `get_workspace_info` definition (empty argument schema and saved read-only description), which lacks annotations. That exception applies only when the server reports the hard-coded allowlisted `serverInfo` `TE3/3.27.1` (a missing version never matches), and it also requires `compatibleServer: "TE3/3.27.1"` as a second factor the caller observed. Echoing whatever another server reports does not enable it. Other unannotated tools, and that tool on any other server identity, are rejected. Its
configured response property must equal the expected disposable model identity.
A missing server, denied token, missing tool, unannotated tool, or model mismatch
fails before any follow-on work. Each inspection uses a fresh session; there is no
automatic replay. The streamable HTTP reader matches JSON-RPC response IDs, bounds the stream and response deadline, sends the negotiated protocol version, and rejects redirects. Only JSON-RPC responses match a request: a server-initiated request that reuses our id is skipped. One overall deadline (25 s, under the 30 s tool timeout) bounds the whole inspection. Endpoints may be `127.0.0.1`, `localhost` or `[::1]`. The adapter does not start the server, alter permissions, save,
or run a model script. Native TE3 tool names and response properties remain
version-dependent, so configure them from the running server's tools/list result
and verify live behavior separately under a machine grant. The saved 3.27.1 tool list did not include a workspace response; the adapter accepts a configured identity property from direct JSON, `structuredContent`, or one JSON text content block, while the live response shape remains unverified.

`te3_layout` distinguishes docked pane hiding from document closing. Its
close/keep actions preserve document tabs. Named show/hide accepts AI Assistant,
TOM Explorer, and Properties. Showing an absent pane currently refuses input
because the View-menu reopen locator has not been live-verified. The
`assistant-model-review` preset targets AI Assistant width 680, TOM Explorer width 480, and Properties height 320 physical pixels. At the 1800-pixel minimum, it budgets 40 pixels for borders and splitters and reserves at least 600 pixels for the editor. `plan-preset` is offline and accepts optional `windowWidth`/`windowHeight` (defaults 1920x1080); `apply-preset` attaches to TE3 and is live-unverified.
