# TE3 screenshot runner

A separate executable drives TE3 through typed C# recipes. The generic MCP server
contains no TE3 profile interpreter. See [recipes](RECIPES.md), [locators](UI-MAP.md)
and [status](VERIFICATION.md). The [596-image backlog](screenshot-backlog.json)
retains original source/page mappings; related recipes are not approved replicas.
Agents should start with the compact [agent quickstart](AGENT-QUICKSTART.md),
then load only the relevant locator or recipe section.

## Configure and build

Optional `captureVariant` (for example `preferences-150pct`) labels a run;
`expectedDpi: 144` requires 150% effective main-window scaling (96 DPI is 100%).
Each checkpoint records observed main-window DPI, monitor bounds, work area and
window bounds in `captureEnvironments`; a changed context during capture fails
the checkpoint. Runs retain separate output directories, so variants do not
overwrite each other. This does not change display settings or yet verify the
DPI of every popup. Older manifests have no inferred display metadata.

Copy `example.config.json` to gitignored `local.config.json`; adjust executable
paths, server and docs-copy root. Paths are config-relative and expand Windows
environment variables. Repeat defaults to one. Auto fixture mode uses offline
BIM copies where possible and the existing fixed database for engine recipes.

For a no-server handoff, use `offline.config.json`. It selects fifteen offline
recipes, forbids engine recipes, and has no promotion
destination. It still launches TE3 and temporarily normalizes/restores preferences,
so desktop permission is required. `validate ... --no-focus` only checks local inputs.

```powershell
dotnet build src/FlaUI.Automation/FlaUI.Automation.csproj -c Debug
$runner = "./src/FlaUI.Automation/bin/Debug/net8.0-windows/FlaUI.Automation.exe"
& $runner list automation/local.config.json
& $runner validate automation/local.config.json
```

## Execute

Every live handoff requires explicit desktop permission. Close TE3 first; the
runner refuses existing instances. If Windows refuses activation, click the
identified test window. No speculative input is allowed.

```powershell
& $runner run automation/local.config.json --scenario file-load-three-cycles
& $runner run automation/local.config.json
```

Runs retain a manifest, PNGs and index.html. Review actual screenshots against
originals before publishing; a passing test is not visual approval.
Failed runs include the last step and full exception in the manifest/HTML, plus
a failure screenshot when capture succeeds. No uncertain input is replayed automatically.

`compose` normally stacks labeled panels. For an original-sized view, `cropOnly`
requires one panel and omits margins/labels. An optional `outline` adds an explicit
red annotation inside the crop; it never rescales UI pixels. See
`auto-rollback.composition.json` for the verified source geometry.
Optional `arrows` draw bounded green left-pointing annotations on a crop-only
image. `file-formats.composition.json` pairs with the recipe's pre/post-capture
row-position checks. These annotations are explicit additions, not raw UI pixels.

Commands: `run CONFIG [--scenario ID] [--repeat N]`, `list CONFIG`,
`validate CONFIG`, `compose MANIFEST SPEC OUT`,
`promote RUN_DIR CHECKPOINT DEST [--overwrite]`, and `recover MANIFEST`.

## Safety

- Engine recipes use the existing database named by `fixedSlot` (must begin with
  `fla_`). The runner never creates, resets or deletes databases. Recipe edits can
  persist there; prepare suitable metadata yourself and use offline mode by default.
- Settings backups live under LocalAppData/FlaUI-MCP/settings-backups. Restore
  only after TE3 closes; if shutdown fails retain backup and use explicit recovery.
- CLI timeouts require confirmed child exit before reset. An unconfirmed process
  blocks subsequent CLI operations; do not delete pending-cli.json to bypass it.
- Recovery has effects and needs appropriate permission.
- Promotion copies only into the configured docs-copy root. Existing files require
  explicit overwrite and a clean tracked destination. Approval is a human workflow,
  not a generated receipt.
- Do not modify the original docs checkout or publish over a running MCP server.
  Complex infrastructure remains deferred.

## Test and migration

```powershell
dotnet test tests/FlaUI.Mcp.Tests/FlaUI.Mcp.Tests.csproj -c Debug
dotnet test tests/FlaUI.Automation.Tests/FlaUI.Automation.Tests.csproj -c Debug
dotnet build tests/FlaUI.Mcp.IntegrationTests/FlaUI.Mcp.IntegrationTests.csproj -c Debug
```

The first two suites are no-focus. The integration suite launches apps: build
only until a desktop handoff. Recipe/profile JSON, dependency planners, dashboards,
release descriptors and approval receipts were removed. Select typed recipe IDs.
FLAUI_MCP_PROFILE is obsolete and produces a stderr migration warning.
Historical artifacts are unchanged; old runs do not verify the rewritten runner.
Pre-refactor code/docs are recoverable under ignored
artifacts/refactor-backup-20260912-172012.
