/// <summary>The three observed TOM Explorer states: visible, another tab selected, or the pane closed.</summary>
internal static class TomPaneOpen
{
    public static async Task<string?> FindTab(Func<string, Task<bool>> exists)
    {
        foreach (var title in new[] { "TOM Explorer", "TOM Explorer (Changed)" })
        {
            if (await exists(title))
            {
                return title;
            }
        }

        return null;
    }

    public static async Task Verify(
        Func<Task<bool?>> treeVisible,
        Func<Task<string?>> findTab,
        Func<string, Task> verifySelected
    )
    {
        if (await treeVisible() == true)
        {
            return;
        }

        var title =
            await findTab()
            ?? throw new InvalidOperationException(
                "TOM Explorer tree visibility is unknown and no TOM Explorer tab is exposed."
            );
        await verifySelected(title);
    }

    public static async Task Ensure(
        Func<Task<bool>> treeVisible,
        Func<Task<string?>> findTab,
        Func<string, Task> selectTab,
        Func<Task> reopenPane
    )
    {
        if (await treeVisible())
        {
            return;
        }

        if (await findTab() is { } tabTitle)
        {
            await selectTab(tabTitle);
        }
        else
        {
            await reopenPane();
        }

        if (!await treeVisible())
        {
            throw new InvalidOperationException(
                "TOM Explorer tree is still hidden after selecting its tab or reopening the pane; inspect before retrying."
            );
        }
    }
}
