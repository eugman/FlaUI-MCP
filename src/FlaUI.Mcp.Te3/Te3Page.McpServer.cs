using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

public sealed partial class Te3Page
{
    private string McpDialogHandle()
    {
        var pid = host.Sessions.GetWindowProcessId(handle);
        var windows = Win32Desktop.GetTopLevelWindows(pid).Where(w => w.Title == "MCP Server").ToArray();
        if (windows.Length != 1)
        {
            throw new InvalidOperationException("Expected exactly one native MCP Server window.");
        }

        return host.Sessions.RegisterNativeWindow(windows[0].Hwnd, pid);
    }

    public async Task OpenNativeMcpServer()
    {
        var before = TitledWindows().Select(w => w.Hwnd).ToArray();
        if (TitledWindows().Any(w => w.Title == "MCP Server"))
        {
            return;
        }

        var status = await Resolve(new(new(NameContains: "MCP ", ControlType: "Button"), StatusBarTarget().Selector));
        var name = status.Properties.Name.ValueOrDefault;
        if (name is not ("MCP Stopped" or "MCP Started" or "MCP Running"))
        {
            throw new InvalidOperationException("Unrecognized MCP status button: " + name);
        }

        await Click(new(new(Name: name, ControlType: "Button"), StatusBarTarget().Selector));
        var (_, window) = await WaitForNewWindow(before, TimeSpan.FromSeconds(15));
        if (window.Title != "MCP Server")
        {
            throw new InvalidOperationException("Status opened an unexpected window: " + window.Title);
        }
    }

    public async Task VerifyNativeMcpServer()
    {
        var dialogHandle = McpDialogHandle();
        var dialog = host.Sessions.GetInputTarget(dialogHandle);
        var target = McpServerMarkerTarget();
        var marker = await Task.Run(() =>
                query.Resolve(
                    dialogHandle,
                    target.Selector,
                    target.Within,
                    false,
                    budget: new SearchBudget(3000, TimeSpan.FromSeconds(2))
                )
            )
            .WaitAsync(TimeSpan.FromSeconds(5));
        var dialogBounds =
            Win32Desktop.GetWindowBounds(dialog.Hwnd)
            ?? throw new InvalidOperationException("MCP Server window disappeared.");
        var rect = marker.BoundingRectangle;
        var markerBounds = System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (rect.IsEmpty || !dialogBounds.Contains(markerBounds))
        {
            throw new InvalidOperationException("MCP Server controls are not inside the visible dialog.");
        }
    }
}
