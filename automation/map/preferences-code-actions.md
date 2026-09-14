# Preferences: Code Actions

1. Physically click `MenuItem "Tools"` in `MenuBar "Main menu"`, then `Button "Preferences..."`. Don't Invoke it: UIA blocks until the dialog closes. Get the Preferences handle from `windows_list_windows`.
2. Fill `Edit automationId="searchPreferences"` with `Code Actions`. Physically click `TreeItem value="Code Actions"` in `automationId="treePreferences"`, not the Pane with the same label.
3. Clear the search. This blanks the pane: page down the tree until the row is visible, physically click a fresh ref of it, and check `CheckBox "Show code actions"` shows in `automationId="DAX Editor.Code Actions"`.

Don't change settings.
