using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

public sealed partial class Te3Page
{
    public async Task OpenCSharpScriptFile(string file, string script)
    {
        // Leave transient property editors before invoking an application command.
        await WaitForMainWindowReady();
        await Click(ExpressionTabTarget());
        await Keys("Ctrl+O");
        await Expect(OpenFileDialogTarget());
        await Fill(OpenFileNameTarget(), file);
        await Keys("Enter", OpenFileNameTarget());
        await Absent(OpenFileDialogTarget());
        await Expect(CsharpTextTarget(), script);
    }

    public async Task CloseCSharpDocument()
    {
        await Keys("Ctrl+W", CsharpInputTarget());
        await Absent(CsharpTextTarget());
    }

    public async Task CloseOptionalDocument(string title)
    {
        var target = DocumentTabTarget(title);
        reportStep?.Invoke($"Check optional document: {title}");
        var present = await Task.Run(() =>
                query.IsPresent(handle, target.Selector, budget: new SearchBudget(3000, TimeSpan.FromSeconds(2)))
            )
            .WaitAsync(TimeSpan.FromSeconds(5));
        if (!present)
        {
            return;
        }

        await Click(target);
        await Keys("Ctrl+W");
        await Absent(target);
    }

    public async Task AssertTwoOutputRows()
    {
        await Expect(ScriptOutputTarget());
        await Count(ScriptOutputRowsTarget(), 2);
    }

    public async Task AssertOutputCell(string row, string value)
    {
        await Expect(ScriptOutputCellTarget(row), value);
    }

    public async Task AssertNoOutputObjectName(string property)
    {
        await Absent(ScriptOutputPropertyTarget(property));
    }

    public async Task AssertOutputProperty(string property, string value)
    {
        await Expect(ScriptOutputPropertyTarget(property), value);
    }

    public async Task AssertScalarOutput(string value)
    {
        await Expect(ScriptOutputTarget());
        await Expect(ScriptOutputTextTarget(), value);
    }

    public async Task CloseScriptOutput()
    {
        await Click(ScriptOutputCloseTarget());
    }

    public async Task AssertOutputClosed()
    {
        // Closing a modal and returning control to its owner are separate transitions.
        // Require the owner to be enabled before querying the disappearing dialog.
        await WaitForMainWindowReady();
        await Absent(ScriptOutputTarget());
    }

    private async Task WaitForMainWindowReady()
    {
        reportStep?.Invoke("Wait for main window to be enabled after modal transition");
        var owner = host.Sessions.GetInputTarget(handle);
        await WaitForAbsence(() =>
        {
            owner.EnsureAlive();
            return !Win32Desktop.IsWindowEnabled(owner.Hwnd);
        });
    }

    public async Task UndoModelChange()
    {
        await Keys("CTRL+Z", TomExplorerTarget());
    }

    public async Task ShowPreviewDescription(string measure, string before, string after)
    {
        await Click(PreviewSalesRowTarget());
        await Keys("Right", PreviewTreeTarget());
        await Keys("Right", PreviewTreeTarget());
        await Expect(PreviewMeasuresRowTarget());
        await Click(PreviewMeasuresRowTarget());
        await Keys("Right", PreviewTreeTarget());
        await Keys("Right", PreviewTreeTarget());
        await Expect(PreviewMeasureRowTarget(measure));
        await Click(PreviewMeasureRowTarget(measure));
        await Keys("Right", PreviewTreeTarget());
        await Keys("Right", PreviewTreeTarget());
        await Expect(PreviewDescriptionRowTarget(before, after), null);
    }

    public async Task SelectDocument(string tab)
    {
        await Click(DocumentTabTarget(tab));
    }

    public async Task RefreshBpa()
    {
        await Click(BpaRefreshTarget());
    }

    public async Task ShowBpaBottom()
    {
        await Keys("Ctrl+End", BpaTreeTarget());
    }

    public async Task AssertBpaViolation(string rule)
    {
        await Expect(BpaRuleTarget(rule));
    }

    public async Task AssertBpaClear(string rule)
    {
        await Absent(BpaRuleTarget(rule));
    }

    public async Task CollapseObjectTree()
    {
        await Click(CollapseTomTarget(), false);
    }

    public async Task OpenTom()
    {
        // A docked TOM Explorer pane has no tab to click; its tree being on screen is enough.
        if (await IsOnScreen(TomTreeTarget()) == true)
        {
            return;
        }

        await Click(TomTabTarget());
    }

    public async Task SetProperty(string property, string value)
    {
        await Click(PropertyTarget(property));
        await Fill(PropertyInputTarget(), value);
        await Keys("Enter", PropertyInputTarget());
        await Expect(PropertyTarget(property), value);
    }

    public async Task PreviewCSharpScript()
    {
        await Click(RunPreviewTarget());
        await Expect(ScriptPreviewTarget());
    }

    public async Task AcceptScriptPreview()
    {
        await Click(PreviewAcceptTarget());
        await Absent(ScriptPreviewTarget());
    }

    public async Task CancelScriptPreview()
    {
        await Click(PreviewRevertTarget());
        await Absent(ScriptPreviewTarget());
    }

    public async Task RunCSharpScript()
    {
        await Click(RunScriptTarget());
    }

    public async Task OpenBpa()
    {
        await Click(BpaTabTarget());
    }

    public async Task AssertProperty(string property, string value)
    {
        await Expect(PropertyTarget(property), value);
    }

    public async Task ResetSearch()
    {
        await Fill(SearchTarget(), "");
    }

    public async Task SelectObject(string objectName)
    {
        await Click(ObjectTarget(objectName));
    }

    public async Task SelectTable(string table)
    {
        await SelectObjectPath(table, table, "Table");
    }

    public async Task ExpandSelected()
    {
        await Keys("Right", TomExplorerTarget());
    }

    public async Task SetExpression(string expression)
    {
        await Click(ExpressionTabTarget());
        // Scintilla: UIA may report focus on a child of the daxscilla control, so focus on it or inside it counts.
        await Fill(ExpressionInputTarget(), expression);
        await Expect(ExpressionTarget(), expression);
        await Keys("F5");
    }

    public async Task AssertMissingMeasureError()
    {
        await Expect(MissingMeasureDiagnosticTarget());
    }

    public async Task AssertNoErrors()
    {
        await Absent(ErrorsTarget());
    }

    public async Task Save()
    {
        await Keys("Ctrl+S");
    }

    public async Task NewQuery()
    {
        await Click(NewQueryTarget());
    }

    public async Task RunQuery(string query)
    {
        // Scintilla, as for the expression editor.
        await Fill(QueryInputTarget(), query);
        await Expect(QueryTextTarget(), query);
        await Keys("F5", QueryInputTarget());
    }

    public async Task AssertQueryResult(string result)
    {
        await Expect(QueryResultTarget(), result);
    }
}
