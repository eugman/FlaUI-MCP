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

Closed gap: `te3_navigate` `tom-explorer/changes-off` turns the TOM Explorer filter off on its own (the CheckBox, polled until it reads off, then checked on arrival). Verified live on te3-cfa803c (2026-10-21) with `tom-explorer/changes` (title `TOM Explorer (Changed)`, then `TOM Explorer`; 1 input each). Turning it off drops the TOM Explorer selection to "(Nothing selected)": the host does not restore it, so navigate to the object again afterwards if the shot needs it. Earlier the filter was turned off in a live session with a raw Toggle on the CheckBox, which dropped the TOM Explorer selection. Properties' own Show changes has no bindable command and no host tool; the TOM Explorer one is bindable as `TomExplorer.ToggleChangedOnly`.

## Object navigation can outrun the 30-second tool deadline

Seen in TE3 3.27.1 on SpaceParts. Navigating to `Brands[Brand]` walks the filtered tree with Down presses. With and without `folder` (`1. Brand Hierarchy`), the walk was cancelled at the 30-second deadline after 15 inputs, leaving another row selected (`Table "Products"`).

The deadline is Core's fixed 30 s per tool call (`ToolRegistry`); a broker `--timeout` does not lift it, and this host cannot change it. The walk now stops itself once 18 s have passed since the tool call started (opening TOM Explorer, the Show changes check and the typed search count), which leaves about 6 s to clear the search, and fails with the rows visited, the inputs sent, the row it stopped on and its table, instead of being cancelled mid-key. Each Down waits at most 1.5 s, reads included. The earlier 20 s stop, counted from the start of the walk, was verified live. The 18 s total-time stop was verified live on 2026-10-21: without `folder` the walk stopped itself after 8 rows and 16 inputs, cleared the search and reported the row it stopped on. With `folder: "1. Brand Hierarchy"` it reached `Brands[Brand]` in 16.7 s and 13 inputs. The host ignored `folder` then, so that success came from a faster walk, not from the argument: whether this column is reached depends on walk speed against the 18 s stop, and the clock leaves little headroom. `folder` is now honoured (the object must be inside it, and the walk stops once it leaves the table), which gives a precise reason on a miss but no speed; not yet run live. A Down that does not move within 1.5 s while a row is shown below the selection is now reported as a slow tree, not as the end of the tree. That check sees only the rows UI Automation has realized, and at most the first 200 TreeItems the selection read returns: when the selection is the last realized row (at the bottom edge of the pane, or past row 200 of a long tree), a slow Down is still reported as the end of the tree. A deep object can still be out of reach; raising the limit needs a Core change. After a failure, observe the selection with `te3_model_state`. Don't retry blindly.

## The object walk entered Roles and misread a reached column

Seen in TE3 3.27.1 on SpaceParts (2026-10-21). With `folder: "1. Brand Hierarchy"`, `te3_navigate object` Brands/Brand stopped on the 18 s clock after 7 rows on Table Permission "Brands" under role Brand VPs: the typed search also matches role table permissions, and Roles comes before Tables. Without `folder` it reached Column "Brand" (Node50, depth 4) but reported "unknown type / table none": the status bar's load slot was blank, so the whole bar mapped as unrecognized and the walk saw no type; a selected display folder also leaves the selection slot on the previous object.

Changed in the host: rows are placed by DataItem depth, typed from the selection slot only when it names the row and otherwise from Properties' ObjectType, groups other than Tables are collapsed before walking on, and a walk that stops on the object (confirmed by Properties' Name and ObjectType) is not a miss. Not yet re-run live.

## Filling the Scintilla editors without a focus check

The expression editor and the DAX query editor are Scintilla controls (`daxscilla`). Whether UI Automation reports keyboard focus on the control itself, or on a child, was never checked live, so the host fills them with `verifyFocus: false`: Core's Value pattern is tried first, and if it falls back to the keyboard (Ctrl+A, Backspace, text) it does not first confirm focus reached the editor. Every other field the host fills asks for the check.

Next: once Core's `verifyFocus: "self-or-descendant"` (Core c807de5) is merged here, and the editors are seen to report focus that way live, switch both calls to it.

## Object walk timing on te3-cfa803c (still open)

Live on 2026-10-21 (te3-cfa803c), `te3_navigate object` Brands/Brand/Column stopped on the 18 s clock both ways, at about 3.5-4 s a row, 13 inputs and about 21 s wall each: with `folder` after 4 rows on Group "Roles" (the time went on collapsing groups), without after 5 rows on Partition "Brands", two rows short of the column. Budget Rate/Rate/Column (in folder 1. Facts, near the top) passed in 21.8 s with 12 inputs. Stop reasons were accurate (typed rows, the real table). Groups collapsed during a walk stay collapsed or are re-expanded by TE3 when the search clears (the partition moved from Node12 to Node39).

Again on te3-97a4512 (2026-10-21), at about 2.3 s a row: with `folder` it stopped after 8 rows on Partition "Brands" (17 inputs, 20.2 s); without it ran past the column to folder "3. Managers", which it read as "unknown type, table none" (Properties: ObjectType Folder; the status-bar selection slot was stale on Column "Product Brand VP"). Budget Rate[Rate] (14.1 s) and the measure Orders[Order Lines] (10.1 s) passed. A folder miss reached "the end of the filtered tree" on Products[Sub Brand Name] while it read as if Brands had been searched; the reason now names the row it ended on.

On te3-6535504 (2026-10-21) four of six walks were right: Brands[Brand] with folder (rows, 22 rows read, 1 select, 10 inputs, 10.1 s) and the folder miss passed; without a folder or type the already-selected column was turned down as "no new selection" and the level taken; Orders[Order Lines] was turned down because it was read before the status bar caught up; Budget Rate[Rate] and a calc-group measure fell to keys with 0 rows read (the read came before the filter rendered) and ended in the wrong place reporting "table none". Changed since: an already-selected pick is confirmed without a select, the confirmation polls about 1.5 s for TE3 to name the label, the read waits for the filtered tree to render and never falls back on an empty read, and a miss says the type asked for and where the selection ended with its table from the rows. Not yet run live; keep this row open.

On te3-ea21625 the row strategy picked and selected Column Brand in 5.1 s and 8 inputs, then turned it down because a column's Properties grid has no ObjectType row; the other walks fell back to keys from the current selection (the root was off screen, so the indent was not measured) and failed or reported "table none". Changed since: the type comes from the status bar first, the row read starts with Home and pages past the viewport, an untyped row at table depth under Tables is the table, and objectType is optional. Not yet run live; keep this row open.

Changed in the host after that: the keyboard walk is no longer the primary strategy. te3_navigate object reads every realized row of the filtered tree once (TreeItem text, DataItem indent for depth), picks the object by names and depth, selects it with one guarded Select and confirms with Properties' Name and ObjectType; the reply's `walk` gives rowsRead and indentPx. The keyboard walk below is the fallback when the indent cannot be measured, or the tree scrolls and holds no candidate. Not yet run live.

Changed in the host before that: one read per row (the settled read is reused as the next key's "before" and for the collapse check), Properties only for rows whose text the walk is looking for, Left only on an open group, and no group collapsed once the clock has run out. Not yet re-timed live. TOM Explorer's own type filters (Ctrl+1..7, documented) would shrink the tree further but are not used: their state has no verified read-back and they change the user's view (deferred).

## Not built yet

- A typed menu tool using the Alt mnemonics (File Alt+F, Edit Alt+E, View Alt+V, Model Alt+M, Tools Alt+O, Window Alt+W, Help Alt+H, verified on 3.27.1): deferred because the popup entries reached after Alt+letter have not been mapped live.
- MCP-based selection and error assertions through TE3's own MCP server: deferred because the server's selection tool names are not known and the server is stopped with AutoStart off on the test machine (the host never starts it). `te3_native_preflight` is the only route so far.
- TOM Explorer type filters (Ctrl+1..7) for the object walk: see above.

## The runner's activation click is checked by position only

When Windows refuses to bring the runner's TE3 forward, the run continues only after a mouse click whose point lies inside TE3's window rectangle (and no typing). The point is not hit-tested: a click on another window that overlaps TE3's rectangle, followed by TE3 coming forward, is accepted as the go-ahead. A hit test (which window was under the click) is deferred to the Core rebase, where Core's ClickHitTest and FromPoint go through the desktop seam.

## Closing a floating pane (closed)

`te3_layout close` once ignored the floating Dependencies window, then only pointed at windows_close. Verified live on te3-6535504 (2026-10-21): `close ["Dependencies"]` closed it in 1 input and 0.7 s through windows_close on the window's own handle, read it back gone, and left AI Assistant, TOM Explorer and Properties; the reply now names that route.

## Closing TE3 needs an active interaction

`windows_close` on the TE3 window is a desktop change like any other, so it is refused (not-dispatched, 0 inputs) after `te3_interaction_end`. The host keeps it that way rather than exempting its own close: end the interaction only after TE3 is closed, or begin a new one for the teardown (te3-6535504 did the latter). `te3_layout restore` needs no interaction, so it can run after the end.

