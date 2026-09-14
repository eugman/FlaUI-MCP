# Preferences: Auto Formatting

1. Click `MenuItem "Tools"` in `MenuBar "Main menu"` with `physical: true`, then `Button "Preferences..."` with `physical: true`. Don't Invoke it: UIA blocks until the dialog closes. Get the Preferences handle from `windows_list_windows`; if it isn't listed yet, list again.
2. Fill `Edit automationId="searchPreferences"` with `Auto Formatting`. Click `TreeItem value="Auto Formatting"` in `automationId="treePreferences"` with `physical: true`.
3. Clear the search. A TE3 bug blanks the settings pane whenever the search text changes: page down the tree until the row is visible, click it again with `physical: true`, and check `CheckBox "Auto format code as you type"` shows in `automationId="DAX Editor.Auto Formatting"`.
4. Size the dialog to 1155 × 714 with `windows_place_window`.

Don't change settings. Don't click a pane's center to focus it; it can toggle a checkbox.
