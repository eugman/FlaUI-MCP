using System.Drawing;
using FlaUI.Mcp.Core;

/// <summary>A docked pane or a document tab as te3_layout sees it; bounds are screen pixels.</summary>
public sealed record LayoutPart(string Title, string Kind, Rectangle Bounds);

/// <summary>
/// The TE3 window as the pane actions of te3_layout use it: read its panes and windows, press a pane's title-bar
/// button. The live one goes through Core's guarded click; tests script one.
/// </summary>
public interface ILayoutSurface
{
    /// <summary>The main window's UIA rectangle, which listed bounds are relative to.</summary>
    Rectangle Origin { get; }

    /// <summary>The main window's native handle.</summary>
    nint MainWindow { get; }

    /// <summary>Docked panes (kind "pane") and closable document tabs (kind "document").</summary>
    IReadOnlyList<LayoutPart> Parts();

    /// <summary>Whether the pane titled <paramref name="title"/> shows the button now (Close, Maximize, Restore).</summary>
    bool ButtonShown(string title, string button);

    /// <summary>Presses that button of that part; counted as input once it goes out.</summary>
    Task Press(string title, string button);

    /// <summary>A top-level window's screen bounds, or null when unreadable.</summary>
    Rectangle? BoundsOf(nint hwnd);

    /// <summary>A top-level window's class and owner, or null when it cannot be read.</summary>
    WindowShape? ShapeOf(nint hwnd);

    /// <summary>The TE3 process's top-level windows now.</summary>
    IReadOnlyList<Win32WindowInfo> Windows();

    /// <summary>Closes a top-level TE3 window (a floating pane) through Core's guarded windows_close.</summary>
    Task CloseWindow(nint hwnd);

    /// <summary>A pause between reads.</summary>
    Task Delay(int ms);
}
