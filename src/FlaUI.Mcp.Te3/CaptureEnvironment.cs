using System.Drawing;
using FlaUI.Mcp.Core;

/// <summary>Observed main-window display context, not an inferred screenshot scale.</summary>
/// <param name="MonitorDpi">
/// The monitor's DPI. Recorded beside the window's because a window that has not re-negotiated
/// after a scaling change keeps its old DPI, and a native capture then renders at that scale into a
/// bitmap sized for this one — correctly sized, wrongly scaled (FlaUI.md 26). Null when unreadable.
/// </param>
public sealed record CaptureEnvironment(
    string Monitor,
    Rectangle MonitorBounds,
    Rectangle WorkingArea,
    Rectangle MainWindowBounds,
    uint MainWindowDpi,
    uint? MonitorDpi = null
)
{
    public double MainWindowScalePercent => MainWindowDpi * 100.0 / 96;

    /// <summary>Whether the window and its monitor disagree about DPI; see <see cref="MonitorDpi"/>.</summary>
    public bool DpiMismatch => MonitorDpi is { } monitor && monitor != MainWindowDpi;

    public static CaptureEnvironment Observe(nint hwnd)
    {
        var bounds =
            Win32Desktop.GetWindowBounds(hwnd) ?? throw new InvalidOperationException("Capture window disappeared");
        var dpi =
            DpiUtility.WindowDpi(hwnd) ?? throw new InvalidOperationException("Could not read capture window DPI");
        var monitor = Screen.FromHandle(hwnd);
        return new(monitor.DeviceName, monitor.Bounds, monitor.WorkingArea, bounds, dpi, DpiUtility.MonitorDpi(hwnd));
    }

    public void RequireScale(int? expectedDpi)
    {
        if (expectedDpi is { } expected && expected != MainWindowDpi)
        {
            throw new InvalidOperationException(
                $"Capture requires {expected} DPI; main window reports {MainWindowDpi}. Display settings were not changed."
            );
        }
    }
}
