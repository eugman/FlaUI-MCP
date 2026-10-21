# Known issues

## Clearing the Preferences search blanks the settings pane

Seen in TE3 3.26.3.12966. Clearing the Preferences search box, with its × button or by deleting the text, blanks the settings pane. It does this even when you chose the category from the tree and the search box was already empty. The category stays highlighted.

Workaround: after any change to the search text, click the category again. That brings its settings back.

## Clicking a container can hit a child control

A click on the centre of a pane, made just to focus it, once toggled the "Always prefix extension columns" checkbox. Native-window hit checks prove which window was hit, not which UIA child.

Workaround: don't click pane centres to focus them. Focus a known focusable control instead.

## Dragging the shared edge of touching panes undocks the lower pane

Seen in TE3 3.27.1 at 125%. TOM Explorer and Properties touch with no gap. A drag that starts on the shared edge grabs the Properties title bar, and Properties became a floating `Properties` window. The splitter is in the upper pane's last few rows.

Workaround: start splitter drags a few pixels inside the upper or left pane. Check `windows_list_windows` for a floating pane window after any failed resize.

## Show changes hides everything else in TOM Explorer

Seen in TE3 3.27.1. With the TOM Explorer `Show changes` filter on and no unsaved changes, the tree is empty and the title reads `TOM Explorer (Changed)`. Object navigation then reports that the table is missing.

Fixed in the host: `te3_navigate object` turns Show changes off before it walks, and reports "show-changes filter active" with a next action if it stays on. Needs live verification.

## Object navigation can outrun the 30-second tool deadline

Seen in TE3 3.27.1 on SpaceParts. Navigating to `Brands[Brand]` walks the filtered tree with Down presses. With and without `folder` (`1. Brand Hierarchy`), the walk was cancelled at the 30-second deadline after 15 inputs, leaving another row selected (`Table "Products"`).

The deadline is Core's fixed 30 s per tool call (`ToolRegistry`); a broker `--timeout` does not lift it, and this host cannot change it. The walk now stops itself after 20 s and fails with the rows visited, the inputs sent, the row it stopped on and its table, instead of being cancelled mid-key. A deep object can still be out of reach; raising the limit needs a Core change. After a failure, observe the selection with `te3_model_state`. Don't retry blindly.
