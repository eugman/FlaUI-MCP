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

## A dock pane touching a document on its left did not resize (closed)

Seen in TE3 3.27.1 at 125%, window restored at (0,0,1920,1080): the TOM Explorer drag, 3 px inside the document, left TOM Explorer at 760.

Fixed in the host and verified live on 2026-10-21 (te3-d276329): `apply-preset assistant-model-review` passed in 3 steps, 1 attempt and 15 inputs each, 45 in all. AI Assistant 663 -> 680 (drag (661,573)->(678,573)); TOM Explorer 739 -> 480 with the drag starting 3 px inside TOM Explorer, (1183,375)->(1442,375); Properties 397 -> 320 (drag (1679,647)->(1679,724), just above the shared edge). Nothing undocked.

Side effect: TE3 rewrites `%LocalAppData%\TabularEditor3\Layouts.json`, `UiPreferences.json` and `Preferences.json` when it closes, so the preset (and the window state) persists into the next launch. Snapshot those files before a run and restore them afterwards with TE3 closed.

## Show changes hides everything else in TOM Explorer

Seen in TE3 3.27.1. With the TOM Explorer `Show changes` filter on and no unsaved changes, the tree is empty and the title reads `TOM Explorer (Changed)`. Object navigation then reports that the table is missing.

Fixed in the host: `te3_navigate object` turns Show changes off before it walks, and reports "show-changes filter active" with a next action if it stays on. Verified live.

Gap: no host tool turns the filter off on its own. `tom-explorer/changes` turns it on, and only `te3_navigate object` turns it off, as part of its walk. In the live session it was turned off with a raw Toggle on the CheckBox, which dropped the TOM Explorer selection.

## Object navigation can outrun the 30-second tool deadline

Seen in TE3 3.27.1 on SpaceParts. Navigating to `Brands[Brand]` walks the filtered tree with Down presses. With and without `folder` (`1. Brand Hierarchy`), the walk was cancelled at the 30-second deadline after 15 inputs, leaving another row selected (`Table "Products"`).

The deadline is Core's fixed 30 s per tool call (`ToolRegistry`); a broker `--timeout` does not lift it, and this host cannot change it. The walk now stops itself once 18 s have passed since the tool call started (opening TOM Explorer, the Show changes check and the typed search count), which leaves about 6 s to clear the search, and fails with the rows visited, the inputs sent, the row it stopped on and its table, instead of being cancelled mid-key. Each Down waits at most 1.5 s, reads included. The earlier 20 s stop, counted from the start of the walk, was verified live. The 18 s total-time stop was verified live on 2026-10-21: without `folder` the walk stopped itself after 8 rows and 16 inputs, cleared the search and reported the row it stopped on. With `folder: "1. Brand Hierarchy"` it reached `Brands[Brand]` in 16.7 s and 13 inputs. The host ignored `folder` then, so that success came from a faster walk, not from the argument: whether this column is reached depends on walk speed against the 18 s stop, and the clock leaves little headroom. `folder` is now honoured (the object must be inside it, and the walk stops once it leaves the table), which gives a precise reason on a miss but no speed; not yet run live. A Down that does not move within 1.5 s while a row is shown below the selection is now reported as a slow tree, not as the end of the tree. That check sees only the rows UI Automation has realized, and at most the first 200 TreeItems the selection read returns: when the selection is the last realized row (at the bottom edge of the pane, or past row 200 of a long tree), a slow Down is still reported as the end of the tree. A deep object can still be out of reach; raising the limit needs a Core change. After a failure, observe the selection with `te3_model_state`. Don't retry blindly.

## The object walk entered Roles and misread a reached column

Seen in TE3 3.27.1 on SpaceParts (2026-10-21). With `folder: "1. Brand Hierarchy"`, `te3_navigate object` Brands/Brand stopped on the 18 s clock after 7 rows on Table Permission "Brands" under role Brand VPs: the typed search also matches role table permissions, and Roles comes before Tables. Without `folder` it reached Column "Brand" (Node50, depth 4) but reported "unknown type / table none": the status bar's load slot was blank, so the whole bar mapped as unrecognized and the walk saw no type; a selected display folder also leaves the selection slot on the previous object.

Changed in the host: rows are placed by DataItem depth, typed from the selection slot only when it names the row and otherwise from Properties' ObjectType, groups other than Tables are collapsed before walking on, and a walk that stops on the object (confirmed by Properties' Name and ObjectType) is not a miss. Not yet re-run live.
