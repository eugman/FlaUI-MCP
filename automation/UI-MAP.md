# TE3 UI map

Locators and behaviour observed in TE3 3.26.3, 3.27.0 and 3.27.1. Treat every label as a release observation: if a selector stops matching, observe the new UI before changing it.

## General rules

- Get fresh handles from `windows_list_windows`. Dialogs and popups have their own handles, and a handle (`w1`) is not a ref (`w1e5`).
- Text on screen is usually in `value`, not `name` (TOM rows are named like `Name row 87`, Preferences rows `Node0`). Use the `text:` selector, which matches either.
- Presence doesn't prove selection, and a selected row doesn't prove its pane is showing. Check the pane's controls.
- Minimized windows expose incomplete UIA trees.
- Numeric HWND-like automation IDs are unstable. Use labels plus control type.
- After a timeout, observe again before any input. Never replay input blindly.
- Before driving a model, `te3_model_state` tells you whether it is loaded and connected.

## Locators

| Area | Selector | Notes |
|---|---|---|
| Main menu | MenuBar `Main menu` | MenuItems File … Help. Popup entries are Buttons; only submenu parents are MenuItems. |
| TOM Explorer | `treeList` in `TabularExplorerView` | Rows expose names as Value. Qualify the table. |
| TOM Explorer Show changes | CheckBox `Show changes` in ToolBar `TOM Explorer` > Group `Dock Top` > Pane `TabularExplorerView` | Observed 3.27.1 with Toggle and Invoke patterns; `tom-explorer/changes` turns it on and verifies it is on, and `te3_navigate object` turns it off before walking the tree. |
| Properties | `PropertyGridView` | Categories are TreeItems, properties DataItems with values. |
| Expression editor | `DaxEditor` / `daxscilla` in `QuickEditorView` | A Property ComboBox picks which DAX property is edited. |
| DAX query | `daxEditor` / `daxscilla` | Result cell: DataItem `[Value] row 1`. |
| Messages | `MessagesView` | Error cells hold the full diagnostic. |
| C# script | `cSharpEditor1` | Run with Button `Run script`. |
| Script output | `ScriptOutputForm` (rootOnly, includeOwned) | `dataTextBox`, `btnClose`. |
| Script preview | `ScriptPreviewDialog` (rootOnly, includeOwned) | `diffTreeList`, `okButton`, `revertButton`. |
| Auto-rollback | CheckBox `Auto-rollback` | Toggle pattern. Assert it; don't click it. |
| BPA | `treeBpa` in `BestPracticeAnalyzerView` | Refresh first; match the exact rule Value. |
| Open File | owned Window `Open File` | Filename is Edit `1148`. A ComboBox shares that ID, so keep the Edit constraint. |
| Preferences | Native window `Preferences` | `searchPreferences`, `treePreferences`. The UIA root lookup timed out in 3.27.1; register the native window and query controls on that handle. |

| AI Assistant > AI Provider | Pane `AI Assistant.AI Provider` | Observed 3.27.1: ComboBoxes `Choose provider` and `Model name`. Do not capture or print API key controls. |
| AI Assistant > Preferences | Pane `AI Assistant.Preferences` | Observed 3.27.1: CheckBoxes `Allow AI assistant to run C# scripts directly` and `Preview changes`. |
| AI Features > MCP Server | Pane `AI Features.MCP Server` | Observed 3.27.1: CheckBoxes `Enable MCP Server`, `Start MCP server automatically`, `Require access token`; Spinner `Port`. |
| AI Features > Permissions | Pane `AI Features.Permissions` | Observed 3.27.1: ComboBoxes `Model metadata:`, `Model data:`, `Best Practice Analyzer:`, `Documents:`, `Macros:`. |

## Menus

- Open a menu with one physical click, then confirm it opened before clicking again.
- Popups are separate same-process native windows. Find entries from the popup's handle, not the whole editor.
- UIA Focus on popup items returns without selecting them. Hover selects but adds a tooltip. Keyboard `Home`/`Down` highlights without tooltips; check `HasKeyboardFocus` after each step.
- Background captures leave open menus out. Capture menus with a plain screenshot.
- Cold-start menu discovery can take over five seconds.

**Menu contents (3.26.3)**

- **File:** New and Open (submenus), Revert, Close Document, Close Model, Save, Save As..., Save to Folder..., Save All, Recent Files, Recent Tabular Models, Exit.
  - New: Model..., New DAX Query, New Pivot Grid, New DAX Script, New Diagram, New C# Script.
  - Open: Model from File..., Model from DB..., Model from Folder..., File..., Import from Metric View YAML....
- **Model:** Deploy..., Serialization options..., Import tables..., Update schema (all tables)... (disabled offline), Script DAX, Refresh model, then the Add entries (Table, Calculated Table, Calculation Group, Data Source, Perspective, User-Defined Function, Shared Expression, Relationship, Role, Translation).
- **Tools:** Customize..., Preferences..., Manage BPA rules....
- **Window:** layouts (Default, Classic), Theme, Language, Reset Window Layout, Windows....
- **Help:** About shows the licence holder, installations and session ID. Redact them before publishing.
- **3.27 adds:** Tools > MCP Server, Help > Get Started, File > Reload from disk.

## Preferences

1. Open it by physically clicking Tools, then the `Preferences...` Button. Don't Invoke it: the call blocks until the dialog closes.
2. Wait for the `Preferences` window and use its own handle.
3. Search with `searchPreferences`. Typing filters; setting the value doesn't.
4. Select the TreeItem by Value inside `treePreferences`. Names like `Node0` and indexes change after filtering.
5. Clearing the search blanks the settings pane and can move the selected row out of view (see [KNOWN-ISSUES](KNOWN-ISSUES.md)). Page down the tree until the row shows, click it again physically, and check the pane's controls.
6. Scope controls to the pane's automation ID, e.g. `DAX Editor.Auto Formatting`, `DAX Editor.Code Actions`, `File Formats.General`, `File Formats.Save-to-folder`, `Tabular Editor.User Interface`.
7. Close with Cancel. Never press OK to make a screenshot match.

More notes:
- Several rows are labelled `General`, and UIA doesn't nest them under their parents. Select the parent, press Right, and check that exactly one child is selected.
- Some tree rows expose their checked state through `LegacyIAccessible.State` bit `0x10`, not the Toggle pattern.
- ComboBox dropdowns (`Alt+Down`) open separate native popups. Map them from the popup handle.

## TOM Explorer

- Top-level rows are folders: Data Sources, Functions, Perspectives, Relationships, Roles, Shared Expressions, Tables. Tables appear only after Tables is expanded.
- Only rows in view are exposed, and only TreeItems report selection (the DataItem cell with the same value doesn't).
- To reach a table, collapse the tree, select Tables with Home and Down, note the row below it, press Right, then press Down until the TreeItem with that value is selected. Reaching the noted row means the value is not under Tables.
- A typed search also matches relationships, partitions and role permissions with the same name. Don't assume the first match is the table.
- Don't Invoke rows: it starts inline editing. Click physically, then move the pointer away so a tooltip doesn't cover the next capture.
- Shift+F10 opens a row's context menu. Its Create cascade opens with a physical click.

## Dialogs

| Dialog | Close with | Notes |
|---|---|---|
| New Model | Escape | Model Name, Compatibility Level, Use workspace database. |
| Load Semantic Model from Database | Escape | Server, authentication radio buttons, Advanced options. |
| Layouts | title-bar Close | Escape doesn't close it, and it has no Cancel. |
| Windows | title-bar Close (`Close`) | Escape doesn't close it. |
| Manage Best Practice Rules | Escape or Cancel | |
| MCP Server (3.27.1) | OK | Status-bar Button `MCP Stopped` opened this dialog in the live session. Pane `McpServerDialog` contains SplitButton `Export configuration`; `native-mcp-server` only opens and verifies it. Do not click Start server unless separately intended. |

A dialog can disappear between window enumeration and a UIA read. Treat `ElementNotAvailableException` as "observe again", not as proof that it closed.

## C# scripts

1. Select Expression Editor and press `Ctrl+O`. Get the `Open File` handle.
2. Fill Edit `1148` with the absolute `.csx` path and press Enter with `verifyFocus: true`. Don't use the address bar.
3. Check the source in `cSharpEditor1` before running anything.
4. Output appears in `ScriptOutputForm`. Preview rows expose compound values such as `description;before;;description;after`.

## Capture

- Background captures (`background: true`) leave out owned dialogs and open menus.
- Preferences UIA bounds exclude the title bar, but the background PNG includes it. Use native window bounds as the origin.
- After resizing a DevExpress dialog, wait about 500 ms before capturing: the native bounds settle before the pixels do.
- TE3 adds untitled same-process shadow windows: 7 px around menus and 10 px around lists. The capture guards allow exactly these and nothing else.

## AI and change review map (3.27.1)

| Route | Evidence | Companion status |
| --- | --- | --- |
| Native MCP Server dialog | Live status button and dialog pane controls | `native-mcp-server` |
| Preferences > AI Features > MCP Server | Live UIA tree and pane controls | `preferences/mcp-server` |
| Preferences > AI Features > Permissions | Live UIA tree and pane controls | `preferences/permissions` |
| Preferences > AI Features > AI Assistant > AI Provider | Live UIA tree and pane controls | `preferences/ai-provider` |
| Preferences > AI Features > AI Assistant > Preferences | Live UIA tree and pane controls | `preferences/ai-assistant` |
| View > AI Assistant | Live View popup Button `AI Assistant` and Pane `AI Assistant` (2026-10-21) | `te3_layout reopen-pane` (reopen verified by hand; the action itself is verified live for Properties only) |
| View > Properties | Live View popup Button `Properties` (shortcut F4) and Pane `Properties` (2026-10-21) | `te3_layout reopen-pane`, verified live: see Docked panes |
| TOM Explorer > Show changes | Live UIA CheckBox with Toggle pattern; [official behavior](https://docs.tabulareditor.com/en/features/unsaved-changes.html) | `tom-explorer/changes` |
| Properties > Show changes | Live CheckBox `Show changes` in ToolBar `Tools` > Group `Dock Top` > Pane `PropertyGridView`, Toggle and Invoke patterns (2026-10-21) | Observed only |

The MCP Server and Permissions routes inspect settings only. They do not change grants or start the MCP server.

## Docked panes and View menu (3.27.1)

Observed 3.27.1 (2026-10-21) on `localhost/sp-demo-mcp`, main window maximized on a 1920x1200 monitor at 125% (window and monitor DPI 120, not 150%). Bounds are physical pixels.

| Area | Selector | Notes |
|---|---|---|
| Main window | Window titled `<database> (Import) - <server> - Tabular Editor 3.27.1 - Enterprise Edition` | UIA BoundingRectangle (0,30,1920,1110); native window bounds (-9,-9,1938,1158). |
| Main menu | MenuBar `Main menu` | MenuItems File, Edit, View, Expression Editor (only while that document has focus), Model, Tools, Window, Help. |
| AI Assistant | Pane `AI Assistant` > `AiChatView` | TitleBar `AI Assistant` with Buttons `Close`, `Auto Hide` (no `Maximize`). ToolBar `Tools`: `New conversation`, `Delete conversation`, ComboBox `barConversations`, `Open Custom Instructions Folder`, `AI Assistant Settings`; Button `Send` (`send-button`). Chat is WebView2 (`myapp.local/index.html`). |
| TOM Explorer | Pane `TOM Explorer` inside Tab `Tabular Editor 3` | Tabs `TOM Explorer` and `Best Practice Analyzer` share the container. TitleBar Buttons `Close`, `Auto Hide`, `Maximize`. Title reads `TOM Explorer (Changed)` while Show changes is on. Search Edit `searchControl1`. |
| Properties | Pane `Properties` > `PropertyGridView` > Table `propertyGrid` | ToolBar `Tools`: CheckBoxes `Categorized`, `Alphabetical`, `Show changes`, `Property descriptions`. This is the Properties Show changes control. |
| Expression Editor | Document Pane `Expression Editor` > `QuickEditorView` | ToolBar `DAX Script Tools` with ComboBox `Property`. Listed by `te3_layout_read` as kind `document`. |
| Status bar | StatusBar `Status bar` | Buttons `<n> BP issues` and `MCP Stopped`. |
| View menu entries | Buttons in the untitled popup (find with `includeOwned: true`) | `TOM Explorer`, `AI Assistant`, `DAX Package Manager`, `Best Practice Analyzer`, `Messages`, `Data Refresh`, `Expression Editor`, `Macros`, `VertiPaq Analyzer`, `Dependencies`, `DAX Optimizer`, `Calendar Editor`, `Perspective Editor`, `Metadata Translation Editor`, `Properties`; submenu MenuItems `Debug`, `Toolbars`. The popup is not listed by `windows_list_windows`. |

Not open in this layout, so not observed: DAX query, Messages, Macros, Data preview panes.

Reopening a pane: hide it, click MenuItem `View` with `physical: true`, find Button `<pane title>` with `includeOwned: true`, click it with `physical: true`. Verified for Properties, AI Assistant and TOM Explorer; each came back at its previous bounds.

`te3_layout reopen-pane` verified live on 3.27.1 for Properties (2026-10-21): `hide-pane` took 1 input, `reopen-pane` 3, and Properties came back at its previous bounds (TOM Explorer shrank back to its old height). A second `reopen-pane` on the visible pane sent no input and returned `changed: false`. A document title (`Expression Editor`) is refused before input as a document, not a dockable pane in the View menu map.

View popup (3.27.1): Button `Properties` shows shortcut F4. Its ancestry is a nameless `Menu` with its own hwnd inside a nameless owned `Window`, so find it with `includeOwned: true`. Escape closes the popup. A `windows_screenshot` of the main window handle includes the open popup; a `fullScreen` capture is refused under the app allowlist.

Pane bounds from `te3_layout_read list` (UIA-window-relative): AI Assistant (0,61,1000,1017), Expression Editor document (1000,61,160,1017), TOM Explorer (1160,61,760,590), Properties (1160,651,760,427). `maximize-pane Properties` gave TOM Explorer height 36 and Properties (1160,97,760,981); `restore-pane` returned the original sizes.

Coordinate spaces: `te3_layout_read list` is relative to the UIA window rectangle; a `strictNative` capture's origin is the native window rectangle. Maximized, a listed point maps to PNG pixels at (+9, +39). Restored and placed at (0,0,1920,1080), the UIA root is (1,39,1918,1040) and a listed point maps at about (+1, +39). Use `windows_screenshot` with `locate` instead of converting: located AI Assistant (9,100,1000,1017), Properties (1169,690,760,427) and the menu bar (9,39,1920,29) matched the pixels.

Touching panes: the TOM Explorer and Properties containers share an edge (y 651) with no gap. The splitter sits in TOM Explorer's last rows; y 651 is the top of the Properties title bar.

Restored at (0,0,1920,1080) the panes listed as AI Assistant (0,61,638,947), Expression Editor document (638,61,520,947), TOM Explorer (1158,61,760,549) and Properties (1158,610,760,398). TOM Explorer's left edge touches the document with no gap. `apply-preset assistant-model-review` there moved AI Assistant to 680 with a drag 3 px inside AI Assistant. The TOM Explorer drag, 3 px inside the document, left it at 760, and nothing undocked. The host now starts that drag 3 px inside TOM Explorer; see [KNOWN-ISSUES](KNOWN-ISSUES.md).

Pane bounds versus content bounds: `te3_layout_read list` gives the pane container including its title bar, TOM Explorer (1159,100,760,549) in screen pixels with the window at (0,0). The content Pane `TabularExplorer` located in a `strictNative` capture starts below the title bar at (1164,132,751,478), and Edit `searchControl1` at (1164,162,751,26). Pick the rectangle you mean when you crop.

TOM Explorer object walk (SpaceParts, 2026-10-21): column `Brands[Brand]` is in display folder `1. Brand Hierarchy`. `te3_navigate object` reaches it only with `folder: "1. Brand Hierarchy"` (16.7 s, 13 inputs). Without `folder` the walk stops on the 18 s clock after 8 rows.

| Dialog (3.27.1) | Selector | Notes |
|---|---|---|
| Open File | owned Window `Open File` from toolbar Button `Open...` | Observed 3.27.1 (2026-10-21): ID `1148` is shared by a Pane, ComboBox `File name:` and Edit `File name:`; keep the Edit constraint. ComboBox `Files of type:` is `1136`. Cancel is Button `Cancel`, ID `2`. |
| Preferences | Window `Preferences` | Observed 3.27.1: Cancel is Button `Cancel` (numeric ID, unstable). All six `preferences/*` destinations arrived with the Preferences window left open between calls. |
| MCP Server | Window `MCP Server`, Pane `McpServerDialog` > `layoutControl` | Observed 3.27.1: Buttons `OK`, `Start server`, `Copy`; SplitButton `Export configuration`; read-only Edit `Server URL:`; permission ComboBoxes `Model metadata:` (Deny/Read/Read-Write), `Model data:` (Deny/Read), `Best Practice Analyzer:`, `Documents:`, `Macros:`. Observed only; no permission changed. |
