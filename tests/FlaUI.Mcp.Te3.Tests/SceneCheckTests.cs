using System.Drawing;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>The checks a scene capture takes before and after its grab, over scripted desktops; nothing is captured.</summary>
public sealed class SceneCheckTests
{
    private const nint Owner = 0x10;
    private const nint Dialog = 0x20;
    private const int Pid = 42;

    private static readonly Rectangle OwnerBounds = new(0, 0, 1920, 1080);
    private static readonly Rectangle DialogBounds = new(96, 192, 555, 254);
    private static readonly Rectangle Scene = new(0, 0, 900, 600);
    private static readonly Rectangle WorkArea = new(0, 0, 1920, 1040);

    private static Win32WindowInfo Window(nint hwnd, string title = "", int pid = Pid, bool cloaked = false) =>
        new(hwnd, title, pid, true, false, cloaked);

    private static SceneCheck.Spec Modal(Rectangle? scene = null) =>
        new(Owner, new InputTarget(Dialog, Pid, 0), scene ?? Scene, OwnerBounds, DialogBounds, Modal: true);

    private static SceneCheck.Desktop Desktop(
        nint? foreground = null,
        Win32WindowInfo[]? above = null,
        Dictionary<nint, Rectangle?>? bounds = null,
        Rectangle? workArea = null
    )
    {
        var known = new Dictionary<nint, Rectangle?> { [Owner] = OwnerBounds, [Dialog] = DialogBounds };
        foreach (var (hwnd, rectangle) in bounds ?? [])
        {
            known[hwnd] = rectangle;
        }

        return new(
            foreground ?? Dialog,
            [Window(Dialog, "Script output"), .. above ?? [], Window(Owner, "Model - Tabular Editor 3")],
            hwnd => known.TryGetValue(hwnd, out var r) ? r : null,
            [workArea ?? WorkArea]
        );
    }

    private static string Refusal(SceneCheck.Spec spec, SceneCheck.Desktop desktop) =>
        Assert.Throws<InvalidOperationException>(() => SceneCheck.Verify(spec, desktop)).Message;

    [Fact]
    public void AnUnchangedUnobscuredSceneMayBeGrabbed() => SceneCheck.Verify(Modal(), Desktop());

    [Fact]
    public void AModalSceneNeedsTheDialogInFront() =>
        Assert.Contains("focus or geometry changed", Refusal(Modal(), Desktop(foreground: Owner)));

    [Fact]
    public void AMenuSceneNeedsTheOwnerInFront() =>
        SceneCheck.Verify(Modal() with { Modal = false }, Desktop(foreground: Owner));

    [Fact]
    public void AMovedDialogStopsTheCapture() =>
        Assert.Contains(
            "focus or geometry changed",
            Refusal(Modal(), Desktop(bounds: new() { [Dialog] = DialogBounds with { X = 100 } }))
        );

    [Fact]
    public void ASceneOutsideTheOwnerIsClipped() =>
        Assert.Contains("is clipped", Refusal(Modal(new Rectangle(-10, 0, 900, 600)), Desktop()));

    [Fact]
    public void ASceneOffTheWorkAreaIsClipped() =>
        Assert.Contains("is clipped", Refusal(Modal(), Desktop(workArea: new Rectangle(0, 0, 800, 600))));

    [Fact]
    public void AnOutsideOwnerSceneMustBeTheUnionOfOwnerAndPopup()
    {
        var union = Rectangle.Union(OwnerBounds, DialogBounds);
        var spec = Modal(union) with { IncludeOutsideOwner = true };

        SceneCheck.Verify(spec, Desktop(workArea: union));
        Assert.Contains("is clipped", Refusal(spec with { Scene = Scene }, Desktop()));
    }

    [Fact]
    public void AnotherAppsWindowOverTheSceneObscuresIt()
    {
        var toast = Window(0x30, "Toast", pid: 7);

        var message = Refusal(Modal(), Desktop(above: [toast], bounds: new() { [0x30] = new(800, 500, 300, 200) }));

        Assert.Contains("obscured", message);
        Assert.Contains("title=Toast", message);
    }

    [Fact]
    public void AWindowWithUnreadableBoundsAboveTheOwnerCountsAsAnObstruction() =>
        Assert.Contains("obscured", Refusal(Modal(), Desktop(above: [Window(0x30, "Unknown", pid: 7)])));

    [Fact]
    public void CloakedOffSceneAndBelowOwnerWindowsDoNotObscure() =>
        SceneCheck.Verify(
            Modal(),
            Desktop(
                above: [Window(0x30, "Hidden", pid: 7, cloaked: true), Window(0x40, "Far", pid: 7)],
                bounds: new() { [0x30] = new(0, 0, 500, 500), [0x40] = new(1500, 900, 100, 100) }
            )
        );

    [Fact]
    public void ThePopupsOwnShadowDoesNotObscure()
    {
        var shadow = Window(0x30);
        var around = Rectangle.Inflate(DialogBounds, 10, 10);

        SceneCheck.Verify(Modal(), Desktop(above: [shadow], bounds: new() { [0x30] = around }));
    }

    [Fact]
    public void AnOwnerMissingFromTheWindowListIsReportedAsObscured()
    {
        var desktop = Desktop() with { Windows = [Window(Dialog, "Script output")] };

        Assert.Contains("obscured", Refusal(Modal(), desktop));
    }
}
