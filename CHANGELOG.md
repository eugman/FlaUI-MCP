# Changelog

Changes to the Tabular Editor 3 tools and runner. Changes to the generic `windows_*` server are in the
[FlaUI-MCP-Core changelog](https://github.com/eugman/FlaUI-MCP-Core/blob/main/CHANGELOG.md).

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Changed
- The repo builds on the shared FlaUI-MCP-Core and takes its changes by merge. The host project is now `FlaUI.Mcp.Te3` (was `FlaUI.Automation`).
- Personal documentation recipes, run configs, the screenshot backlog, the SpaceParts fixture and the agent study live in a gitignored `local/` folder. The runner compiles `local/recipes` when it exists; the repo ships the eight smoke recipes.
- `te3_navigate` and `te3_model_state` find the running TE3 when `processId` is omitted or null, and ask for it only when several are open.
- `automation/reset-engine.ps1` creates (`-Create`) and resets the `fla_te3_small` database that engine recipes use.
- The `te3-ui` skill ships the composed TE3 guidance instead of an empty placeholder. Docs are consolidated into `automation/README.md`, `UI-MAP.md` and `KNOWN-ISSUES.md`.

### Added
- `te3_model_state` on the TE3 companion server: a read-only status-bar observation reporting load state, Best Practice Analyzer count, caret position, selected object and connection state. Takes no desktop focus and sends no input. A status bar that does not match the observed shape — wrong panel count, or panels that collide when mapped — is reported as unrecognized rather than mapped on a guess. "Recognized" means those checks held, not that the layout is proven.

### Fixed
- A run no longer needs manual recovery because TE3 rewrote a file nothing restores. `SettingsChanges` hashes every file in the TE3 settings folder while `SettingsLease` restores four, so ordinary launch churn (MRU lists, session state, caches) marked the run `NeedsRecovery` — which `ArtifactFiles.Promote` refuses outright, blocking every capture from it with no action that could clear it. Only a leased file left changed is a recovery condition; the rest are warnings, surfaced in `index.html`.
- `Unchanged()` compares the persisted model *before* saving on top of it, so a recipe that mutated the fixture is reported rather than having the mutation committed and then reported. The in-memory path still flushes with Ctrl+S to see an unsaved edit, which the code says plainly.
- `ParkPoint` clamps to both edges of the window. Clamping only the lower bound put the pointer one pixel above a zero-height rectangle, failing the obscured check it was added to avoid; a rectangle with no interior is now refused outright.
- Backlog promotion could publish the clean capture of an item that needs redaction. The requirement lived only in prose, while the About dialog shows the licence holder, installations and session id. Items now carry `requiresRedaction`, every identity-revealing item sets it, and the check lives in `ArtifactFiles.Promote` itself, so the raw `promote <run> <item> <path>` command line cannot bypass it either.
