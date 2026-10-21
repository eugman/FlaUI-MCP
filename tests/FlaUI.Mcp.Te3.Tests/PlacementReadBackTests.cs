using System.Drawing;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>A placement Core reports as a mismatch is accepted only as OS minimum-size growth; scripted calls only.</summary>
public sealed class PlacementReadBackTests
{
    private static readonly Rectangle Requested = new(96, 192, 555, 254);

    // Core's windows_place_window when the window did not land exactly: completed, verification "mismatch".
    private static CoreCallException Mismatch() =>
        new(
            "windows_place_window",
            new McpToolResult
            {
                IsError = true,
                Content = [new() { Text = "Window did not land where requested" }],
                Outcome = new ToolOutcome("completed", "not-tracked") { Verification = "mismatch", InputsSent = 1 },
            }
        );

    private static CoreCallException OtherFailure() =>
        new(
            "windows_place_window",
            new McpToolResult
            {
                IsError = true,
                Content = [new() { Text = "refused" }],
                Outcome = new ToolOutcome("partial", "not-tracked") { InputsSent = 1 },
            }
        );

    [Fact]
    public async Task GrowthToTheMinimumSizeAtTheSamePlaceIsReadBackWithItsDeltas()
    {
        var steps = new List<string>();

        var observed = await PlacementReadBack.Place(
            () => throw Mismatch(),
            () => new Rectangle(96, 192, 569, 261),
            Requested,
            steps.Add
        );

        Assert.Equal(new Rectangle(96, 192, 569, 261), observed);
        Assert.Contains(steps, s => s.Contains("dw +14") && s.Contains("dh +7"));
    }

    [Theory]
    [InlineData(100, 192, 569, 261)] // moved
    [InlineData(96, 192, 500, 254)] // shrank
    public async Task AMismatchThatMovedOrShrankTheWindowIsAFailure(int x, int y, int width, int height) =>
        await Assert.ThrowsAsync<CoreCallException>(() =>
            PlacementReadBack.Place(() => throw Mismatch(), () => new Rectangle(x, y, width, height), Requested, null)
        );

    [Fact]
    public async Task AnyOtherPlacementFailureIsStillAFailure() =>
        await Assert.ThrowsAsync<CoreCallException>(() =>
            PlacementReadBack.Place(() => throw OtherFailure(), () => Requested, Requested, null)
        );

    [Fact]
    public async Task AWindowThatCannotBeReadBackIsAFailure() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PlacementReadBack.Place(() => throw Mismatch(), () => null, Requested, null)
        );

    [Fact]
    public async Task AnExactPlacementReturnsTheReadBack() =>
        Assert.Equal(
            Requested,
            await PlacementReadBack.Place(() => Task.CompletedTask, () => Requested, Requested, null)
        );
}
