namespace FlaUI.Mcp.Core;

/// <summary>Show-state of a top-level window: normal, maximized or minimized.</summary>
public static partial class Win32Desktop
{
    private const int ShowMaximized = 3;
    private const int ShowMinimized = 6;
    private const int ShowRestore = 9;

    public static string GetWindowState(nint hwnd)
    {
        if (IsIconic(hwnd))
        {
            return WindowStates.Minimized;
        }

        return IsZoomed(hwnd) ? WindowStates.Maximized : WindowStates.Normal;
    }

    /// <summary>ShowWindow for the state (twice for normal from minimized-maximized). ShowWindow returns the previous visibility, not success.</summary>
    public static void SetWindowState(nint hwnd, string requested)
    {
        var command = requested switch
        {
            WindowStates.Maximized => ShowMaximized,
            WindowStates.Minimized => ShowMinimized,
            WindowStates.Normal => ShowRestore,
            _ => throw new ArgumentException($"Unknown window state '{requested}'"),
        };
        ShowWindow(hwnd, command);
        // A window minimized from maximized restores to maximized; one more restore reaches normal.
        if (requested == WindowStates.Normal && IsZoomed(hwnd))
        {
            ShowWindow(hwnd, ShowRestore);
        }
    }
}

public static class WindowStates
{
    public const string Normal = "normal";
    public const string Maximized = "maximized";
    public const string Minimized = "minimized";

    public static readonly string[] All = [Normal, Maximized, Minimized];

    /// <summary>Accepts the verbs agents reach for ("maximize", "restore") as well as the states.</summary>
    public static string Parse(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "normal" or "restore" or "restored" => Normal,
            "maximize" or "maximized" => Maximized,
            "minimize" or "minimized" => Minimized,
            _ => throw new ArgumentException("state must be one of normal|restore, maximize, minimize"),
        };
}
