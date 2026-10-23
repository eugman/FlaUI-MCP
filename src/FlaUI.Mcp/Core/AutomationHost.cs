using FlaUI.Mcp.Tools;

namespace FlaUI.Mcp.Core;

/// <summary>Owns shared services; the runner omits MCP-only window-management tools.</summary>
public sealed class AutomationHost : IDisposable
{
    public SessionManager Sessions { get; }
    public ElementRegistry Elements { get; } = new();
    public PendingInvokeTracker Pending { get; } = new();
    public ToolRegistry Tools { get; }
    private readonly ProcessPolicy _policy;

    public AutomationHost(ProcessPolicy policy, bool includeDesktopTools = false, Action? onToolActivity = null)
    {
        _policy = policy;
        Sessions = new(policy);
        Tools = new(onToolActivity: onToolActivity);
        try
        {
            ToolComposition.Register(Tools, Sessions, Elements, Pending, policy, includeDesktopTools);
        }
        catch
        {
            Sessions.Dispose();
            throw;
        }
    }

    /// <summary>Register shared tools into a product registry using this host's sessions and guards.</summary>
    public void RegisterAllToolsInto(ToolRegistry registry, bool includeDesktopTools = true)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (registry.GetToolDefinitions().Any(tool => tool.Name.StartsWith("windows_", StringComparison.Ordinal)))
        {
            throw new ArgumentException("Registry already contains a shared Windows tool.", nameof(registry));
        }

        ToolComposition.Register(registry, Sessions, Elements, Pending, _policy, includeDesktopTools);
    }

    public void Dispose() => Sessions.Dispose();
}
