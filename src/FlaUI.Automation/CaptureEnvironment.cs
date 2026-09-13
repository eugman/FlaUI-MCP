using System.Drawing;
using System.Runtime.InteropServices;
using PlaywrightWindows.Mcp.Core;

/// <summary>Observed main-window display context, not an inferred screenshot scale.</summary>
public sealed record CaptureEnvironment(string Monitor, Rectangle MonitorBounds, Rectangle WorkingArea,
    Rectangle MainWindowBounds, uint MainWindowDpi)
{
    public double MainWindowScalePercent => MainWindowDpi * 100.0 / 96;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    public static CaptureEnvironment Observe(nint hwnd)
    {
        var bounds = Win32Desktop.GetWindowBounds(hwnd) ?? throw new InvalidOperationException("Capture window disappeared");
        var dpi = GetDpiForWindow(hwnd);
        if (dpi == 0) throw new InvalidOperationException("Could not read capture window DPI");
        var monitor = Screen.FromHandle(hwnd);
        return new(monitor.DeviceName, monitor.Bounds, monitor.WorkingArea, bounds, dpi);
    }

    public void RequireScale(int? expectedDpi)
    {
        if (expectedDpi is { } expected && expected != MainWindowDpi)
            throw new InvalidOperationException($"Capture requires {expected} DPI; main window reports {MainWindowDpi}. Display settings were not changed.");
    }
}
