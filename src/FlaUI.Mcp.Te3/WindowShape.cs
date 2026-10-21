using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// A top-level window's Win32 class and owner, which tell a floating dock pane from a dialog: the floating Dependencies
/// pane is an unowned WindowsForms10.Window.8 window (3.27.1, vte3b finding 7), while TE3's dialogs are owned by its
/// main window. Read without UI Automation.
/// </summary>
public sealed record WindowShape(string ClassName, nint Owner)
{
    private const uint OwnerQuery = 4; // GW_OWNER

    /// <summary>The window's class and owner, or null when the window is gone.</summary>
    public static WindowShape? Read(nint hwnd)
    {
        var name = new StringBuilder(256);
        return GetClassName(hwnd, name, name.Capacity) == 0 ? null : new(name.ToString(), GetWindow(hwnd, OwnerQuery));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hwnd, StringBuilder name, int capacity);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint hwnd, uint command);
}
