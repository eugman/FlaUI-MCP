using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>A failed read is not absence: whether a control is on screen; scripted reads only.</summary>
public sealed class ScreenPresenceTests
{
    [Fact]
    public void AControlThatIsNotFoundIsNotOnScreen() =>
        Assert.False(ScreenPresence.Read(() => throw new ElementNotFoundException("found 0"), "Show changes"));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AReadThatWorkedIsTheAnswer(bool visible) =>
        Assert.Equal(visible, ScreenPresence.Read(() => visible, "x"));

    [Fact]
    public void AReadThatFailedIsAFailureNotAbsence()
    {
        // A COM failure read as "no Show changes checkbox"; the walk then reported "No X under table".
        var error = Assert.Throws<InvalidOperationException>(() =>
            ScreenPresence.Read(
                () => throw new System.Runtime.InteropServices.COMException("RPC busy", unchecked((int)0x8001010A)),
                "the TOM Explorer Show changes checkbox"
            )
        );

        Assert.Contains("Show changes", error.Message);
        Assert.Contains("not known", error.Message);
    }

    [Fact]
    public void AnIncompleteSearchIsAFailureNotAbsence() =>
        Assert.Throws<InvalidOperationException>(() =>
            ScreenPresence.Read(() => throw new InvalidOperationException("Incomplete UIA search"), "x")
        );

    [Fact]
    public void CancellationStillPropagates() =>
        Assert.Throws<OperationCanceledException>(() =>
            ScreenPresence.Read(() => throw new OperationCanceledException(), "x")
        );
}
