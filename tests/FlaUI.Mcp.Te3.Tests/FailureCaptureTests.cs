using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;
using static CoreResults;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// What a failed run captures for diagnosis. Reads only, so they run without the grant, after the user revoked it;
/// a capture that does not happen says why in the manifest. Scripted reads; nothing is captured.
/// </summary>
public sealed class FailureCaptureTests
{
    [Fact]
    public async Task TheFailureCaptureRunsWithoutTheGrantAndAsksForABackgroundReadOnly()
    {
        // After the user's input revoked the grant, the capture went through the guarded Invoke, was refused, and an
        // empty catch hid why.
        var calls = new List<(string Tool, JsonElement Args)>();
        var manifest = new RunManifest();

        await RunExecutor.CaptureFailure(
            (tool, args) =>
            {
                calls.Add((tool, JsonSerializer.SerializeToElement(args)));
                return Task.FromResult(Text("ok"));
            },
            "w1",
            "out",
            manifest
        );

        Assert.Equal(["windows_screenshot", "windows_find"], calls.Select(c => c.Tool));
        Assert.True(calls[0].Args.GetProperty("background").GetBoolean());
        Assert.False(calls[0].Args.TryGetProperty("parkPointer", out _));
        Assert.Empty(manifest.DiagnosticNotes);
    }

    [Fact]
    public async Task ACaptureThatFailsSaysWhyInTheManifest()
    {
        var manifest = new RunManifest();

        await RunExecutor.CaptureFailure(
            (tool, _) =>
                tool == "windows_screenshot"
                    ? throw new InvalidOperationException("window gone")
                    : Task.FromResult(Text("element tree unreadable", error: true)),
            "w1",
            "out",
            manifest
        );

        Assert.Contains(manifest.DiagnosticNotes, n => n.Contains("failure.png") && n.Contains("window gone"));
        Assert.Contains(manifest.DiagnosticNotes, n => n.Contains("windows_find") && n.Contains("unreadable"));
    }

    [Fact]
    public async Task NoWindowYetMeansNoCaptureAndSaysSo()
    {
        var manifest = new RunManifest();

        await RunExecutor.CaptureFailure(
            (_, _) => throw new InvalidOperationException("unreachable"),
            null,
            "out",
            manifest
        );

        Assert.Contains(manifest.DiagnosticNotes, n => n.Contains("no TE3 window"));
    }

    [Fact]
    public void TheRunCapturesItsFailureOnlyThroughTheGrantFreeRead()
    {
        var sources = DirectInputJournalTests.Sources();
        var executor = File.ReadAllText(Path.Combine(sources, "RunExecutor.cs"));

        Assert.DoesNotContain("\"windows_screenshot\"", executor);
        Assert.Contains("CaptureFailure(", executor);
        Assert.DoesNotContain("catch { }", executor);
    }
}
