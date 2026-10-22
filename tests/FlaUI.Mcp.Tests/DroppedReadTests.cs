using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A read that fails is never taken as "nothing there": not the window baseline a modal is detected against, not a
/// sibling's bounds in the hit test, and not the window a no-op state change skips. Each refuses before input.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class DroppedReadTests
{
    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    [Fact]
    public void AnUnreadableWindowBaselineRefusesBeforeThePatternCall()
    {
        var called = false;

        var error = Assert.Throws<InputRefusedException>(() =>
            ModalAwareInvoker.Execute(
                4321,
                "Invoke on 'Save'",
                () => called = true,
                new PendingInvokeTracker(_ => 1),
                TimeSpan.FromSeconds(1),
                _ => throw new System.ComponentModel.Win32Exception(5)
            )
        );

        Assert.Equal("window-baseline-unreadable", error.Refusal.Code);
        Assert.Contains("no input sent", error.Message);
        Assert.False(called);
    }

    [Fact]
    public void ASiblingWhoseBoundsCannotBeReadCountsAsCovering()
    {
        var desktop = new FakeDesktop();
        var target = Element();
        var list = Element();
        var unreadable = Element();
        desktop.Parents[target] = list;
        desktop.Parents[unreadable] = list;
        desktop.ControlTypes[list] = ControlType.List;
        desktop.Descriptions[unreadable] = "Custom 'overlay'";
        desktop.HitTest = _ => list;
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() => ClickPoint.Verify(new Point(10, 10), target));
        Assert.Contains("Custom 'overlay', whose bounds could not be read", error.Message);
    }

    [Fact]
    public void AModalIsNamedOnlyFromAWindowTheDisabledOneOwns()
    {
        var main = new Win32WindowInfo(1, "Main", 7, true, false, false);
        var baseline = new WindowBaseline([main]);
        var splash = new Win32WindowInfo(5, "Loading", 7, true, false, false);

        Assert.Equal("(unknown title)", baseline.NewModal([main with { IsEnabled = false }, splash]));
        Assert.Equal(
            "Save As",
            baseline.NewModal([
                main with
                {
                    IsEnabled = false,
                },
                splash,
                new(6, "Save As", 7, true, false, false) { Owner = 1 },
            ])
        );
    }

    [Fact]
    public async Task ANoOpStateChangeStillChecksTheWindowCanBeDriven()
    {
        var desktop = new FakeDesktop
        {
            State = WindowStates.Maximized,
            TargetIntegrity = new(ProcessIntegrity.High, false),
        };
        using var installed = desktop.Install();
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, FakeDesktop.TargetHwnd, FakeDesktop.TargetPid, FakeDesktop.Target);
        var registry = new ToolRegistry();
        registry.RegisterTool(new WindowStateTool(new ElementRegistry(), new PendingInvokeTracker(), sessions));

        var result = await registry.ExecuteToolAsync(
            "windows_set_window_state",
            JsonSerializer.SerializeToElement(new { handle, state = "maximize" })
        );

        Assert.True(result.IsError);
        Assert.Equal("integrity-blocked", result.Outcome!.Failure!.Code);
    }
}
