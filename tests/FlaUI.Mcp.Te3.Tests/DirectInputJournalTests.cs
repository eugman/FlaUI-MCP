using System.Text.RegularExpressions;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// Input the TE3 host sends itself, not through a Core tool, must reach the run journal like tool calls do.
/// Reads the sources; nothing runs.
/// </summary>
public sealed class DirectInputJournalTests
{
    private static readonly Regex DirectInput = new(@"\binput\.Send\(|\.FocusWindow\(|\.CloseMainWindow\(");
    private static readonly Regex Journaled = new(@"\bRunJournal\.Input\(|\bSendInput\(");

    private static string Sources()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory != null;
            directory = directory.Parent
        )
        {
            var sources = Path.Combine(directory.FullName, "src", "FlaUI.Mcp.Te3");
            if (Directory.Exists(sources))
            {
                return sources;
            }
        }

        throw new DirectoryNotFoundException("src/FlaUI.Mcp.Te3 not found above " + AppContext.BaseDirectory);
    }

    [Theory]
    [InlineData("Te3Page.cs")]
    [InlineData("RunExecutor.cs")]
    [InlineData("RunSafety.cs")]
    public void EveryDirectInputGoesThroughTheJournal(string file)
    {
        var lines = File.ReadAllLines(Path.Combine(Sources(), file));
        var unjournaled = Enumerable
            .Range(0, lines.Length)
            .Where(i => DirectInput.IsMatch(lines[i]) && !lines[i].TrimStart().StartsWith("//"))
            .Where(i => !Enumerable.Range(Math.Max(0, i - 3), Math.Min(i, 3) + 1).Any(j => Journaled.IsMatch(lines[j])))
            .Select(i => $"{file}:{i + 1}: {lines[i].Trim()}")
            .ToList();

        Assert.Empty(unjournaled);
    }
}
