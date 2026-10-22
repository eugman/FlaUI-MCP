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

    /// <summary>A clip and a playhead overlay over it, each under <paramref name="overlayWindow"/> or the clip's.</summary>
    private static (FakeDesktop Desktop, AutomationElement Clip, AutomationElement Playhead) Timeline(
        bool overlayInAnotherWindow = false
    )
    {
        var main = Element();
        var other = Element();
        var clip = Element();
        var playhead = Element();
        var desktop = new FakeDesktop { HitTest = _ => playhead };
        desktop.ControlTypes[main] = FlaUI.Core.Definitions.ControlType.Window;
        desktop.ControlTypes[other] = FlaUI.Core.Definitions.ControlType.Window;
        desktop.Parents[clip] = main;
        desktop.Parents[playhead] = overlayInAnotherWindow ? other : main;
        desktop.AutomationIds[playhead] = "selectionPlayhead";
        desktop.Descriptions[playhead] = "Pane 'selectionPlayhead'";
        desktop.ElementBounds[clip] = new Rectangle(448, 1101, 200, 17);
        return (desktop, clip, playhead);
    }

    [Fact]
    public void AListedOverlayOverTheTargetPassesThrough()
    {
        var (desktop, clip, _) = Timeline();
        using var installed = desktop.Install();
        using var passThrough = ClickHitTest.PassThrough("selectionPlayhead", "informationPopup");

        Assert.Equal("pass-through", ClickPoint.Verify(Pressed, clip));
    }

    [Fact]
    public async Task ARegistrationInAnotherFlowIsNotSeen()
    {
        // The list was process-wide, so a test registering it made unrelated tests running beside it pass through.
        var (desktop, clip, _) = Timeline();
        using var installed = desktop.Install();
        using var registered = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        var other = Task.Run(() =>
        {
            using var passThrough = ClickHitTest.PassThrough("selectionPlayhead");
            registered.Set();
            done.Wait(Eventually.Bound);
        });
        try
        {
            Assert.True(registered.Wait(Eventually.Bound));
            Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(Pressed, clip));
        }
        finally
        {
            done.Set();
            await other;
        }
    }

    [Fact]
    public async Task ARegistrationReachesTheCallsItsFlowStartsLater()
    {
        // A host registers at startup, then serves: every call it starts afterwards must see the list.
        var (desktop, clip, _) = Timeline();
        using var installed = desktop.Install();
        using var passThrough = ClickHitTest.PassThrough("selectionPlayhead");

        Assert.Equal("pass-through", await Task.Run(() => ClickPoint.Verify(Pressed, clip)));
    }

    [Fact]
    public void AListedOverlayInAnotherWindowDoesNotPassThrough()
    {
        // The AutomationId alone would wave through any window's element of that name, a popup or another app's.
        var (desktop, clip, _) = Timeline(overlayInAnotherWindow: true);
        using var installed = desktop.Install();
        using var passThrough = ClickHitTest.PassThrough("selectionPlayhead");

        var error = Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(Pressed, clip));
        Assert.Equal("click-point-covered", error.Refusal.Code);
    }

    [Fact]
    public void AListedOverlayWhoseWindowCannotBeReadDoesNotPassThrough()
    {
        var (desktop, clip, playhead) = Timeline();
        desktop.Parents.Remove(playhead);
        using var installed = desktop.Install();
        using var passThrough = ClickHitTest.PassThrough("selectionPlayhead");

        Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(Pressed, clip));
    }

    [Fact]
    public void TheHostsCheckCanRefuseAPassThrough()
    {
        // Camtasia: an acting part (a trim handle, say) under the point means the press would act, not select.
        var (desktop, clip, _) = Timeline();
        using var installed = desktop.Install();
        using var passThrough = ClickHitTest.PassThrough(
            (point, target, overlay) => point == Pressed ? "a trim handle lies under the point" : null,
            "selectionPlayhead"
        );

        var error = Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(Pressed, clip));
        Assert.Contains("a trim handle lies under the point", error.Message);
        Assert.Contains("no input sent", error.Message);
    }

    [Fact]
    public void TheHostsCheckSeesTheTargetAndTheOverlayAndCanAllow()
    {
        var (desktop, clip, playhead) = Timeline();
        using var installed = desktop.Install();
        (AutomationElement Target, AutomationElement Overlay)? seen = null;
        using var passThrough = ClickHitTest.PassThrough(
            (_, target, overlay) =>
            {
                seen = (target, overlay);
                return null;
            },
            "selectionPlayhead"
        );

        Assert.Equal("pass-through", ClickPoint.Verify(Pressed, clip));
        Assert.Same(clip, seen!.Value.Target);
        Assert.Same(playhead, seen.Value.Overlay);
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
