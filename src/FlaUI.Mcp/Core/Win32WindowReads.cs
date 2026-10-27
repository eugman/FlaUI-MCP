namespace FlaUI.Mcp.Core;

/// <summary>
/// The Win32 reads that can fail without throwing, behind delegates so offline tests can make them fail.
/// A failed primitive read gives 0 or null, never a value left over in the out parameter or buffer.
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

    /// <summary>The shape of GetWindowText and GetClassName: the characters copied, or 0 when none were.</summary>
    internal delegate int WindowTextReader(nint hwnd, char[] buffer, int capacity);

    private const int TitleCapacity = 512;
    private const int ClassNameCapacity = 256;

    /// <summary>
    /// The characters Windows reports it copied, or null when it copied none. Only the reported length is read,
    /// so a failed read cannot hand back what the buffer held before.
    /// </summary>
    internal static string? ReadText(nint hwnd, int capacity, WindowTextReader read)
    {
        var buffer = new char[capacity];
        var length = read(hwnd, buffer, capacity);
        return length > 0 ? new string(buffer, 0, Math.Min(length, capacity)) : null;
    }

    /// <summary>
    /// A visible top-level window as <see cref="GetTopLevelWindows"/> lists it, or null when it is skipped:
    /// another process's window when <paramref name="processId"/> is set, or a window whose owner cannot be
    /// read. A failed process read does not prove the window was destroyed; ordinary enumeration omits it
    /// because it cannot pass a process allowlist. Occlusion enumeration retains it with PID 0 when
    /// <paramref name="keepUnknownOwner"/> is true. An untitled window keeps an empty title.
    /// </summary>
    internal static Win32WindowInfo? ReadWindow(
        nint hwnd,
        int? processId,
        ThreadProcessReader owner,
        WindowTextReader title,
        bool keepUnknownOwner = false
    )
    {
        var pid = OwnerProcessId(hwnd, owner);
        if ((pid == 0 && !keepUnknownOwner) || (processId.HasValue && pid != processId.Value))
        {
            return null;
        }

        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        var isCloaked = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
        return new Win32WindowInfo(
            hwnd,
            ReadText(hwnd, TitleCapacity, title) ?? string.Empty,
            pid,
            IsWindowEnabled(hwnd),
            (exStyle & WS_EX_TOOLWINDOW) != 0,
            isCloaked
        )
        {
            Owner = GetWindow(hwnd, GwOwner),
            ClassName = GetWindowClassName(hwnd),
        };
    }
}
