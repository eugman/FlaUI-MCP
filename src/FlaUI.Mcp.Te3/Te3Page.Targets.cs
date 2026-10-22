using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

public sealed partial class Te3Page
{
    public sealed record Target(ElementSelector Selector, ElementSelector? Within = null, bool IncludeOwned = false);

    public static Target ScriptOutputTarget() => new(new(AutomationId: "ScriptOutputForm", RootOnly: true), null, true);

    public static Target RunPreviewTarget() => new(new(Name: "Run with preview", ControlType: "Button"), null, false);

    public static Target ScriptPreviewTarget() =>
        new(new(AutomationId: "ScriptPreviewDialog", RootOnly: true), null, true);

    public static Target OpenFileDialogTarget() =>
        new(new(Name: "Open File", ControlType: "Window", RootOnly: true), null, true);

    public static Target OpenFileNameTarget() =>
        new(
            new(AutomationId: "1148", ControlType: "Edit"),
            new(Name: "Open File", ControlType: "Window", RootOnly: true),
            true
        );

    public static Target CsharpTextTarget() => new(new(AutomationId: "cSharpEditor1"), null, false);

    public static Target CsharpInputTarget() =>
        new(new(AutomationId: "daxscilla"), new(AutomationId: "cSharpEditor1"), false);

    public static Target ScriptOutputRowsTarget() =>
        new(new(ControlType: "ListItem"), new(AutomationId: "ScriptOutputForm", RootOnly: true), true);

    public static Target ScriptOutputCellTarget(string row) =>
        new(
            new(Name: $"Object row {row}", ControlType: "DataItem", Visible: true),
            new(AutomationId: "ScriptOutputForm", RootOnly: true),
            true
        );

    public static Target ScriptOutputPropertyTarget(string property) =>
        new(
            new(Name: $"{property}", ControlType: "DataItem", Visible: true),
            new(AutomationId: "ScriptOutputForm", RootOnly: true),
            true
        );

    public static Target ScriptOutputTextTarget() =>
        new(
            new(AutomationId: "dataTextBox", ControlType: "Edit"),
            new(AutomationId: "ScriptOutputForm", RootOnly: true),
            true
        );

    public static Target ScriptOutputCloseTarget() =>
        new(
            new(AutomationId: "btnClose", ControlType: "Button"),
            new(AutomationId: "ScriptOutputForm", RootOnly: true),
            true
        );

    public static Target TomExplorerTarget() => new(new(AutomationId: "treeList"), null, false);

    public static Target PreviewSalesRowTarget() =>
        new(
            new(ControlType: "TreeItem", Value: "Sales;;;Sales;", Visible: true),
            new(AutomationId: "diffTreeList"),
            true
        );

    public static Target PreviewTreeTarget() =>
        new(new(AutomationId: "diffTreeList"), new(AutomationId: "ScriptPreviewDialog", RootOnly: true), true);

    public static Target PreviewMeasuresRowTarget() =>
        new(
            new(ControlType: "TreeItem", Value: "measures;;;measures;", Visible: true),
            new(AutomationId: "diffTreeList"),
            true
        );

    public static Target PreviewMeasureRowTarget(string measure) =>
        new(
            new(ControlType: "TreeItem", Value: $"{measure};;;{measure};", Visible: true),
            new(AutomationId: "diffTreeList"),
            true
        );

    public static Target PreviewDescriptionRowTarget(string before, string after) =>
        new(
            new(ControlType: "TreeItem", Value: $"description;{before};;description;{after}", Visible: true),
            new(AutomationId: "diffTreeList"),
            true
        );

    public static Target DocumentTabTarget(string tab) => new(new(ControlType: "TabItem", Name: $"{tab}"), null, false);

    public static Target BpaRefreshTarget() =>
        new(new(ControlType: "Button", Name: "Refresh"), new(AutomationId: "BestPracticeAnalyzerView"), false);

    public static Target BpaTreeTarget() => new(new(AutomationId: "treeBpa"), null, false);

    public static Target BpaRuleTarget(string rule) =>
        new(new(ControlType: "DataItem", Value: $"{rule}"), new(AutomationId: "treeBpa"), false);

    public static Target CollapseTomTarget() =>
        new(new(Name: "Collapse all", ControlType: "Button"), new(AutomationId: "TabularExplorerView"), false);

    public static Target TomTabTarget() => new(new(Name: "TOM Explorer", ControlType: "TabItem"), null, false);

    // The tree inside TOM Explorer's view, which exists whether TOM Explorer is a tab or a docked pane
    // (observed on 3.27.1: Pane "TOM Explorer" > TabularExplorerView > treeList).
    public static Target McpServerMarkerTarget() =>
        new(
            new(Name: "Export configuration", ControlType: "SplitButton", Visible: true),
            new(AutomationId: "McpServerDialog"),
            false
        );

    public static Target TomChangesTarget() =>
        new(
            new(Name: "Show changes", ControlType: "CheckBox", Pattern: "Toggle", Visible: true),
            new(AutomationId: "TabularExplorerView"),
            false
        );

    public static Target TomTreeTarget() =>
        new(new(AutomationId: "treeList"), new(AutomationId: "TabularExplorerView"), false);

    public static Target PropertyTarget(string property) =>
        new(new(Name: $"{property}", ControlType: "DataItem"), new(AutomationId: "PropertyGridView"), false);

    public static Target PropertyInputTarget() =>
        new(
            new(Name: "Editing control", ControlType: "Edit", Pattern: "Value"),
            new(AutomationId: "PropertyGridView"),
            false
        );

    public static Target PreviewAcceptTarget() =>
        new(new(AutomationId: "okButton"), new(AutomationId: "ScriptPreviewDialog", RootOnly: true), true);

    public static Target PreviewRevertTarget() =>
        new(new(AutomationId: "revertButton"), new(AutomationId: "ScriptPreviewDialog", RootOnly: true), true);

    public static Target RunScriptTarget() => new(new(Name: "Run script", ControlType: "Button"), null, false);

    public static Target BpaTabTarget() =>
        new(new(Name: "Best Practice Analyzer", ControlType: "TabItem"), null, false);

    public static Target SearchTarget() =>
        new(new(AutomationId: "searchControl1"), new(AutomationId: "TabularExplorerView"), false);

    public static Target ObjectTarget(string objectName) =>
        new(new(ControlType: "TreeItem", Value: $"{objectName}"), new(AutomationId: "treeList"), false);

    public static Target ExpressionTabTarget() =>
        new(new(Name: "Expression Editor", ControlType: "TabItem"), null, false);

    public static Target ExpressionInputTarget() =>
        new(new(AutomationId: "daxscilla"), new(AutomationId: "QuickEditorView"), false);

    public static Target ExpressionTarget() =>
        new(new(AutomationId: "DaxEditor"), new(AutomationId: "QuickEditorView"), false);

    public static Target MissingMeasureDiagnosticTarget() =>
        new(
            new(
                ControlType: "DataItem",
                Value: "The value for [Missing Measure] cannot be determined. Either [Missing Measure] doesn't exist, or there is no current row for a column named [Missing Measure]."
            ),
            new(AutomationId: "MessagesView"),
            false
        );

    public static Target ErrorsTarget() =>
        new(new(ControlType: "DataItem", Value: "Error"), new(AutomationId: "MessagesView"), false);

    public static Target NewQueryTarget() => new(new(Name: "New DAX Query", ControlType: "Button"), null, false);

    public static Target QueryInputTarget() =>
        new(new(AutomationId: "daxscilla"), new(AutomationId: "daxEditor"), false);

    public static Target QueryTextTarget() => new(new(AutomationId: "daxEditor"), null, false);

    public static Target QueryResultTarget() => new(new(Name: "[Value] row 1", ControlType: "DataItem"), null, false);

    public static Target WindowMenuTarget() =>
        new(new(Name: "Window", ControlType: "MenuItem"), new(Name: "Main menu", ControlType: "MenuBar"), false);

    public static Target DefaultLayoutTarget() => new(new(Name: "Default layout", ControlType: "Button"), null, true);

    public static Target AutoRollbackTarget() => new(new(Name: "Auto-rollback", ControlType: "CheckBox"));

    public static Target StatusBarTarget() => new(new(ControlType: "StatusBar"));
}
