using System.Runtime.CompilerServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

public sealed class WindowStateTargetTests
{
    [Fact]
    public async Task ARefPairedWithAnotherWindowHandleIsRejectedBeforeInput()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var refs = new ElementRegistry();
        var reference = refs.Register(
            "w1",
            (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement))
        );
        refs.SetWindowProcessId("w1", FakeDesktop.TargetPid);
        var registry = new ToolRegistry();
        registry.RegisterTool(new WindowStateTool(refs, new PendingInvokeTracker(), sessions));

        var result = await registry.ExecuteToolAsync(
            "windows_set_window_state",
            JsonSerializer.SerializeToElement(
                new
                {
                    @ref = reference,
                    handle = "w2",
                    state = "maximize",
                }
            )
        );

        Assert.True(result.IsError);
        Assert.Contains($"Ref {reference} belongs to window w1, not w2.", result.Content[0].Text);
        Assert.Empty(desktop.StateRequests);
    }
}
