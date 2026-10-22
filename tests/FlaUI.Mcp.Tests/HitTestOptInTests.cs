using System.Drawing;
using System.Runtime.CompilerServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Two opt-ins a host chooses: overlays it knows do not take a press (Camtasia's selectionPlayhead line over a
/// clip) pass the hit test through, and a drag start can name elements it must not land on (TE3's neighbouring
/// pane captions), refused before the button goes down.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class HitTestOptInTests
{
    private static readonly Point Pressed = new(548, 1109);

    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    [Fact]
    public void AListedOverlayOverTheTargetPassesThrough()
    {
        var clip = Element();
        var playhead = Element();
        var desktop = new FakeDesktop { HitTest = _ => playhead };
        desktop.AutomationIds[playhead] = "selectionPlayhead";
        desktop.ElementBounds[clip] = new Rectangle(448, 1101, 200, 17);
        using var installed = desktop.Install();
        using var passThrough = ClickHitTest.PassThrough("selectionPlayhead", "informationPopup");

        Assert.Equal("pass-through", ClickPoint.Verify(Pressed, clip));
    }

    [Fact]
    public void AnOverlayNotListedStillRefuses()
    {
        var clip = Element();
        var playhead = Element();
        var desktop = new FakeDesktop { HitTest = _ => playhead };
        desktop.AutomationIds[playhead] = "selectionPlayhead";
        desktop.ElementBounds[clip] = new Rectangle(448, 1101, 200, 17);
        using var installed = desktop.Install();

        Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(Pressed, clip));
    }

    [Fact]
    public void AListedOverlayOutsideTheTargetDoesNotVouchForIt()
    {
        var clip = Element();
        var popup = Element();
        var desktop = new FakeDesktop { HitTest = _ => popup };
        desktop.AutomationIds[popup] = "informationPopup";
        desktop.ElementBounds[clip] = new Rectangle(0, 0, 10, 10);
        using var installed = desktop.Install();
        using var passThrough = ClickHitTest.PassThrough("informationPopup");

        Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(Pressed, clip));
    }

    [Fact]
    public void ADragStartOnAnElementItMustNotHitIsRefused()
    {
        var caption = Element();
        var captionText = Element();
        var desktop = new FakeDesktop { HitTest = _ => captionText };
        desktop.Parents[captionText] = caption;
        desktop.Descriptions[captionText] = "Text 'Properties'";
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() =>
            ClickHitTest.RequireNotWithin(Pressed, target: Element(), [caption])
        );
        Assert.Equal("click-point-covered", error.Refusal.Code);
        Assert.Contains("must not be pressed", error.Message);
    }

    [Fact]
    public void ADragStartElsewherePasses()
    {
        var caption = Element();
        var splitter = Element();
        using var installed = new FakeDesktop { HitTest = _ => splitter }.Install();

        ClickHitTest.RequireNotWithin(Pressed, target: Element(), [caption]);
    }
}
