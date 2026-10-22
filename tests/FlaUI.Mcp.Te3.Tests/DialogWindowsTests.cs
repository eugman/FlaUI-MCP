using FlaUI.Mcp.Core;
using Xunit;

/// <summary>
/// Which new window is the dialog a TE3 action opened, and whether a dialog closed: the first new titled window was
/// taken (a tooltip or another TE3 window could be it), and a cloaked window counted as closed. Scripted windows.
/// </summary>
public sealed class DialogWindowsTests
{
    private const nint Main = 0x10;

    private static Win32WindowInfo Window(
        nint hwnd,
        string title,
        nint owner,
        string className = "WindowsForms10.Window.8.app.0.1",
        bool cloaked = false
    ) => new(hwnd, title, 7, true, false, cloaked) { Owner = owner, ClassName = className };

    [Fact]
    public void TheDialogIsTheNewTitledWindowTe3sMainWindowOwns()
    {
        Win32WindowInfo[] now =
        [
            Window(Main, "Model - Tabular Editor 3", 0),
            Window(0x20, "Tip", Main, "tooltips_class32"),
            Window(0x30, "Dependencies", 0),
            Window(0x40, "Preferences", Main),
        ];

        Assert.Equal(0x40, DialogWindows.NewDialog(now, [Main], Main)?.Hwnd);
    }

    [Fact]
    public void WithNoOwnedDialogTheOneNewDialogClassWindowIsTaken()
    {
        Win32WindowInfo[] now = [Window(0x20, "Tip", Main, "tooltips_class32"), Window(0x60, "MCP Server", 0)];

        Assert.Equal(0x60, DialogWindows.NewDialog(now, [], Main)?.Hwnd);
    }

    [Fact]
    public void TwoNewOwnedDialogsAreRefusedNotGuessed()
    {
        Win32WindowInfo[] now = [Window(0x40, "Preferences", Main), Window(0x50, "Save changes?", Main, "#32770")];

        var error = Assert.Throws<InvalidOperationException>(() => DialogWindows.NewDialog(now, [], Main));

        Assert.Contains("Preferences", error.Message);
        Assert.Contains("Save changes?", error.Message);
    }

    [Fact]
    public void ACloakedWindowIsNotClosed()
    {
        Assert.Equal("closed", DialogWindows.State([Window(Main, "Main", 0)], 0x40));
        Assert.Equal("cloaked", DialogWindows.State([Window(0x40, "Preferences", Main, cloaked: true)], 0x40));
        Assert.Equal("open", DialogWindows.State([Window(0x40, "Preferences", Main)], 0x40));
    }
}
