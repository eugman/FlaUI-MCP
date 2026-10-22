using System.Text.Json;
using FlaUI.Core.Definitions;
using FlaUI.Mcp;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A batch row's "action" names the batch step, not the tool's own action argument: passed through, a click
/// row's "action":"click" made windows_click force a mouse click on every row, skipping the select pattern and
/// the already-selected guard on tree rows.
/// </summary>
public sealed class BatchRowTests
{
    [Fact]
    public async Task AClickRowReachesTheToolWithoutTheBatchAction()
    {
        var rows = JsonSerializer.Deserialize<JsonElement[]>("""[{"action":"click","ref":"w1e3"}]""")!;
        JsonElement? sent = null;

        await BatchTool.ExecuteRows(
            rows,
            (_, row) =>
            {
                sent = row;
                return Task.FromResult(new McpToolResult { Content = [] });
            }
        );

        Assert.False(sent!.Value.TryGetProperty("action", out _));
        Assert.Equal("w1e3", sent.Value.GetProperty("ref").GetString());
        // With no action argument, windows_click selects a tree row instead of clicking it.
        Assert.Equal("select", ClickTool.ChooseAction(null, false, ControlType.TreeItem, _ => true));
    }
}
