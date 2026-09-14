# Open and run a C# script

1. Click `TabItem "Expression Editor"` and send `Ctrl+O`. Get the `Window "Open File"` handle from `windows_list_windows`.
2. Fill `Edit automationId="1148"` with the absolute `.csx` path (a ComboBox shares that ID). Send `Enter` to it with `verifyFocus: true`. Don't use the address bar or `Ctrl+L`; that opened Windows' app chooser.
3. Check the source in `automationId="cSharpEditor1"`. Run only when the task allows it: click `Button "Run script"` with `physical: true`.
4. Output is `automationId="ScriptOutputForm"`: text in `Edit automationId="dataTextBox"`, close with `Button automationId="btnClose"`.

Don't change file associations or dismiss other apps' dialogs.
