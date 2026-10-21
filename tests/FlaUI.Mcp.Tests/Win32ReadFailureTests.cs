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
}
