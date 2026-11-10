# TE3 automation: current cautions

This is the operational index. A live observation on one TE3 build or display
scale does not prove the same behavior on another. Dated run notes remain in Git
history rather than a fresh clone.

## Preferences search

In TE3 3.26.3, clearing the Preferences search blanked the settings pane even
when its category stayed highlighted. Select the category again and confirm its
controls before editing. This behavior has not been rechecked on every build.

## Focus and pane resizing

Clicking a pane's center to focus it can activate a child control. Focus a known
control instead. When adjacent dock panes touch, use `te3_layout resize` rather
than a raw edge drag; it guards the drag start and reports an unexpected floating
pane. On one 3.27.1 session at 175% display scaling, `assistant-model-review`
applied with AI Assistant 679 px, TOM Explorer 479 px, and Properties 321 px
against its 680/480/320 px targets. Recheck on other DPI/window geometries.

## Object navigation

`te3_navigate object` can stop before reaching a deep object under its tool
deadline. Inspect its structured `walk` result and read `te3_model_state` after
a miss; do not repeat input blindly. Some same-name and root-offscreen cases
were fixed offline and still need a granted live pass. In a 3.27.1 live run,
searching `Type` returned measured rows but omitted `Products[Type]` inside a
collapsed display folder. This complete-search miss now tries the table anchor;
that correction has an offline regression, but has not yet been retested live.

## Raw TOM Explorer drags

A raw `windows_mouse drag` on a TOM Explorer row moved a measure to another
table in a 2026-10-22 run. Do not use that gesture as a harmless demonstration:
the tree can scroll during the drag and TE3 reparents on drop.

## Activation click and dialogs

The runner's activation prompt checks the click position inside TE3's window;
it does not prove which overlapping window received that click. Unknown-dialog
guards have offline tests. In a 3.27.1 live run, `native-mcp-server` opened the
dialog, a request to navigate to Preferences refused input while it remained
open, and title-bar Close dismissed it with MCP still stopped. Observe the open
windows before mutation if a new dialog appears.

## Fixed offline 2026-11-10, not live-verified

Each of these has offline tests only. Check it on the next granted live pass, then delete its line.
- Run cleanup after a revocation: after your input the run ends its own TE3 without a prompt and restores your preferences. It kills that TE3 (verified by PID, start time and executable) with no close request, so no save prompt appears. Verify by touching the mouse mid-recipe with an unsaved edit in the model: no dialog appears, TE3 is gone, `actions.jsonl` has a `te3_kill` entry with the reason and no `te3_close_main_window`, the manifest has the revocation note in `diagnosticNotes`, `settingsRestored` and not `needsRecovery`, and no `recover` is needed.
- Normal run end: `actions.jsonl` records whether the close message was posted, and a `te3_kill` with its reason when TE3 did not exit; the manifest then carries the kill note. Check one run that ends on a save prompt.
- Settings backup removal: after a clean run and after `recover`, the `fla_settings_<guid>` folder is gone and the manifest says `settingsBackupRemoved`.
- `te3_layout keep`: a pane that stays open after its Close is in `unresolved` and the proof is a mismatch; keeping a document tab or a floating pane verifies. Check both on a live TE3.
- Recipe model checks after Save (`PersistedCheck`): tested on a fake clock, but its use in `RecipeContext` has not run against a real model. Run the smoke recipes once and check that the TE CLI's output for a metadata mismatch contains `Metadata assertion failed` (the text the retry looks for) and that `te save` overwrites the confirming `model-after.bim` read.
- `te3_layout resize`: the size is read until it is stable. The read loop is tested on scripted bounds only; check a resize and a preset on a live TE3.
- `te3_layout restore`: the files it replaces are copied to `<name>.replaced-<time>` first and that folder restores them. Check one restore and one undo with TE3 closed.
- `te3_native_preflight` `tokenEnv`: only a `FLAUI_TE3_` variable is accepted. Check one preflight against TE3's server with a token in `FLAUI_TE3_MCP_TOKEN`.
- `te3_native_preflight` paging: `tools/list` follows `nextCursor`. TE3 3.27.1 answered in one page, so this path has only a scripted server behind it.

## Not built yet

- A typed menu tool for TE3's Alt mnemonics: popup destinations need mapping.
- Selection and error assertions through TE3's own MCP server: the observed
  3.27.1 server advertised 24 tools, but this host has verified only the
  read-only `get_workspace_info` identity call. Selection and error behavior
  still need a granted live check.
- TOM Explorer Ctrl+1..7 type filters in the object walk: no verified state
  readback is available yet.
