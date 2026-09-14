<!-- Rung 1: generic FlaUI MCP usage, no TE3 content. Add only lines that fix failures seen in at least two training trials of the rung below (0c), each with the trial IDs that justify it. -->

<!-- 0c formatting-01, -02, -03: a MenuItem-only find listed just the menu bar, so the agent clicked the open header again and closed the menu. -->
- After clicking a menu header, take a screenshot before clicking again; a second click closes the menu. Menu entries can be Buttons, so find them by name without controlType.

<!-- 0c formatting-01, -02, object-02, -03: find by name missed tree items whose text is in value. -->
- Tree and list items often keep their text in value, not name; find them by value.

<!-- 0c formatting-01, -02, -03: saved images omitted the dialog title bar, yet the agents claimed it was readable. -->
- Save final window captures by handle with background:true so the title bar is included, then check every requirement against the saved image before claiming success.
