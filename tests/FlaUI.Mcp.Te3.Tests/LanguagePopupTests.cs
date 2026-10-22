using Xunit;

/// <summary>
/// The language capture's popup is closed and confirmed gone whatever fails after it was found (re-review of
/// 4c9ba7e: a failing list check ran before the popup's handle was kept, so the close skipped the absence check).
/// Scripted steps; nothing is sent.
/// </summary>
public sealed class LanguagePopupTests
{
    [Fact]
    public async Task AFailingCheckAfterThePopupWasFoundStillClosesThatPopup()
    {
        var closed = new List<(nint Hwnd, Exception? Failure)>();
        var failure = new InvalidOperationException("Expected one language list with descendant items");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PopupScope.Run(
                () => Task.FromResult(("popup", (nint)0x77)),
                _ => throw failure,
                (hwnd, error) =>
                {
                    closed.Add((hwnd, error));
                    return Task.CompletedTask;
                }
            )
        );

        Assert.Same(failure, thrown);
        Assert.Equal([((nint)0x77, (Exception?)failure)], closed);
    }

    [Fact]
    public async Task AFailureBeforeThePopupWasFoundClosesWithNoHandle()
    {
        var closed = new List<nint>();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            PopupScope.Run<string>(
                () => throw new TimeoutException(),
                _ => Task.CompletedTask,
                (hwnd, _) =>
                {
                    closed.Add(hwnd);
                    return Task.CompletedTask;
                }
            )
        );

        Assert.Equal([(nint)0], closed);
    }
}
