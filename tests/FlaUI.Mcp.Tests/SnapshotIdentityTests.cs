using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A snapshot gives its handle an input identity only from that handle's own window: a scoped snapshot of a child
/// element (a pane with its own HWND, a WPF element with none) must not replace it with the child's.
/// </summary>
public sealed class SnapshotIdentityTests
{
    [Theory]
    [InlineData(500, true, 500, 500)] // the handle's own window
    [InlineData(0, false, 500, 500)] // a scoped WPF element: the handle's window, known to the caller
    [InlineData(777, false, 500, 500)] // a child pane with its own HWND: still the handle's window
    [InlineData(500, true, 0, 500)] // a window root, the handle's HWND unknown: the root
    [InlineData(777, false, 0, 0)] // a child root, the handle's HWND unknown: no identity is taken
    [InlineData(0, false, 0, 0)]
    public void TheIdentityComesFromTheHandlesOwnWindow(int root, bool rootIsWindow, int window, int expected) =>
        Assert.Equal((nint)expected, SnapshotBuilder.IdentityWindow(root, rootIsWindow, window));
}
