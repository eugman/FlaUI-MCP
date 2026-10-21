using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Win32 reads that fail report nothing, so a stale out value or buffer cannot pass a check.</summary>
public sealed class Win32ReadFailureTests
{
    private const uint StaleProcessId = 4242;

    // Windows returned no thread id, but the out parameter still holds a process id.
    private static uint FailedOwnerRead(nint hwnd, out uint processId)
    {
        processId = StaleProcessId;
        return 0;
    }

    private static uint OwnerRead(nint hwnd, out uint processId)
    {
        processId = StaleProcessId;
        return 1;
    }

    [Fact]
    public void AFailedOwnerReadReportsNoProcess()
    {
        Assert.Equal(0, Win32Desktop.OwnerProcessId(100, FailedOwnerRead));
    }

    [Fact]
    public void AnOwnerReadReportsItsProcess()
    {
        Assert.Equal((int)StaleProcessId, Win32Desktop.OwnerProcessId(100, OwnerRead));
    }

    // Windows copied nothing, but the buffer already holds text.
    private static int FailedTextRead(nint hwnd, char[] buffer, int capacity)
    {
        "stale".CopyTo(buffer);
        return 0;
    }

    // Windows copied three characters into a buffer that holds more.
    private static int ShortTextRead(nint hwnd, char[] buffer, int capacity)
    {
        "abcdef".CopyTo(buffer);
        return 3;
    }

    [Fact]
    public void AFailedTextReadReportsNoText()
    {
        Assert.Null(Win32Desktop.ReadText(100, 16, FailedTextRead));
    }

    [Fact]
    public void ATextReadReportsOnlyTheCopiedLength()
    {
        Assert.Equal("abc", Win32Desktop.ReadText(100, 16, ShortTextRead));
    }

    // hwnd 0 keeps the window style and cloak reads, which are not faked, from reaching a real window.
    [Fact]
    public void EnumerationSkipsAWindowWhoseOwnerCannotBeRead()
    {
        Assert.Null(Win32Desktop.ReadWindow(0, null, FailedOwnerRead, ShortTextRead));
        Assert.Null(Win32Desktop.ReadWindow(0, (int)StaleProcessId, FailedOwnerRead, ShortTextRead));
    }

    [Fact]
    public void EnumerationKeepsAnUntitledWindowOfTheRequestedProcess()
    {
        var window = Win32Desktop.ReadWindow(0, (int)StaleProcessId, OwnerRead, FailedTextRead);

        Assert.NotNull(window);
        Assert.Equal(string.Empty, window.Title);
        Assert.Equal((int)StaleProcessId, window.ProcessId);
        Assert.Null(Win32Desktop.ReadWindow(0, 1, OwnerRead, ShortTextRead));
    }

    // Not a window: user32 rejects the handle, so every read about it fails.
    private const nint NoWindow = unchecked((nint)0x7FFFFFFF);

    [Fact]
    public void DescribeSaysWhenAWindowCannotBeRead()
    {
        Assert.Equal($"hwnd={NoWindow}, pid=0, class=unreadable", Win32Desktop.Describe(NoWindow));
    }
}
