using System.Runtime.InteropServices;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

public sealed class Win32EnumerationTests
{
    [Fact]
    public void FailedEnumerationDoesNotReturnPartialWindowList()
    {
        var partial = new Win32WindowInfo(123, "partial", 42, true, false, false);

        var failure = Assert.Throws<System.ComponentModel.Win32Exception>(() =>
            Win32WindowEnumeration.Collect(
                visit =>
                {
                    visit(123);
                    Marshal.SetLastPInvokeError(5);
                    return false;
                },
                _ => partial
            )
        );
        Assert.Equal(5, failure.NativeErrorCode);
    }

    [Fact]
    public void CompleteEnumerationReturnsOnlyReadableWindows()
    {
        var visible = new Win32WindowInfo(123, "visible", 42, true, false, false);

        var windows = Win32WindowEnumeration.Collect(
            visit =>
            {
                visit(123);
                visit(456);
                return true;
            },
            hwnd => hwnd == 123 ? visible : null
        );

        Assert.Equal([visible], windows);
    }
}
