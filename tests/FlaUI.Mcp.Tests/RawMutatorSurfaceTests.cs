using System.Reflection;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// The raw Win32 mutators skip the input lease, the integrity and permission checks, the count and the
/// offline switch. Product code reaches them only through the guarded API, so a new bypass does not compile.
/// </summary>
public sealed class RawMutatorSurfaceTests
{
    [Theory]
    [InlineData("FocusWindow")]
    [InlineData("CloseWindow")]
    [InlineData("PlaceWindow")]
    [InlineData("SetWindowState")]
    [InlineData("SetClipboardText")]
    [InlineData("PreserveAndSetClipboardText")]
    public void TheRawMutatorIsNotPublic(string name) =>
        Assert.DoesNotContain(
            typeof(Win32Desktop).GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.Name == name
        );
}
