using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// "A window opened" and "a modal blocks input" are judged against what was there before: only new windows
/// count, tooltips, menus, tool windows and cloaked windows never do, and a modal is a window owned by a
/// disabled one, never just "some window is disabled, so blame the first enabled one".
/// </summary>
public sealed class WindowBaselineTests
{
    private static Win32WindowInfo Window(nint hwnd, string title, bool enabled = true) =>
        new(hwnd, title, 7, enabled, IsToolWindow: false, IsCloaked: false);

    private static readonly Win32WindowInfo Main = Window(1, "Contoso Studio");

    [Fact]
    public void ADisabledPaletteDoesNotMakeTheMainWindowAModal()
    {
        // A splash or palette is disabled; the main window is enabled, titled and owned by nobody.
        var failure = ClickTool.ClickFailure("Stale ref.", "X", [Main, Window(5, "Palette", enabled: false)]);

        Assert.Equal("Stale ref.", failure);
    }

    [Fact]
    public void AWindowOwnedByADisabledOneIsNamedAsTheModal()
    {
        var failure = ClickTool.ClickFailure(
            "Blocked.",
            "X",
            [Main with { IsEnabled = false }, Window(2, "Save changes?") with { Owner = 1 }]
        );

        Assert.StartsWith("Blocked. Window \"Save changes?\" appears to be a modal dialog", failure);
    }

    [Fact]
    public void ATooltipIsNotTheWindowAClickOpened()
    {
        var tooltip = Window(9, "Browse for a file") with { ClassName = "tooltips_class32", Owner = 1 };

        Assert.Null(ClickTool.NewWindowTitle([Main], [Main, tooltip]));
    }

    [Fact]
    public void ToolAndCloakedWindowsAreNotOpenedWindows()
    {
        var opened = new WindowBaseline([Main]).Opened([
            Main,
            Window(3, "Palette") with
            {
                IsToolWindow = true,
            },
            Window(4, "Suspended") with
            {
                IsCloaked = true,
            },
            Window(5, "Open") with
            {
                Owner = 1,
            },
        ]);

        Assert.Equal([5], opened.Select(w => (int)w.Hwnd));
    }

    [Fact]
    public void AnOwnedDialogIsPreferredOverAnUnownedWindow()
    {
        var opened = new WindowBaseline([Main]).Opened([
            Main,
            Window(6, "Splash"),
            Window(5, "Open") with
            {
                Owner = 1,
            },
        ]);

        Assert.Equal("Open", opened[0].Title);
    }

    [Fact]
    public void ANewModalNeedsItsOwnerToBecomeDisabled()
    {
        var baseline = new WindowBaseline([Main]);
        var dialog = Window(5, "Open") with { Owner = 1 };

        Assert.Null(baseline.NewModal([Main, dialog]));
        Assert.Equal("Open", baseline.NewModal([Main with { IsEnabled = false }, dialog]));
    }
}
