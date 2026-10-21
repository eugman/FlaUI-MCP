using System.Text.Json;
using FlaUI.Mcp;

/// <summary>Step loops for te3_layout actions that change several panes: a failure keeps what earlier steps did.</summary>
internal static class LayoutSteps
{
    /// <summary>A step loop failed part-way; carries what the earlier steps already did to the layout.</summary>
    public sealed class ProgressException(string message, string name, List<object> completed, Exception inner)
        : InvalidOperationException(message, inner)
    {
        public string Name => name;
        public List<object> Completed => completed;
    }

    /// <summary>Like <see cref="Progress(string, Func{Task{object?}})"/>; the step calls sent before its input goes out.</summary>
    public static Task<List<object>> Progress(string name, Func<Action<object>, Task<object?>> step) =>
        Progress(name, () => step(_ => { }));

    /// <summary>Runs step until it returns null and collects what each step did, also when a later step fails.</summary>
    public static async Task<List<object>> Progress(string name, Func<Task<object?>> step)
    {
        var completed = new List<object>();
        try
        {
            while (await step() is { } result)
            {
                completed.Add(result);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ProgressException(ex.Message, name, completed, ex);
        }

        return completed;
    }

    /// <summary>The failure text: plain, or JSON naming the steps that completed before it.</summary>
    public static string Describe(string tool, Exception ex) =>
        ex is ProgressException progress
            ? JsonSerializer.Serialize(
                new Dictionary<string, object>
                {
                    ["error"] = $"{tool}: {ex.Message}",
                    [progress.Name] = progress.Completed,
                },
                McpProtocol.JsonOptions
            )
            : $"{tool}: {ex.Message}";
}
