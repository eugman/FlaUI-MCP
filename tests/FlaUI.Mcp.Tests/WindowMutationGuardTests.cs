using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Focus, close and pointer parking are mutations: they take the same guards as input.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class WindowMutationGuardTests
{
    private static SessionManager Sessions(out string handle)
    {
        var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        handle = sessions.PublishWindow(null, FakeDesktop.TargetHwnd, FakeDesktop.TargetPid, FakeDesktop.Target);
        return sessions;
    }

    private static T Denied<T>(Func<T> run)
    {
        MutationGuard.BeforeMutation.Value = () => throw new InvalidOperationException("mutation denied");
        try
        {
            return run();
        }
        finally
        {
            MutationGuard.BeforeMutation.Value = null;
        }
    }

    [Fact]
    public void FocusChecksMutationPermissionBeforeFocusing()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        using var installed = desktop.Install();
        using var sessions = Sessions(out var handle);

        var error = Denied(() => Assert.Throws<InvalidOperationException>(() => sessions.FocusWindow(handle)));

        Assert.Equal("mutation denied", error.Message);
        Assert.Equal(0, desktop.FocusAttempts);
    }

    [Fact]
    public void FocusIsRefusedForAHigherIntegrityWindow()
    {
        var desktop = new FakeDesktop
        {
            ForegroundWindow = FakeDesktop.OtherHwnd,
            TargetIntegrity = new(ProcessIntegrity.High, false),
        };
        using var installed = desktop.Install();
        using var sessions = Sessions(out var handle);

        var error = Assert.Throws<InputRefusedException>(() => sessions.FocusWindow(handle));

        Assert.Equal("integrity-blocked", error.Refusal.Code);
        Assert.Equal(0, desktop.FocusAttempts);
    }

    [Fact]
    public async Task FocusWaitsForTheInputLease()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd };
        using var installed = desktop.Install();
        using var sessions = Sessions(out var handle);

        using (GuardedInput.AcquireLease())
        {
            var focus = Task.Run(() => sessions.FocusWindow(handle));
            await Assert.ThrowsAsync<TimeoutException>(() => focus.WaitAsync(TimeSpan.FromSeconds(10)));
        }

        Assert.Equal(0, desktop.FocusAttempts);
    }

    [Fact]
    public void CloseChecksMutationPermissionBeforePosting()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        using var sessions = Sessions(out var handle);

        Denied(() => Assert.Throws<InvalidOperationException>(() => sessions.CloseWindow(handle)));

        Assert.Equal(0, desktop.CloseRequests);
    }

    [Fact]
    public void CloseIsRefusedForAUiAccessWindow()
    {
        var desktop = new FakeDesktop { TargetIntegrity = new(ProcessIntegrity.Medium, true) };
        using var installed = desktop.Install();
        using var sessions = Sessions(out var handle);

        var error = Assert.Throws<InputRefusedException>(() => sessions.CloseWindow(handle));

        Assert.Equal("integrity-blocked", error.Refusal.Code);
        Assert.Equal(0, desktop.CloseRequests);
    }

    [Fact]
    public async Task UnpostedCloseIsAFailedNotDispatchedCall()
    {
        var desktop = new FakeDesktop { PostCloseResult = false };
        using var installed = desktop.Install();
        using var sessions = Sessions(out var handle);
        var registry = new ToolRegistry();
        registry.RegisterTool(new CloseWindowTool(sessions));

        var result = await registry.ExecuteToolAsync(
            "windows_close",
            JsonSerializer.SerializeToElement(new { handle })
        );

        Assert.True(result.IsError);
        Assert.Contains("not posted", result.Content[0].Text);
        Assert.Equal(1, desktop.CloseRequests);
        Assert.Equal("not-dispatched", result.Outcome!.Dispatch);
    }

    [Fact]
    public async Task PostedCloseCountsAsDispatchedInput()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        using var sessions = Sessions(out var handle);
        var registry = new ToolRegistry();
        registry.RegisterTool(new CloseWindowTool(sessions));

        var result = await registry.ExecuteToolAsync(
            "windows_close",
            JsonSerializer.SerializeToElement(new { handle })
        );

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(1, result.Outcome!.InputsSent);
    }

    [Fact]
    public void ParkChecksMutationPermissionBeforeMovingThePointer()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        Denied(() => Assert.Throws<InvalidOperationException>(() => PointerParking.Park(new Rectangle(0, 0, 10, 10))));

        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public void UiaFocusFallbackRefusesWhileTheProviderIsBlocked()
    {
        using var installed = new FakeDesktop().Install();
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        // Registered before a native handle was known, so focus would fall back to a UIA call.
        var window = (Window)RuntimeHelpers.GetUninitializedObject(typeof(Window));
        var handle = sessions.PublishWindow(window, 0, 77, null);
        var tracker = new PendingInvokeTracker(_ => 456);
        tracker.Begin(77, "Invoke on 'Open'");

        Assert.Throws<ProviderBlockedException>(() => sessions.FocusWindow(handle, tracker));
        Assert.Throws<ProviderBlockedException>(() => sessions.CloseWindow(handle, tracker));
    }
}
