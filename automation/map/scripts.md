# Open and close a C# script safely

TE3 3.26.3. Script execution requires the task's explicit scope; the companion navigation tools do not execute scripts.

Activate Expression Editor and send Ctrl+O. Discover owned `Window Name="Open File"`, not `Open`. Inside it, resolve **Edit automationId="1148"**; a ComboBox shares that ID, so keep the Edit constraint. Fill that exact control with the absolute `.csx` path, read back its Value, then send Enter to the fresh Edit ref with `verifyFocus:true`.

Do not use Ctrl+L or type paths into the address bar. A previous agent trial produced Windows' external app chooser through file-dialog navigation. Do not change the `.csx` association or dismiss an unrelated chooser automatically. A same-process foreground window is not proof of correct field focus.

Wait for settled Open File absence, then verify the source exposed by parent `cSharpEditor1`; child editor is `daxscilla`. An exact source comparison must not use truncated text. Only execute the supplied script when authorized, using the observed `Button Name="Run script"` physically.

Output window is `automationId="ScriptOutputForm"`; scalar Edit `dataTextBox`; close Button `btnClose`. Verify output, close the exact output, and require settled absence. Close the script document with Ctrl+W only after selecting its correct tab. To prove the document absent, search descendant TabItems: rootOnly=true cannot prove a tab disappeared.

The scripted runner completed repeated cycles historically; later unguided/reference agent cycles failed and were contaminated by an external chooser. These are different results. Recheck dialog cleanliness before a new trial; do not claim the agent workflow is verified from runner evidence.
