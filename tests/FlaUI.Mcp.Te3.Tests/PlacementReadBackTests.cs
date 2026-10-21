using System.Drawing;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>A window placement Core says was not exact is read back and reported; scripted calls only.</summary>
public sealed class PlacementReadBackTests
{
    private static readonly Rectangle Requested = new(96, 192, 555, 254);

    private static CoreCallException Refused(int inputsSent) =>
        new(
            "windows_place_window",
            new McpToolResult
            {
                IsError = true,
                Content = [new() { Text = "Window placed at (96,192,569,261), not exactly as requested." }],
                Outcome = new ToolOutcome(ToolOutcome.DispatchFor(true, inputsSent), "not-tracked")
                {
                    InputsSent = inputsSent,
                },
            }
        );

    [Fact]
    public async Task APlacementThatMovedTheWindowButNotExactlyIsReadBackWithItsDeltas()
    {
        // Core now fails a placement that does not land exactly; the OS minimum size of the dialog is kept on purpose.
        var steps = new List<string>();

        var observed = await PlacementReadBack.Place(
            () => throw Refused(1),
            () => new Rectangle(96, 192, 569, 261),
            Requested,
            steps.Add
        );

        Assert.Equal(new Rectangle(96, 192, 569, 261), observed);
        Assert.Contains(steps, s => s.Contains("dw +14") && s.Contains("dh +7"));
    }

    [Fact]
    public async Task APlacementThatSentNothingIsStillAFailure() =>
        await Assert.ThrowsAsync<CoreCallException>(() =>
            PlacementReadBack.Place(() => throw Refused(0), () => Requested, Requested, null)
        );

    [Fact]
    public async Task AWindowThatCannotBeReadBackIsAFailure() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PlacementReadBack.Place(() => throw Refused(1), () => null, Requested, null)
        );

    [Fact]
    public async Task AnExactPlacementReturnsTheReadBack()
    {
        var observed = await PlacementReadBack.Place(() => Task.CompletedTask, () => Requested, Requested, null);

        Assert.Equal(Requested, observed);
    }
}
