# TE3 Preferences can show a blank content pane

Observed in TE3 3.26.3. The exact application/provider cause is unconfirmed.
Clearing Preferences search can leave its content blank even while UIA reports
a selected category. Both native background and actual screen captures showed
the issue; both Value-pattern clearing and keyboard clearing reproduced it.

Manual reproduction:

1. Physically click Tools > Preferences. Avoid Invoke: its modal handler blocks UIA.
2. Search for Code Actions and physically select the matching category.
3. Confirm its controls are visible, then clear search with Ctrl+A, Backspace.
4. Inspect the actual pane, not merely the selected-row accessibility property.
5. If blank, search again and physically reselect the category; close/reopen if needed.
6. Cancel to avoid persisting preference changes.

A successful interaction test is not visual approval of its screenshot.
