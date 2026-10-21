using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A failure screenshot must not need the grant: Diagnostics.Capture renders the window by handle with no
/// lease, permission check, cancellation, focus or pointer move, and says why when it captured nothing.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class DiagnosticsCaptureTests
{
    private static string TempPng() => Path.Combine(Path.GetTempPath(), $"flaui-diag-{Guid.NewGuid():N}.png");

    [Fact]
    public void ItCapturesWhileTheGrantIsRevokedTheLeaseHeldAndTheCallCancelled()
    {
        var desktop = new FakeDesktop { WindowImage = [1, 2, 3] };
        using var installed = desktop.Install();
        using var trip = PermissionTrip.DenyAfter(0);
        using var lease = GuardedInput.AcquireLease();
        var operation = new OperationContext();
        operation.Stop.Cancel();
        OperationContext.Current.Value = operation;
        var path = TempPng();
        try
        {
            var capture = Diagnostics.Capture(FakeDesktop.TargetHwnd, path);

            Assert.True(capture.Saved, capture.SkipReason);
            Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
            Assert.Equal(0, trip.Checks);
            Assert.Equal(0, desktop.Dispatched);
            Assert.Equal(0, desktop.FocusAttempts);
        }
        finally
        {
            OperationContext.Current.Value = null;
            File.Delete(path);
        }
    }

    [Fact]
    public void ACaptureThatFailsSaysWhy()
    {
        using var installed = new FakeDesktop { WindowImage = null }.Install();
        var path = TempPng();

        var capture = Diagnostics.Capture(FakeDesktop.TargetHwnd, path);

        Assert.False(capture.Saved);
        Assert.Equal("The window could not be rendered: no image (fake)", capture.SkipReason);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void AnUnwritablePathIsASkipReasonNotAnException()
    {
        using var installed = new FakeDesktop { WindowImage = [1] }.Install();

        var capture = Diagnostics.Capture(FakeDesktop.TargetHwnd, Path.Combine(TempPng(), "missing", "x.png"));

        Assert.False(capture.Saved);
        Assert.StartsWith("The capture could not be written: ", capture.SkipReason);
    }

    [Fact]
    public void ARefusedParkStillCapturesWithAWarning()
    {
        var warning = ScreenshotTool.ParkRefused(new TimeoutException("Desktop input is busy; no input sent."));

        Assert.Equal(
            "parkPointer was refused, so the pointer was not moved: Desktop input is busy; no input sent. "
                + "The capture was taken anyway; a hover state or tooltip may show in it.",
            warning
        );
    }

    [Fact]
    public void AHungRenderIsBoundedAndReportedAsTimedOut()
    {
        using var release = new ManualResetEventSlim();
        using var installed = new FakeDesktop { WindowImage = [1], RenderBlock = release }.Install();
        var path = TempPng();
        try
        {
            var capture = Diagnostics.Capture(FakeDesktop.TargetHwnd, path, TimeSpan.FromMilliseconds(100));

            Assert.False(capture.Saved);
            Assert.Equal("The window did not render within 100 ms; it may be hung.", capture.SkipReason);
        }
        finally
        {
            release.Set();
            File.Delete(path);
        }
    }

    [Fact]
    public void TheCaptureRecordsTheWindowsAndTheTargetsTree()
    {
        var desktop = new FakeDesktop
        {
            WindowImage = [1],
            WindowsOf = _ =>
                [
                    new Win32WindowInfo(FakeDesktop.TargetHwnd, "Main", FakeDesktop.TargetPid, false, false, false),
                    new Win32WindowInfo(7, "Save changes?", FakeDesktop.TargetPid, true, false, false)
                    {
                        Owner = FakeDesktop.TargetHwnd,
                    },
                ],
            Tree = "Window 'Main'\n  Button 'OK'",
        };
        using var installed = desktop.Install();
        var path = TempPng();
        try
        {
            var capture = Diagnostics.Capture(FakeDesktop.TargetHwnd, path);

            Assert.Equal(["Main", "Save changes?"], capture.Windows.Select(w => w.Title));
            Assert.Equal("Window 'Main'\n  Button 'OK'", capture.Tree);
            var notes = File.ReadAllText(path + ".txt");
            Assert.Contains("Save changes?", notes);
            Assert.Contains("Button 'OK'", notes);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".txt");
        }
    }
}
