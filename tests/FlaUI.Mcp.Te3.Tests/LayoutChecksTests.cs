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

    [Fact]
    public void AWindowThatMovedWhileItsPanesWereMeasuredRefusesTheDrag()
    {
        // The pane bounds and the window origin are read in one pass; a window that moved between the two origin
        // reads would shift the raw-point drag, so nothing is sent.
        var at = new Rectangle(0, 0, 1920, 1080);

        LayoutChecks.RequireStill(at, at);
        var error = Assert.Throws<InvalidOperationException>(() =>
            LayoutChecks.RequireStill(at, new Rectangle(10, 0, 1920, 1080))
        );

        Assert.Contains("moved", error.Message);
        Assert.Contains("no drag sent", error.Message);
    }

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
            LayoutChecks.Baseline(
                [main, dependencies],
                main.Hwnd,
                _ => new WindowShape("WindowsForms10.Window.8.app.0.1e2bef7_r3_ad1", 0)
            )
        );
    }

    [Theory]
    [InlineData("Dependencies", "WindowsForms10.Window.8.app.0.1e2bef7_r3_ad1", 0x10)] // owned: a dialog
    [InlineData("Dependencies", "#32770", 0)] // a Win32 dialog class
    [InlineData("Properties", "WindowsForms10.Window.8.app.0.1e2bef7_r3_ad1", 0)] // not a pane seen floating
    public void ATitleAloneDoesNotMakeAWindowAFloatingPane(string title, string className, int owner)
    {
        // A dialog titled like a pane must not be waved through: a floating pane is unowned and of the WinForms
        // window class (vte3b finding 7); TE3's dialogs are owned by its main window.
        var main = new Win32WindowInfo(0x10, "Model - Tabular Editor 3", 7, true, false, false);
        var window = new Win32WindowInfo(0x30, title, 7, true, false, false);

        Assert.Throws<InvalidOperationException>(() =>
            LayoutChecks.Baseline([main, window], main.Hwnd, _ => new WindowShape(className, owner))
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

    [Theory]
    [InlineData(480, 320, false)]
    [InlineData(null, null, false)]
    [InlineData(480, null, true)]
    [InlineData(null, 320, true)]
    public void AResizeNamesExactlyOneOfWidthAndHeightBeforeAnythingElse(int? width, int? height, bool valid)
    {
        // The skip for a pane already at its size took the width and ignored a height passed with it.
        if (valid)
        {
            Assert.Equal(width ?? height, SplitterPlan.RequireOne(width, height));
        }
        else
        {
            Assert.Contains(
                "exactly one",
                Assert.Throws<ArgumentException>(() => SplitterPlan.RequireOne(width, height)).Message
            );
        }
    }
}
