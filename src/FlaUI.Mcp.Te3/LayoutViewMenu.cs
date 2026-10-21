using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;

/// <summary>
/// The live View menu for te3_layout reopen-pane (see <see cref="PaneReopen"/>): finds MenuItem View in the main menu and
/// the popup Button named after the pane (an owned native window, so includeOwned), and clicks each physically. A match
/// in the main window (an auto-hide side tab or toolbar Button with the pane's name) is never clicked.
/// </summary>
internal sealed class LayoutViewMenu(AutomationHost host, string handle, Window window, InputTally inputs)
    : PaneReopen.IViewMenu
{
    public Task<bool> IsVisible(string title) => Task.FromResult(Te3Layout.PaneVisible(window, title));

    private const int PopupWaitMs = 1500;
    private const int PopupPollMs = 150;

    // Enough matches to get past main-window Buttons that share the pane's name.
    private const int EntryMatches = 10;

    public async Task OpenViewMenu()
    {
        var reply = await FindReply(
            new { controlType = "MenuItem", name = "View" },
            new { controlType = "MenuBar", name = "Main menu" },
            includeOwned: false
        );
        await Click(FirstRef(reply));
    }

    // The View popup is an owned native window that windows_list_windows does not list. It is outside the main
    // window's subtree and shows a moment after the View click, so the find covers the process's other top-level
    // windows (includeOwned) and is repeated until the popup's Button appears. Core searches the main window first,
    // so each match's ancestry is checked and only one inside a non-main window of the process is taken.
    public async Task ClickEntry(string title) =>
        await Click(
            await FindEntry(
                () =>
                    FindReply(
                        new { controlType = "Button", name = title },
                        within: null,
                        includeOwned: true,
                        maxResults: EntryMatches,
                        ancestry: true
                    ),
                SearchedWindows,
                title,
                pick: reply => PopupRef(reply, PopupHwnds())
            )
        );

    public Task CloseMenu() =>
        inputs.Invoke(
            () =>
                host.Tools.ExecuteToolAsync(
                    "windows_send_keys",
                    JsonSerializer.SerializeToElement(new { handle, chord = "Escape" })
                ),
            tool: "windows_send_keys"
        );

    /// <summary>The process's visible top-level windows other than the main one: where the View popup can be.</summary>
    private HashSet<long> PopupHwnds()
    {
        var main = host.Sessions.GetWindowHwnd(handle);
        var pid = host.Sessions.GetWindowProcessId(handle);
        return Win32Desktop.GetTopLevelWindows(pid).Where(w => w.Hwnd != main).Select(w => (long)w.Hwnd).ToHashSet();
    }

    /// <summary>
    /// The first match of an ancestry find whose chain passes through one of <paramref name="popups"/>; null when every
    /// match is elsewhere (the main window), so no main-window Button is clicked.
    /// </summary>
    internal static string? PopupRef(string? ancestryReply, IReadOnlySet<long> popups)
    {
        try
        {
            using var reply = JsonDocument.Parse(ancestryReply ?? "");
            foreach (var match in reply.RootElement.GetProperty("ancestors").EnumerateArray())
            {
                var inPopup = match
                    .GetProperty("chain")
                    .EnumerateArray()
                    .Any(node => node.TryGetProperty("hwnd", out var hwnd) && popups.Contains(hwnd.GetInt64()));
                if (inPopup && match.GetProperty("ref").GetString() is { Length: > 0 } found)
                {
                    return found;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }

        return null;
    }

    /// <summary>
    /// Repeats <paramref name="find"/> until it gives a ref or the wait runs out; the failure names the windows the
    /// last attempt searched. No click is sent here.
    /// </summary>
    internal static async Task<string> FindEntry(
        Func<Task<string?>> find,
        Func<IEnumerable<string>> windows,
        string title,
        int waitMs = PopupWaitMs,
        int pollMs = PopupPollMs,
        Func<string?, string?>? pick = null
    )
    {
        pick ??= TryFirstRef;
        string? reply = null;
        for (var waited = 0; ; waited += pollMs)
        {
            reply = await find();
            if (pick(reply) is { } found)
            {
                return found;
            }

            if (waited >= waitMs)
            {
                break;
            }

            await Task.Delay(pollMs);
        }

        throw new InvalidOperationException(
            $"View menu entry '{title}' not found after {waitMs} ms; no click sent. Searched windows: "
                + string.Join(", ", windows())
                + ". "
                + reply
        );
    }

    /// <summary>The TE3 main window and the process's other visible top-level windows (popups), as a find covers them.</summary>
    private IEnumerable<string> SearchedWindows()
    {
        var main = host.Sessions.GetWindowHwnd(handle);
        var pid = host.Sessions.GetWindowProcessId(handle);
        var names = new List<string> { $"main {Describe(window.Title, main)}" };
        names.AddRange(
            Win32Desktop
                .GetTopLevelWindows(pid)
                .Where(w => w.Hwnd != main)
                .Select(w => "owned " + Describe(w.Title, w.Hwnd))
        );
        return names;
    }

    private static string Describe(string? title, nint hwnd) =>
        (string.IsNullOrEmpty(title) ? "untitled" : $"'{title}'") + $" (0x{hwnd:X})";

    private async Task<string?> FindReply(
        object selector,
        object? within,
        bool includeOwned,
        int maxResults = 1,
        bool ancestry = false
    )
    {
        var args = new Dictionary<string, object>
        {
            ["handle"] = handle,
            ["selector"] = selector,
            ["includeOwned"] = includeOwned,
            ["maxResults"] = maxResults,
            ["ancestry"] = ancestry,
        };
        if (within != null)
        {
            args["within"] = within;
        }

        var found = await host.Tools.ExecuteToolAsync("windows_find", JsonSerializer.SerializeToElement(args));
        return found.Content.FirstOrDefault()?.Text;
    }

    private async Task Click(string reference)
    {
        var click = JsonSerializer.SerializeToElement(
            new
            {
                @ref = reference,
                handle,
                physical = true,
            }
        );
        await inputs.Invoke(() => host.Tools.ExecuteToolAsync("windows_click", click), tool: "windows_click");
    }

    internal static string FirstRef(string? findReply) =>
        TryFirstRef(findReply)
        ?? throw new InvalidOperationException("View menu entry not found; no click sent. " + findReply);

    private static string? TryFirstRef(string? findReply)
    {
        try
        {
            using var reply = JsonDocument.Parse(findReply ?? "");
            var elements = reply.RootElement.GetProperty("elements");
            if (elements.GetArrayLength() > 0 && elements[0].GetProperty("ref").GetString() is { Length: > 0 } found)
            {
                return found;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }

        return null;
    }
}
