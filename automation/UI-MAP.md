# TE3 UI map

Locators and behaviour observed in TE3 3.26.3 and 3.27.0. Treat every label as a release observation: if a selector stops matching, observe the new UI before changing it.

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
| Preferences | Window `Preferences` (rootOnly, includeOwned) | `searchPreferences`, `treePreferences`. |

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
- To reach a table, collapse the tree, click Tables, press Right, then press Down until the TreeItem with that value is selected.
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
| Tools > MCP Server (3.27) | Escape or Cancel | Don't start the server unless you mean to. |

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
