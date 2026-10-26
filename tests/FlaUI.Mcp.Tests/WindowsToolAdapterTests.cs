using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

public sealed class WindowsToolAdapterTests
{
    [Fact]
    public async Task RoutesWindowsRecoveryAndGrantWithoutSwallowingProductCallbacks()
    {
        var registry = new ToolRegistry();
        using var windows = new AutomationHost(ProcessPolicy.AllowAll);
        var productFailures = 0;
        var productTargetFailures = 0;
        var productObservations = 0;
        var productScopes = 0;
        var grants = 0;
        var pauses = 0;
        var recovered = 0;
        registry.FailedAfterInput = (_, _) => productFailures++;
        registry.FailedAfterInputWithTarget = (_, _, _) => productTargetFailures++;
        registry.ObservedAfterSuccess = (_, _, _, _) => productObservations++;
        registry.ExecutionScopeForTool = (_, _, _) =>
        {
            productScopes++;
            return ValueTask.FromResult<IAsyncDisposable?>(null);
        };

        WindowsToolAdapter.Configure(
            registry,
            windows,
            new ProcessPolicy(["Preferred"], advisory: true),
            () => grants++,
            _ => ValueTask.FromResult<IAsyncDisposable?>(null),
            _ => pauses++,
            () => recovered++
        );

        Assert.Contains("windows_launch", registry.GetToolDefinitions().Select(tool => tool.Name));
        Assert.Contains(
            "outside the preferred apps",
            registry.AdvisoryWarning!("windows_launch", JsonSerializer.SerializeToElement(new { app = "wt.exe" }), null)
        );
        registry.MutationPermissionForTool!("windows_launch", McpToolAnnotations.Input(false))!();
        Assert.Equal(1, grants);
        Assert.Null(registry.MutationPermissionForTool!("windows_snapshot", McpToolAnnotations.ReadOnly));
        Assert.Null(registry.MutationPermissionForTool!("product_tool", McpToolAnnotations.Input(false)));
        await registry.ExecutionScopeForTool!("windows_snapshot", McpToolAnnotations.ReadOnly, CancellationToken.None);
        Assert.Equal(0, productScopes);
        await registry.ExecutionScopeForTool!("product_tool", null, CancellationToken.None);
        Assert.Equal(1, productScopes);

        var target = new ProcessIdentity(42, 7);
        registry.FailedAfterInputWithTarget!(
            "windows_send_keys",
            new ToolOutcome("send", "failed") { Status = "failed", InputsSent = 1 },
            target
        );
        registry.ObservedAfterSuccess!(
            "windows_snapshot",
            null,
            new ToolOutcome("read", "ok") { Status = "ok" },
            new ProcessIdentity(43, 7)
        );
        Assert.Equal(0, recovered);
        registry.ObservedAfterSuccess!(
            "windows_snapshot",
            null,
            new ToolOutcome("read", "ok") { Status = "ok" },
            target
        );
        Assert.Equal(1, recovered);
        Assert.Equal(1, pauses);
        registry.FailedAfterInput!("product_tool", new ToolOutcome("send", "failed"));
        registry.FailedAfterInputWithTarget!("product_tool", new ToolOutcome("send", "failed"), target);
        registry.ObservedAfterSuccess!("product_tool", null, new ToolOutcome("read", "ok") { Status = "ok" }, null);
        Assert.Equal(1, productFailures);
        Assert.Equal(1, productTargetFailures);
        Assert.Equal(1, productObservations);
    }

    [Fact]
    public void AdvisoryAndRecoveryKeepReadOnlyAndIdentityBoundaries()
    {
        var preferred = new ProcessPolicy(["Preferred"], advisory: true);
        Assert.Null(WindowsToolAdapter.Warning(preferred, "windows_list_windows", null, null));
        Assert.Null(WindowsToolAdapter.Warning(preferred, "product_tool", null, null));
        Assert.Null(
            WindowsToolAdapter.Warning(
                new ProcessPolicy(["Preferred"]),
                "windows_launch",
                JsonSerializer.SerializeToElement(new { app = "wt.exe" }),
                null
            )
        );
        Assert.Contains("clipboard source", WindowsToolAdapter.Warning(preferred, "windows_get_clipboard", null, null));

        var registry = new ToolRegistry();
        using var windows = new AutomationHost(ProcessPolicy.AllowAll);
        var recovered = 0;
        WindowsToolAdapter.Configure(
            registry,
            windows,
            preferred,
            () => { },
            _ => ValueTask.FromResult<IAsyncDisposable?>(null),
            _ => { },
            () => recovered++
        );
        var target = new ProcessIdentity(42, 7);
        registry.FailedAfterInputWithTarget!(
            "windows_send_keys",
            new ToolOutcome("send", "failed") { Status = "failed", InputsSent = 1 },
            target
        );
        registry.ObservedAfterSuccess!(
            "windows_snapshot",
            null,
            new ToolOutcome("read", "pending") { Status = "ok" },
            target
        );
        registry.ObservedAfterSuccess!(
            "windows_snapshot",
            null,
            new ToolOutcome("read", "sent") { Status = "ok", InputsSent = 1 },
            target
        );
        registry.ObservedAfterSuccess!(
            "windows_snapshot",
            null,
            new ToolOutcome("read", "ok") { Status = "ok" },
            new ProcessIdentity(43, 7)
        );
        Assert.Equal(0, recovered);
        registry.ObservedAfterSuccess!(
            "windows_get_text",
            null,
            new ToolOutcome("read", "ok") { Status = "ok" },
            target
        );
        Assert.Equal(1, recovered);
    }
}
