using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>The cancel sequence over a scripted dialog; nothing is sent to the desktop.</summary>
public sealed class DialogCancelTests
{
    private readonly List<string> clicks = [];
    private bool open = true;

    private DialogCancel.Steps Dialog(
        Func<ElementSelector, Task<string>>? resolve = null,
        Func<string, Task>? click = null
    ) =>
        new(
            () => Task.CompletedTask,
            _ => open ? throw new TimeoutException("Dialog still open") : Task.CompletedTask,
            resolve ?? (button => Task.FromResult(button.Name!)),
            click
                ?? (
                    reference =>
                    {
                        clicks.Add(reference);
                        return Task.CompletedTask;
                    }
                )
        );

    [Fact]
    public async Task ACancelThatMayHaveBeenSentIsNeverFollowedByClose()
    {
        // A pending invoke or a modal opened by Cancel: the click may have gone out.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DialogCancel.Run(
                Dialog(click: reference =>
                {
                    clicks.Add(reference);
                    throw new InvalidOperationException("Invoke is still pending; a modal dialog opened.");
                })
            )
        );

        Assert.Equal(["Cancel"], clicks);
        Assert.Contains("a modal dialog opened", error.Message);
        Assert.Contains("Cancel", error.Message);
    }

    [Fact]
    public async Task ACancelThatWentOutButLeftTheDialogOpenIsNotFollowedByClose()
    {
        var error = await Assert.ThrowsAsync<TimeoutException>(() => DialogCancel.Run(Dialog()));

        Assert.Equal(["Cancel"], clicks);
        Assert.Contains("Cancel", error.Message);
    }

    [Fact]
    public async Task AMissingCancelButtonFallsBackToClose()
    {
        await Assert.ThrowsAsync<TimeoutException>(() =>
            DialogCancel.Run(
                Dialog(resolve: button =>
                    button.Name == "Cancel"
                        ? throw new InvalidOperationException("No element matched")
                        : Task.FromResult(button.Name!)
                )
            )
        );

        Assert.Equal(["Close"], clicks);
    }

    [Fact]
    public async Task ACancelRefusedBeforeAnyInputFallsBackToClose()
    {
        await Assert.ThrowsAsync<TimeoutException>(() =>
            DialogCancel.Run(
                Dialog(click: reference =>
                {
                    clicks.Add(reference);
                    return reference == "Cancel" ? throw ClickFailed("Point is covered.") : Task.CompletedTask;
                })
            )
        );

        Assert.Equal(["Cancel", "Close"], clicks);
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("timed_out_pending")]
    public async Task ACancelCoreSaysMayHaveGoneOutIsNotFollowedByClose(string dispatch)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DialogCancel.Run(
                Dialog(click: reference =>
                {
                    clicks.Add(reference);
                    throw ClickFailed("Click is pending; no input sent.", dispatch);
                })
            )
        );

        Assert.Equal(["Cancel"], clicks);
    }

    [Fact]
    public async Task TextThatSaysNoInputWasSentDoesNotProveIt()
    {
        // Only Core's outcome proves the click never went out.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DialogCancel.Run(
                Dialog(click: reference =>
                {
                    clicks.Add(reference);
                    throw new InvalidOperationException("Point is covered; no input sent.");
                })
            )
        );

        Assert.Equal(["Cancel"], clicks);
    }

    private static CoreCallException ClickFailed(string text, string dispatch = "not-dispatched") =>
        new(
            "windows_click",
            new FlaUI.Mcp.McpToolResult
            {
                IsError = true,
                Content = [new() { Text = text }],
                Outcome = new ToolOutcome(dispatch, "not-tracked") { Status = "failed", InputsSent = 0 },
            }
        );

    [Fact]
    public async Task ADialogThatClosesNeedsNoClick()
    {
        open = false;

        await DialogCancel.Run(Dialog());

        Assert.Empty(clicks);
    }
}
