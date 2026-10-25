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
| Preferences > User Interface | ComboBox `Model source detail` | Observed 3.27.1 with Value `Full path`. Preferences UIA can enumerate controls on inactive pages, so first navigate to User Interface and confirm the visible page before changing this value. |

| AI Assistant > AI Provider | Pane `AI Assistant.AI Provider` | Observed 3.27.1: ComboBoxes `Choose provider` and `Model name`. Do not capture or print API key controls. |
| AI Assistant > Preferences | Pane `AI Assistant.Preferences` | Observed 3.27.1: CheckBoxes `Allow AI assistant to run C# scripts directly` and `Preview changes`. |
| AI Features > MCP Server | Pane `AI Features.MCP Server` | Observed 3.27.1: CheckBoxes `Enable MCP Server`, `Start MCP server automatically`, `Require access token`; Spinner `Port`. |
| AI Features > Permissions | Pane `AI Features.Permissions` | Observed 3.27.1: ComboBoxes `Model metadata:`, `Model data:`, `Best Practice Analyzer:`, `Documents:`, `Macros:`. |

On 2026-10-25, the database dialog's Server dropdown exposed a recent server as a `ListItem`. Selecting it sent `SelectionItem.Select`, but the subsequent fallback click was refused because the reported point overlapped the Local instance ComboBox. The host paused further input. Observe `te3_layout_read list`, reconcile the broker's partial operation without replay, and inspect the dialog before continuing. The pin/unpin context menu remains unmapped; this observation does not prove a pin operation. On the 175% left display, this TE3 process reported window DPI 120 and monitor DPI 168. A `screen-uia-bounds` screenshot showed the dialog correctly; native captures and UIA-to-image geometry require separate verification at that scaling.

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

`te3_navigate tom-explorer` now checks whether the tree is visible, selects its existing `TOM Explorer` or `TOM Explorer (Changed)` tab when another tab is active, or reopens a closed pane through the mapped View menu, then reads back the tree. This route is offline-tested but awaits live verification on 3.27.1.

`te3_layout keep` preserves unmapped panes (including debugger panes) and returns them in `preservedUnmapped`; closing an unmapped pane could persist across restarts. Explicit `close` remains available.

Reopening a pane: hide it, click MenuItem `View` with `physical: true`, find Button `<pane title>` with `includeOwned: true`, click it with `physical: true`. Verified for Properties, AI Assistant and TOM Explorer; each came back at its previous bounds.

`te3_layout reopen-pane` verified live on 3.27.1 for Properties (2026-10-21): `hide-pane` took 1 input, `reopen-pane` 3, and Properties came back at its previous bounds (TOM Explorer shrank back to its old height). A second `reopen-pane` on the visible pane sent no input and returned `changed: false`. A document title (`Expression Editor`) is refused before input as a document, not a dockable pane in the View menu map.

View popup (3.27.1): Button `Properties` shows shortcut F4. Its ancestry is a nameless `Menu` with its own hwnd inside a nameless owned `Window`, so find it with `includeOwned: true`. Escape closes the popup. A `windows_screenshot` of the main window handle includes the open popup; a `fullScreen` capture is refused under the app allowlist.

Pane bounds from `te3_layout_read list` (UIA-window-relative): AI Assistant (0,61,1000,1017), Expression Editor document (1000,61,160,1017), TOM Explorer (1160,61,760,590), Properties (1160,651,760,427). `maximize-pane Properties` gave TOM Explorer height 36 and Properties (1160,97,760,981); `restore-pane` returned the original sizes.

Coordinate spaces (Verified, 3.27.1, 2026-10-21, host te3-d276329): with the window placed restored at (0,0,1920,1080), `te3_layout_read list` bounds equal window and PNG pixels: AI Assistant lists at (1,100,...) and its title bar is at PNG y about 103, and the located BPA content (1444,132) sits inside the listed TOM/BPA container (1439,100,480,627). The earlier "+1, +39 restored" offset is stale; maximized offsets were (+9, +39) on an earlier build. TE3 remembers its window state (UiPreferences.json `RememberWindowState`), so it can start restored: call `windows_set_window_state` before relying on maximized offsets. Use `windows_screenshot` with `locate` rather than converting.

Touching panes: the TOM Explorer and Properties containers share an edge (y 651) with no gap. The splitter sits in TOM Explorer's last rows; y 651 is the top of the Properties title bar.

Restored at (0,0,1920,1080) the panes listed as AI Assistant (0,61,638,947), Expression Editor document (638,61,520,947), TOM Explorer (1158,61,760,549) and Properties (1158,610,760,398). TOM Explorer's left edge touches the document with no gap. `apply-preset assistant-model-review` there moved AI Assistant to 680 with a drag 3 px inside AI Assistant. The TOM Explorer drag, 3 px inside the document, left it at 760, and nothing undocked. The host now starts that drag 3 px inside TOM Explorer; see [KNOWN-ISSUES](KNOWN-ISSUES.md).

Pane bounds versus content bounds: `te3_layout_read list` gives the pane container including its title bar, TOM Explorer (1159,100,760,549) in screen pixels with the window at (0,0). The content Pane `TabularExplorer` located in a `strictNative` capture starts below the title bar at (1164,132,751,478), and Edit `searchControl1` at (1164,162,751,26). Pick the rectangle you mean when you crop.

TOM Explorer object walk (SpaceParts, 2026-10-21): column `Brands[Brand]` is in display folder `1. Brand Hierarchy`. One `te3_navigate object` call with `folder: "1. Brand Hierarchy"` reached it in 16.7 s and 13 inputs; one without `folder` stopped on the 18 s clock after 8 rows. The host ignored `folder` at the time, so both calls ran the same walk and the difference was walk speed, not the argument. Since then `folder` is honoured: the object must be inside that folder, and the walk stops once it leaves the table. It does not make the walk faster.

TOM Explorer row depth (Verified, 3.27.1, 2026-10-21): every TreeItem `NodeN` spans the row at the same x; its DataItem `Name row N` (same N, y, height 22 and Value) starts at TreeItem.x + 48 + 24 x depth, model root depth 0 (Tables 1, a table 2, a display folder or a table's column 3, a column in a folder 4, hierarchy levels 5; under Roles a role is 2 and its table permission 3). The status-bar selection slot does not follow a selected display folder (it kept `Column "Type"`); Properties showed ObjectType `Folder`. The 48 and 24 px are the 125% values: the host measures both at runtime from the model root (row 0) and its first child (row 1), and reports depth unknown when they are not in view. The object walk takes each row's depth from its DataItem, its type from the status bar when the slot names that row and otherwise from Properties' ObjectType, collapses top-level groups other than Tables (Left, checked by the selection staying put), and counts a table only under Tables. Not yet re-run live.

| Dialog (3.27.1) | Selector | Notes |
|---|---|---|
| Open File | owned Window `Open File` from toolbar Button `Open...` | Observed 3.27.1 (2026-10-21): ID `1148` is shared by a Pane, ComboBox `File name:` and Edit `File name:`; keep the Edit constraint. ComboBox `Files of type:` is `1136`. Cancel is Button `Cancel`, ID `2`. |
| Preferences | Window `Preferences` | Observed 3.27.1: Cancel is Button `Cancel` (numeric ID, unstable). All six `preferences/*` destinations arrived with the Preferences window left open between calls. |
| MCP Server | Window `MCP Server`, Pane `McpServerDialog` > `layoutControl` | Observed 3.27.1: Buttons `OK`, `Start server`, `Copy`; SplitButton `Export configuration`; read-only Edit `Server URL:`; permission ComboBoxes `Model metadata:` (Deny/Read/Read-Write), `Model data:` (Deny/Read), `Best Practice Analyzer:`, `Documents:`, `Macros:`. Observed only; no permission changed. |

## Verified map, TE3 3.27.1 (2026-10-21)

Every row below was observed live on 3.27.1 Enterprise (SpaceParts, `localhost sp-demo-mcp`, 1920x1200 at 125%, host te3-d276329). Verified: yes.

| Name | ControlType | AutomationId | ClassName | Parent chain | Notes |
|---|---|---|---|---|---|
| `NodeN` (Value = label) | TreeItem | - | - | Tree `treeList` > Pane `TabularExplorerView` | Invoke, Value, SelectionItem; no ExpandCollapse; same x and width on every row. `treeList` has no patterns |
| `Name row N` (Value = label) | DataItem | - | - | TreeItem row N > treeList | x = TreeItem.x + 48 + 24 x depth (model root = depth 0); Invoke and Value only |
| `Vertical` | ScrollBar | (numeric) | WindowsForms10 | treeList | Value = index of the first visible row. Only rows in view are realized |
| `Properties` | Pane | `PropertyGrid` | WindowsForms10 | `panelContainer1` > Main | TitleBar Close, Auto Hide, Maximize |
| category (`Metadata`, `Misc`) | TreeItem | - | - | Table `propertyGrid` | Invoke, SelectionItem |
| property name | Header | - | - | `propertyGrid` > Row Headers | |
| property value (named like the property, e.g. `ObjectType`) | DataItem | - | - | `propertyGrid` > Cells | Value pattern. A display folder shows Name, ObjectType `Folder` and Path |
| `barEditItem1` | ComboBox | - | - | ToolBar `Tools` > Properties | property search box |
| `Status bar` | StatusBar | (numeric) | - | Main | Text `Static` slots (text in Value), Button `<n> BP issues`, Button `MCP Stopped`. The load slot can be blank; the selection slot does not follow a selected display folder |
| `Messages` | Pane | `Messages` | WindowsForms10 | Main | `MessagesView` > Table `MainView` (`gridControl1`); Headers Object, Type, Message, Caused by, Line, Column. Docks at the bottom, 200 px |
| `Macros` | Pane | `ManageMacros` | WindowsForms10 | Main | `ManageMacrosView` > ToolBar `Tools`: Open macro, Edit macro properties, Delete macro. Docks at the bottom only 50 px tall |
| `Best Practice Analyzer` | Pane | `BestPracticeAnalyzer` | WindowsForms10 | Tab `Tabular Editor 3` (shared with TOM Explorer) | `BestPracticeAnalyzerView` > Tree `treeBpa`; row Value `rule (n objects);;sev;category`. ToolBar: Manage rules..., Go to object, Ignore object (Toggle), Generate fix script, Apply fix, Expand all, Collapse all, Show ignored (Toggle), Refresh |
| `Data Refresh` | Pane | `ModelProcessing` | WindowsForms10 | Main | `ModelProcessingView` > treeList; Cancel All, Cancel, Clear completed, Clear queued, Pause (Toggle), Expand all, Collapse all. Bottom, 200 px |
| `VertiPaq Analyzer` | Pane | `VertiPaqAnalyzer` | WindowsForms10 | Main | Buttons Summary, Tables, Columns, Relationships, Partitions, Collect Stats, Import, Export. Bottom, 200 px |
| `Dependencies` | Window (floating) | `DaxDependencies` | WindowsForms10.Window.8 | top-level, owner 0 | Opens floating from View. Not a tool window: style 0x16010000, exstyle WS_EX_APPWINDOW, no WS_EX_TOOLWINDOW, owner 0 (shows in Alt-Tab); listed by windows_list_windows with its own handle. `DaxDependencyView`; RadioButtons Objects that depend on this, Objects on which this depends, Relationships starting from this table; CheckBoxes `chkTrackTomExplorer`, `chkShowInactive` |
| `DAX Package Manager` | Window (document) | `DaxPackageManager` | - | Tab document group | Opens as a document from View. `searchControl`; Browse, Installed, Updates (`checkButton_*`); `checkEdit_ShowPreview`; `gridControl_Packages`; Provider `checkedComboBoxEdit_Providers` |
| `DAX Query 1` | Pane (document) | GUID | - | document group | File > New > New DAX Query. `daxEditor` > `daxscilla`; the menu bar shows `Query` |
| document tab `Close` | Button | - | - | TabItem `<doc>` > Tab | Invoke closes an unmodified document without a prompt |
| Window menu entries | Button / MenuItem | - | - | owned popup Menu | New... (submenu), Float, Pin Tab, Close All, Reset Window Layout, `1 <doc>`, Windows..., Capture Layout, Layouts..., Default layout, Classic layout, Theme (submenu), Default palette, Language (submenu) |
| `Layouts` | Window | `openedWindowsDialog` | WindowsForms10 | owned by Main | Apply Layout, Load Layout..., Remove Layout, Remove All Layouts, Save Layout, Save Layout As..., OK. Rows of list `documentListBox` are not exposed; lists `Default` and `Classic`. Close with TitleBar `Close` (aid `Close`) |
| Preferences > Keyboard | Pane | `Tabular Editor.Keyboard` | - | `pnlSectionContent` | Edit `Show commands containing:`; List of 388 ListItems named `Area.Command`; ComboBox `Shortcuts for selected command:`; Edit `Press shortcut keys:`; Assign shortcut; Remove shortcut; Reset all |
| Preferences tree | TreeItem `Node0..17` | - | - | `treePreferences` | Tabular Editor, Power BI, Proxy Settings, Modeling Operations, Updates and Feedback, User Interface, TOM Explorer, DAX Debugger, Keyboard, Miscellaneous, AI Features, AI Assistant, MCP Server, Permissions, Data Browsing, Pivot Grid, DAX Query, Table Preview (more below the scroll) |
| `MCP Server` | Window | - (Pane `McpServerDialog`) | - | owned | Edit `urlEdit`, Button `urlCopyButton`, permission ComboBoxes, Button `startStopButton` (Start server), SplitButton `exportButton`, Button `Open audit folder`, OK, TitleBar Close. No Cancel and no auto-start control (that is Preferences > AI Features > MCP Server) |
| `Tabular Editor Deployment Wizard` | Window | `DeploymentWizard` | - | owned | `wizardControl1`; page `pg00Welcome` "Tabular Model Deployment"; `< Back`, `Next >`, `Cancel` |
| `Open File` | Window | - | - | owned, from toolbar `Open...` | File name `1148` (ComboBox + Edit; keep the Edit constraint), Files of type `1136`, Cancel |
| Main menu (MSAA) | menu items | hwnd of MenuBar | - | Main | Alt mnemonics: File Alt+F, Edit Alt+E, View Alt+V, Model Alt+M, Tools Alt+O, Window Alt+W, Help Alt+H. The contextual menu (Expression Editor / Query / Table Permission) has none. windows_find does not expose AccessKey |
| View popup items | Button | - | - | owned popup | No mnemonics; the only shortcut shown is F4 on `Properties` |
| File menu shortcuts | - | - | - | File popup | Close Document Ctrl+W, Save Ctrl+S, Save All Ctrl+Shift+S, Exit Alt+F4, New > Model... Ctrl+N. The 3.27.1 File menu has `Reload from server` (not Revert) |

Keyboard commands (Preferences > Keyboard): TOM Explorer Show changes is bindable as `TomExplorer.ToggleChangedOnly`; the Properties grid's Show changes has no command. Also bindable: `Window.CaptureLayout`, `Window.Layouts...`, a `View.*` command per pane (including `View.GitChanges`), `StatusBar.BarStatusMcp`, `Tools.McpServerDetails` and the `TomExplorer.Toggle*` filters. No command exists for Reset Window Layout, Default or Classic layout, or Float. `KeyboardCustomizations` in UiPreferences.json is `{"Custom":{}}`.

Settings files: `%LocalAppData%\TabularEditor3\Layouts.json` (Version 2; `Layouts[]` with Panels Dock/Type/SizeFactor/Visibility/FloatPosition, and Menus; `DaxDependencies` and `FindReplace` are `Dock: Float` by default), `UiPreferences.json` (RememberWindowBounds, RememberWindowState, MainWindowState, MainWindowRectangle, ModelSourceDetail, ShowWhatsNew) and `Preferences.json` (`Mcp`: Enabled, Port 42100, AutoStart false). TE3 rewrites all three when it closes, so a preset applied in a session persists into the next launch. No policies were set (`HKCU`/`HKLM\Software\Policies\Tabular Editor ApS\TE3` absent).

## Host actions verified live on te3-cfa803c (TE3 3.27.1, 2026-10-21)

Verified: yes, unless the row says otherwise.

| Action | What was seen |
|---|---|
| `te3_layout close-document` | `DAX Query 1` closed with 1 input in 2.9 s, read back gone, no dialog. Refused before input for `Properties` (a pane) and for an already closed title (lists the open documents) |
| `te3_layout snapshot` / `restore` | Snapshot hashes matched the live files; after TE3's close rewrote Layouts.json and Preferences.json, restore put all three back and the hashes read back. (Then gated on the grant; now file-only and grant-free, not re-run) |
| `te3_navigate tom-explorer/changes` / `changes-off` | On, then off, 1 input each; the TOM selection drops to "(Nothing selected)" on off |
| `te3_env_check` | Policies not set (user and machine), licence present, Mcp Enabled / Port 42100 / AutoStart false, window state Normal at 0,0,1920,1080, Layouts v2 count 1 |
| `te3_layout_read list` floating | `Dependencies` listed with kind `floating` and its bounds |
| `te3_layout resize` (strict) | TOM Explorer 467 -> 480 in 1 attempt, 15 inputs, 5.3 s |
| `te3_layout apply-preset assistant-model-review` | Second live pass: 45 inputs; TOM Explorer and Properties were already at target (now skipped without input, not re-run) |
| `paused-after-failure` (then spelled `paused_after_failure`) | A failed reopen-pane paused input; te3_navigate was refused with the code; `te3_layout_read list` lifted it |
| Known-prompt guard | No false positive with only the main window open |
| Object walk | Not promoted: see KNOWN-ISSUES "Object walk timing on te3-cfa803c" |

## Host actions verified live on te3-97a4512 (TE3 3.27.1, 2026-10-21)

| Action | What was seen |
|---|---|
| `te3_layout snapshot` / `restore` without the grant | Snapshot "vte3d-te3-97a4512" before te3_interaction_begin: 0.3 s, the reply carried the three hashes (= the live files). After TE3's close rewrote Layouts.json and Preferences.json, restore after te3_interaction_end returned per-file hashes and verified: true, and the files read back as the snapshot |
| `apply-preset` at target | From 663/467: AI Assistant and TOM Explorer 15 inputs each, Properties skipped with 0. A second call at target: all three skipped, 0 inputs, 1.9 s |
| `te3_model_state`, message slot blank | Recognized, with selection and connection read (the caret slot was blank too, so the round-3 "Ln 0 Col 0 INS" case was not reproduced) |
| Object navigation | Budget Rate[Rate] (14.1 s, 12 inputs) and Orders[Order Lines] (10.1 s, 10 inputs) passed with the keyboard walk; Brands[Brand] failed both ways (see KNOWN-ISSUES). The row strategy that replaced the walk as the primary has not been run live |

## Host actions verified live on te3-ea21625 (TE3 3.27.1, 2026-10-21)

| Action | What was seen |
|---|---|
| `te3_layout reopen-pane Dependencies` | completed/ok, 3 inputs, 2.6 s: `visible: true`, `floating: true` |
| Floating-pane rule | The floating Dependencies window (hwnd A511A2) has owner 0 and class `WindowsForms10.Window.8.app.0.1e2bef7_r3_ad1`, as the host's Win32 owner/class rule requires |
| TOM Explorer rows | treeList (a Tree, class WindowsForms10.Window.8.app...) has no patterns and realizes only the ~23 on-screen TreeItems (none offscreen) |
| Properties for a column | 9 rows, no ObjectType row (Name Brand, Folder 1. Brand Hierarchy, Source Column Brand): a column's type comes from the status bar's selection slot (`Column "Brand"`) |
| Object navigation, row strategy | Brands[Brand] with folder: strategy rows, rowsRead 23, indentPx 24, 1 select; the column (row 11 of the filtered tree, Node50 once the search cleared) was selected in 5.1 s and 8 inputs, the status bar read Column "Brand", and the Properties-only confirmation then turned it down (since fixed, not re-run). Not promoted |


## Host actions verified live on te3-eb2ed6a (TE3 3.27.1, 2026-10-22)

Window restored at (0,0,1920,1080) on `localhost/sp-demo-mcp` (TE3 remembered Normal state; no maximize). Verified: yes, unless the row says otherwise.

| Action | What was seen |
|---|---|
| `te3_layout_read list` at launch | AI Assistant (0,61,663,947), Expression Editor document (663,61,738,947), TOM Explorer (1401,61,467,627), Properties (1401,688,467,320) |
| `te3_layout hide-pane` / `reopen-pane Properties` | 1 input, then 3 inputs (View, popup Button `Properties`), 2.2 s each; bounds identical before and after |
| `te3_layout resize TOM Explorer 480` | 467 -> 480 exactly, drag (1404,374)->(1391,374), 15 inputs, 5.2 s; Properties followed to 480 wide, the document shrank 738 -> 725 |
| `te3_layout apply-preset assistant-model-review` | AI Assistant 663 -> 680, drag (660,534)->(677,534), 15 inputs, 5.3 s; TOM Explorer and Properties skipped at target. After: AI Assistant (0,61,680,947), document (680,61,708,947), TOM Explorer (1388,61,480,627), Properties (1388,688,480,320) |
| `te3_layout snapshot` / `restore` | Snapshot before launch, restore after `windows_close` and `te3_interaction_end`: all three hashes read back |
| `te3_navigate expression-editor` | 1 input, 0.4 s; arrival is the TabItem `Expression Editor`; `daxscilla` reads `keyboardFocus: true` afterwards |
| `te3_navigate tom-explorer/changes` / `changes-off` | 1 input each (0.8-1.3 s); with changes on the tree lists only the changed path (root, Tables, table, folder, object) and reads empty when the model is clean; off drops the selection to "(Nothing selected)" |
| `te3_native_preflight` | Connection refused at 127.0.0.1:42100 (server stopped), 2.2 s, no input |
| Object walks | One of seven right (the typed folder miss); see KNOWN-ISSUES "Object walk timing" for the table-row-in-view false negative |
| `windows_mouse drag` on a TOM row | Not promoted: it moved the measure to another table; see KNOWN-ISSUES |

| Name | ControlType | AutomationId | ClassName | Parent chain | Notes |
|---|---|---|---|---|---|
| `daxscilla` | Pane | `daxscilla` | `WindowsForms10.Scintilla.app.0.1e2bef7_r3_ad1` | `DaxEditor` > `QuickEditorView` > `container6` > Window `QuickEditor` "Expression Editor" > Main | No patterns; takes keyboard focus itself (`keyboardFocus: true` after `te3_navigate expression-editor`). Bounds (710,188,679,880) with the preset applied. Not realized while the editor shows `(No DAX properties)`; `DaxEditor`'s name is the expression text |
| `Property` | ComboBox | - | - | ToolBar `DAX Script Tools` > `QuickEditorView` | Value `Expression`; next to it a Text `Static` reading `on Measure [<name>]` with markup |
| `Edit` | MenuItem | - | - | MenuBar `Main menu` | Bounds (70,64,47,29); physical click opens the popup |
| `Undo Move objects` | Button | - | - | nameless Menu (hwnd) inside a nameless owned Window | Edit popup entry, found with `includeOwned: true`; the label names the last edit. Physical click undid the move (2 inputs) |
| TreeItem `NodeN` | TreeItem | - | - | `treeList` | Does not take UIA SetFocus: `windows_send_keys` with a row ref and `verifyFocus` is refused with `focus-not-verified` before input |

TOM Explorer after a search clears (2026-10-22): the selected row is scrolled to the first visible row (its table and folder rows sit above the viewport), so a confirmation that needs the table row in view fails even though the status bar names the object. Properties shows Name and Display Folder for a column and Display Folder for a measure. The host (643bc77) confirms arrival from the status bar and Properties plus the rows read before the clear, and reads the display folder as DataItem `DisplayFolder`, then `Display Folder`. Verified on te3-2ac1444 (2026-10-22): the grid names a column's DataItems after their labels with spaces (`Data Type`, `Description`, `Display Folder`, `Format String`, `Hidden`, `Name`, `Sort By Column`, `Source Column`, `Summarize By`; no ObjectType row for a column), so `Display Folder` is the spelling that matches.

Calculation groups in SpaceParts (read from the DMV, not from the tree): `Z01CG1 - Quantity`, `Z01CG2 - Value (Budget Rate)`, `Z01CG4 - Lines`, `Z02CG1 - Unit`, `Z03CG1 - Sales Target`, `Z03CG2 - Orders Target`, `Z04CG1 - Time Intelligence`, each with columns `Measure` and `Ordinal`. Objects named `Rate` exist in both `Budget Rate` (folder `1. Facts`) and `Exchange Rate`.

## Host actions verified live on te3-2ac1444 (TE3 3.27.1, 2026-10-22, afternoon)

Window restored at (0,0,1920,1080), same layout as the morning. Verified: yes, unless the row says otherwise.

| Action | What was seen |
|---|---|
| `te3_navigate object`, row strategy | Brands[Brand] with folder 10 inputs / 1 select; the same request again 5 inputs / 0 selects; Orders[Order Lines] and Orders[Net Orders (Quantity)] 10 inputs / 1 select each; proof "<object> (<type>) in <table>" with source "TE3 status bar or Properties, read after the walk; the table from the rows in view, else from the rows read before the search cleared" |
| `te3_navigate object`, misses | Typed folder miss ends with no selection; Budget Rate[Rate] and Exchange Rate[Rate] miss with the folder opens named; the calc group column misses naming both searches, no keyboard walk. See KNOWN-ISSUES |
| `te3_layout resize TOM Explorer 480` | 467 -> 480, drag (1404,374)->(1391,374), 15 inputs, 3.4 s, 125% insets |
| `te3_navigate native-mcp-server` + `te3_model_state` | Dialog `MCP Server` open as w2; the status bar read normally through it |
| `windows_find` DataItem in `propertyGrid` | A column's rows are named by label with spaces: `Display Folder` holds the folder |
| `te3_layout snapshot` / `restore` | Snapshot before launch, restore after close and end: verified |
| Activation at launch | The first `te3_navigate` after `windows_launch` was refused `activation-denied` (WindowsTerminal in front) with 1 input (the focus call); a second call right after was refused before any input (`actionDispatched: false`, 0 inputs, no quarantine) |
