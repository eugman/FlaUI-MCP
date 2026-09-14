# Preferences: Auto Formatting

TE3 3.26.3. Requires desktop permission and a ready TE3 window. Change only dialog/search/selection state, not preference values.

1. **Locate:** physically click `MenuItem Name="Tools"` within `MenuBar Name="Main menu"`, then `Button Name="Preferences..."` in its same-process popup. Do not Invoke Preferences: its modal handler can block UIA. Obtain the owned Preferences dialog's own handle from `windows_list_windows`.
2. **Select:** find `Edit automationId="searchPreferences"` under that handle and fill `Auto Formatting`. Within `treePreferences`, physically click a fresh `TreeItem Value="Auto Formatting"` ref; check Selected=true. Do not use `rootOnly:true` for child searches.
3. **Check content:** clear search and reobserve. If the selected row is offscreen, send PageDown to the tree, requerying after each step, up to eight pages; physically reselect the fresh row. Require empty search, the selected row, and visible content within `automationId="DAX Editor.Auto Formatting"`, including `CheckBox Name="Auto format code as you type"`. Selection alone does not establish an active pane. If content stays blank, report the blank-pane issue.
4. **Size:** 1155 × 714 physical pixels at 120 DPI produced complete captures in the reviewed trials. This is a starting point, not a guarantee at other DPI/settings. If resizing is needed, use `windows_place_window`, inspect its observed bounds and keep the dialog within one monitor. Do not change display scaling.
5. **Capture and inspect:** read only the `capture` topic for whole-window capture and the final visual checklist. Do not click a pane center to focus it: that point may belong to a checkbox. Never press OK or Use default formatting settings to make the image match.

Companion alternative: `te3_navigate` destination `preferences/auto-formatting`, then `te3_capture` with that same scene ID. Capture does not resize. Its assessment covers scene identity and file saving, not visual completeness.

Read toggle/value states from compact scoped find results. Defaults belong to the normalized fixture, not arbitrary user sessions. When cleanup is requested, cancel the exact Preferences dialog and require settled absence.
