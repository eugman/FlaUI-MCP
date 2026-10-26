using System.Text.Json;

namespace FlaUI.Mcp.Core;

/// <summary>Composes generic Windows tools beside a product host without replacing its callbacks.</summary>
public static class WindowsToolAdapter
{
    private const string Prefix = "windows_";

    /// <summary>Wire the shared tools to the host's existing grant and recovery callbacks.</summary>
    public static void Configure(
        ToolRegistry registry,
        AutomationHost windows,
        ProcessPolicy policy,
        Action require,
        Func<CancellationToken, ValueTask<IAsyncDisposable?>> enterWindows,
        Action<string> pause,
        Action observed
    )
    {
        windows.RegisterAllToolsInto(registry);
        var recovery = new WindowsRecovery(pause, observed);
        var productFailure = registry.FailedAfterInput;
        var productTargetFailure = registry.FailedAfterInputWithTarget;
        var productObservation = registry.ObservedAfterSuccess;
        var productScope = registry.ExecutionScopeForTool;
        registry.FailedAfterInput = (name, outcome) =>
        {
            if (!IsWindows(name))
            {
                productFailure?.Invoke(name, outcome);
            }
        };
        registry.FailedAfterInputWithTarget = (name, outcome, target) =>
        {
            if (IsWindows(name))
            {
                recovery.Failed(name, outcome, target);
            }
            else
            {
                productTargetFailure?.Invoke(name, outcome, target);
            }
        };
        registry.ObservedAfterSuccess = (name, args, outcome, target) =>
        {
            if (IsWindows(name))
            {
                recovery.Observed(name, outcome, target);
            }
            else
            {
                productObservation?.Invoke(name, args, outcome, target);
            }
        };
        registry.ExecutionScopeForTool = (name, annotations, token) =>
            IsWindows(name) && name is not ("windows_list_windows" or "windows_operation_status")
                ? enterWindows(token)
                : productScope?.Invoke(name, annotations, token) ?? ValueTask.FromResult<IAsyncDisposable?>(null);
        registry.MutationPermissionForTool = (name, annotations) =>
            IsWindows(name) && annotations?.ReadOnlyHint != true ? require : null;
        registry.AdvisoryWarning = (name, args, target) => Warning(policy, name, args, target);
    }

    /// <summary>Install only the shared warning for hosts that already compose Core tools.</summary>
    public static void InstallAdvisory(ToolRegistry registry, ProcessPolicy policy) =>
        registry.AdvisoryWarning = (name, args, target) => Warning(policy, name, args, target);

    private static bool IsWindows(string name) => name.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Warn on cross-app use; the policy remains advisory and never denies input.</summary>
    public static string? Warning(ProcessPolicy policy, string name, JsonElement? args, ProcessIdentity? target)
    {
        if (!policy.IsAdvisory || !IsWindows(name) || name is "windows_list_windows" or "windows_operation_status")
        {
            return null;
        }

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

    private sealed class WindowsRecovery(Action<string> pause, Action observed)
    {
        private readonly object sync = new();
        private ProcessIdentity? failedTarget;
        private bool paused;

        internal void Failed(string name, ToolOutcome outcome, ProcessIdentity? target)
        {
            lock (sync)
            {
                failedTarget = target;
                paused = true;
                var instruction =
                    target == null
                        ? "Begin a new interaction grant before another input."
                        : "Observe the same window with a handle before another input.";
                pause($"{name} failed after sending {outcome.InputsSent ?? 0} input(s); {instruction}");
            }
        }

        internal void Observed(string name, ToolOutcome outcome, ProcessIdentity? target)
        {
            if (
                outcome.Status != "ok"
                || outcome.IsPending
                || outcome.InputsSent > 0
                || name is not ("windows_snapshot" or "windows_screenshot" or "windows_find" or "windows_get_text")
            )
            {
                return;
            }

            lock (sync)
            {
                if (!paused || target == null || target != failedTarget)
                {
                    return;
                }

                observed();
                paused = false;
                failedTarget = null;
            }
        }
    }
}
