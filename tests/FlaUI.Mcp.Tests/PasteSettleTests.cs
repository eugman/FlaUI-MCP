using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// After Ctrl+V the control's value can take a while to change (an editor that reformats, a busy app); the
/// read-back waits up to 2 s for it, and stops early when the operation is cancelled or times out.
/// </summary>
public sealed class PasteSettleTests
{
    [Fact]
    public void AValueThatChangesAfterASecondIsStillSeen()
    {
        var clock = 0L;
        var value = PasteTool.SettledValue(
            () => clock >= 1200 ? "pasted" : "before",
            "before",
            () => clock,
            () => clock += 25,
            CancellationToken.None
        );

        Assert.Equal("pasted", value);
    }

    [Fact]
    public void TheWaitEndsAfterTwoSeconds()
    {
        var clock = 0L;
        var reads = 0;
        PasteTool.SettledValue(
            () =>
            {
                reads++;
                return "before";
            },
            "before",
            () => clock,
            () => clock += 25,
            CancellationToken.None
        );

        Assert.Equal(2000 / 25 + 1, reads);
    }

    [Fact]
    public void ACancelledOperationStopsTheWait()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var reads = 0;
        PasteTool.SettledValue(
            () =>
            {
                reads++;
                return "before";
            },
            "before",
            () => 0,
            () => { },
            stop.Token
        );

        Assert.Equal(1, reads);
    }
}
