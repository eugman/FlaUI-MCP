using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// What a windows_mouse drag reports after its release: the element UI Automation finds under the pointer
/// (droppedOn) and, for a resizeTo drag, the size the ref reached against the one requested, as its proof.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class DragReportTests
{
    private static readonly Rectangle Row = new(433, 214, 435, 22);

    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static (ElementRegistry Refs, SessionManager Sessions, string Handle) Window()
    {
        var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, 0, FakeDesktop.TargetPid, FakeDesktop.Target);
        return (new ElementRegistry(), sessions, handle);
    }

    private static Task<McpToolResult> Run(MouseTool tool, object arguments)
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(tool);
        return registry.ExecuteToolAsync(tool.Name, Args(arguments));
    }

    private static JsonElement Reply(McpToolResult result) => JsonDocument.Parse(result.Content[0].Text!).RootElement;

    [Fact]
    public async Task ADragReportsWhatUiAutomationFindsUnderThePointerAfterTheRelease()
    {
        // Verified on the row up to the release; the app re-laid out at the drop, and the reply says where it went.
        var row = Element();
        var folder = Element();
        var desktop = new FakeDesktop();
        desktop.ElementBounds[row] = Row;
        desktop.Descriptions[folder] = "TreeItem '2. Quantity'";
        desktop.HitTest = _ => desktop.Dispatched >= 15 ? folder : row;
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, row);

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                @ref = reference,
                toRef = reference,
            }
        );

        Assert.True(result.IsError != true, result.Content[0].Text);
        Assert.Equal("TreeItem '2. Quantity'", Reply(result).GetProperty("droppedOn").GetString());
    }

    [Fact]
    public async Task ADropOnNothingOrAnUnreadableHitTestIsSaidSo()
    {
        var row = Element();
        var desktop = new FakeDesktop();
        desktop.ElementBounds[row] = Row;
        desktop.HitTest = _ => desktop.Dispatched >= 15 ? null : row;
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, row);
        var tool = new MouseTool(refs, new PendingInvokeTracker(), sessions);

        var nothing = await Run(
            tool,
            new
            {
                action = "drag",
                @ref = reference,
                toRef = reference,
            }
        );
        desktop.HitTest = _ => desktop.Dispatched >= 30 ? throw new TimeoutException("provider blocked") : row;
        var unreadable = await Run(
            tool,
            new
            {
                action = "drag",
                @ref = reference,
                toRef = reference,
            }
        );

        Assert.Equal("nothing UI Automation can see", Reply(nothing).GetProperty("droppedOn").GetString());
        Assert.Equal("unreadable (provider blocked)", Reply(unreadable).GetProperty("droppedOn").GetString());
    }

    [Fact]
    public async Task ARawPointDragHasNoRefToAskThrough()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                handle,
                from = new { x = 10, y = 10 },
                to = new { x = 50, y = 10 },
            }
        );

        Assert.True(result.IsError != true, result.Content[0].Text);
        Assert.False(Reply(result).TryGetProperty("droppedOn", out var droppedOn), droppedOn.ToString());
    }

    [Theory]
    [InlineData("right", 300, 300, "verified", "ok")]
    [InlineData("right", 300, 290, "mismatch", "failed")]
    [InlineData("left", 300, 300, "verified", "ok")]
    [InlineData("bottom", 40, 40, "verified", "ok")]
    [InlineData("top", 40, 36, "mismatch", "failed")]
    public async Task AResizeDragProvesTheSizeTheRefReached(
        string edge,
        int requested,
        int reached,
        string verdict,
        string status
    )
    {
        var pane = Element();
        var desktop = new FakeDesktop();
        var tall = edge is "top" or "bottom";
        var after = tall ? new Rectangle(433, 214, 435, reached) : new Rectangle(433, 214, reached, 22);
        desktop.OnGeometry = _ => new ClickGeometry(false, null, desktop.Dispatched >= 15 ? after : Row, []);
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, pane);

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                @ref = reference,
                fromEdge = edge,
                resizeTo = requested,
            }
        );

        Assert.Equal(status, result.Outcome!.Status);
        Assert.Equal(verdict, result.Outcome.Verification);
        Assert.Equal(verdict, result.Outcome.Proof!.Verdict);
        Assert.Equal(requested.ToString(), result.Outcome.Proof.Expected);
        Assert.Equal(reached.ToString(), result.Outcome.Proof.Observed);
        Assert.Equal(tall ? "element-height" : "element-width", result.Outcome.Proof.Source);
        Assert.True(result.Outcome.Proof.Independent);
        Assert.Equal(status == "failed", result.IsError == true);
        var text = result.Content[0].Text!;
        Assert.Equal(
            status == "failed",
            text.StartsWith("Released, but the element did not reach the requested size: ", StringComparison.Ordinal)
        );
        var reply = JsonDocument.Parse(text[text.IndexOf('{')..]).RootElement.GetProperty("resized");
        Assert.Equal(requested.ToString(), reply.GetProperty("requested").GetString());
        Assert.Equal(reached.ToString(), reply.GetProperty("reached").GetString());
    }

    [Fact]
    public async Task AResizeWhoseBoundsCannotBeReadBackIsUnverified()
    {
        var pane = Element();
        var desktop = new FakeDesktop();
        desktop.OnGeometry = _ =>
            desktop.Dispatched >= 15
                ? throw new InvalidOperationException("gone")
                : new ClickGeometry(false, null, Row, []);
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, pane);

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                @ref = reference,
                fromEdge = "right",
                resizeTo = 300,
            }
        );

        Assert.Equal("unverified", result.Outcome!.Status);
        Assert.Equal("unavailable", result.Outcome.Proof!.Verdict);
        Assert.Null(result.Outcome.Proof.Observed);
        Assert.True(result.IsError != true);
    }

    [Fact]
    public async Task ADragWithoutAResizeCarriesNoProof()
    {
        var row = Element();
        var desktop = new FakeDesktop();
        desktop.ElementBounds[row] = Row;
        using var installed = desktop.Install();
        var (refs, sessions, handle) = Window();
        using var _ = sessions;
        var reference = refs.Register(handle, row);

        var result = await Run(
            new MouseTool(refs, new PendingInvokeTracker(), sessions),
            new
            {
                action = "drag",
                @ref = reference,
                toRef = reference,
            }
        );

        Assert.Null(result.Outcome!.Proof);
        Assert.Equal("unverified", result.Outcome.Status);
        Assert.False(Reply(result).TryGetProperty("resized", out var resized), resized.ToString());
    }
}
