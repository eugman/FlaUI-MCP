# Preferences: Auto Formatting

1. Click `MenuItem "Tools"` in `MenuBar "Main menu"` with `physical: true`, then `Button "Preferences..."` with `physical: true`. Don't Invoke it: UIA blocks until the dialog closes. Get the Preferences handle from `windows_list_windows`; if it isn't listed yet, list again.
2. Don't use the search box. In `automationId="treePreferences"`, click a visible `TreeItem` with `physical: true` and send `PageDown` until `TreeItem value="DAX Editor"` is found. Select it and send `Right` to expand it.
3. Click `TreeItem value="Auto Formatting"` with `physical: true`, and check `CheckBox "Auto format code as you type"` shows in `automationId="DAX Editor.Auto Formatting"`.
4. Size the dialog to 1155 × 714 with `windows_place_window`.

Don't change settings. Don't click a pane's center to focus it; it can toggle a checkbox.
