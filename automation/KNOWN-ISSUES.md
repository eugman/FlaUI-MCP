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

## Not built yet

- A typed menu tool for TE3's Alt mnemonics: popup destinations need mapping.
- Selection and error assertions through TE3's own MCP server: the observed
  3.27.1 server advertised 24 tools, but this host has verified only the
  read-only `get_workspace_info` identity call. Selection and error behavior
  still need a granted live check.
- TOM Explorer Ctrl+1..7 type filters in the object walk: no verified state
  readback is available yet.
