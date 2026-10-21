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

    [Fact]
    public void TheFakeDesktopOverridesEveryDesktopCallCoreCanMake()
    {
        // A desktop call the fake does not override falls through to the real desktop. Core's next release adds
        // ElementAt, ParentOf, SameElement, TopLevelWindows, Monitors, MovePointer, FocusedControl, ClipboardText and
        // CaptureWindow: this fails at that rebase until FakeDesktop overrides each one.
        var missing = typeof(InputDesktop)
            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
            .Where(m => m.IsVirtual && !m.IsFinal && m.DeclaringType == typeof(InputDesktop))
            .Where(m =>
                typeof(FakeDesktop)
                    .GetMethod(m.Name, m.GetParameters().Select(p => p.ParameterType).ToArray())
                    ?.DeclaringType != typeof(FakeDesktop)
            )
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(missing);
    }
}
