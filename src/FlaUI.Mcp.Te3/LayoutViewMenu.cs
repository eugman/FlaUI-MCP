using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;

/// <summary>
/// The live View menu for te3_layout reopen-pane (see <see cref="PaneReopen"/>): finds MenuItem View in the main menu and
/// the popup Button named after the pane (an owned native window, so includeOwned), and clicks each physically.
/// </summary>
internal sealed class LayoutViewMenu(AutomationHost host, string handle, Window window, InputTally inputs)
    : PaneReopen.IViewMenu
{
    public Task<bool> IsVisible(string title) => Task.FromResult(Te3Layout.PaneVisible(window, title));

    public Task OpenViewMenu() =>
        ClickFound(
            new { controlType = "MenuItem", name = "View" },
            new { controlType = "MenuBar", name = "Main menu" },
            includeOwned: false
        );

    // The View popup is an owned native window that windows_list_windows does not list.
    public Task ClickEntry(string title) =>
        ClickFound(new { controlType = "Button", name = title }, within: null, includeOwned: true);

    private async Task ClickFound(object selector, object? within, bool includeOwned)
    {
        var args = new Dictionary<string, object>
        {
            ["handle"] = handle,
            ["selector"] = selector,
            ["includeOwned"] = includeOwned,
            ["maxResults"] = 1,
        };
        if (within != null)
        {
            args["within"] = within;
        }

        var found = await host.Tools.ExecuteToolAsync("windows_find", JsonSerializer.SerializeToElement(args));
        var reference = FirstRef(found.Content.FirstOrDefault()?.Text);
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

    internal static string FirstRef(string? findReply)
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

        throw new InvalidOperationException("View menu entry not found; no click sent. " + findReply);
    }
}
