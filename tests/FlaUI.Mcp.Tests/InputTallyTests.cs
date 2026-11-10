using System.Runtime.InteropServices;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// What the low-level hooks record, driven with fabricated hook structs: where the injected flag sits in each
/// struct, that only the user's own events count, and what is kept of them (a count and a kind; for a button-down
/// its point; never a key).
/// </summary>
public sealed class InputTallyTests
{
    private const int KeyDown = 0x0100;
    private const int LeftDown = 0x0201;
    private const int LeftUp = 0x0202;
    private const int Wheel = 0x020A;

    /// <summary>A KBDLLHOOKSTRUCT: vkCode, scanCode, flags, time, extra.</summary>
    private static void Key(InputTally tally, uint flags, int vkCode = 0x41)
    {
        var data = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.WriteInt32(data, 0, vkCode);
            Marshal.WriteInt32(data, 4, 0x1E);
            Marshal.WriteInt32(data, 8, unchecked((int)flags));
            Marshal.WriteInt32(data, 12, 0);
            Marshal.WriteInt64(data, 16, 0);
            tally.Observe(true, KeyDown, data, now: 10_000);
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    /// <summary>An MSLLHOOKSTRUCT: point x, y, mouseData, flags, time, extra.</summary>
    private static void Mouse(InputTally tally, int message, int x, int y, uint flags, long now = 10_000)
    {
        var data = Marshal.AllocHGlobal(32);
        try
        {
            Marshal.WriteInt32(data, 0, x);
            Marshal.WriteInt32(data, 4, y);
            Marshal.WriteInt32(data, 8, 0);
            Marshal.WriteInt32(data, 12, unchecked((int)flags));
            Marshal.WriteInt32(data, 16, 0);
            Marshal.WriteInt64(data, 24, 0);
            tally.Observe(false, message, data, now);
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
    }

    [Fact]
    public void AUsersKeyCountsOnceAndKeepsNoKey()
    {
        var tally = new InputTally();

        Key(tally, flags: 0);

        Assert.Equal(1, tally.Sequence);
        Assert.Equal(new InputCounts(1, 0), tally.Counts);
        var last = tally.LastEvent!;
        Assert.Equal((1L, "keyboard", (string?)null), (last.Sequence, last.Kind, last.MouseKind));
    }

    [Theory]
    [InlineData(0x10u)] // LLKHF_INJECTED
    [InlineData(0x02u)] // LLKHF_LOWER_IL_INJECTED
    [InlineData(0x12u)]
    public void AnInjectedKeyIsNotTheUsers(uint flags)
    {
        var tally = new InputTally();

        Key(tally, flags);

        Assert.Equal(0, tally.Sequence);
        Assert.Equal(new InputCounts(0, 0), tally.Counts);
        Assert.Null(tally.LastEvent);
    }

    [Theory]
    [InlineData(0x01u)] // LLKHF_EXTENDED
    [InlineData(0x20u)] // LLKHF_ALTDOWN
    [InlineData(0x80u)] // LLKHF_UP
    public void AKeysOtherFlagsDoNotMakeItInjected(uint flags)
    {
        var tally = new InputTally();

        Key(tally, flags);

        Assert.Equal(1, tally.Sequence);
    }

    [Fact]
    public void TheKeyboardFlagsAreReadAtTheirOwnOffsetNotTheMouseOne()
    {
        // Offset 8 holds the keyboard flags; a keyboard struct's offset 12 (time) must not be taken for them.
        var tally = new InputTally();
        var data = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.WriteInt32(data, 0, 0x41);
            Marshal.WriteInt32(data, 4, 0x1E);
            Marshal.WriteInt32(data, 8, 0);
            Marshal.WriteInt32(data, 12, 0x13);
            Marshal.WriteInt64(data, 16, 0);
            tally.Observe(true, KeyDown, data, now: 10_000);
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }

        Assert.Equal(1, tally.Sequence);
    }

    [Theory]
    [InlineData(0x01u)] // LLMHF_INJECTED
    [InlineData(0x02u)] // LLMHF_LOWER_IL_INJECTED
    public void AnInjectedClickIsNotTheUsers(uint flags)
    {
        var tally = new InputTally();

        Mouse(tally, LeftDown, 300, 400, flags);

        Assert.Equal(0, tally.Sequence);
        Assert.Equal(new InputCounts(0, 0), tally.Counts);
    }

    [Fact]
    public void AUsersButtonDownIsCountedWithItsPointAndTheUpIsNot()
    {
        var tally = new InputTally();

        Mouse(tally, LeftDown, 300, 400, flags: 0);
        Mouse(tally, LeftUp, 300, 400, flags: 0);

        Assert.Equal(2, tally.Sequence);
        Assert.Equal(new InputCounts(0, 1, new System.Drawing.Point(300, 400)), tally.Counts);
        Assert.Equal("button", tally.LastEvent!.MouseKind);
    }

    [Fact]
    public void AButtonDownOnAMonitorLeftOfOrAboveThePrimaryKeepsItsNegativePoint()
    {
        var tally = new InputTally();

        Mouse(tally, LeftDown, -1920, -5, flags: 0);

        Assert.Equal(new System.Drawing.Point(-1920, -5), tally.Counts.LastButtonDown);
    }

    [Fact]
    public void AWheelAndAMoveAreNamedByKind()
    {
        var tally = new InputTally();

        Mouse(tally, Wheel, 10, 10, flags: 0);
        Assert.Equal("wheel", tally.LastEvent!.MouseKind);

        // No injected reference point yet, so this move is not drift: it counts.
        Mouse(tally, PhysicalInputObserver.MouseMove, 500, 500, flags: 0);
        Assert.Equal(
            (2L, "mouse", "move"),
            (tally.LastEvent!.Sequence, tally.LastEvent.Kind, tally.LastEvent.MouseKind)
        );
        Assert.Equal(new InputCounts(0, 0), tally.Counts);
    }

    [Fact]
    public void ATinyDriftAfterAnInjectedMoveIsToleratedThroughTheSameWiring()
    {
        // The hook hands every mouse event, injected too, to the drift filter with its point and the clock.
        var tally = new InputTally();
        Mouse(tally, PhysicalInputObserver.MouseMove, 500, 500, flags: 0x01, now: 10_000);

        Mouse(tally, PhysicalInputObserver.MouseMove, 503, 501, flags: 0, now: 12_000);

        Assert.Equal(0, tally.Sequence);
        Assert.Equal(1, tally.ToleratedMoves);
    }
}
