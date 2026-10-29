using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FlaUI.Mcp.Core;

internal static class Win32WindowEnumeration
{
    internal static IReadOnlyList<Win32WindowInfo> Collect(
        Func<Func<nint, bool>, bool> enumerate,
        Func<nint, Win32WindowInfo?> read
    )
    {
        var windows = new List<Win32WindowInfo>();
        var completed = enumerate(hwnd =>
        {
            if (read(hwnd) is { } window)
            {
                windows.Add(window);
            }

            return true;
        });
        if (!completed)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "EnumWindows failed; window list is incomplete.");
        }

        return windows;
    }
}
