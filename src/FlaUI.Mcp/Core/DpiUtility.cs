using System;
using System.Runtime.InteropServices;

namespace FlaUI.Mcp.Core;

public static class DpiUtility
{
    // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
    private static readonly IntPtr PerMonitorV2 = new IntPtr(-4);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);

    public static void EnablePerMonitorV2() =>
        EnablePerMonitorV2(
            () => SetProcessDpiAwarenessContext(PerMonitorV2),
            () => AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), PerMonitorV2)
        );

    /// <summary>
    /// Make the process per-monitor-v2 aware or refuse to start. The call fails when a manifest or host
    /// already set an awareness; that is acceptable only when the awareness in force is per-monitor-v2.
    /// Anything else silently scales every capture bound and click point.
    /// </summary>
    internal static void EnablePerMonitorV2(Func<bool> set, Func<bool> isPerMonitorV2)
    {
        if (set() || isPerMonitorV2())
        {
            return;
        }

        throw new InvalidOperationException(
            "Windows refused to make this process per-monitor-v2 DPI aware, and the awareness in force is "
                + "something else. Capture bounds and click points would be scaled wrongly on this desktop, so "
                + "FlaUI-MCP will not start. Remove any DPI setting from the host's manifest or compatibility "
                + "options, or start it from a process that is not DPI-virtualized."
        );
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    /// <summary>
    /// The DPI a window is laying itself out for, or null when it cannot be read.
    /// </summary>
    /// <param name="hwnd">The window to measure.</param>
    /// <returns>Its DPI, or null.</returns>
    public static uint? WindowDpi(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? null
        : GetDpiForWindow(hwnd) is var dpi && dpi != 0 ? dpi
        : null;

    /// <summary>
    /// The DPI of the monitor a window currently sits on, or null when it cannot be read.
    /// Accurate only because the process is per-monitor-v2 aware: without that, this reports the
    /// system DPI for every monitor and would invent mismatches on a mixed-DPI desktop.
    /// </summary>
    /// <param name="hwnd">The window whose monitor to measure.</param>
    /// <returns>That monitor's effective DPI, or null.</returns>
    public static uint? MonitorDpi(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }
        // MONITOR_DEFAULTTONEAREST keeps a window straddling an edge resolvable.
        var monitor = MonitorFromWindow(hwnd, 2);
        if (monitor == IntPtr.Zero)
        {
            return null;
        }
        // MDT_EFFECTIVE_DPI. Windows reports square DPI in practice, so dpiY is discarded.
        return GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX != 0 ? dpiX : null;
    }

    /// <summary>
    /// Explain a window/monitor DPI disagreement, which invalidates every geometry assumption a
    /// capture makes. Observed in the field after a scaling change with the app left running: the
    /// native (PrintWindow) render came back oversized by exactly the ratio of the two values while
    /// reporting the expected bitmap dimensions.
    /// </summary>
    /// <param name="windowDpi">DPI the window is laying out for.</param>
    /// <param name="monitorDpi">DPI of the monitor it is on.</param>
    /// <returns>A warning to attach to the result, or null when the two agree or are unknown.</returns>
    public static string? DescribeMismatch(uint? windowDpi, uint? monitorDpi)
    {
        if (windowDpi is not { } window || monitorDpi is not { } monitor || window == monitor)
        {
            return null;
        }

        var ratio = (double)window / monitor;
        // Deliberately not prescribing a restart: DPI-unaware processes always report 96, and
        // system-DPI-aware windows can retain the primary display's sign-in DPI on another monitor.
        return $"Window DPI {window} does not match monitor DPI {monitor} (ratio {ratio:0.###}). "
            + "Geometry from this capture is unreliable: a native capture can render at the window's "
            + "scale into a bitmap sized for the monitor's. Common after changing display scaling with "
            + "the app still running, docking or undocking, or moving between mixed-DPI monitors; also "
            + "expected for system-DPI-aware apps on a monitor that differs from the primary "
            + "display's sign-in DPI, and permanent for DPI-unaware apps, where the screen pixels "
            + "are correct.";
    }
}
