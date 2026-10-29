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

    // te3b-37/38: te3_navigate native-mcp-server opened the owned "MCP Server" dialog, and te3_model_state then read
    // the status bar as if nothing were open. The read-only tool lists such a dialog (title, class, owned, advice
    // when known); navigate and layout refuse it before input as precondition-failed unless the caller targets it.
    private static readonly Win32WindowInfo McpServer = new(0x20, "MCP Server", 7, true, false, false);
    private static readonly Win32WindowInfo Dependencies = new(0x30, "Dependencies", 7, true, false, false);

    private static WindowShape? Shape(nint hwnd) =>
        hwnd switch
        {
            0x20 => new WindowShape("WindowsForms10.Window.8.app.0.1e2bef7_r3_ad1", 0x10),
            0x30 => new WindowShape("WindowsForms10.Window.8.app.0.1e2bef7_r3_ad1", 0),
            _ => new WindowShape("WindowsForms10.Window.8.app.0.1e2bef7_r3_ad1", 0),
        };

    [Fact]
    public void AnUnknownOwnedDialogIsListedByTheReadWithItsTitleClassAndOwner()
    {
        var open = Te3Dialogs.OpenDialogs([Main, McpServer, Dependencies], Main.Hwnd, Shape);

        var dialog = Assert.Single(open);
        Assert.Equal("MCP Server", dialog.Title);
        Assert.StartsWith("WindowsForms10.Window.8.", dialog.ClassName);
        Assert.True(dialog.Owned);
        Assert.Null(dialog.Advice);
        Assert.Empty(Te3Dialogs.OpenDialogs([Main, Dependencies], Main.Hwnd, Shape));
    }

    [Fact]
    public void AnUnknownOwnedDialogRefusesNavigationAndLayoutBeforeInputUnlessTargeted()
    {
        Win32WindowInfo[] windows = [Main, McpServer];

        var navigate = Assert.Throws<InvalidOperationException>(() =>
            Te3Dialogs.RequireNoOtherWindow(windows, Main.Hwnd, "Preferences", Shape)
        );
        var layout = Assert.Throws<InvalidOperationException>(() => LayoutChecks.Baseline(windows, Main.Hwnd, Shape));
        Te3Dialogs.RequireNoOtherWindow(windows, Main.Hwnd, "MCP Server", Shape);

        Assert.Equal("precondition-failed", CoreFailure.BeforeInput(navigate, 0)?.Code);
        Assert.Equal("precondition-failed", CoreFailure.BeforeInput(layout, 0)?.Code);
        Assert.False(CoreFailure.BeforeInput(navigate, 0)?.ActionDispatched);
        Assert.Contains("MCP Server", navigate.Message);
    }

    [Theory]
    [InlineData("MCP Server")]
    [InlineData("Preferences")]
    public void NavigationRefusesTwoDialogsWithTheAllowedTitle(string title)
    {
        var windows = new[]
        {
            Main,
            new Win32WindowInfo(0x20, title, 7, true, false, false),
            new Win32WindowInfo(0x21, title, 7, true, false, false),
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            Te3Dialogs.RequireNoOtherWindow(windows, Main.Hwnd, title, Shape)
        );

        Assert.Contains(title, error.Message);
        Assert.Contains("two", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("precondition-failed", CoreFailure.BeforeInput(error, 0)?.Code);
    }

    [Fact]
    public void AnOwnedDialogWithToolWindowStyleStillBlocksLayoutBeforeInput()
    {
        Win32WindowInfo[] windows = [Main, McpServer with { IsToolWindow = true }];

        var open = Assert.Single(Te3Dialogs.OpenDialogs(windows, Main.Hwnd, Shape));
        var error = Assert.Throws<InvalidOperationException>(() => LayoutChecks.Baseline(windows, Main.Hwnd, Shape));

        Assert.Equal("MCP Server", open.Title);
        Assert.Equal("precondition-failed", CoreFailure.BeforeInput(error, 0)?.Code);
        Assert.False(CoreFailure.BeforeInput(error, 0)?.ActionDispatched);
        Assert.Contains("MCP Server", error.Message);
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
