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

    /// <summary>A step whose input went out before it failed: its effect on the layout is unconfirmed.</summary>
    public sealed record AttemptedStep(object Attempted, string Error);

    /// <summary>Runs step until it returns null and collects what each step did, also when a later step fails.</summary>
    public static Task<List<object>> Progress(string name, Func<Task<object?>> step) => Progress(name, _ => step());

    /// <summary>
    /// As above; the step calls sent once its input went out (see <see cref="Sending"/>), so a step that fails after
    /// that is recorded as attempted. Every failure keeps the record, COM failures included; cancellation propagates.
    /// </summary>
    public static async Task<List<object>> Progress(string name, Func<Action<object>, Task<object?>> step)
    {
        var completed = new List<object>();
        object? sent = null;
        try
        {
            while (await step(item => sent = item) is { } result)
            {
                completed.Add(result);
                sent = null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (sent != null)
            {
                completed.Add(new AttemptedStep(sent, ex.Message));
            }

            throw new ProgressException(ex.Message, name, completed, ex);
        }

        return completed;
    }

    /// <summary>Runs one step's input; marks the step sent when the tally shows the input went out, even if it then failed.</summary>
    public static async Task Sending(InputTally inputs, Action<object> sent, object item, Func<Task> input)
    {
        var before = inputs.Sent;
        try
        {
            await input();
        }
        finally
        {
            if (inputs.Sent > before)
            {
                sent(item);
            }
        }
    }

    /// <summary>The failure as JSON: the error, whether any input went out, and the steps completed before it.</summary>
    public static string Describe(string tool, Exception ex, int inputsSent)
    {
        var reply = new Dictionary<string, object>
        {
            ["error"] = $"{tool}: {ex.Message}",
            ["actionDispatched"] = inputsSent > 0,
            ["inputsSent"] = inputsSent,
        };
        if (ex is ProgressException progress)
        {
            reply[progress.Name] = progress.Completed;
        }

        return JsonSerializer.Serialize(reply, McpProtocol.JsonOptions);
    }
}
