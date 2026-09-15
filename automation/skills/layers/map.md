<!-- Condition 4/5 map: automation/skills/te3-notes concatenated as is. -->

# TE3 map

Locators from TE3 3.26.3. Read only the topic for your task, plus `capture` for screenshots.

| Topic | Use for |
|---|---|
| `preferences-auto-formatting` | Preferences > DAX Editor > Auto Formatting |
| `preferences-code-actions` | Preferences > DAX Editor > Code Actions |
| `objects` | Selecting or expanding a table, column or measure |
| `scripts` | Opening and running a C# script |
| `capture` | Saving and checking a screenshot |

- Get handles from `windows_list_windows`. Dialogs have their own handle. A handle (`w1`) is not a ref (`w1e5`).
- `rootOnly: true` searches only the root, never its descendants.
- A selected row doesn't prove its pane is showing. Check the pane's controls.
- After a timeout, observe again before any input. Never replay input blindly.

# Preferences: Auto Formatting

1. Click `MenuItem "Tools"` in `MenuBar "Main menu"` with `physical: true`, then `Button "Preferences..."` with `physical: true`. Don't Invoke it: UIA blocks until the dialog closes. Get the Preferences handle from `windows_list_windows`; if it isn't listed yet, list again.
2. Fill `Edit automationId="searchPreferences"` with `Auto Formatting`. Click `TreeItem value="Auto Formatting"` in `automationId="treePreferences"` with `physical: true`.
3. Clear the search. A TE3 bug blanks the settings pane whenever the search text changes: page down the tree until the row is visible, click it again with `physical: true`, and check `CheckBox "Auto format code as you type"` shows in `automationId="DAX Editor.Auto Formatting"`.
4. Size the dialog to 1155 × 714 with `windows_place_window`.

Don't change settings. Don't click a pane's center to focus it; it can toggle a checkbox.

# Preferences: Code Actions

1. Click `MenuItem "Tools"` in `MenuBar "Main menu"` with `physical: true`, then `Button "Preferences..."` with `physical: true`. Don't Invoke it: UIA blocks until the dialog closes. Get the Preferences handle from `windows_list_windows`; if it isn't listed yet, list again.
2. Fill `Edit automationId="searchPreferences"` with `Code Actions`. Click `TreeItem value="Code Actions"` in `automationId="treePreferences"` with `physical: true`, not the Pane with the same label.
3. Clear the search. A TE3 bug blanks the settings pane whenever the search text changes: page down the tree until the row is visible, click it again with `physical: true`, and check `CheckBox "Show code actions"` shows in `automationId="DAX Editor.Code Actions"`.

Don't change settings.

# Select a model object

1. Click `TabItem "TOM Explorer"`. In `automationId="TabularExplorerView"`, clear `searchControl1` if it has text.
2. In `automationId="treeList"`, rows expose names as Value, usually on DataItem. Find the row with `windows_find` by value. If it isn't there, expand its table or display folder (such as `Smoke tests`) by sending Right to that row, then find again.
3. Click the row with `physical: true`. Don't Invoke rows; it starts inline editing.
4. In `automationId="PropertyGridView"`, check the DataItems `Name`, `Object Type` and `DAX identifier`. The DAX identifier must name the table, e.g. `'Comparison'[Amount]`: a name alone can't tell Sales[Amount] from Comparison[Amount].

Don't edit Properties.

# Open and run a C# script

1. Click `TabItem "Expression Editor"` and send `Ctrl+O`. Get the `Window "Open File"` handle from `windows_list_windows`.
2. Fill `Edit automationId="1148"` with the absolute `.csx` path (a ComboBox shares that ID). Send `Enter` to it with `verifyFocus: true`. Don't use the address bar or `Ctrl+L`; that opened Windows' app chooser.
3. Check the source in `automationId="cSharpEditor1"`. Run only when the task allows it: click `Button "Run script"` with `physical: true`.
4. Output is `automationId="ScriptOutputForm"`: text in `Edit automationId="dataTextBox"`, close with `Button automationId="btnClose"`.

Don't change file associations or dismiss other apps' dialogs.

# Capture and check

1. Save the dialog or main window once by handle with `background: true`, an absolute `savePath` and `includeImage: true`. Plain capture can omit the title bar.
2. Look at the returned image. A successful tool result doesn't mean the screenshot is complete.
3. Check the task's required content is visible and unclipped. For Auto Formatting: title and OK/Cancel readable, Auto Formatting selected, search empty, and every control visible including `Use default formatting settings`.
4. If clipped, resize the window, capture to a new path, and look again. Don't change settings or display scaling to fix framing.
