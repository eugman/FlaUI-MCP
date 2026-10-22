using System.Text.RegularExpressions;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Tools read the desktop (foreground, focus, window bounds and owners, the clipboard) through InputDesktop,
/// so the fake drives every focus and foreground decision offline and no offline result depends on the
/// user's real desktop. Only the pure occlusion helpers and the z-order snapshot stay direct.
/// </summary>
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
