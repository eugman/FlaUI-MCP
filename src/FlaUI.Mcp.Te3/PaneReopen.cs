using FlaUI.Mcp.Core;

/// <summary>
/// Reopens a hidden docked pane from TE3's View menu: open the View menu, then click the popup Button named after the
/// pane. Only entries observed in the View popup (TE3 3.27.1, automation/UI-MAP.md) are clicked; any other title is
/// refused before input.
/// </summary>
internal static class PaneReopen
{
    public const string UnverifiedLocator =
        "Pane is hidden. Its reopen menu locator is not verified for this TE3 build; no input sent.";

    private const int SettleMs = 2000;
    private const int PollMs = 200;

    /// <summary>
    /// View popup Buttons observed on TE3 3.27.1 that open docked panes. The popup's "Expression Editor" entry is left
    /// out: it opens a document, which the pane check never counts as visible.
    /// </summary>
    internal static readonly string[] ViewMenuPanes =
    [
        "TOM Explorer",
        "AI Assistant",
        "DAX Package Manager",
        "Best Practice Analyzer",
        "Messages",
        "Data Refresh",
        "Macros",
        "VertiPaq Analyzer",
        "Dependencies",
        "DAX Optimizer",
        "Calendar Editor",
        "Perspective Editor",
        "Metadata Translation Editor",
        "Properties",
    ];

    public static bool IsMapped(string title) => ViewMenuPanes.Contains(title);

    /// <summary>View popup entries observed on TE3 3.27.1 that open a document, not a docked pane.</summary>
    internal static readonly string[] ViewMenuDocuments = ["Expression Editor"];

    /// <summary>The refusal for a document title: reopen-pane only opens docked panes.</summary>
    public static string NotAPane(string title) =>
        $"'{title}' is a document, not a dockable pane in the View menu map; reopen-pane opens only docked panes. "
        + "No input sent.";

    /// <summary>The View menu as te3_layout drives it.</summary>
    public interface IViewMenu
    {
        /// <summary>Whether a docked pane with this title is showing.</summary>
        Task<bool> IsVisible(string title);

        /// <summary>Whether an open document tab has this title.</summary>
        Task<bool> IsDocument(string title);

        /// <summary>Click MenuItem View physically.</summary>
        Task OpenViewMenu();

        /// <summary>Click the View popup Button with this name physically.</summary>
        Task ClickEntry(string title);

        /// <summary>Press Escape once to close the open View menu.</summary>
        Task CloseMenu();

        /// <summary>
        /// Whether the pane is showing as a floating window of its own (Dependencies opens floating by default, an
        /// unowned top-level window; 3.27.1).
        /// </summary>
        Task<bool> IsFloating(string title) => Task.FromResult(false);

        /// <summary>The pause between reads while the pane settles; a test's menu passes its own clock.</summary>
        Task Delay(int ms) => Task.Delay(ms);
    }

    /// <summary>Shows the pane; a visible pane is left alone, and a mapped hidden one is reopened and checked.</summary>
    public static async Task<object> Show(IViewMenu menu, string title)
    {
        if (await menu.IsVisible(title) || await menu.IsFloating(title))
        {
            return new
            {
                pane = title,
                visible = true,
                floating = await menu.IsFloating(title),
                changed = false,
            };
        }

        if (ViewMenuDocuments.Contains(title) || await menu.IsDocument(title))
        {
            throw new InvalidOperationException(NotAPane(title));
        }

        if (!IsMapped(title))
        {
            throw new InvalidOperationException(UnverifiedLocator);
        }

        await menu.OpenViewMenu();
        try
        {
            await menu.ClickEntry(title);
        }
        catch (Exception ex) when (ex is InvalidOperationException or AutomationRefusal)
        {
            // The View click left the menu open, so the next tool's keys would go to it.
            await menu.CloseMenu();
            throw new InvalidOperationException(ex.Message + " View menu closed with Escape (1 input).", ex);
        }

        for (var waited = 0; waited <= SettleMs; waited += PollMs)
        {
            // Docked, or floating as a window of its own: either way the pane is showing.
            var floating = await menu.IsFloating(title);
            if (floating || await menu.IsVisible(title))
            {
                return new
                {
                    pane = title,
                    visible = true,
                    floating,
                    changed = true,
                };
            }

            await menu.Delay(PollMs);
        }

        throw new InvalidOperationException(
            $"Pane '{title}' did not appear after View > {title}; inspect before retrying."
        );
    }
}
