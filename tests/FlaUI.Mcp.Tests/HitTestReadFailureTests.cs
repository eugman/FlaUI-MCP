using System.Drawing;
using System.Runtime.CompilerServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>A hit test that cannot read what lies beside the target refuses the press, as an unreadable hit does.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class HitTestReadFailureTests
{
    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    [Fact]
    public void UnreadableSiblingsOfTheTargetCountAsCoveringIt()
    {
        // The container answers for the point, so the siblings beside the target decide; they cannot be read.
        var window = Element();
        var pane = Element();
        var row = Element();
        var desktop = new FakeDesktop { HitTest = _ => pane };
        desktop.ControlTypes[window] = ControlType.Window;
        desktop.ControlTypes[pane] = ControlType.Pane;
        desktop.Parents[pane] = window;
        desktop.Parents[row] = pane;
        desktop.ElementBounds[row] = new Rectangle(0, 0, 100, 20);
        desktop.ChildrenUnreadable.Add(pane);
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(new Point(10, 10), row));

        Assert.Equal(RefusalCodes.ClickPointCovered, error.Code);
        Assert.Contains("could not be read", error.Message);
        Assert.Contains("no input sent", error.Message);
    }
}
