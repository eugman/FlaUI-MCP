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

/// <summary>Tests written to kill mutants that survived in the click and mouse tools.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class PointerMutationTests
{
    private static readonly Point From = new(10, 10);
    private static readonly Point To = new(50, 10);

    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public void ADragPressesMovesAndReleasesThroughGuardedInput()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        PointerActions.Drag(FakeDesktop.Target, From, To, 4);

        // Move to the start, press, each path step, release.
        Assert.Equal(PointerPlan.DragPath(From, To, 4).Count + 3, desktop.Dispatched);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ADragOffTheTargetAtEitherEndSendsNothing(bool startCovered)
    {
        var covered = startCovered ? From : To;
        var desktop = new FakeDesktop
        {
            WindowUnder = point => point == covered ? FakeDesktop.OtherHwnd : FakeDesktop.TargetHwnd,
        };
        using var installed = desktop.Install();

        var error = Assert.Throws<InputRefusedException>(() => PointerActions.Drag(FakeDesktop.Target, From, To, 4));

        Assert.Equal("Point is not on the target window (off-window or covered); no input sent.", error.Message);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task AMouseRefFindsItsWindowWhenNoHandleIsGiven()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var (refs, sessions, tracker) = BlockedWindow();
        using var _ = sessions;
        var reference = refs.Register("w1", Element());

        var result = await new MouseTool(refs, tracker, sessions).ExecuteAsync(
            Args(new { action = "park", @ref = reference })
        );

        Assert.Equal("pending", result.Outcome!.Provider);
    }

    [Fact]
    public async Task AnExplicitHandleWinsOverTheRefsWindow()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var (refs, sessions, tracker) = BlockedWindow();
        using var _ = sessions;
        var elsewhere = refs.Register("w9", Element());

        var result = await new MouseTool(refs, tracker, sessions).ExecuteAsync(
            Args(
                new
                {
                    action = "park",
                    handle = "w1",
                    @ref = elsewhere,
                }
            )
        );

        Assert.Equal("pending", result.Outcome!.Provider);
    }

    private static (ElementRegistry, SessionManager, PendingInvokeTracker) BlockedWindow()
    {
        var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, FakeDesktop.TargetHwnd, FakeDesktop.TargetPid, FakeDesktop.Target);
        Assert.Equal("w1", handle);
        var tracker = new PendingInvokeTracker();
        tracker.Begin(FakeDesktop.TargetPid, "Invoke on 'Open...'");
        return (new ElementRegistry(), sessions, tracker);
    }

    [Fact]
    public void AnUnsupportedRequestedPatternIsNamed()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ClickTool.ChooseAction("toggle", false, ControlType.Button, _ => false)
        );

        Assert.Equal("This control does not support 'toggle'.", error.Message);
    }

    [Theory]
    [InlineData(new[] { "select", "invoke" }, "select")]
    [InlineData(new[] { "toggle" }, "toggle")]
    [InlineData(new[] { "invoke", "toggle" }, "invoke")]
    [InlineData(new[] { "select" }, "select")]
    [InlineData(new string[0], null)]
    public void TheDefaultPatternFollowsTheControl(string[] supported, string? expected)
    {
        var type = expected == "select" && supported.Length > 1 ? ControlType.TreeItem : ControlType.Button;

        Assert.Equal(expected, ClickTool.ChooseAction(null, false, type, supported.Contains));
    }

    [Fact]
    public void AModalIsNamedOnlyWhenItHasATitle()
    {
        Win32WindowInfo Window(nint hwnd, string title, bool enabled) => new(hwnd, title, 1, enabled, false, false);

        var titled = ClickTool.ClickFailure(
            "Blocked.",
            "X",
            [
                Window(1, "Main", false),
                Window(2, "", true) with
                {
                    Owner = 1,
                },
                Window(3, "Save As", true) with
                {
                    Owner = 1,
                },
            ]
        );
        var untitled = ClickTool.ClickFailure(
            "Blocked.",
            "X",
            [Window(1, "Main", false), Window(2, "", true) with { Owner = 1 }]
        );

        Assert.Contains("Window \"Save As\" appears to be a modal dialog", titled);
        Assert.Equal("Blocked.", untitled);
    }

    [Fact]
    public void ObservingASelectionStillHonoursCancellation()
    {
        Assert.Throws<OperationCanceledException>(() =>
            ClickTool.ObserveSelection(() => throw new OperationCanceledException())
        );
    }

    /// <summary>A clock that is never moved and cannot be: only a poll cap ends a wait on it.</summary>
    private sealed class FrozenClock : TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    [Fact]
    public void ASettlingReadOnAStillManualClockAdvancesItWithoutAPauseHook()
    {
        // The fakes that only hold the clock still set no OnPause; their pauses must still move it.
        var clock = new ManualTimeProvider();
        using var installed = new FakeDesktop { Clock = clock }.Install();
        var started = clock.GetTimestamp();

        Assert.False(Eventually.Returns(() => ClickTool.ObserveSelection(() => false)));
        Assert.Equal(TimeSpan.FromMilliseconds(500), clock.GetElapsedTime(started));
    }

    [Fact]
    public void ASettlingReadOnAClockThatNeverMovesStopsAfterItsPolls()
    {
        var polls = 0;
        using var installed = new FakeDesktop { Clock = new FrozenClock(), OnPause = _ => { } }.Install();

        Assert.False(
            Eventually.Returns(() =>
                ClickTool.ObserveSelection(() =>
                {
                    polls++;
                    return false;
                })
            )
        );
        // One read up front, then one after each 50 ms pause of the 500 ms window.
        Assert.Equal(11, polls);
    }

    [Fact]
    public void ObservingASelectionWaitsForTheProvider()
    {
        // The fake desktop's pauses move its clock, so the 500 ms observation window passes without waiting.
        var clock = new ManualTimeProvider();
        using var installed = new FakeDesktop { Clock = clock, OnPause = clock.Advance }.Install();
        var started = clock.GetTimestamp();

        var selected = ClickTool.ObserveSelection(() => clock.GetElapsedTime(started) > TimeSpan.FromMilliseconds(200));
        var selectedAt = clock.GetElapsedTime(started);
        var never = ClickTool.ObserveSelection(() => false);

        Assert.True(selected);
        Assert.Equal(TimeSpan.FromMilliseconds(250), selectedAt);
        Assert.False(never);
        Assert.Equal(TimeSpan.FromMilliseconds(500), clock.GetElapsedTime(started) - selectedAt);
    }

    [Theory]
    [InlineData(
        false,
        "Check windows_operation_status or take a screenshot by handle before continuing; do not repeat the click."
    )]
    [InlineData(
        true,
        "Invoked OK; the app's handler is still running. Check windows_operation_status or take a screenshot by handle before continuing; do not repeat the click."
    )]
    public void APendingClickSaysWhatToDoNext(bool dispatched, string ending)
    {
        var result = ClickTool.PatternResult(
            new PatternCallResult(PatternCallOutcome.StillPending, Dispatched: dispatched),
            "Invoked OK"
        );

        Assert.EndsWith(ending, result.Content[0].Text);
    }

    [Theory]
    [InlineData("invoke")]
    [InlineData("select")]
    [InlineData("expand")]
    [InlineData("collapse")]
    [InlineData("toggle")]
    public void EveryPatternActionCanBeRequested(string action)
    {
        Assert.Equal(action, ClickTool.ChooseAction(action, false, ControlType.Button, _ => true));
    }

    [Fact]
    public void PointerPointsAreResolvedUnderTheInputLease()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var held = false;

        PointerActions.Move(
            FakeDesktop.Target,
            () =>
            {
                var gate = DesktopLease.Held;
                held = gate;
                return From;
            },
            0
        );

        Assert.True(held);
        Assert.Equal(1, desktop.Dispatched);
    }
}
