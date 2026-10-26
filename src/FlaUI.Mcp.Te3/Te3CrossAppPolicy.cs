using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;

/// <summary>TE3 is preferred; shared Windows tools can explicitly target other apps.</summary>
public static class Te3CrossAppPolicy
{
    public static ProcessPolicy PreferredPolicy() =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ProcessPolicy.EnvironmentVariable))
            ? new ProcessPolicy(["TabularEditor3"], advisory: true)
            : ProcessPolicy.FromEnvironment();

    public static void Install(ToolRegistry registry, ProcessPolicy policy) =>
        WindowsToolAdapter.InstallAdvisory(registry, policy);

    internal static string? Warning(ProcessPolicy policy, string name, JsonElement? args, ProcessIdentity? target) =>
        WindowsToolAdapter.Warning(policy, name, args, target);
}
