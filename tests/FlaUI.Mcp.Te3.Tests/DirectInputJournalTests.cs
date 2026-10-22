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

    internal static string Sources()
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

    // The line and every call it is an argument of, innermost first: each line above it whose unclosed '(' encloses
    // it. Statements inside a lambda argument balance their own parentheses, so the walk passes them; a member
    // declaration ends it.
    private static string EnclosingCalls(string[] lines, int index)
    {
        var enclosing = new List<string> { lines[index] };
        var depth = 0;
        for (var start = index; start > 0 && !IsMember(lines[start]); )
        {
            start--;
            depth += lines[start].Count(c => c == '(') - lines[start].Count(c => c == ')');
            if (depth > 0)
            {
                enclosing.Add(lines[start]);
                depth = 0;
            }
        }

        return string.Join(' ', enclosing);
    }

    private static readonly Regex Member = new(@"^    (?:public|private|internal) ");

    private static bool IsMember(string line) => Member.IsMatch(line);

    [Theory]
    [InlineData("Te3Page.cs")]
    [InlineData("Te3Page.Tom.cs")]
    [InlineData("Te3Page.Walk.cs")]
    [InlineData("RunExecutor.cs")]
    [InlineData("RunExecutor.Window.cs")]
    [InlineData("RunSafety.cs")]
    [InlineData("Te3Layout.cs")]
    [InlineData("DialogCancel.cs")]
    public void EveryDirectInputGoesThroughTheJournal(string file)
    {
        var lines = File.ReadAllLines(Path.Combine(Sources(), file));
        var unjournaled = Enumerable
            .Range(0, lines.Length)
            .Where(i => DirectInput.IsMatch(lines[i]) && !lines[i].TrimStart().StartsWith("//"))
            .Where(i => !Journaled.IsMatch(EnclosingCalls(lines, i)))
            .Select(i => $"{file}:{i + 1}: {lines[i].Trim()}")
            .ToList();

        Assert.Empty(unjournaled);
    }

    [Fact]
    public void ThePagesOwnClicksUseCoresClickPoint()
    {
        // A raw clickable point skips ClickPoint's visibility checks and Core's hit test under the input lease;
        // GuardedInput.Click reads and checks the point itself.
        var source = File.ReadAllText(Path.Combine(Sources(), "Te3Page.cs"));

        Assert.DoesNotContain("GetClickablePoint", source);
        Assert.DoesNotContain("Mouse.Click(", source);
    }

    [Fact]
    public void AnObscuredPointerTargetIsAStructuredRefusal()
    {
        var error = Te3Page.Obscured(new(100, 4242, 0), "Model menu is obscured");

        Assert.Equal("point-not-on-target", error.Refusal.Code);
        Assert.Equal(4242, error.Refusal.Target?.Pid);
        Assert.Equal("Model menu is obscured; no input sent.", error.Message);
    }

    [Fact]
    public void EveryFillSaysWhetherCoreMustSeeFocusMove()
    {
        // Core's windows_fill keyboard fallback types Ctrl+A, Backspace and the text; whether it first checks that focus
        // reached the field is a per-call decision, never left to Core's default.
        var page = string.Concat(Directory.GetFiles(Sources(), "Te3Page*.cs").Select(File.ReadAllText));
        var fills = Regex.Matches(page, @"invoke\(\s*""windows_fill"",\s*new\s*\{[^}]*\}");

        Assert.NotEmpty(fills);
        Assert.All(fills, fill => Assert.Contains("verifyFocus", fill.Value));
        Assert.DoesNotMatch(@"await Fill\([^;]*\);", Regex.Replace(page, @"await Fill\([^;]*verifyFocus[^;]*\);", ""));
    }

    [Fact]
    public void TheScintillaEditorsAreFilledOnlyWithFocusOnTheEditorOrInsideIt()
    {
        // The expression and query editors (Scintilla) were filled with no focus check, so Ctrl+A could go wherever
        // focus was. Core accepts focus on the ref or a descendant ("self-or-descendant"), which these hosts report.
        var page = string.Concat(Directory.GetFiles(Sources(), "Te3Page*.cs").Select(File.ReadAllText));

        Assert.DoesNotMatch(@"await Fill\([^;]*verifyFocus: false[^;]*\);", page);
        Assert.Matches(
            @"await Fill\(ExpressionInputTarget\(\), expression, verifyFocus: FocusRules\.SelfOrDescendantName\)",
            page
        );
        Assert.Matches(
            @"await Fill\(QueryInputTarget\(\), query, verifyFocus: FocusRules\.SelfOrDescendantName\)",
            page
        );
    }

    [Fact]
    public void TheSplitterDragRefusesAStartOnANeighbouringCaption()
    {
        // A splitter start a few pixels off lands on the next pane's caption or a document tab, and dragging that
        // floats or re-docks the pane. Core's fromMustNotHit hit-tests the start under the lease and refuses first.
        var preset = File.ReadAllText(Path.Combine(Sources(), "Te3Layout.Preset.cs"));

        Assert.Matches(@"fromMustNotHit\s*=\s*avoid", preset);
        Assert.Matches(@"Captions\(parts, pane\.Title\)", preset);
    }
}
