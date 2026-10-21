using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using static FlaUI.Mcp.IntegrationTests.PointerTestSupport;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// What GuardedInput reports it changed before any input (activation, control focus), how it counts input,
/// and the identity checks it runs first. Each test names the mutation-testing survivors it kills.
/// </summary>
[Collection("TestApps")]
public class GuardedInputChangeTests(TestAppFixture fixture)
{
    private nint WinFormsHwnd => fixture.Session.GetWindowHwnd(fixture.WinFormsHandle);
    private nint WpfHwnd => fixture.Session.GetWindowHwnd(fixture.WpfHandle);

    [Fact]
    public void ActivatingABackgroundTargetIsReportedAndCounted()
    {
        // Kills GuardedInput:103 Equality (activated never true), GuardedInput:104 Logical, Negate and LogicalNot
        // (an activation reported as no change), GuardedInput:109 Statement (activation not counted),
        // GuardedInput:110 String (empty note).
        Foreground(WinFormsHwnd);
        var context = Counting();
        try
        {
            using var input = new GuardedInput(fixture.Session.GetInputTarget(fixture.WpfHandle));
            Assert.Equal("the target window was activated", input.Changed);
            Assert.Equal(1, context.InputsSent);
            Assert.Equal(WpfHwnd, Win32Desktop.GetForegroundWindow());
        }
        finally
        {
            StopCounting();
        }
    }

    [Fact]
    public void TargetAlreadyInFrontWithoutAControlChangesNothing()
    {
        // Kills GuardedInput:57 Negate (no element still reported as "the control was focused").
        Foreground(WpfHwnd);
        var context = Counting();
        try
        {
            using var input = new GuardedInput(fixture.Session.GetInputTarget(fixture.WpfHandle));
            Assert.Null(input.Changed);
            Assert.Equal(0, context.InputsSent);
        }
        finally
        {
            StopCounting();
        }
    }

    [Fact]
    public void FocusingTheControlIsReportedOnceAndNotWhenItAlreadyHasFocus()
    {
        // Kills GuardedInput:118 Negate and LogicalNot, GuardedInput:124 Boolean (a gained focus reported as
        // none), GuardedInput:123 Statement (focus change not counted), GuardedInput:59 String (empty note),
        // and, on the second guard, GuardedInput:118 Logical (hadFocus && ...) and GuardedInput:120 Boolean
        // (an unchanged focus reported as a change).
        SelectTab(fixture, fixture.WinFormsHandle, "FormsTab");
        var name = Resolve(fixture, fixture.WinFormsHandle, new(AutomationId: "NameTextBox"));
        var password = Resolve(fixture, fixture.WinFormsHandle, new(AutomationId: "PasswordTextBox"));
        var target = fixture.Session.GetInputTarget(fixture.WinFormsHandle);
        Foreground(WinFormsHwnd);
        password.Focus();
        Assert.True(WaitFor(() => password.Properties.HasKeyboardFocus.Value));
        var context = Counting();
        try
        {
            using (var input = new GuardedInput(target, name))
            {
                Assert.Equal("the control was focused", input.Changed);
                Assert.Equal(1, context.InputsSent);
            }

            Assert.True(name.Properties.HasKeyboardFocus.Value);
            using (var again = new GuardedInput(target, name))
            {
                Assert.Null(again.Changed);
                Assert.Equal(1, context.InputsSent);
            }
        }
        finally
        {
            StopCounting();
        }
    }

    [Fact]
    public void ActivationIsTheFirstChangeEvenWhenTheControlIsFocusedAfterIt()
    {
        // Kills GuardedInput:59 CoalesceAssignment (the control focus overwrites the activation note).
        SelectTab(fixture, fixture.WinFormsHandle, "FormsTab");
        var name = Resolve(fixture, fixture.WinFormsHandle, new(AutomationId: "NameTextBox"));
        var password = Resolve(fixture, fixture.WinFormsHandle, new(AutomationId: "PasswordTextBox"));
        Foreground(WinFormsHwnd);
        password.Focus();
        Assert.True(WaitFor(() => password.Properties.HasKeyboardFocus.Value));
        Foreground(WpfHwnd);
        using var input = new GuardedInput(fixture.Session.GetInputTarget(fixture.WinFormsHandle), name);
        Assert.Equal("the target window was activated", input.Changed);
        Assert.True(name.Properties.HasKeyboardFocus.Value);
    }

    [Fact]
    public void TypingSendsOneChunkPerSixtyFourCharacters()
    {
        // Kills GuardedInput:397 Equality (> 64, <= 64), Negate, GuardedInput:399 Statement (one send per
        // character) and GuardedInput:406 Equality (>= 0 sends an empty trailing chunk).
        SelectTab(fixture, fixture.WinFormsHandle, "FormsTab");
        var name = Resolve(fixture, fixture.WinFormsHandle, new(AutomationId: "NameTextBox"));
        var value = name.Patterns.Value.Pattern;
        value.SetValue("");
        var context = Counting();
        try
        {
            using var input = new GuardedInput(
                fixture.Session.GetInputTarget(fixture.WinFormsHandle),
                name,
                verifyFocus: true
            );
            var start = context.InputsSent;
            input.Type(new string('a', 64));
            Assert.Equal(start + 1, context.InputsSent);
            input.Type(new string('b', 65));
            Assert.Equal(start + 3, context.InputsSent);
            var expected = new string('a', 64) + new string('b', 65);
            Assert.True(WaitFor(() => value.Value.Value == expected), $"Typed text was '{value.Value.Value}'.");
        }
        finally
        {
            StopCounting();
            value.SetValue("");
        }
    }

    [Fact]
    public void StaleTargetIdentityIsRefusedBeforeAnyChange()
    {
        // Kills GuardedInput:14 Logical (start time and window owner must both match) and GuardedInput:53
        // Statement (EnsureAlive skipped).
        var live = fixture.Session.GetInputTarget(fixture.WpfHandle);
        var stale = live with { StartedTicks = live.StartedTicks + 1 };
        Foreground(WinFormsHwnd);
        var context = Counting();
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => new GuardedInput(stale));
            Assert.Equal("Stale process/window identity.", error.Message);
            Assert.Equal(0, context.InputsSent);
            Assert.Equal(WinFormsHwnd, Win32Desktop.GetForegroundWindow());
        }
        finally
        {
            StopCounting();
        }
    }

    [Fact]
    public void CaptureRefusesAWindowOfAnotherProcess()
    {
        // Kills GuardedInput:22 Logical (hwnd == 0 || pid == 0 && owner check: a nonzero pair skips the check).
        var error = Assert.Throws<InvalidOperationException>(() =>
            InputTarget.Capture(WinFormsHwnd, fixture.Session.GetWindowProcessId(fixture.WpfHandle))
        );
        Assert.Equal("Cannot establish window ownership.", error.Message);
    }

    [Fact]
    public void SecondLeaseTimeoutSaysDesktopInputIsBusy()
    {
        // Kills GuardedInput:182 String (the timeout message emptied).
        using var first = GuardedInput.AcquireLease();
        var error = Assert.Throws<TimeoutException>(GuardedInput.AcquireLease);
        Assert.Equal("Desktop input is busy; no input sent.", error.Message);
    }

    [Fact]
    public async Task ModelessOwnedWindowIsNotTheTarget()
    {
        // Kills GuardedInput:346 Logical (!enabled || owned: the modeless window counts as the target, so the
        // guard skips activation and then fails), GuardedInput:341 Logical (an enabled target's owned window
        // passes Verify), GuardedInput:318 String (Verify's message emptied).
        var processId = WinFormsProcessId(fixture);
        SelectTab(fixture, fixture.WinFormsHandle, "DialogsTab");
        var (_, open) = Ref(fixture, fixture.WinFormsHandle, new(AutomationId: "OpenModelessButton"));
        Assert.Contains("Invoked", await fixture.CallTool(new ClickTool(fixture.Elements), new { @ref = open }));
        Win32WindowInfo? modeless = null;
        Assert.True(
            WaitFor(() =>
            {
                modeless = Win32Desktop
                    .GetTopLevelWindows(processId)
                    .SingleOrDefault(w => w.Title == "Test Modeless Dialog");
                return modeless != null;
            })
        );
        try
        {
            Foreground(modeless!.Hwnd);
            using var input = new GuardedInput(fixture.Session.GetInputTarget(fixture.WinFormsHandle));
            Assert.Equal("the target window was activated", input.Changed);
            Assert.Equal(WinFormsHwnd, Win32Desktop.GetForegroundWindow());

            Foreground(modeless.Hwnd);
            var error = Assert.Throws<InvalidOperationException>(input.Verify);
            Assert.Equal("Input target/focus changed or is disabled; no further input sent.", error.Message);
        }
        finally
        {
            Win32Desktop.CloseWindow(modeless!.Hwnd);
            Assert.True(
                WaitFor(() => Win32Desktop.GetTopLevelWindows(processId).All(w => w.Title != "Test Modeless Dialog"))
            );
        }
    }
}
