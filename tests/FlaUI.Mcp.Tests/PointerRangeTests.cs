using System.Drawing;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

public sealed class PointerRangeTests
{
    [Fact]
    public void DragPathInterpolatesAcrossTheFullIntegerRangeWithoutWrapping()
    {
        var path = PointerPlan.DragPath(
            new Point(int.MinValue, int.MinValue),
            new Point(int.MaxValue, int.MaxValue),
            2
        );
        Assert.Equal(Point.Empty, path[0]);
        Assert.Equal(new Point(int.MaxValue, int.MaxValue), path[1]);
    }
}
