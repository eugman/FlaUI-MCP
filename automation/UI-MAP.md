# TE3 3.26.3 locator notes

Agent guidance is built as skill layers by [the ladder study](experiments/LADDER.md),
using author notes in [skills/te3-notes](skills/te3-notes/start.md). The notes below are
historical runner observations. Live status is in [VERIFICATION.md](VERIFICATION.md).

| Area | Locator | Notes |
|---|---|---|
| Main menu | MenuBar Name Main menu | MenuItem Window; Default layout Button |
| TOM Explorer | treeList | DataItem Value is object name; qualify table |
| New measure | Edit Editing control, Value New Measure | Alt+1 then Value/Enter |
| Expression | DaxEditor / daxscilla within QuickEditorView | Parent Name exposes source |
| Query | daxEditor / daxscilla | Case matters |
| Query result | DataItem Name [Value] row 1 | One-cell Value |
| Properties | PropertyGridView | Named DataItems and editing child |
| Messages | MessagesView | Error cells expose full diagnostic |
| C# source/input | cSharpEditor1 / daxscilla | Load file, assert parent text |
| Open File | owned Window Name Open File | Root-only; not Open |
| Filename | Edit ID 1148 in Open File | ComboBox shares ID; retain Edit constraint |
| Output | ScriptOutputForm | dataTextBox and btnClose |
| Preview | ScriptPreviewDialog | diffTreeList, okButton, revertButton |
| Auto-rollback | CheckBox Name Auto-rollback | Toggle pattern; assert On, do not blindly click |
| BPA | treeBpa / BestPracticeAnalyzerView | Refresh; exact rule Value |

Minimized windows expose incomplete UIA trees. TOM grid Invoke may enter inline
edit; select via fresh bounds then verify identity. Owned popups require explicit
same-process discovery. Presence does not prove selection, and truncated text
does not prove exact equality.

Preview rows expose compound Values such as Sales;;;Sales; and
description;before;;description;after. They lack ExpandCollapse; the observed
two-Right-key sequence expands selected rows.

Cold startup menu discovery can exceed five seconds; the last verified profile
used a 15-second bounded startup lookup. Background owner capture excludes owned
dialogs; component images must not be claimed as a full-scene original replica.

## Model menu (TE3 3.26.3, offline)

Main menu MenuBar > Model MenuItem supports Invoke. Place the pointer on this
observed menu-bar item before Invoke; keeping it inside the popup creates tooltips.
The popup is a same-process non-activating native window. `Home` selects Deploy;
each `Down` must assert the next exact item's HasKeyboardFocus before continuing:
Serialization options..., Import tables..., Script DAX, Refresh model, Add Table,
Add Calculated Table, Add Calculation Group. Update schema (all tables)... is
disabled offline and skipped. Refresh model is a MenuItem; other entries are Buttons.
The current wording is **Add**, not the original screenshot's **New**.

Do not use UIA Focus on these popup items: it returned without selecting them.
Mouse hover sets HasKeyboardFocus but creates a tooltip; moving away clears it.
The verified keyboard path produces the highlight without tooltips. Escape closes
the popup; persisted model metadata remains unchanged. Capture guards allow only
the exact same-process untitled 7-pixel popup shadow, not arbitrary overlapping windows.
Each run saves the scoped native-window/element map alongside the PNG.

## Verified dialog lifecycle

Language choices: select User Interface, scope to `Tabular Editor.User Interface`,
ComboBox `User interface language`; expect English. Alt+Down opens a separate
native popup containing one List and six ListItems. Current captions:
English, Deutsch (Beta), Español (Preview), Français (Beta), 中文(简体) (Preview),
日本語 (Beta). English alone selected. The list's shadow is 10px (menus use 7px).
Capture the actual Preferences/popup union, not only the background dialog;
Escape then verify English unchanged. Recipe: `preferences-language-choices`.

Preferences: physically click Tools on Main menu, then physically click the observed
Preferences... Button using its same-process popup HWND. Invoke on Preferences...
kept its handler running; do not replay it. Wait for native title Preferences and
input idle before UIA discovery. The window contains pane PreferencesDialog,
treePreferences and searchPreferences. Cancel is scoped to that exact root window;
then require main-window readiness and settled Preferences absence.

TreeItem names are Node0/Node1 etc.; select by **Value**, scoped to treePreferences.
Names/indexes change after filtering. Search Auto Formatting produces 10 visible
nodes; an unfiltered visible viewport had 45. Whole-dialog maps can truncate and
are diagnostics only. Prefer selected-pane scope: DAX Editor.Auto Formatting
produced a complete 62-node map. Numeric HWND-like IDs on controls are unstable;
use labels + control type (Spinner also requires Value pattern to avoid its child).

Clearing search hides the selected row outside the accessible viewport. ScrollItem
was not usable. Reobserve visible rows and send PageDown to the exact tree, bounded
to eight pages, then physically click a fresh target and verify SelectionItem.IsSelected.
Clearing can also blank the content, even with a selected row reported by UIA.
Verify the expected pane's visible controls, not just selection. See
[KNOWN-ISSUES.md](KNOWN-ISSUES.md) for manual reproduction and recovery.
Auto Formatting uses a 1155x714 dialog; await resize idle, verify native bounds and
monitor containment, and enforce dimensions again on the captured PNG. Its 14
checkboxes and four numeric/casing values are read-only assertions; never click
Use default formatting settings or OK to force a screenshot match.

The saved Code Actions tree map contains multiple General rows (Text Editors,
DAX Editor, File Formats, Model Deployment, DAX Optimizer), all at UIA depth 2.
Visual indentation is not represented as parent/child UIA nesting.
SelectPreferencesFirstChild selects the unique parent, sends Right (then Down if
still on the parent), and requires a fresh complete tree observation with exactly
one parent and one selected child immediately after it. DAX General also asserts
the DAX Editor.General pane before checking settings. Do not use global Value
General or assume an ancestor scope will distinguish these rows. Three unit tests
cover duplicate General labels, wrong parents and ambiguous/incomplete selection.

File Formats shows its General pane when the parent is selected; no child click
is needed for L064. Scope controls to File Formats.General. Current compatibility
is disabled 1600 (Azure Analysis Services / SQL Server 2022+), not the original's
1500. Include sensitive is an added unchecked toggle. These are display assertions
only: never enable serialization or workspace operations to capture this pane.
For its two arrows, require the selected parent followed by visible General and
Save-to-folder rows, then verify the intended arrow tips against row bounds before
and after capture. Use native window bounds as the origin: Preferences UIA bounds
exclude the title bar/border, whereas the PrintWindow PNG includes them.

Save-to-folder uses pane File Formats.Save-to-folder. Current Serialization mode
ComboBox defaults to Database.json (default), disabling the checkbox/tree controls.
Alt+Down opens a native popup with ListItems Database.json (default),
Database.json (customizable), and TMDL. Map from the popup HWND, not the entire
editor: a broad traversal can consume its budget before reaching owned popups.
The popup has six accessible nodes; separate untitled shadow windows have one.
The original's Use recommended settings checkbox is absent in this version.
Escape closes the verified default-mode popup; Down on the still-default combo
selects Database.json (customizable), whose value is rechecked before staging.
Enabling the first three serialization options removes the separate Perspectives,
Relationships and Translations depth rows, matching the old tree structure.
Tree rows expose checked state through LegacyIAccessible.State bit 0x10
(STATE_SYSTEM_CHECKED), not UIA Toggle. Select Tables then Right to reveal Columns;
reselect Data Sources and verify its selection plus all six original checked rows.
Cancel discards the staged options. At 809x437 the current pane scrolls and hides
the depth tree; 1155x714 is used for a readable version-update attempt instead.

Code Actions reuses the same search/clear/reselect path. Scope controls to
DAX Editor.Code Actions: Show code actions (Toggle On), Apply variable casing
(Toggle Off), Variable prefix (ComboBox Value underscore), Extension column prefix
(ComboBox Value @), Preferred variable casing (ComboBox Value Default, IsEnabled
false). Assert the visible tree starts with Pivot Grid and includes Model Deployment.
Current layout also shows lower categories; the historical bottom edge is not
reproduced. This is a documented fidelity gap, not permission to crop out controls
or fabricate the old tree. Cancel after capture; no code action is executed.

File loading: select Expression Editor, Ctrl+O, resolve the owned Open File
window, fill Edit 1148, Enter, wait for settled dialog absence, assert exact
cSharpEditor1 source. Running and closing output then closing the script tab
passed three cycles in `fla_20260913_003611_b830233e` without database access.
The three output captures are byte-identical; persisted model metadata is unchanged.

A dialog may disappear between native window enumeration and UIA FromHandle or
property access. Absence polling retries only ElementNotAvailableException and
COM 0x80040201, resets settlement, and re-enumerates roots. It never interprets a
provider failure as successful absence. Other errors and hung reads still fail.

## Discovery on the SpaceParts fixture (TE3 3.26.3, 2026-09-15)

Maps from the `discovery-backlog` run (`artifacts/te3/fla_20260915_104919_ab1414b6`).

- **Main menu:** File, Edit, View, Expression Editor, Model, Tools, Window, Help, plus an unnamed-looking `Item` MenuItem.
  Popup entries are Buttons; only submenu parents are MenuItems.
- **File:** New and Open (MenuItems), Revert, Close Document, Close Model, Save, Save As..., Save to Folder..., Save All,
  Recent Files and Recent Tabular Models (MenuItems), Exit. A physical click on New inside its popup opens the submenu:
  Model..., New DAX Query, New Pivot Grid, New DAX Script, New Diagram, New C# Script.
- **Edit:** Undo, Redo, Find..., Replace..., Cut, Copy, Paste, Delete, Select All, Code Assist (MenuItem), Word Wrap (CheckBox).
- **View:** TOM Explorer, AI Assistant, DAX Package Manager, Best Practice Analyzer, Messages, Data Refresh, Expression Editor,
  Macros, VertiPaq Analyzer, Dependencies, DAX Optimizer, Calendar Editor, Perspective Editor, Metadata Translation Editor,
  Debug and Toolbars (MenuItems), Properties.
- **Model:** Deploy..., Serialization options..., Import tables..., Update schema (all tables)..., Script DAX, Refresh model,
  Add Table, Add Calculated Table, Add Calculation Group, Add Data Source (Legacy), Add Data Source (Power Query),
  Add Perspective, Add User-Defined Function, Add Shared Expression, Add Relationship, Add Role, Add Translation.
  On this fixture the enabled order differs from the small fixture's walk in `CaptureCalculationGroupMenu`.
- **Tools:** Customize..., Preferences..., Manage BPA rules... (lowercase r).
- **Window:** New... (MenuItem), Float, Pin Tab, New Horizontal/Vertical Tab Group, Close All, Reset Window Layout, numbered
  documents, Windows..., Capture Layout, Layouts..., Default layout, Classic layout, Theme (MenuItem), Default palette,
  Language (MenuItem).
- **Help:** Online Documentation, Onboarding Guide, Community Support, Dedicated Support, What's New, About Tabular Editor.
  About shows the licence holder, installations and session ID; redact before promotion.
- **Dialogs:** File > New > Model... opens "New Model" (Model Name Edit, Compatibility Level ComboBox, Use workspace database
  CheckBox, Direct Lake guidance link, OK/Cancel); Escape closes it. Help > About Tabular Editor opens "About Tabular Editor 3".
  "Model from DB..." matched more than one control from the unscoped lookup; scope it to the Open submenu popup.
- **TOM Explorer:** top-level rows are folders (Data Sources, Functions, Perspectives, Relationships, Roles, Shared Expressions,
  Tables); tables sit under the collapsed Tables folder, so a table row is only found after expanding Tables.
  Shift+F10 on a folder opens a context menu with Create (MenuItem, a cascade) and Properties; Data Sources adds Import tables....
- **Expression Editor:** a Property ComboBox (value `Expression`) selects which DAX property is edited.
- **Preferences search:** AI Provider, Keyboard, Proxy Settings and TOM Explorer each return their section; "Compiler" and
  "Features" return nothing.
- A plain (non-background) `windows_screenshot` of the main window includes an open popup menu; background captures do not.

Round 2 (`fla_20260915_111556_d7a3fd70`), using the recipe helpers:

- **File > Open:** Model from File..., Model from DB..., Model from Folder..., File..., Import from Metric View YAML....
  `OpenMenuPath` resolved "Model from DB..." uniquely by searching only the Open popup.
- **Window > Language:** CheckBoxes English, Deutsch (Beta), Español (Preview), Français (Beta), 中文(简体) (Preview),
  日本語 (Beta). **Window > Theme:** Basic, Bezier, Blue, Dark, Light.
- **Edit > Code Assist:** Peek Definition, Go to definition, Define Measure, Inline Measure, Format DAX, Comment Code,
  Uncomment Code, Comment/Uncomment, Refactor, Show Calltip, Autocomplete, Show Code Actions.
- **View > Toolbars:** Tools, Status bar, DAX Query, DAX Script, DAX Expression, C# Script, Pivot Grid, Table Preview, Debug,
  Perspective Editor, Metadata Translation Editor, Customize ....
- **Roles context menu > Create:** Role. Shift+F10 on the Roles row opens the menu; the Create cascade opens with a physical click.
- **Load Semantic Model from Database:** Server and Local instance ComboBoxes; Integrated, Microsoft Entra MFA and Username
  and password RadioButtons; Username/Password Edits; Advanced options with Mode (Read/Write) and Status bar color; OK/Cancel.
  Escape closes it.
- **Layouts:** a Name/Path grid (Default, Classic) with Apply Layout, Load Layout..., Remove Layout, Remove All Layouts,
  Save Layout, Save Layout As... and OK, and no Cancel. **Escape does not close it** (the grid takes it); use the
  title-bar Close button. `CancelDialog` now falls back to Cancel, then Close, and never presses OK.
- **TOM Explorer rows:** after expanding Tables, rows below the viewport (Invoices) cannot be clicked physically, and a
  collapsed-then-expanded tree pushes top-level folders such as Roles out of view. Searching "Invoices" shows both the
  table and its partition with the same value. The helpers now take the first match in tree order, call ScrollIntoView
  when the row supports ScrollItem, and collapse the tree before walking a path.
- **Invoices on the offline fixture:** Properties show Incremental Refresh Enabled True and Refresh Policy
  "5 year rolling, 10 day incremental"; the Expression Editor offers "Default Detail Rows Expression" on the table.

## Original scalar-output image: remaining fidelity gap

Original `c-sharp-script-output-function.png` is 704x361, including a source strip
and a compact output dialog. `csharp-scalar-scene` now captures that arrangement
with exactly `"Hello World".Output();` and a 555x254 dialog. It observes the owner
and dialog bounds, excludes invisible maximized borders, and checks foreground,
stable geometry and overlapping windows before/after actual screen capture.
After placement, allow 500 ms for the DevExpress resize repaint; unchanged native
bounds alone did not prove stable pixels in the first repetition experiment.
TE3's untitled same-process shadow has the observed dialog bounds inflated by 10.
Unknown overlapping windows fail the capture. Geometry tests include negative
monitor coordinates. Current tab is C# Script 3.csx, not C# Script 3*, styling
differs, and the checkbox label clips even at dialog width 580. This is a strong
attempted replica with a remaining visual defect, not an approved replacement.

Auto-rollback source recipe passed with checkbox Toggle On, exact 21-line source,
and unchanged persisted model. Optional What's New tab can be closed by selecting
that exact TabItem, Ctrl+W, then a settled absence check. The refined original-sized
crop and red-outline geometry are pinned in auto-rollback.composition.json; assert
the source image dimensions instead of rescaling when display geometry changes.

Offline preview, scalar output, object properties and two-row string output all
passed in the eight-recipe batch listed in VERIFICATION.md. The current list output
shows Alpha/Beta without a Type column. Do not infer that historical column layouts
can be reproduced merely because the underlying rows are correct.
