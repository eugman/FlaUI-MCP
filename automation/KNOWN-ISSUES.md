# Known issues

## Clearing the Preferences search blanks the settings pane

Seen in TE3 3.26.3.12966. Clearing the Preferences search box, with its × button or by deleting the text, blanks the settings pane. It does this even when you chose the category from the tree and the search box was already empty. The category stays highlighted.

Workaround: after any change to the search text, click the category again. That brings its settings back.

## Clicking a container can hit a child control

A click on the centre of a pane, made just to focus it, once toggled the "Always prefix extension columns" checkbox. Native-window hit checks prove which window was hit, not which UIA child.

Workaround: don't click pane centres to focus them. Focus a known focusable control instead.

## Dragging the shared edge of touching panes undocks the lower pane

Seen in TE3 3.27.1 at 125%. TOM Explorer and Properties touch with no gap. A drag that starts on the shared edge grabs the Properties title bar, and Properties became a floating `Properties` window. The splitter is in the upper pane's last few rows.

Changed in the host: `te3_layout resize` starts the drag 3 px inside the upper or left pane when two dock panes touch or are at most 2 px apart, and fails with `undocked: true` and a next action when a new titled window of the TE3 process appears after the drag. Neither is verified live. The host does not check that the pointer is on the splitter before the drag (the resize cursor shape is not read).

## A dock pane touching a document on its left did not resize

Seen in TE3 3.27.1 at 125%, window restored at (0,0,1920,1080). `apply-preset assistant-model-review` moved AI Assistant (left of the Expression Editor document) from 638 to 680 with a drag 3 px inside AI Assistant. The next step dragged 3 px inside the document, left of TOM Explorer, to narrow TOM Explorer from 760 to 480. TOM Explorer stayed at 760, nothing undocked, and the preset stopped. The call sent 30 inputs in all, about 15 per drag (Core's drag is a press, 12 moves and a release). The reply did not say what the drag reached.

Changed in the host: beside a document the drag now starts 3 px inside the dock pane, since the splitter is the dock pane's border (the AI Assistant drag shows this). Between two dock panes it stays in the upper or left pane. Each preset step is one drag followed by a measured check, and the preset stops at the first miss. The error names the edge dragged, the drag's start and end in window pixels, the requested and reached size and the attempt count. Each step reports requested, reached, attempts and inputs, and after a failure the reply carries `layoutAfterFailure`, the pane list read back after the failure. The new drag point beside a document needs live verification. The reply's Core outcome is still `partial` with verification `not-established`, so the broker still quarantines the host after a failed step.

Workaround: after a failed preset, read `layoutAfterFailure` (or `te3_layout_read list`) before retrying. Resize the pane by hand if the host drag misses again.

Workaround: if a resize fails with `undocked: true`, dock the pane again by hand and check `te3_layout_read list` before retrying.

## Show changes hides everything else in TOM Explorer

Seen in TE3 3.27.1. With the TOM Explorer `Show changes` filter on and no unsaved changes, the tree is empty and the title reads `TOM Explorer (Changed)`. Object navigation then reports that the table is missing.

Fixed in the host: `te3_navigate object` turns Show changes off before it walks, and reports "show-changes filter active" with a next action if it stays on. Verified live.

Gap: no host tool turns the filter off on its own. `tom-explorer/changes` turns it on, and only `te3_navigate object` turns it off, as part of its walk. In the live session it was turned off with a raw Toggle on the CheckBox, which dropped the TOM Explorer selection.

## Object navigation can outrun the 30-second tool deadline

Seen in TE3 3.27.1 on SpaceParts. Navigating to `Brands[Brand]` walks the filtered tree with Down presses. With and without `folder` (`1. Brand Hierarchy`), the walk was cancelled at the 30-second deadline after 15 inputs, leaving another row selected (`Table "Products"`).

The deadline is Core's fixed 30 s per tool call (`ToolRegistry`); a broker `--timeout` does not lift it, and this host cannot change it. The walk now stops itself once 18 s have passed since the tool call started (opening TOM Explorer, the Show changes check and the typed search count), which leaves about 6 s to clear the search, and fails with the rows visited, the inputs sent, the row it stopped on and its table, instead of being cancelled mid-key. Each Down waits at most 1.5 s, reads included. The earlier 20 s stop, counted from the start of the walk, was verified live. The 18 s total-time stop was verified live on 2026-10-21: without `folder` the walk stopped itself after 8 rows and 16 inputs, cleared the search and reported the row it stopped on. With `folder: "1. Brand Hierarchy"` it reached `Brands[Brand]` in 16.7 s and 13 inputs, so `folder` is needed for that column and the clock leaves little headroom. A deep object can still be out of reach; raising the limit needs a Core change. After a failure, observe the selection with `te3_model_state`. Don't retry blindly.
