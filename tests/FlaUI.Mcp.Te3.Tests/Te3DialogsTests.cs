using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>Known TE3 prompts are refused before input with what to do about them; scripted window lists only.</summary>
public sealed class Te3DialogsTests
{
    private static readonly Win32WindowInfo Main = new(0x10, "Model - Tabular Editor 3", 7, true, false, false);

    [Theory]
    [InlineData("Get Started", "first launch")]
    [InlineData("External changes detected", "Reload or Ignore")]
    [InlineData("Select workspace database", ".tmuo")]
    public void AKnownPromptIsRefusedBeforeLayoutInputWithItsAdvice(string title, string advice)
    {
        var prompt = new Win32WindowInfo(0x40, title, 7, true, false, false);

        var error = Assert.Throws<InvalidOperationException>(() =>
            LayoutChecks.Baseline([Main, prompt], Main.Hwnd, _ => null)
        );

        Assert.Contains(title, error.Message);
        Assert.Contains(advice, error.Message);
        Assert.Contains("no input sent", error.Message);
    }

    [Fact]
    public void AnUnknownDialogIsStillRefusedWithoutAdvice() => Assert.Null(Te3Dialogs.Advice("Something else"));

    [Fact]
    public void NavigationRefusesAKnownPromptWithItsAdvice()
    {
        var prompt = new Win32WindowInfo(0x40, "External changes detected", 7, true, false, false);

        var error = Assert.Throws<InvalidOperationException>(() =>
            Te3Dialogs.RequireNoOtherWindow([Main, prompt], Main.Hwnd, "Preferences", _ => null)
        );

        Assert.Contains("Reload or Ignore", error.Message);
    }

    [Fact]
    public void NavigationAllowsTheFloatingDependenciesPane() =>
        Te3Dialogs.RequireNoOtherWindow(
            [Main, new Win32WindowInfo(0x30, "Dependencies", 7, true, false, false)],
            Main.Hwnd,
            "Preferences",
            _ => new WindowShape("WindowsForms10.Window.8.app.0.1e2bef7_r3_ad1", 0)
        );

    [Fact]
    public void NavigationRefusesAWindowTitledLikeAPaneWithoutItsAutomationId() =>
        Assert.Throws<InvalidOperationException>(() =>
            Te3Dialogs.RequireNoOtherWindow(
                [Main, new Win32WindowInfo(0x30, "Dependencies", 7, true, false, false)],
                Main.Hwnd,
                "Preferences",
                _ => null
            )
        );

    [Fact]
    public void APreferencesWindowOpenBeforeTheCommandDoesNotCountAsOpened()
    {
        // OpenPreferences polled for any window titled Preferences, so one already open (or left over) passed at once.
        var stale = new Win32WindowInfo(0x40, "Preferences", 7, true, false, false);
        var baseline = new WindowBaseline([stale]);

        Assert.False(Te3Dialogs.Opened(baseline, [stale], "Preferences"));
        Assert.True(Te3Dialogs.Opened(baseline, [stale, stale with { Hwnd = 0x41 }], "Preferences"));
    }
}
