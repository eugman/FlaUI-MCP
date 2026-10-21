# Known issues

## Clearing the Preferences search blanks the settings pane

Seen in TE3 3.26.3.12966. Clearing the Preferences search box, with its × button or by deleting the text, blanks the settings pane. It does this even when you chose the category from the tree and the search box was already empty. The category stays highlighted.

Workaround: after any change to the search text, click the category again. That brings its settings back.

## Clicking a container can hit a child control

A click on the centre of a pane, made just to focus it, once toggled the "Always prefix extension columns" checkbox. Native-window hit checks prove which window was hit, not which UIA child.

Workaround: don't click pane centres to focus them. Focus a known focusable control instead.

## Dragging the shared edge of touching panes undocks the lower pane

Seen in TE3 3.27.1 at 125%. TOM Explorer and Properties touch with no gap. A drag that starts on the shared edge grabs the Properties title bar, and Properties became a floating `Properties` window. The splitter is in the upper pane's last few rows.

Changed in the host: `te3_layout resize` now starts the drag 3 px inside the upper or left pane when the panes touch or are at most 2 px apart, and fails with `undocked: true` and a next action when a new titled window of the TE3 process appears after the drag. Neither is verified live, and side-by-side touching panes were never tried. The host does not check that the pointer is on the splitter before the drag (the resize cursor shape is not read).

Workaround: if a resize fails with `undocked: true`, dock the pane again by hand and check `te3_layout_read list` before retrying.

## Show changes hides everything else in TOM Explorer

Seen in TE3 3.27.1. With the TOM Explorer `Show changes` filter on and no unsaved changes, the tree is empty and the title reads `TOM Explorer (Changed)`. Object navigation then reports that the table is missing.

Fixed in the host: `te3_navigate object` turns Show changes off before it walks, and reports "show-changes filter active" with a next action if it stays on. Verified live.

## Object navigation can outrun the 30-second tool deadline

Seen in TE3 3.27.1 on SpaceParts. Navigating to `Brands[Brand]` walks the filtered tree with Down presses. With and without `folder` (`1. Brand Hierarchy`), the walk was cancelled at the 30-second deadline after 15 inputs, leaving another row selected (`Table "Products"`).

The deadline is Core's fixed 30 s per tool call (`ToolRegistry`); a broker `--timeout` does not lift it, and this host cannot change it. The walk now stops itself once 18 s have passed since the tool call started (opening TOM Explorer, the Show changes check and the typed search count), which leaves about 6 s to clear the search, and fails with the rows visited, the inputs sent, the row it stopped on and its table, instead of being cancelled mid-key. Each Down waits at most 1.5 s, reads included. The earlier 20 s stop, counted from the start of the walk, was verified live; the 18 s total-time stop needs live verification. A deep object can still be out of reach; raising the limit needs a Core change. After a failure, observe the selection with `te3_model_state`. Don't retry blindly.
