# Preferences: Auto Formatting

1. Physically click `MenuItem "Tools"` in `MenuBar "Main menu"`, then `Button "Preferences..."`. Don't Invoke it: UIA blocks until the dialog closes. Get the Preferences handle from `windows_list_windows`.
2. Fill `Edit automationId="searchPreferences"` with `Auto Formatting`. Physically click `TreeItem value="Auto Formatting"` in `automationId="treePreferences"`.
3. Clear the search. This blanks the pane: page down the tree until the row is visible, physically click a fresh ref of it, and check `CheckBox "Auto format code as you type"` shows in `automationId="DAX Editor.Auto Formatting"`.
4. Size the dialog to 1155 × 714 with `windows_place_window`.

Don't change settings. Don't click a pane's center to focus it; it can toggle a checkbox.
