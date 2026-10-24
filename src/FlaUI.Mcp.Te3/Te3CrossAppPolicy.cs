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
        registry.AdvisoryWarning = (name, args, target) => Warning(policy, name, args, target);

    internal static string? Warning(ProcessPolicy policy, string name, JsonElement? args, ProcessIdentity? target)
    {
        if (
            !policy.IsAdvisory
            || !name.StartsWith("windows_", StringComparison.Ordinal)
            || name is "windows_list_windows" or "windows_operation_status"
        )
        {
            return null;
        }

        // The clipboard is shared across processes. The focused window does not prove
        // which app placed the current text there, even when that window is TE3.
        if (name == "windows_get_clipboard")
        {
            return "The Windows clipboard source cannot be attributed to an application. "
                + "The operation remains allowed; verify the text came from the intended app.";
        }

        if (
            name == "windows_launch"
            && args is { ValueKind: JsonValueKind.Object } launch
            && launch.TryGetProperty("app", out var app)
            && app.ValueKind == JsonValueKind.String
        )
        {
            return policy.DescribeAdvisory(app.GetString());
        }

        var pid = target?.ProcessId ?? 0;
        if (
            pid == 0
            && args is { ValueKind: JsonValueKind.Object } request
            && request.TryGetProperty("title", out var title)
            && title.ValueKind == JsonValueKind.String
        )
        {
            var matches = Win32Desktop
                .GetTopLevelWindows()
                .Where(window => window.Title == title.GetString() && !window.IsCloaked)
                .Take(2)
                .ToArray();
            if (matches.Length != 1)
            {
                return policy.DescribeAdvisory(null);
            }

            pid = matches[0].ProcessId;
        }

        pid = pid == 0 ? Win32Desktop.GetForegroundWindowProcessId() : pid;
        return policy.DescribeAdvisory(ProcessPolicy.NameOf(pid));
    }
}
