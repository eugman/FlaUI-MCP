using FlaUI.Mcp.Tools;

namespace FlaUI.Mcp.Core;

/// <summary>Shared tool instances and explicit MCP-only discovery, launch and batch extensions.</summary>
internal static class ToolComposition
{
    public static void Register(
        ToolRegistry registry,
        SessionManager sessions,
        ElementRegistry elements,
        PendingInvokeTracker pending,
        ProcessPolicy policy,
        bool includeDesktopTools
    )
    {
        if (registry.SecondaryPending != null && registry.SecondaryPending != pending)
        {
            throw new ArgumentException("Registry already has a secondary pending tracker.", nameof(registry));
        }

        var productResolver = registry.ResolveTarget;
        var productToolResolver = registry.ResolveTargetForTool;
        ProcessIdentity? CoreTarget(System.Text.Json.JsonElement? arguments) =>
            sessions.ResolveTarget(arguments, elements);
        registry.ResolveTarget = arguments => productResolver?.Invoke(arguments) ?? CoreTarget(arguments);
        registry.ResolveTargetForTool = (name, arguments) =>
        {
            if (name.StartsWith("windows_", StringComparison.Ordinal))
            {
                return CoreTargetForTool(name, arguments, sessions, elements);
            }

            return productToolResolver?.Invoke(name, arguments)
                ?? productResolver?.Invoke(arguments)
                ?? CoreTarget(arguments);
        };
        if (registry.Pending == null)
        {
            registry.Pending = pending;
        }
        else if (registry.Pending != pending)
        {
            registry.SecondaryPending = pending;
        }

        var query = new ElementQuery(sessions, elements, pending);
        registry.RegisterTool(
            new OperationStatusTool(
                registry.Operations,
                pending,
                registry.Pending != pending ? registry.Pending : registry.SecondaryPending
            )
        );
        registry.RegisterTool(new FindTool(query));
        registry.RegisterTool(new WaitTool(query));
        registry.RegisterTool(new SnapshotTool(sessions, elements, pending));
        registry.RegisterTool(new ClickTool(elements, pending));
        registry.RegisterTool(new FillTool(elements, pending));
        registry.RegisterTool(new TypeTool(elements, pending, policy, sessions));
        registry.RegisterTool(new PasteTool(elements, pending, policy, sessions));
        // Shared, not desktop-only: product hosts read back editors that expose no text to UI
        // Automation (e.g. Scintilla) to verify what they pasted.
        registry.RegisterTool(new GetClipboardTool(policy));
        registry.RegisterTool(new SendKeysTool(elements, pending, policy, sessions));
        registry.RegisterTool(new ScreenshotTool(sessions, elements, pending, policy));
        registry.RegisterTool(new WindowPlacementTool(elements, pending, sessions));
        registry.RegisterTool(new WindowStateTool(elements, pending, sessions));
        registry.RegisterTool(new MouseTool(elements, pending, sessions));
        if (includeDesktopTools)
        {
            registry.RegisterTool(new LaunchTool(sessions));
            registry.RegisterTool(new GetTextTool(elements, pending));
            registry.RegisterTool(new ListWindowsTool(sessions, policy));
            registry.RegisterTool(new FocusWindowTool(sessions, pending));
            registry.RegisterTool(new CloseWindowTool(sessions, pending));
            registry.RegisterTool(new BatchTool(registry));
        }
    }

    private static ProcessIdentity? CoreTargetForTool(
        string name,
        System.Text.Json.JsonElement? arguments,
        SessionManager sessions,
        ElementRegistry elements
    )
    {
        var target = sessions.ResolveTarget(arguments, elements);
        if (target != null || name != "windows_snapshot")
        {
            return target;
        }

        if (
            arguments is { ValueKind: System.Text.Json.JsonValueKind.Object } args
            && args.TryGetProperty("handle", out var handle)
            && handle.ValueKind == System.Text.Json.JsonValueKind.String
            && !string.IsNullOrEmpty(handle.GetString())
        )
        {
            return null;
        }

        return sessions.ResolveForegroundTarget();
    }
}
