# TE3 automation: current cautions

This is the short operational index. The detailed 2026-10-21–25 runs, including
closed defects and superseded workarounds, are preserved in the
[dated issue record](history/known-issues-through-20261025.md). A previous live
observation is not proof that another TE3 build or display scale behaves the
same way.

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
were fixed offline after the dated live runs and still need a granted live pass.
The detailed timing and failure evidence is in the dated issue record.

## Raw TOM Explorer drags

A raw `windows_mouse drag` on a TOM Explorer row moved a measure to another
table in a 2026-10-22 run. Do not use that gesture as a harmless demonstration:
the tree can scroll during the drag and TE3 reparents on drop. See the dated
record for the observed move and its undo.

## Activation click and dialogs

The runner's activation prompt checks the click position inside TE3's window;
it does not prove which overlapping window received that click. Unknown-dialog
guards have offline tests, but the MCP Server dialog case still needs a granted
live check. Observe the open windows before mutation if a new dialog appears.

## Not built yet

- A typed menu tool for TE3's Alt mnemonics: popup destinations need mapping.
- Selection and error assertions through TE3's own MCP server: tool names and
  response behavior need a granted live check.
- TOM Explorer Ctrl+1..7 type filters in the object walk: no verified state
  readback is available yet.
