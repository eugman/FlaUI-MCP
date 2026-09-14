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
- The Preferences search steps below are only for Auto Formatting and Code Actions; reach any other Preferences page by navigating the tree, without searching.
