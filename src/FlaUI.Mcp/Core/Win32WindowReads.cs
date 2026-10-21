namespace FlaUI.Mcp.Core;

/// <summary>
/// The Win32 reads that can fail without throwing, behind delegates so offline tests can make them fail.
/// A failed read gives 0 or null, never a value left over in the out parameter or buffer.
/// </summary>
public static partial class Win32Desktop
{
    /// <summary>The shape of GetWindowThreadProcessId: the thread id, or 0 when the window cannot be read.</summary>
    internal delegate uint ThreadProcessReader(nint hwnd, out uint processId);

    /// <summary>
    /// The window's owning process, or 0 when Windows cannot say. A zero thread id means the read failed,
    /// whatever the out value holds, so callers refuse it as they refuse any unknown owner
    /// (<see cref="InputTarget.Capture"/>: "Cannot establish window ownership").
    /// </summary>
    internal static int OwnerProcessId(nint hwnd, ThreadProcessReader read)
    {
        var threadId = read(hwnd, out var processId);
        return threadId == 0 ? 0 : (int)processId;
    }
}
