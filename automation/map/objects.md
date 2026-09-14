# Qualified model-object navigation

TE3 3.26.3. Requires a loaded model and desktop permission; no model edits or saves.

Activate TabItem `TOM Explorer`. Scope to `TabularExplorerView`: clear `searchControl1`, use `Button Name="Collapse all"`. The object tree is `treeList`; rows expose object names through Value, usually as DataItem, not Name. Some filtered provider views expose TreeItems: inspect narrowly if the expected row type is absent.

Select `Tables`, expand with Right targeted to the tree, select the exact table, and verify its Properties Name before expanding it. Expand only the needed display-folder path, then physically select the fresh exact object row. Do not use an unqualified global object-name lookup while multiple tables are expanded. Invoke can enter inline editing.

In `PropertyGridView`, read DataItem rows named `Name`, `Object Type`, and `DAX identifier`. A column called Amount alone cannot distinguish Sales from Comparison. Require the requested type and table-qualified DAX identifier, e.g. `'Sales'[Amount]`; for Table require its exact name/type. Missing, ambiguous, or unreadable identity evidence is not successful navigation. Do not edit the Properties grid to make identity assertions pass.

Alternatively, use `searchControl1` to find the object name, inspect the matching rows under their table parents, and physically select the requested one. Keep the same qualified Properties identity check; a matching name alone is insufficient. Clear the search before capture only when the requested scene requires an unfiltered tree.

Companion example: `te3_navigate(processId:PID, destination:"object", table:"Comparison", objectName:"Amount", objectType:"Column")`; check `assessment.sceneIdentity`, then `te3_capture(processId:PID, scene:"object", table:"Comparison", objectName:"Amount", objectType:"Column", savePath:ABSOLUTE_PNG)`. Capture does not select the object. Optional `folder` uses backslash-separated segments. Exact qualified column identity was verified live on TE3 3.26.3; fail rather than infer when property evidence is unavailable.

For tests, navigate to Sales/Amount, Comparison/Amount, and Sales/Smoke tests/Total Amount in the prepared fixture. Screenshot must visibly show the selected object. Use `capture` guidance; no saving is needed.
