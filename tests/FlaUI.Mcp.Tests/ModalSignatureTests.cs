using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Only a window that blocks its owner is a modal dialog: a new owned window while a window of the process
/// went disabled. A tooltip, a menu or a tool window that appears while a pattern call runs is not, and must
/// not mark the provider pending for later ref tools.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class ModalSignatureTests
{
    private const int Pid = 4321;
    private const nint Main = 1;

    private static Win32WindowInfo Window(nint hwnd, string title, bool enabled = true) =>
        new(hwnd, title, Pid, enabled, IsToolWindow: false, IsCloaked: false);

    /// <summary>Run a blocked pattern call while the process shows <paramref name="later"/> after the first look.</summary>
    private static PatternCallResult Run(params Win32WindowInfo[] later)
    {
        var desktop = new FakeDesktop();
        var calls = 0;
        desktop.WindowsOf = _ => Interlocked.Increment(ref calls) == 1 ? [Window(Main, "Main")] : later;
        using var installed = desktop.Install();
        using var hold = new ManualResetEventSlim();
        try
        {
            return ModalAwareInvoker.Execute(
                Pid,
                "Invoke on 'Save'",
                hold.Wait,
                new PendingInvokeTracker(_ => 1),
                TimeSpan.FromMilliseconds(300)
            );
        }
        finally
        {
            hold.Set();
        }
    }

    [Fact]
    public void ATooltipIsNotAModal() =>
        Assert.Equal(
            PatternCallOutcome.StillPending,
            Run(
                Window(Main, "Main"),
                Window(2, "Save the file") with
                {
                    ClassName = "tooltips_class32",
                    Owner = Main,
                }
            ).Outcome
        );

    [Fact]
    public void AToolWindowIsNotAModal() =>
        Assert.Equal(
            PatternCallOutcome.StillPending,
            Run(Window(Main, "Main"), Window(2, "Palette") with { IsToolWindow = true, Owner = Main }).Outcome
        );

    [Fact]
    public void ANewWindowThatBlocksNothingIsNotAModal() =>
        Assert.Equal(
            PatternCallOutcome.StillPending,
            Run(Window(Main, "Main"), Window(2, "Find") with { Owner = Main }).Outcome
        );

    [Fact]
    public void AnOwnedWindowThatDisablesItsOwnerIsTheModal()
    {
        var result = Run(
            Window(Main, "Main", enabled: false),
            Window(3, "Tip") with
            {
                ClassName = "tooltips_class32",
            },
            Window(2, "Save As") with
            {
                Owner = Main,
            }
        );

        Assert.Equal(PatternCallOutcome.ModalDetected, result.Outcome);
        Assert.Equal("Save As", result.ModalTitle);
    }
}
