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
pane. The current DPI-scaled splitter offsets still need a live 175% pass.

## Object navigation

`te3_navigate object` can stop before reaching a deep object under its tool
deadline. Inspect its structured `walk` result and read `te3_model_state` after
a miss; do not repeat input blindly. Some same-name and root-offscreen cases
were fixed offline and still need a granted live pass.

## Raw TOM Explorer drags

A raw `windows_mouse drag` on a TOM Explorer row moved a measure to another
table in a 2026-10-22 run. Do not use that gesture as a harmless demonstration:
the tree can scroll during the drag and TE3 reparents on drop.

## Activation click and dialogs

The runner's activation prompt checks the click position inside TE3's window;
it does not prove which overlapping window received that click. Unknown-dialog
guards have offline tests, but the MCP Server dialog case still needs a granted
live check. Observe the open windows before mutation if a new dialog appears.

## Not built yet

- A typed menu tool for TE3's Alt mnemonics: popup destinations need mapping.
- Selection and error assertions through TE3's own MCP server: the observed
  3.27.1 server advertised 24 tools, but this host has verified only the
  read-only `get_workspace_info` identity call. Selection and error behavior
  still need a granted live check.
- TOM Explorer Ctrl+1..7 type filters in the object walk: no verified state
  readback is available yet.
