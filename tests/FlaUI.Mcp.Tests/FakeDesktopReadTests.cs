using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Offline tests must never read the machine's real clipboard or keyboard focus: what they saw depended on the
/// desktop the suite ran on. The fake answers both reads itself.
/// </summary>
public sealed class FakeDesktopReadTests
{
    [Theory]
    [InlineData(nameof(InputDesktop.ClipboardText))]
    [InlineData(nameof(InputDesktop.FocusedControl))]
    public void TheFakeOverridesTheRead(string read)
    {
        var method = typeof(FakeDesktop).GetMethod(read, Type.EmptyTypes)!;

        Assert.Equal(typeof(FakeDesktop), method.DeclaringType);
    }

    [Fact]
    public void TheClipboardReadsBackWhatTheFakeWasGiven()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        Assert.Null(InputDesktop.Current.ClipboardText().Text);
        Assert.True(InputDesktop.Current.SetClipboardText("pasted"));

        var read = InputDesktop.Current.ClipboardText();
        Assert.Equal("pasted", read.Text);
        Assert.Null(read.Failure);
    }

    [Fact]
    public void FocusIsTheFakesTargetUnlessATestMovesIt()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        Assert.Equal(FakeDesktop.TargetHwnd, InputDesktop.Current.FocusedControl()!.RootHwnd);
        Assert.Equal(FakeDesktop.TargetPid, InputDesktop.Current.FocusedControl()!.ProcessId);

        desktop.Focused = null;
        Assert.Null(InputDesktop.Current.FocusedControl());
    }
}
