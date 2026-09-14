# Preferences: Code Actions

1. Click `MenuItem "Tools"` in `MenuBar "Main menu"` with `physical: true`, then `Button "Preferences..."` with `physical: true`. Don't Invoke it: UIA blocks until the dialog closes. Get the Preferences handle from `windows_list_windows`; if it isn't listed yet, list again.
2. Don't use the search box. In `automationId="treePreferences"`, click a visible `TreeItem` with `physical: true` and send `PageDown` until `TreeItem value="DAX Editor"` is found. Select it and send `Right` to expand it.
3. Click `TreeItem value="Code Actions"` with `physical: true`, not the Pane with the same label, and check `CheckBox "Show code actions"` shows in `automationId="DAX Editor.Code Actions"`.

Don't change settings.
