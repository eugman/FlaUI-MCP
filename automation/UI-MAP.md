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
| File > Open > Model from DB | Button `Model from DB...` in the main toolbar, or File > Open submenu | Opens the owned `Load Semantic Model from Database` window. Identify the dialog by its title before reading controls; opening it does not connect. |
| Database connection dialog | ComboBox `Server:` (`cmbServer`), `Local instance:` (`cmbLocalInstance`), `Mode:` (`cmbMode`); Buttons `OK`, `Cancel` | `Server:` contains an Edit and a separate child Button `Open`. Use `windows_find` with ancestry to distinguish this arrow from the other `Open` buttons. `Cancel` closes without connecting. The default Mode observed was `Read/Write`, so never use `OK` merely to inspect recents. |
| Preferences > User Interface | ComboBox `Model source detail` | Observed 3.27.1 choices: `Full path`, `Name only`, `Hidden`. On a disconnected local folder copy, `Name only` removed the source path from the window title immediately after Preferences `OK`; restoring `Full path` restored that title suffix immediately. `Hidden` was observed but not applied. Preferences UIA can enumerate controls on inactive pages, so first select User Interface and confirm the page before changing this value. |

| AI Assistant > AI Provider | Pane `AI Assistant.AI Provider` | Observed 3.27.1: ComboBoxes `Choose provider` and `Model name`. Do not capture or print API key controls. |
| AI Assistant > Preferences | Pane `AI Assistant.Preferences` | Observed 3.27.1: CheckBoxes `Allow AI assistant to run C# scripts directly` and `Preview changes`. |
| AI Features > MCP Server | Pane `AI Features.MCP Server` | Observed 3.27.1: CheckBoxes `Enable MCP Server`, `Start MCP server automatically`, `Require access token`; Spinner `Port`. |
| AI Features > Permissions | Pane `AI Features.Permissions` | Observed 3.27.1: ComboBoxes `Model metadata:`, `Model data:`, `Best Practice Analyzer:`, `Documents:`, `Macros:`. |

The recent-server pin/unpin context menu is not mapped. On a 175% display, a 3.27.1 process reported window DPI 120 and monitor DPI 168; a `screen-uia-bounds` screenshot showed the database dialog correctly, but native capture and UIA-to-image geometry still require separate verification. Recheck the dropdown before automating it.

## Menus

- Open a menu with one physical click, then confirm it opened before clicking again.
- Popups are separate same-process native windows. Find entries from the popup's handle or use `windows_find` on the editor with `includeOwned: true`.
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

File > Recent Tabular Models is a local-model list, not the recent-server pin control. Opening the database dialog with toolbar `Model from DB...`, inspecting it, and canceling without connecting has been verified on 3.27.1.

## Preferences

1. Open it by physically clicking Tools, then the `Preferences...` Button. Don't Invoke it: the call blocks until the dialog closes.
2. Wait for the `Preferences` window and use its own handle.
3. Search with `searchPreferences`. Typing filters; setting the value doesn't.
4. Select the TreeItem by Value inside `treePreferences`. Names like `Node0` and indexes change after filtering.
5. In a 3.26.3 observation, clearing the search blanked the settings pane even though its category stayed selected (see [KNOWN-ISSUES](KNOWN-ISSUES.md)). After clearing the search on any build, confirm the page's controls before editing; if it is blank, reselect the category. The row may need scrolling back into view.
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
| View > AI Assistant | Live View popup Button `AI Assistant` and Pane `AI Assistant` | `te3_layout reopen-pane` verified live on 3.27.1; check the pane after reopening. |
| View > Properties | Live View popup Button `Properties` (shortcut F4) and Pane `Properties` (2026-10-21) | `te3_layout reopen-pane`, verified live: see Docked panes |
| TOM Explorer > Show changes | Live UIA CheckBox with Toggle pattern; [official behavior](https://docs.tabulareditor.com/en/features/unsaved-changes.html) | `tom-explorer/changes` |
| Properties > Show changes | Live CheckBox `Show changes` in ToolBar `Tools` > Group `Dock Top` > Pane `PropertyGridView`, Toggle and Invoke patterns (2026-10-21) | Observed only |

The MCP Server and Permissions routes inspect settings only. They do not change grants or start the MCP server.

## Docked panes and View menu (3.27.1)

Observed on TE3 3.27.1. Pane bounds are physical pixels and vary with window size and DPI; query them at runtime.

| Area | Selector | Notes |
|---|---|---|
| Main window | Window titled `<database> (Import) - <server> - Tabular Editor 3.27.1 - Enterprise Edition` | Confirm the exact local or disposable model identity before input. |
| Main menu | MenuBar `Main menu` | MenuItems File, Edit, View, Expression Editor (only while that document has focus), Model, Tools, Window, Help. |
| AI Assistant | Pane `AI Assistant` > `AiChatView` | TitleBar `AI Assistant` with Buttons `Close`, `Auto Hide` (no `Maximize`). ToolBar `Tools`: `New conversation`, `Delete conversation`, ComboBox `barConversations`, `Open Custom Instructions Folder`, `AI Assistant Settings`. Chat is WebView2 (`myapp.local/index.html`); message Edit `message-input` and Button `Send` (`send-button`) were found on 3.27.1. |
| TOM Explorer | Pane `TOM Explorer` inside Tab `Tabular Editor 3` | Tabs `TOM Explorer` and `Best Practice Analyzer` share the container. TitleBar Buttons `Close`, `Auto Hide`, `Maximize`. Title reads `TOM Explorer (Changed)` while Show changes is on. Search Edit `searchControl1`. |
| Properties | Pane `Properties` > `PropertyGridView` > Table `propertyGrid` | ToolBar `Tools`: CheckBoxes `Categorized`, `Alphabetical`, `Show changes`, `Property descriptions`. This is the Properties Show changes control. |
| Expression Editor | Document Pane `Expression Editor` > `QuickEditorView` | ToolBar `DAX Script Tools` with ComboBox `Property`. Listed by `te3_layout_read` as kind `document`. |
| Status bar | StatusBar `Status bar` | Buttons `<n> BP issues` and `MCP Stopped`. |
| View menu entries | Buttons in the untitled popup (find with `includeOwned: true`) | `TOM Explorer`, `AI Assistant`, `DAX Package Manager`, `Best Practice Analyzer`, `Messages`, `Data Refresh`, `Expression Editor`, `Macros`, `VertiPaq Analyzer`, `Dependencies`, `DAX Optimizer`, `Calendar Editor`, `Perspective Editor`, `Metadata Translation Editor`, `Properties`; submenu MenuItems `Debug`, `Toolbars`. The popup is not listed by `windows_list_windows`. |

`te3_navigate tom-explorer` now checks whether the tree is visible, selects its existing `TOM Explorer` or `TOM Explorer (Changed)` tab when another tab is active, or reopens a closed pane through the mapped View menu, then reads back the tree. This route is offline-tested but awaits live verification on 3.27.1.

`te3_layout keep` preserves unmapped panes (including debugger panes) and returns them in `preservedUnmapped`; closing an unmapped pane could persist across restarts. Explicit `close` remains available.

Reopening a pane: `te3_layout reopen-pane` was verified live for Properties and AI Assistant on 3.27.1. It opens View, finds the Button named after the pane in the owned popup, and verifies the pane is showing. A visible pane is a no-op. A document title (`Expression Editor`) is refused before input because it is not a dockable pane in the View menu map.

View popup (3.27.1): Button `Properties` shows shortcut F4. Its ancestry is a nameless `Menu` with its own hwnd inside a nameless owned `Window`, so find it with `includeOwned: true`. Escape closes the popup. A `windows_screenshot` of the main window handle includes the open popup; a `fullScreen` capture is refused under the app allowlist.

`te3_layout_read list` gives pane-container bounds relative to the UIA window rectangle, including title bars; content bounds start below them. Use `windows_screenshot` with `locate` for image pixels instead of hardcoded offsets. TE3 remembers its window state, so confirm or set that state before comparing layout geometry. Touching TOM Explorer and Properties panes share an edge; use guarded `te3_layout resize`, not a raw drag.

At 175% scaling in one 3.27.1 live session, `plan-preset` requested 680/480/320 px for AI Assistant/TOM Explorer/Properties. `apply-preset` skipped the first two within 1 px and resized Properties; `te3_layout_read` then reported 679/479/321 px. The window reported DPI 120 while its monitor reported DPI 168; native screenshots showed black padding, so use `locate` or UIA bounds for proof rather than treating native image pixels as UIA coordinates.

For TOM Explorer object navigation, `folder` constrains the requested object to that display folder; it does not speed up the walk. Rows can match role permissions or same-named objects, and the status-bar selection can lag or retain a previous label for folders. The host reads row depth at runtime, corroborates selection with Properties, and reports a bounded miss in its `walk` result. Inspect that result instead of retrying blindly.

A live `Products[Type]` request in `1. Product Hierarchy` missed after a complete `Type` search showed other tables but not that collapsed child; the selection was left at `Customers[Customer Hierarchy]`. The row strategy now tries a table-name search after a complete name-search miss with no candidate. This path has an offline regression and still needs a live retest.

| Dialog (3.27.1) | Selector | Notes |
|---|---|---|
| Open File | owned Window `Open File` from toolbar Button `Open...` | Observed 3.27.1 (2026-10-21): ID `1148` is shared by a Pane, ComboBox `File name:` and Edit `File name:`; keep the Edit constraint. ComboBox `Files of type:` is `1136`. Cancel is Button `Cancel`, ID `2`. |
| Preferences | Window `Preferences` | Observed 3.27.1: Cancel is Button `Cancel` (numeric ID, unstable). All six `preferences/*` destinations arrived with the Preferences window left open between calls. |
| MCP Server | Window `MCP Server`, Pane `McpServerDialog` > `layoutControl` | Observed 3.27.1: Buttons `OK`, `Start server`, `Copy`; SplitButton `Export configuration`; read-only Edit `Server URL:`; permission ComboBoxes `Model metadata:` (Deny/Read/Read-Write), `Model data:` (Deny/Read), `Best Practice Analyzer:`, `Documents:`, `Macros:`. The live server identified itself as `tabular-editor/27.1.0.4`, advertised 24 tools, and returned `modelTitle` in `get_workspace_info`. These are version-specific observations; verify identity before use. |
