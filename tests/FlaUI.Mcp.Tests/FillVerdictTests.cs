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

    [Theory]
    [InlineData("#32770", true)]
    [InlineData("FileNameControlHost", true)]
    [InlineData("Chrome_WidgetWin_1", false)]
    public void AControlInsideACommonFileDialogIsRecognisedByAnAncestorsClass(string ancestorClass, bool expected)
    {
        var field = Element();
        var host = Element();
        var dialog = Element();
        var desktop = new FakeDesktop();
        desktop.Parents[field] = host;
        desktop.Parents[host] = dialog;
        desktop.ControlTypes[dialog] = ControlType.Window;
        desktop.ClassNames[host] = ancestorClass;
        desktop.ClassNames[dialog] = ancestorClass == "FileNameControlHost" ? "#32770" : ancestorClass;
        using var installed = desktop.Install();

        Assert.Equal(expected, FillTool.InFileDialog(field, desktop));
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
