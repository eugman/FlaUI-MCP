/// <summary>The three observed TOM Explorer states: visible, another tab selected, or the pane closed.</summary>
internal static class TomPaneOpen
{
    public static async Task Ensure(
        Func<Task<bool>> treeVisible,
        Func<Task<bool>> tabExists,
        Func<Task> selectTab,
        Func<Task> reopenPane
    )
    {
        if (await treeVisible())
        {
            return;
        }

        if (await tabExists())
        {
            await selectTab();
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
