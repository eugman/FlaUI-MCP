# Preferences: Code Actions

1. Click `MenuItem "Tools"` in `MenuBar "Main menu"` with `physical: true`, then `Button "Preferences..."` with `physical: true`. Don't Invoke it: UIA blocks until the dialog closes. Get the Preferences handle from `windows_list_windows`; if it isn't listed yet, list again.
2. Fill `Edit automationId="searchPreferences"` with `Code Actions`. Click `TreeItem value="Code Actions"` in `automationId="treePreferences"` with `physical: true`, not the Pane with the same label.
3. Clear the search. A TE3 bug blanks the settings pane whenever the search text changes: page down the tree until the row is visible, click it again with `physical: true`, and check `CheckBox "Show code actions"` shows in `automationId="DAX Editor.Code Actions"`.

Don't change settings.
