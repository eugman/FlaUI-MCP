using System.Drawing;
using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>The te3_layout checks that decide whether a change happened; pure inputs, no desktop access.</summary>
public sealed class LayoutChecksTests
{
    private static LayoutChecks.ResizeMeasure Measure(int requested, int reached) =>
        new(
            "TOM Explorer",
            "left",
            new Point(1163, 300),
            new Point(1443, 300),
            new { x = 1160, width = 760 },
            new { x = 1160 + 760 - reached, width = reached },
            requested,
            reached,
            15
        );

    [Theory]
    [InlineData(480, 480)]
    [InlineData(480, 484)]
    [InlineData(480, 476)]
    public void AResizeWithinTheToleranceIsTheStepItMeasured(int requested, int reached)
    {
        var step = JsonSerializer.SerializeToElement(LayoutChecks.Measured(Measure(requested, reached)));

        Assert.Equal(reached, step.GetProperty("reached").GetInt32());
        Assert.Equal(15, step.GetProperty("inputsSent").GetInt32());
    }

    [Theory]
    [InlineData(480, 485)]
    [InlineData(480, 760)]
    public void AResizeThatMissesIsAFailureNotANote(int requested, int reached)
    {
        // A missed drag returned success with only a note, and the screenshot was taken on the wrong layout.
        var error = Assert.Throws<LayoutSteps.StepFailedException>(() =>
            LayoutChecks.Measured(Measure(requested, reached))
        );

        Assert.Equal(reached, JsonSerializer.SerializeToElement(error.Step).GetProperty("reached").GetInt32());
        Assert.Contains("TOM Explorer", error.Message);
    }

    [Fact]
    public void AMissedResizeRepliesWithWhatItMeasured()
    {
        var error = Assert.Throws<LayoutSteps.StepFailedException>(() => LayoutChecks.Measured(Measure(480, 760)));

        var reply = JsonDocument.Parse(LayoutSteps.Describe("te3_layout", error)).RootElement;

        Assert.Equal(760, reply.GetProperty("step").GetProperty("reached").GetInt32());
    }

    [Fact]
    public void KeepRefusesATitleThatNamesNoVisiblePaneOrTab()
    {
        // keep ["TOM Explorr"] closed AI Assistant, TOM Explorer and Properties, then listed the typo as unresolved.
        string[] visible = ["AI Assistant", "TOM Explorer (Changed)", "Properties", "Expression Editor"];

        var error = Assert.Throws<ArgumentException>(() => LayoutChecks.RequireKept(["TOM Explorr"], visible));

        Assert.Contains("TOM Explorr", error.Message);
        Assert.Contains("no input sent", error.Message);
        LayoutChecks.RequireKept(["TOM Explorer", "Expression Editor"], visible);
    }

    [Fact]
    public async Task APaneButtonPressIsConfirmedWhenTheOppositeButtonShows()
    {
        var reads = 0;

        await LayoutChecks.ConfirmPaneButton("Properties", "Maximize", () => ++reads >= 3, _ => Task.CompletedTask);

        Assert.Equal(3, reads);
    }

    [Fact]
    public async Task APaneButtonPressThatChangedNothingIsAFailure()
    {
        // The reply said "maximized" with nothing read back, and TE3 keeps a maximized pane across restarts.
        var waited = 0;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LayoutChecks.ConfirmPaneButton(
                "Properties",
                "Maximize",
                () => false,
                ms =>
                {
                    waited += ms;
                    return Task.CompletedTask;
                }
            )
        );

        Assert.Contains("still shows Maximize", error.Message);
        Assert.InRange(waited, 1000, 1200);
    }

    [Fact]
    public void AWindowOpenBeforeTheClickIsNotADialogTheClickOpened()
    {
        // A floating Properties window left by an earlier undock failed every later close as "opened Properties".
        var main = new Win32WindowInfo(0x10, "Model - Tabular Editor 3", 7, true, false, false);
        var floating = new Win32WindowInfo(0x30, "Properties", 7, true, false, false);
        var dialog = new Win32WindowInfo(0x40, "Save changes?", 7, true, false, false);
        var tool = new Win32WindowInfo(0x50, "Tip", 7, true, true, false);
        var untitled = new Win32WindowInfo(0x60, "", 7, true, false, false);

        Assert.Null(LayoutChecks.NewDialog([main, floating, tool, untitled], main.Hwnd, [main.Hwnd, floating.Hwnd]));
        Assert.Equal(dialog, LayoutChecks.NewDialog([main, floating, dialog], main.Hwnd, [main.Hwnd, floating.Hwnd]));
    }

    [Fact]
    public void TheFloatingDependenciesPaneIsNotAnUnansweredDialog()
    {
        var main = new Win32WindowInfo(0x10, "Model - Tabular Editor 3", 7, true, false, false);
        var dependencies = new Win32WindowInfo(0x30, "Dependencies", 7, true, false, false);

        Assert.Equal(
            [main.Hwnd, dependencies.Hwnd],
            LayoutChecks.Baseline([main, dependencies], main.Hwnd, _ => "DaxDependencies")
        );
    }

    [Theory]
    [InlineData("Dependencies", null)]
    [InlineData("Dependencies", "SomethingElse")]
    [InlineData("Properties", "PropertyGrid")]
    public void ATitleAloneDoesNotMakeAWindowAFloatingPane(string title, string? automationId)
    {
        // A dialog titled like a pane must not be waved through: only a window with the pane's own AutomationId,
        // observed for the floating Dependencies window (DaxDependencies, 3.27.1), is a floating pane.
        var main = new Win32WindowInfo(0x10, "Model - Tabular Editor 3", 7, true, false, false);
        var window = new Win32WindowInfo(0x30, title, 7, true, false, false);

        Assert.Throws<InvalidOperationException>(() =>
            LayoutChecks.Baseline([main, window], main.Hwnd, _ => automationId)
        );
    }

    [Theory]
    [InlineData(480, 480, true)]
    [InlineData(480, 484, true)]
    [InlineData(480, 485, false)]
    public void APaneAlreadyAtItsSizeNeedsNoDrag(int requested, int current, bool skip)
    {
        // Live, apply-preset dragged TOM Explorer and Properties though both were already at target: 15 inputs each.
        Assert.Equal(skip, LayoutChecks.AlreadyThere(requested, current));
        if (skip)
        {
            var step = JsonSerializer.SerializeToElement(LayoutChecks.Skipped("Properties", requested, current));
            Assert.Equal(0, step.GetProperty("inputsSent").GetInt32());
            Assert.True(step.GetProperty("skipped").GetBoolean());
        }
    }
}
