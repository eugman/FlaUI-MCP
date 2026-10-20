using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Windows (UIPI) silently drops input and messages from a lower-integrity process to an elevated or
/// UIAccess window, and SendInput gives no sign. Guarded input must refuse before sending.
/// </summary>
public sealed class IntegrityGuardTests
{
    private static readonly ProcessIntegrity Medium = new(ProcessIntegrity.Medium, false);

    [Fact]
    public void EqualIntegrityIsNotBlocked()
    {
        Assert.Null(ProcessIntegrity.DescribeBlocked(Medium, Medium, 7, "notepad"));
        Assert.Null(ProcessIntegrity.DescribeBlocked(new(ProcessIntegrity.High, false), Medium, 7, "notepad"));
        Assert.Null(
            ProcessIntegrity.DescribeBlocked(
                new(ProcessIntegrity.Medium, true),
                new(ProcessIntegrity.Medium, true),
                7,
                "x"
            )
        );
    }

    [Fact]
    public void ElevatedTargetIsBlockedWithItsLevel()
    {
        var refusal = ProcessIntegrity.DescribeBlocked(Medium, new(ProcessIntegrity.High, false), 7, "regedit");

        Assert.NotNull(refusal);
        Assert.Equal("integrity-blocked", refusal.Code);
        Assert.StartsWith("integrity-blocked: ", refusal.Message);
        Assert.Contains("no input sent", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("regedit", refusal.Target!.Name);
        Assert.Equal(7, refusal.Target.Pid);
        Assert.Equal("high", refusal.Target.IntegrityLevel);
    }

    [Fact]
    public void UiAccessTargetAtTheSameLevelIsBlocked()
    {
        var refusal = ProcessIntegrity.DescribeBlocked(
            Medium,
            new(ProcessIntegrity.Medium, true),
            9,
            "CamtasiaRecorder"
        );

        Assert.NotNull(refusal);
        Assert.Equal("integrity-blocked", refusal.Code);
        Assert.True(refusal.Target!.UiAccess);
        Assert.Contains("UIAccess", refusal.Message);
    }

    [Fact]
    public void UnreadableTargetTokenFailsClosed()
    {
        var refusal = ProcessIntegrity.DescribeBlocked(Medium, null, 11, "secure");

        Assert.NotNull(refusal);
        Assert.Equal("integrity-blocked", refusal.Code);
        Assert.Equal("unknown", refusal.Target!.IntegrityLevel);
    }

    [Fact]
    public void OwnTokenIsReadable()
    {
        var self = ProcessIntegrity.Read(Environment.ProcessId);

        Assert.NotNull(self);
        Assert.True(self.Level >= 0x1000, $"level 0x{self.Level:x}");
    }

    [Fact]
    public async Task InputToAHigherIntegrityWindowIsRefusedBeforeFocusOrInput()
    {
        var desktop = new FakeDesktop
        {
            ForegroundWindow = FakeDesktop.OtherHwnd,
            TargetIntegrity = new(ProcessIntegrity.Medium, true),
        };
        using var installed = desktop.Install();
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, FakeDesktop.TargetHwnd, FakeDesktop.TargetPid, FakeDesktop.Target);
        var registry = new ToolRegistry();
        registry.RegisterTool(new SendKeysTool(new ElementRegistry(), sessions: sessions));

        var result = await registry.ExecuteToolAsync(
            "windows_send_keys",
            JsonSerializer.SerializeToElement(new { handle, chord = "Enter" })
        );

        Assert.True(result.IsError);
        Assert.Contains("integrity-blocked: ", result.Content[0].Text);
        Assert.Equal(0, desktop.Dispatched);
        Assert.Equal(0, desktop.FocusAttempts);
        var outcome = JsonSerializer.SerializeToElement(result.Outcome);
        Assert.Equal("not-dispatched", outcome.GetProperty("dispatch").GetString());
        var failure = outcome.GetProperty("failure");
        Assert.Equal("integrity-blocked", failure.GetProperty("code").GetString());
        Assert.False(failure.GetProperty("actionDispatched").GetBoolean());
        Assert.Equal(FakeDesktop.TargetPid, failure.GetProperty("target").GetProperty("pid").GetInt32());
        Assert.Equal("medium", failure.GetProperty("target").GetProperty("integrityLevel").GetString());
        Assert.True(failure.GetProperty("target").GetProperty("uiAccess").GetBoolean());
    }
}
