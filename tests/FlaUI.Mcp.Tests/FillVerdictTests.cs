using System.Runtime.CompilerServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A Value-pattern fill reads its value back through the pattern that set it. A file dialog's name field accepts
/// SetValue without applying it (a Save As then saved under the default name), so that read is self-reported,
/// never verified, and a control inside such a dialog is filled by keyboard instead.
/// </summary>
public sealed class FillVerdictTests
{
    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static McpToolResult Reply(bool independent, string? observed = "report.pbix") =>
        FillTool.FillReply(
            "File name",
            "report.pbix",
            new TextObservation(observed, independent ? "Text" : "Value"),
            new ToolOutcome("completed", "returned"),
            ControlType.Edit,
            independent
        );

    [Fact]
    public void AValuePatternReadBackIsSelfReportedAndUnverified()
    {
        var result = ToolRegistry.WithDispatch(Reply(independent: false), 1);

        Assert.Equal("self-reported", result.Outcome!.Proof!.Verdict);
        Assert.False(result.Outcome.Proof.Independent);
        Assert.Equal("report.pbix", result.Outcome.Proof.Observed);
        Assert.Equal("unverified", result.Outcome.Status);
        Assert.Equal("self-reported", result.Outcome.Verification);
        Assert.True(result.IsError != true);
        Assert.Equal(
            "Set File name to \"report.pbix\" through its Value pattern, which reads the same back; the app taking "
                + "it is not independently verified. Observe the control before relying on it.",
            result.Content[0].Text
        );
    }

    [Fact]
    public void AnIndependentReadBackStaysVerifiedAndOk()
    {
        var result = ToolRegistry.WithDispatch(Reply(independent: true), 1);

        Assert.Equal("verified", result.Outcome!.Proof!.Verdict);
        Assert.Equal("ok", result.Outcome.Status);
        Assert.Equal("Filled File name with \"report.pbix\"", result.Content[0].Text);
    }

    [Fact]
    public void AMismatchThroughThePatternIsStillAFailure()
    {
        var result = ToolRegistry.WithDispatch(Reply(independent: false, observed: "Untitled"), 1);

        Assert.Equal("mismatch", result.Outcome!.Proof!.Verdict);
        Assert.Equal("failed", result.Outcome.Status);
        Assert.True(result.IsError);
    }

    [Fact]
    public void AnUnreadableReadBackThroughThePatternIsUnavailable()
    {
        var result = ToolRegistry.WithDispatch(Reply(independent: false, observed: null), 1);

        Assert.Equal("unavailable", result.Outcome!.Proof!.Verdict);
        Assert.Equal("unverified", result.Outcome.Status);
    }

    private static (FakeDesktop Desktop, AutomationElement Field, AutomationElement Dialog) Dialog(
        string dialogClass,
        string hostClass = "Pane"
    )
    {
        var field = Element();
        var host = Element();
        var dialog = Element();
        var desktop = new FakeDesktop();
        desktop.Parents[field] = host;
        desktop.Parents[host] = dialog;
        desktop.ControlTypes[dialog] = ControlType.Window;
        desktop.ClassNames[host] = hostClass;
        desktop.ClassNames[dialog] = dialogClass;
        return (desktop, field, dialog);
    }

    [Fact]
    public void AFieldUnderAFileNameControlHostIsInAFileDialog()
    {
        var (desktop, field, _) = Dialog("#32770", "FileNameControlHost");
        using var installed = desktop.Install();

        Assert.True(FillTool.InFileDialog(field, desktop));
    }

    [Theory]
    [InlineData("DirectUIHWND")]
    [InlineData("ShellView")]
    [InlineData("FileNameControlHost")]
    public void AFieldInADialogThatHostsTheShellViewIsInAFileDialog(string hosted)
    {
        var (desktop, field, dialog) = Dialog("#32770");
        var view = Element();
        desktop.Parents[view] = dialog;
        desktop.ClassNames[view] = hosted;
        using var installed = desktop.Install();

        Assert.True(FillTool.InFileDialog(field, desktop));
    }

    [Theory]
    [InlineData("1148")]
    [InlineData("1152")]
    public void TheFileNameFieldItselfIsKnownByItsAutomationId(string automationId)
    {
        var (desktop, field, _) = Dialog("#32770");
        desktop.AutomationIds[field] = automationId;
        using var installed = desktop.Install();

        Assert.True(FillTool.InFileDialog(field, desktop));
    }

    [Theory]
    [InlineData("#32770")]
    [InlineData("Chrome_WidgetWin_1")]
    public void AnyOtherDialogKeepsTheValuePattern(string dialogClass)
    {
        // A MessageBox, Find or Font dialog is "#32770" too: a Static and a Button, no shell view, no file name.
        var (desktop, field, dialog) = Dialog(dialogClass);
        var button = Element();
        desktop.Parents[button] = dialog;
        desktop.ClassNames[button] = "Button";
        using var installed = desktop.Install();

        Assert.False(FillTool.InFileDialog(field, desktop));
    }

    [Fact]
    public void ADialogHostedInsideTheWindowCountsWhenItHostsTheShellView()
    {
        var (desktop, field, dialog) = Dialog("Afx:0000", "#32770");
        var view = Element();
        desktop.Parents[view] = desktop.Parents[field];
        desktop.ClassNames[view] = "DirectUIHWND";
        using var installed = desktop.Install();

        Assert.True(FillTool.InFileDialog(field, desktop));
        Assert.False(FillTool.InFileDialog(dialog, desktop));
    }

    [Fact]
    public void AChildrenReadThatFailsKeepsTheValuePattern()
    {
        var (desktop, field, dialog) = Dialog("#32770");
        desktop.ChildrenUnreadable.Add(dialog);
        using var installed = desktop.Install();

        Assert.False(FillTool.InFileDialog(field, desktop));
    }

    [Fact]
    public void TheWalkStopsAtTheControlsOwnWindow()
    {
        // A dialog above the control's own window is not the control's dialog.
        var field = Element();
        var window = Element();
        var owner = Element();
        var desktop = new FakeDesktop();
        desktop.Parents[field] = window;
        desktop.Parents[window] = owner;
        desktop.ControlTypes[window] = ControlType.Window;
        desktop.ClassNames[owner] = "#32770";
        var view = Element();
        desktop.Parents[view] = owner;
        desktop.ClassNames[view] = "DirectUIHWND";
        using var installed = desktop.Install();

        Assert.False(FillTool.InFileDialog(field, desktop));
        Assert.True(FillTool.InFileDialog(owner, desktop));
    }

    [Fact]
    public void AnUnboundedParentChainEndsTheWalk()
    {
        var field = Element();
        var desktop = new FakeDesktop();
        desktop.Parents[field] = field;
        using var installed = desktop.Install();

        Assert.False(FillTool.InFileDialog(field, desktop));
    }
}
