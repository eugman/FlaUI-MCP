# Changelog

Changes to the Tabular Editor 3 tools and runner. Changes to the generic `windows_*` server are in the
[FlaUI-MCP-Core changelog](https://github.com/eugman/FlaUI-MCP-Core/blob/main/CHANGELOG.md).

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Changed
- **Breaking:** `te3_layout`'s read-only actions (`list`, `show-pane`, `plan-preset`) moved to the new `te3_layout_read`, so each tool has one MCP annotation (`te3_layout_read` read-only, `te3_layout` destructive). `te3_layout` refuses them and names `te3_layout_read`; callers that list panes or plan a preset must call the new tool. Every TE3 tool now declares annotations.
- `FlaUI.Mcp.Te3 mcp` is now the one server to configure. It serves the generic `windows_*` tools (including the window-management extensions) alongside the TE3 tools from one registry, so element refs and window handles work across both. Running `FlaUI.Mcp.exe` next to it is no longer needed.
- A failed `te3_navigate` or `te3_layout` call reports how much input went out the way Core's tools do, in `structuredContent`: `dispatch` (`partial`, `not-dispatched`, or `timed_out_pending` while the failing call may still go out), `inputsSent`, `provider` and the refusal as `failure`. The `actionDispatched` and `inputsSent` fields are no longer in the text. A held TE3 provider is reported as Core's blocked outcome (`not-dispatched`, `provider: pending`).
- TE3 counts input from Core's `inputsSent`, including the activation and focus Core counts, and reads refusals, retries and cancel fallbacks from Core's outcome instead of matching "no input sent" in the text. `te3_navigate` names `activation-denied` and `desktop-unavailable` blockers from the refusal's code.
- The TE3 server serves Core's own `windows_focus`, `windows_close` and `windows_launch`, which now check the grant themselves; the TE3 wrappers, which had replaced Core's tools with ones built without the pending tracker, are gone. The runner focuses its TE3 through Core's guarded focus and counts it, and refuses first while an earlier call still holds TE3's provider. Core's focus by native handle deliberately bypasses the pending tracker (it is how a blocking modal is reached), so the runner checks the tracker itself, as Core's adaptation notes recommend.
- Te3Page's own menu clicks go through `GuardedInput.Click` and Core's `ClickPoint`; a covered pointer target is refused as `point-not-on-target`.
- The run journal's result entries carry `isError`, `content` and `structuredContent` explicitly.
- The TE3 offline tests set `FLAUI_OFFLINE_TESTS=1` and install a fake desktop, so the real desktop refuses input from them.
- `te3_model_state` and `te3_native_preflight` are annotated as Core status reads (`McpToolAnnotations.Status`), so they are answered at once even while a stranded call holds the serial slot. The TE CLI starts through Core's `GuardedDesktop.StartProcess`.
- TE3 coverage and mutation testing moved to `scripts/coverage-te3.ps1` and `docs/mutation-te3.md`; `scripts/coverage.ps1`, `scripts/mutation.ps1` and `docs/mutation.md` now belong to the core and cover its own offline tests. `dotnet-stryker` comes from the core's root tool manifest.

### Added
- `te3_layout` on the TE3 companion server arranges TE3 for a screenshot. `close` and `keep` hide docked panes by title (document tabs are never closed), and stop at a dialog such as a save prompt. `maximize-pane` and `restore-pane` use the pane's own buttons. `resize` drags the dock splitter until a pane is a given width or height, and reports the size reached. Pane titles match with or without TE3's " (Changed)" suffix. `te3_layout_read` lists docked panes and document tabs with bounds.

### Fixed
- `te3_navigate` works when TOM Explorer is a docked pane rather than a tab.
- `te3_navigate` destination `object` picks among rows that share a name (a table, a column and a hierarchy level all called "Date") by the property grid's Object Type. It selects candidates through `windows_click`'s select path, so it never clicks a row that is already selected, which would start an inline rename.
- Engine recipes reset `fixedSlot` to the test model before each recipe. They save into the shared database, so a run of several recipes (or `--repeat`) failed from the second recipe on with a misleading "pass folder" error.
- `te3_model_state` reports a minimized TE3 instead of "Expected one element; found 0": a minimized TE3 leaves its status bar out of the UI Automation tree.
- A local recipe that reuses a recipe id is reported by name.
- The object-not-found message no longer implies the object is in a display folder.

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
