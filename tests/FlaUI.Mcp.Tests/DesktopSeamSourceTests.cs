using System.Text.RegularExpressions;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Tools read the desktop (foreground, focus, window bounds and owners, the clipboard) through InputDesktop,
/// so the fake drives every focus and foreground decision offline and no offline result depends on the
/// user's real desktop. Only the pure occlusion helpers and the z-order snapshot stay direct.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed partial class DesktopSeamSourceTests
{
    [GeneratedRegex(
        @"Win32Desktop\.(?!OcclusionCheck\b|FindObstructions\b|ObserveZOrder\b|GetClipboardOwnerProcessId\b)(\w+)\s*\("
    )]
    private static partial Regex DirectRead();

    [Fact]
    public void ToolsReadTheDesktopOnlyThroughTheSeam()
    {
        var tools = Directory.GetFiles(Path.Combine(Source(), "Tools"), "*.cs");
        var direct = tools
            .SelectMany(file =>
                File.ReadLines(file)
                    .Select((line, index) => (file, line, index))
                    .Where(e => DirectRead().IsMatch(e.line))
            )
            .Select(e => $"{Path.GetFileName(e.file)}:{e.index + 1} {DirectRead().Match(e.line).Groups[1].Value}");

        Assert.Empty(direct);
    }

    /// <summary>
    /// Tools wait through InputDesktop.Pause, and read deadlines from InputDesktop.Time, so a fake desktop decides
    /// what a wait costs and a path that waits can be tested offline. Win32Desktop and the launch polling in SessionManager, which read Windows directly below the seam, keep their own.
    /// </summary>
    [Fact]
    public void ToolsWaitOnlyThroughTheSeam()
    {
        var files = Directory
            .GetFiles(Path.Combine(Source(), "Tools"), "*.cs")
            .Append(Path.Combine(Source(), "Core", "PointerPlan.cs"));
        var direct = files
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(e => e.line.Contains("Thread.Sleep(", StringComparison.Ordinal))
            .Select(e => $"{Path.GetFileName(e.file)}:{e.index + 1}");

        Assert.Empty(direct);
    }

    [Fact]
    public void AHoverAndADragsStepsPauseThroughTheSeam()
    {
        var pauses = new List<TimeSpan>();
        var desktop = new FakeDesktop { OnPause = pauses.Add };
        using var installed = desktop.Install();

        FlaUI.Mcp.Tools.PointerActions.Move(FakeDesktop.Target, () => new(10, 10), hoverMs: 600);
        FlaUI.Mcp.Tools.PointerActions.Drag(FakeDesktop.Target, new(10, 10), new(50, 10), 3);

        Assert.Equal([TimeSpan.FromMilliseconds(600), .. Enumerable.Repeat(TimeSpan.FromMilliseconds(15), 3)], pauses);
    }

    private static string Source()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "FlaUI.Mcp");
            if (Directory.Exists(Path.Combine(candidate, "Tools")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("src/FlaUI.Mcp");
    }
}
