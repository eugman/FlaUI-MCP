# TE3 Preferences can show a blank content pane

Confirmed by hand in TE3 3.26.3.12966 on 2026-09-14. This is a TE3 defect, and a
report is ready in the `bug-reports` repository at
`te3-desktop/preferences-search-clear-blanks-settings-pane/`.

Clearing the Preferences search box, with its × button or by deleting the text,
blanks the settings pane. It does this even when the category was chosen by
navigating the tree and the search box was already empty; the category stays
highlighted. Automation should never use the search box: navigate the tree to
the category instead. Clicking the category again restores its settings.

A successful interaction test is not visual approval of its screenshot.

## Container clicks can hit child controls

In `instruction-study-20260913-r1/luna-formatting-D-01`, a pane click after
resizing appears to have toggled Always prefix extension columns. This is one
observed incident, not a confirmed TE3 defect. Native-window hit checks establish
window ownership, not the exact UIA child under the point. Do not click arbitrary
pane centers just to focus them; use focus on a known focusable target instead.
The runner/companion click audit found intentional menu, tree, tab, property and
command clicks, not an equivalent focus-only pane click. A dedicated test-app
regression documents the ambiguity; it must be run during a desktop handoff.
