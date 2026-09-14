<!-- Rung 1: generic FlaUI MCP usage, no TE3 content. Add only lines that fix failures seen in at least two training trials of the rung below (0c), each with the trial IDs that justify it. -->

<!-- 0c formatting-01, -02, -03: a MenuItem-only find listed just the menu bar, so the agent clicked the open header again and closed the menu.
     Reworded after rung 1: script-01 confirmed menus with background captures, which omit popups, and re-clicked File eight times. -->
- Open a menu with one click, then confirm it with a plain screenshot or windows_find with includeOwned:true before clicking again; match entries by name only, since they may be Buttons.

<!-- 0c formatting-01, -02, -03: saved images omitted the dialog title bar, yet the agents claimed it was readable.
     Reworded after rung 1: intermediate background captures hid menus (script-01) and captured the main window instead of the dialog (code-actions-01, -02). -->
- Use background:true only for the final saved capture, on the handle of the window that holds the requirements (dialogs have their own handle in windows_list_windows), saving it once with includeImage:true and checking that image before claiming success, except for an open menu, which background captures leave out: capture menus with a plain screenshot.

<!-- Dropped after rung 1: "find tree and list items by value". The windows_find no-match hint already covered it, and agents misapplied it to menu entries (formatting-01, code-actions-02, -03). -->
