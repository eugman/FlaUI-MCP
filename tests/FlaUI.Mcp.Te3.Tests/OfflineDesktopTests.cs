using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// A TE3 test that forgot to install the fake desktop, or a mutant that skips a guard, must not type into whatever
/// window is in front. In this assembly the real desktop refuses every change.
/// </summary>
public sealed class OfflineDesktopTests
{
    [Fact]
    public void TheRealDesktopRefusesInputInTheOfflineTe3Tests()
    {
        Assert.Equal("1", Environment.GetEnvironmentVariable(InputDesktop.OfflineVariable));
        Assert.Null(InputDesktop.Override.Value);
        var sent = false;

        Assert.Throws<InvalidOperationException>(() => InputDesktop.Current.Dispatch(() => sent = true));
        Assert.Throws<InvalidOperationException>(() => InputDesktop.Current.Focus(1));
        Assert.Throws<InvalidOperationException>(() => InputDesktop.Current.PostClose(1));
        Assert.Throws<InvalidOperationException>(() => InputDesktop.Current.SetClipboardText("x"));
        Assert.False(sent);
    }

    [Fact]
    public void TheFakeDesktopCountsInputInsteadOfSendingIt()
    {
        var desktop = new FakeDesktop();
        using (desktop.Install())
        {
            InputDesktop.Current.Dispatch(() => throw new InvalidOperationException("sent for real"));
            Assert.Same(desktop, InputDesktop.Current);
        }

        Assert.Equal(1, desktop.Dispatched);
        Assert.Null(InputDesktop.Override.Value);
    }
}
