using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>Telling the popup a click opened from TE3 windows already open; scripted window lists only.</summary>
public sealed class PopupWindowsTests
{
    private static readonly Win32WindowInfo Main = new(0x10, "Model - Tabular Editor 3", 7, true, false, false);
    private static readonly Win32WindowInfo Floating = new(0x30, "Properties", 7, true, false, false);
    private static readonly Win32WindowInfo Popup = new(0x50, "", 7, true, false, false);

    [Fact]
    public void AFloatingPaneOpenBeforeTheClickIsNotThePopup()
    {
        var popups = PopupWindows.New([Main, Floating, Popup], [Main.Hwnd, Floating.Hwnd]);

        Assert.Equal([Popup], popups);
    }

    [Fact]
    public void ACloakedNewWindowIsNotThePopup() =>
        Assert.Empty(PopupWindows.New([Main, Popup with { IsCloaked = true }], [Main.Hwnd]));

    [Fact]
    public void WithoutABaselineNothingIsTakenForThePopup() =>
        Assert.Throws<InvalidOperationException>(() => PopupWindows.New([Main, Popup], null));

    [Fact]
    public void TheViewMenuPicksAnEntryOnlyInANewWindow()
    {
        // The ancestry find also matched the floating Properties pane's own Button; with every non-main window as a
        // candidate, its ref was clicked instead of the View popup's.
        var reply = """
            {"ancestors":[
              {"ref":"floating","chain":[{"hwnd":48}]},
              {"ref":"popup","chain":[{"hwnd":80}]}
            ]}
            """;
        var popups = PopupWindows.New([Main, Floating, Popup], [Main.Hwnd, Floating.Hwnd]);

        Assert.Equal("popup", LayoutViewMenu.PopupRef(reply, popups.Select(w => (long)w.Hwnd).ToHashSet()));
    }

    [Fact]
    public void TheViewMenuAndPopupDiscoveryReadABaselineBeforeTheirClick()
    {
        var sources = DirectInputJournalTests.Sources();
        var menu = File.ReadAllText(Path.Combine(sources, "LayoutViewMenu.cs"));
        var page = File.ReadAllText(Path.Combine(sources, "Te3Page.cs"));

        Assert.Contains("PopupWindows.Baseline(", menu);
        Assert.Contains(".New(", menu);
        Assert.Contains("PopupWindows.New(", page);
        Assert.DoesNotContain("params nint[] exclude", page);
    }
}
