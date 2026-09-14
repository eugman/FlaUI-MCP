# TE3 agent quickstart

Observed on TE3 3.26.3. This is a locator/operation index, not a stored UI snapshot.
Use fresh window handles and refs; never reuse identifiers from saved run maps.

## Companion and guidance

`FlaUI.Automation mcp` adds `te3_navigate`. It attaches by
explicit TE3 PID and never launches, resets preferences or closes TE3. Its handles are
private to that server; don't pass generic-server refs into companion calls. See
[COMPANION.md](COMPANION.md); live status is in [VERIFICATION.md](VERIFICATION.md).

Agent guidance is built as skill layers by [the ladder study](experiments/LADDER.md).
Author notes for those layers are in [skills/te3-notes](skills/te3-notes/start.md).

## Choose the cheapest adequate observation

1. Known screenshot task: find its typed recipe in [RECIPES.md](RECIPES.md).
   Run one recipe, then read its final manifest. Inspect PNGs for visual fidelity.
2. Interactive task: use the relevant selector below with `windows_find`.
   Request a few results in a narrow scope; inspect value/selection/toggle/focus.
   Pattern support is not current state; null means unknown. Request bounds only
   for geometry checks. Use `windows_wait` for settled assertions.
3. Unexpected result: inspect that subtree or owned popup, then widen if needed.
   Ambiguity, truncation and unreadable nodes are not successful assertions.
4. Use a screenshot for geometry, visual quality or inaccessible UI—not after
   every successful text/selection check. Save artifact-only captures with
   `includeImage:false`; load the image only when visual inspection is needed.

| Need | Selector / scope |
|---|---|
| Main menu | Name `Main menu`, MenuBar; named MenuItem beneath it |
| Preferences | Name `Preferences`, Window, rootOnly; includeOwned |
| Preferences search | ID `searchPreferences`, Edit, within Preferences |
| Preferences tree | ID `treePreferences`; TreeItem Value is the label |
| Object tree | ID `treeList` within `TabularExplorerView`; DataItem Value; qualify table |
| C# output | ID `ScriptOutputForm`, rootOnly, includeOwned |
| Preview | ID `ScriptPreviewDialog`, rootOnly, includeOwned |

Refresh `windows_list_windows` after opening Preferences and use its own handle,
not the main editor handle. Example input to `windows_find`:

```json
{"handle":"PREFERENCES_HANDLE","selector":{"automationId":"searchPreferences","controlType":"Edit"},"maxResults":2,"maxNodes":300}
```

The selector budget bounds traversal, not a hung UIA provider. A timeout leaves
the action outcome uncertain: reobserve, never blindly replay input.

## Side-automation boundary

`FlaUI.Automation run automation/offline.config.json --scenario RECIPE` launches
its own TE3 instance, stages a fresh local model and restores preferences afterward.
It is **not** an attach-to-my-open-session command. Require desktop permission;
do not use it while the user's TE3 is open. No-server tasks must stay in offline mode.
Do not add server fixtures or promote images without the relevant authorization.

Read only the matching section of [UI-MAP.md](UI-MAP.md) for deeper interactions.
Repeated General labels require parent-to-child verification; UIA nesting does
not represent the Preferences hierarchy. Popup discovery should start at its
native window rather than traverse the entire editor. Background dialog capture
does not include external dropdowns. Presence alone does not prove selection.

Preferences pitfall: open the `Preferences...` popup button with `physical:true`.
Clearing search can blank the content and move the chosen row offscreen. Re-find
the row in the tree (bounded PageDown if necessary), physically click its fresh
ref, and verify visible controls in the expected pane. A selected row alone is
not proof that its settings are displayed. See [known issue and manual reproduction](KNOWN-ISSUES.md).

After a crash, check process ownership and the latest manifest. If settings were
not restored, retain the reported backup and resolve restoration before another run. Initial needsRecovery=false
does not prove cleanup happened. Successful automation is not docs-image approval.
