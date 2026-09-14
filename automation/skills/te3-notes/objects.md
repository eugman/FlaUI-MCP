# Select a model object

1. Click `TabItem "TOM Explorer"`. In `automationId="TabularExplorerView"`, clear `searchControl1` if it has text.
2. In `automationId="treeList"`, rows expose names as Value, usually on DataItem. Find the row with `windows_find` by value. If it isn't there, expand its table or display folder (such as `Smoke tests`) by sending Right to that row, then find again.
3. Click the row with `physical: true`. Don't Invoke rows; it starts inline editing.
4. In `automationId="PropertyGridView"`, check the DataItems `Name`, `Object Type` and `DAX identifier`. The DAX identifier must name the table, e.g. `'Comparison'[Amount]`: a name alone can't tell Sales[Amount] from Comparison[Amount].

Don't edit Properties.
