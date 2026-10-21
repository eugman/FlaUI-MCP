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
                    return reference == "Cancel"
                        ? throw new InvalidOperationException("Point is covered; no input sent.")
                        : Task.CompletedTask;
                })
            )
        );

        Assert.Equal(["Cancel", "Close"], clicks);
    }

    [Fact]
    public async Task ADialogThatClosesNeedsNoClick()
    {
        open = false;

        await DialogCancel.Run(Dialog());

        Assert.Empty(clicks);
    }
}
