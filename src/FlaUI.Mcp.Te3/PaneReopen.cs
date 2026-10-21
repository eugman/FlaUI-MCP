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

    /// <summary>The View menu as te3_layout drives it.</summary>
    public interface IViewMenu
    {
        /// <summary>Whether a docked pane with this title is showing.</summary>
        Task<bool> IsVisible(string title);

        /// <summary>Click MenuItem View physically.</summary>
        Task OpenViewMenu();

        /// <summary>Click the View popup Button with this name physically.</summary>
        Task ClickEntry(string title);

        /// <summary>Press Escape once to close the open View menu.</summary>
        Task CloseMenu();
    }

    /// <summary>Shows the pane; a visible pane is left alone, and a mapped hidden one is reopened and checked.</summary>
    public static async Task<object> Show(IViewMenu menu, string title)
    {
        if (await menu.IsVisible(title))
        {
            return new
            {
                pane = title,
                visible = true,
                changed = false,
            };
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
        catch (InvalidOperationException ex)
        {
            // The View click left the menu open, so the next tool's keys would go to it.
            await menu.CloseMenu();
            throw new InvalidOperationException(ex.Message + " View menu closed with Escape (1 input).", ex);
        }

        for (var waited = 0; waited <= SettleMs; waited += PollMs)
        {
            if (await menu.IsVisible(title))
            {
                return new
                {
                    pane = title,
                    visible = true,
                    changed = true,
                };
            }

            await Task.Delay(PollMs);
        }

        throw new InvalidOperationException(
            $"Pane '{title}' did not appear after View > {title}; inspect before retrying."
        );
    }
}
