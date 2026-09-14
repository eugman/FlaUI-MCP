# Select a model object

1. Click `TabItem "TOM Explorer"`. In `automationId="TabularExplorerView"`, clear `searchControl1` and click `Button "Collapse all"`.
2. In `automationId="treeList"`, rows expose names as Value, usually on DataItem. Select `Tables`, press Right on the tree, and select the table.
3. Expand the table and click a fresh ref of the object row with `physical: true`. Don't Invoke rows; it starts inline editing. Measures may sit in display folders such as `Smoke tests`; expand the folder row with Right to reveal them.
4. In `automationId="PropertyGridView"`, check the DataItems `Name`, `Object Type` and `DAX identifier`. The DAX identifier must name the table, e.g. `'Comparison'[Amount]`: a name alone can't tell Sales[Amount] from Comparison[Amount].

Don't edit Properties.
