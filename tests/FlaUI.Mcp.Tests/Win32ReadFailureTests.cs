using System.Text.RegularExpressions;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>Failed Win32 reads never pass stale out values; unknown owners still block occlusion proof.</summary>
public sealed class Win32ReadFailureTests
{
    private const uint StaleProcessId = 4242;
    private const uint OwnerProcessIdValue = 5151;

    // Windows returned no thread id, but the out parameter still holds a process id.
    private static uint FailedOwnerRead(nint hwnd, out uint processId)
    {
        processId = StaleProcessId;
        return 0;
    }

    private static uint OwnerRead(nint hwnd, out uint processId)
    {
        processId = OwnerProcessIdValue;
        return 1;
    }

    [Fact]
    public void AFailedOwnerReadReportsNoProcess()
    {
        Assert.Equal(0, Win32Desktop.OwnerProcessId(100, FailedOwnerRead));
    }

    [Fact]
    public void AnOwnerReadReportsItsProcess()
    {
        Assert.Equal((int)OwnerProcessIdValue, Win32Desktop.OwnerProcessId(100, OwnerRead));
    }

    // Windows copied nothing, but the buffer already holds text.
    private static int FailedTextRead(nint hwnd, char[] buffer, int capacity)
    {
        "stale".CopyTo(buffer);
        return 0;
    }

    // Windows copied three characters into a buffer that holds more.
    private static int ShortTextRead(nint hwnd, char[] buffer, int capacity)
    {
        "abcdef".CopyTo(buffer);
        return 3;
    }

    [Fact]
    public void AFailedTextReadReportsNoText()
    {
        Assert.Null(Win32Desktop.ReadText(100, 16, FailedTextRead));
    }

    [Fact]
    public void ATextReadReportsOnlyTheCopiedLength()
    {
        Assert.Equal("abc", Win32Desktop.ReadText(100, 16, ShortTextRead));
    }

    // hwnd 0 keeps the window style and cloak reads, which are not faked, from reaching a real window.
    [Fact]
    public void EnumerationSkipsAWindowWhoseOwnerCannotBeRead()
    {
        Assert.Null(Win32Desktop.ReadWindow(0, null, FailedOwnerRead, ShortTextRead));
        Assert.Null(Win32Desktop.ReadWindow(0, (int)StaleProcessId, FailedOwnerRead, ShortTextRead));
    }

    [Fact]
    public void OcclusionReadKeepsAnUnknownOwnerAsPotentialCover()
    {
        var unknown = Win32Desktop.ReadWindow(0, null, FailedOwnerRead, ShortTextRead, keepUnknownOwner: true);

        Assert.NotNull(unknown);
        Assert.Equal(0, unknown.ProcessId);
        var target = new Win32WindowInfo(42, "Target", 5151, true, false, false);
        var observed = new[]
        {
            new Win32Desktop.WindowBounds(unknown, new System.Drawing.Rectangle(0, 0, 100, 100)),
            new Win32Desktop.WindowBounds(target, new System.Drawing.Rectangle(0, 0, 100, 100)),
        };
        Assert.False(
            Win32Desktop
                .FindObstructions(observed, target.Hwnd, target.ProcessId, new System.Drawing.Rectangle(10, 10, 20, 20))
                .IsClean
        );
    }

    [Fact]
    public void EnumerationKeepsAnUntitledWindowOfTheRequestedProcess()
    {
        var window = Win32Desktop.ReadWindow(0, (int)OwnerProcessIdValue, OwnerRead, FailedTextRead);

        Assert.NotNull(window);
        Assert.Equal(string.Empty, window.Title);
        Assert.Equal((int)OwnerProcessIdValue, window.ProcessId);
        Assert.Null(Win32Desktop.ReadWindow(0, 1, OwnerRead, ShortTextRead));
    }

    // Not a window: user32 rejects the handle, so every read about it fails. The value is arbitrary; it only
    // has to fail handle validation, and its low word indexes far past the user handles a session holds in practice.
    // Deriving one from a real window (create, then destroy) would open a window, which offline tests must not.
    private const nint NoWindow = unchecked((nint)0x7FFFFFFF);

    [Fact]
    public void DescribeSaysWhenAWindowCannotBeRead()
    {
        Assert.Equal($"hwnd={NoWindow}, pid=0, class=unreadable", Win32Desktop.Describe(NoWindow));
    }

    // The fakes above only prove the helpers; this proves the real reads go through them. A direct call such as
    // GetWindowThreadProcessId(hwnd, out pid) would hand back the stale out value these helpers refuse, so the
    // only call-shaped mention of each import must be its extern declaration; everywhere else it is a method group.
    [Fact]
    public void RealWin32ReadsOnlyGoThroughTheFailureAwareHelpers()
    {
        var source = SourceDirectory();
        var calls = Directory
            .EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(file =>
                !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
            )
            .SelectMany(file => File.ReadLines(file).Select(line => (file, line)))
            .Where(entry => Regex.IsMatch(entry.line, @"\b(GetWindowThreadProcessId|GetWindowText|GetClassName)\s*\("))
            .ToArray();

        Assert.Equal(3, calls.Length);
        Assert.All(calls, entry => Assert.Contains("static extern", entry.line));
    }

    private static string SourceDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "FlaUI.Mcp");
            if (File.Exists(Path.Combine(candidate, "Core", "Win32Desktop.cs")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("src/FlaUI.Mcp");
    }
}
