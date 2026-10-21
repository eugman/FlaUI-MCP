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
}
