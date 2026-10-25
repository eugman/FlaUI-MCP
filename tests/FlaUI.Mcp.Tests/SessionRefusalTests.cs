using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>SessionManager's refusals are typed, with codes, instead of plain Exceptions a client must parse.</summary>
public sealed class SessionRefusalTests
{
    private static InputRefusedException Refused(Action call) => Assert.Throws<InputRefusedException>(call);

    [Fact]
    public void ALaunchOffTheAllowlistIsRefusedBeforeAnythingStarts()
    {
        using var sessions = new SessionManager(new ProcessPolicy(["another-app"]));

        var error = Refused(() => sessions.LaunchApp(@"C:\Windows\System32\notepad.exe"));

        Assert.Equal(RefusalCodes.ProcessNotAllowed, error.Code);
        Assert.Equal(RefusalDisposition.NoInput, error.Disposition);
    }

    [Fact]
    public void AWindowOfAProcessThatCannotBeIdentifiedIsRefused()
    {
        using var installed = new FakeDesktop().Install();
        using var sessions = new SessionManager(new ProcessPolicy(["another-app"]));

        Assert.Equal(RefusalCodes.WindowOwnershipUnknown, Refused(() => sessions.RegisterNativeWindow(1, 0)).Code);
    }

    [Fact]
    public void AWindowOfAProcessOffTheAllowlistIsRefused()
    {
        using var installed = new FakeDesktop().Install();
        using var sessions = new SessionManager(new ProcessPolicy(["another-app"]));

        Assert.Equal(
            RefusalCodes.ProcessNotAllowed,
            Refused(() => sessions.RegisterNativeWindow(1, FakeDesktop.OtherPid)).Code
        );
    }

    [Fact]
    public void AnAbsentTitleIsRefusedAsNotFound()
    {
        using var sessions = new SessionManager(ProcessPolicy.AllowAll);

        var error = Refused(() => sessions.AttachToWindow($"no window is titled this {Guid.NewGuid():N}"));

        Assert.Equal(RefusalCodes.WindowNotFound, error.Code);
        Assert.StartsWith("Window not found: ", error.Message);
    }

    [Fact]
    public void ARepeatedExactTitleCannotSilentlyAttachToTheFirstWindow()
    {
        const string title = "same title";
        var windows = new[]
        {
            new Win32WindowInfo(11, title, 101, true, false, false),
            new Win32WindowInfo(12, title, 202, true, false, false),
        };

        var error = Refused(() => SessionManager.UniqueWindowByTitle(windows, title));

        Assert.Equal(RefusalCodes.WindowTitleAmbiguous, error.Code);
        Assert.Equal(RefusalDisposition.NoInput, error.Disposition);
        Assert.Contains("windows_list_windows", error.Message);
        Assert.Equal(
            windows[0],
            SessionManager.UniqueWindowByTitle([windows[0], windows[1] with { IsCloaked = true }], title)
        );
    }

    [Fact]
    public void ALaunchThatStartedAProcessButFoundNoWindowSaysItWentOut()
    {
        var started = ProcessPolicy.LaunchFailed("no window", started: true);

        Assert.Equal(RefusalCodes.LaunchFailed, Assert.IsType<InputRefusedException>(started).Code);
        Assert.Equal(RefusalDisposition.Partial, ((InputRefusedException)started).Disposition);
        Assert.Equal(
            RefusalDisposition.NoInput,
            ((InputRefusedException)ProcessPolicy.LaunchFailed("did not start", started: false)).Disposition
        );
    }
}
