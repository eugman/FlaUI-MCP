using System.Text.RegularExpressions;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// Input the TE3 host sends itself, not through a Core tool, must reach the run journal like tool calls do.
/// Reads the sources; nothing runs.
/// </summary>
public sealed class DirectInputJournalTests
{
    private static readonly Regex DirectInput = new(
        // GuardedInput's constructor activates the target window, so creating one is input too.
        @"\binput\.Send\(|\.FocusWindow\(|\.CloseMainWindow\(|\bnew GuardedInput\("
    );
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

    // The line and the call it is an argument of: back to the line whose unclosed '(' encloses it.
    // Statements inside a lambda argument balance their own parentheses, so the walk passes them and stops at the
    // unclosed one; a member declaration ends it when nothing encloses the line.
    private static string EnclosingCall(string[] lines, int index)
    {
        var depth = 0;
        var start = index;
        while (start > 0 && depth <= 0 && !IsMember(lines[start]))
        {
            start--;
            depth += lines[start].Count(c => c == '(') - lines[start].Count(c => c == ')');
        }

        return string.Join(' ', lines[(depth > 0 ? start : index)..(index + 1)]);
    }

    private static readonly Regex Member = new(@"^    (?:public|private|internal) ");

    private static bool IsMember(string line) => Member.IsMatch(line);

    [Theory]
    [InlineData("Te3Page.cs")]
    [InlineData("RunExecutor.cs")]
    [InlineData("RunSafety.cs")]
    [InlineData("Te3Layout.cs")]
    [InlineData("DialogCancel.cs")]
    public void EveryDirectInputGoesThroughTheJournal(string file)
    {
        var lines = File.ReadAllLines(Path.Combine(Sources(), file));
        var unjournaled = Enumerable
            .Range(0, lines.Length)
            .Where(i => DirectInput.IsMatch(lines[i]) && !lines[i].TrimStart().StartsWith("//"))
            .Where(i => !Journaled.IsMatch(EnclosingCall(lines, i)))
            .Select(i => $"{file}:{i + 1}: {lines[i].Trim()}")
            .ToList();

        Assert.Empty(unjournaled);
    }
}
