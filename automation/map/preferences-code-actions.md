# Preferences: Code Actions

TE3 3.26.3. Requires desktop permission and ready TE3. Do not edit preferences or execute code actions.

Open Tools physically, then the same-process popup `Button Name="Preferences..."` physically. Wait for owned `Window Name="Preferences"`; scope subsequent discovery to it. Fill `Edit automationId="searchPreferences"` with `Code Actions`. Select `TreeItem Value="Code Actions"` in `treePreferences`, not a Pane with the same label.

Clear search, reobserve, and reselect the row. Clearing can scroll it away or blank the pane. PageDown on the exact tree is bounded to eight pages with a fresh lookup after each step. Selection alone is not arrival.

Require empty search, Selected=true on Code Actions, and expected visible controls inside `automationId="DAX Editor.Code Actions"` within dialog bounds:

- CheckBoxes `Show code actions` and `Apply variable casing`; read actual toggle state.
- ComboBoxes `Variable prefix`, `Extension column prefix`, `Preferred variable casing`; read value and enabled state.

Normalized historical fixtures showed On, Off, `_`, `@`, and Default/disabled respectively. Inspect rather than force these values in an attached user session. The original screenshot's tree framing requires additional visual review; a selected section is not proof of a faithful replica.

Do not globally select `General`: several categories expose that same Value at the same UIA depth. For a future General task, verify its unique parent and observed selected first child, plus its section-specific pane; UIA ancestry does not encode the visual hierarchy reliably.

Companion: `te3_navigate(processId:PID, destination:"preferences/code-actions")`; check `assessment.sceneIdentity`, then `te3_capture(processId:PID, scene:"preferences/code-actions", savePath:ABSOLUTE_PNG)`. Capture does not navigate. Use the `capture` topic for geometry and image checks. On requested cleanup, cancel the exact dialog and wait for settled absence. A pending/blocked call must not be replayed blindly.
