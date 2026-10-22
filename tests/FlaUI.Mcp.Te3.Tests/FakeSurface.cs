using System.Drawing;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using static CoreResults;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>A docked layout: panes close when their Close is pressed, and maximize flips the button.</summary>
internal sealed class FakeSurface : ILayoutSurface
{
    public const nint Main = 0x10;

    private readonly InputTally inputs;
    private readonly List<string> panes;

    public FakeSurface(InputTally inputs, params string[] panes)
    {
        this.inputs = inputs;
        this.panes = [.. panes];
    }

    /// <summary>Open document tabs; Close removes one unless it is in <see cref="Stuck"/>.</summary>
    public List<string> Documents { get; } = [];

    public List<string> Presses { get; } = [];

    /// <summary>Extra top-level windows of the TE3 process, read at each check.</summary>
    public List<Win32WindowInfo> Extra { get; } = [];

    /// <summary>Runs after each press.</summary>
    public Action<string>? AfterPress { get; set; }

    /// <summary>When false, Maximize and Restore are pressed but the title bar keeps its button.</summary>
    public bool ButtonsFlip { get; set; } = true;

    /// <summary>Panes Close does not remove.</summary>
    public HashSet<string> Stuck { get; } = [];

    /// <summary>A press that fails after its input went out.</summary>
    public string? FailingPress { get; set; }

    private readonly HashSet<string> maximized = [];

    public Rectangle Origin => new(0, 30, 1920, 1110);

    public nint MainWindow => Main;

    public IReadOnlyList<LayoutPart> Parts() =>
        [
            .. panes.Select((t, i) => new LayoutPart(t, "pane", new Rectangle(0, 61 + i * 100, 500, 100))),
            .. Documents.Select(t => new LayoutPart(t, "document", new Rectangle(680, 61, 760, 947))),
        ];

    public Rectangle? BoundsOf(nint hwnd) => new Rectangle(200, 200, 400, 600);

    /// <summary>Class and owner of the extra windows; the floating Dependencies window is unowned WinForms.</summary>
    public Dictionary<nint, WindowShape> Shapes { get; } = [];

    public WindowShape? ShapeOf(nint hwnd) => Shapes.TryGetValue(hwnd, out var shape) ? shape : null;

    public bool ButtonShown(string title, string button) =>
        Documents.Contains(title) ? button == "Close" : PaneShows(title, button);

    private bool PaneShows(string title, string button) =>
        panes.Contains(title) && (button == "Close" || (button == "Restore") == maximized.Contains(title));

    public async Task Press(string title, string button)
    {
        await inputs.Invoke(() => Task.FromResult(Sent($"pressed {button}", FailingPress == title)), "windows_click");
        Presses.Add($"{button} {title}");
        if (button == "Close" && !Stuck.Contains(title))
        {
            panes.Remove(title);
            Documents.Remove(title);
        }
        else if (ButtonsFlip && button == "Maximize")
        {
            maximized.Add(title);
        }
        else if (ButtonsFlip && button == "Restore")
        {
            maximized.Remove(title);
        }

        AfterPress?.Invoke(title);
        readsSincePress = readsSincePress < 0 ? 0 : readsSincePress;
    }

    /// <summary>A window that opens after the first press, once this many window reads followed it.</summary>
    public (Win32WindowInfo Window, int AfterReads)? LateDialog { get; set; }

    private int readsSincePress = -1;

    public IReadOnlyList<Win32WindowInfo> Windows()
    {
        var late =
            LateDialog is { } dialog && readsSincePress >= 0 && ++readsSincePress > dialog.AfterReads
                ? new[] { dialog.Window }
                : [];
        return [new(Main, "Model - Tabular Editor 3", 7, true, false, false), .. Extra, .. late];
    }

    public Task Delay(int ms) => Task.CompletedTask;

    /// <summary>Floating windows whose own Close leaves them open.</summary>
    public HashSet<nint> StuckWindows { get; } = [];

    public async Task CloseWindow(nint hwnd)
    {
        var window = Extra.Single(w => w.Hwnd == hwnd);
        await inputs.Invoke(() => Task.FromResult(Sent("closed", false)), "windows_close");
        Presses.Add("CloseWindow " + window.Title);
        if (!StuckWindows.Contains(hwnd))
        {
            Extra.Remove(window);
        }
    }
}
