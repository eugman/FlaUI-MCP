using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;

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

    /// <summary>
    /// Runs one step's input; marks the step sent when Core counted input for it, even if it then failed, or when
    /// its failing call is still pending and may yet reach TE3.
    /// </summary>
    public static async Task Sending(InputTally inputs, Action<object> sent, object item, Func<Task> input)
    {
        var before = inputs.Sent;
        try
        {
            await input();
        }
        catch (Exception ex) when (CoreFailure.MayStillGoOut(CoreFailure.Outcome(ex)))
        {
            sent(item);
            throw;
        }
        finally
        {
            if (inputs.Sent > before)
            {
                sent(item);
            }
        }
    }

    /// <summary>Runs one call with its own tally and turns its failure into the reply.</summary>
    public static async Task<McpToolResult> Respond(string tool, Func<InputTally, Task<McpToolResult>> run)
    {
        var inputs = new InputTally();
        try
        {
            return await run(inputs);
        }
        // A stopped call still says what it sent before it stopped.
        catch (Exception ex)
            when (ex is ArgumentException or InvalidOperationException or TimeoutException or OperationCanceledException
            )
        {
            return Failure(tool, ex, inputs.Sent);
        }
    }

    /// <summary>The failure: its text, and how much input went out as Core's outcome (structuredContent).</summary>
    public static McpToolResult Failure(string tool, Exception ex, int inputsSent) =>
        new()
        {
            Content = [new() { Type = "text", Text = Describe(tool, ex) }],
            IsError = true,
            Outcome = CoreFailure.Reply(ex, inputsSent),
        };

    /// <summary>The failure as JSON: the error and the steps completed before it.</summary>
    public static string Describe(string tool, Exception ex)
    {
        var reply = new Dictionary<string, object> { ["error"] = $"{tool}: {ex.Message}" };
        if (ex is ProgressException progress)
        {
            reply[progress.Name] = progress.Completed;
        }

        return JsonSerializer.Serialize(reply, McpProtocol.JsonOptions);
    }

    // Closing a pane can open a dialog on an unfamiliar build. Watch for ~1s and
    // stop there: answering it is the caller's decision.
    public static async Task RequireNoDialog(Window window, string closing)
    {
        var pid = window.Properties.ProcessId.ValueOrDefault;
        var main = window.Properties.NativeWindowHandle.ValueOrDefault;
        for (var waited = 0; waited <= 1000; waited += 200)
        {
            var dialog = Win32Desktop
                .GetTopLevelWindows(pid)
                .FirstOrDefault(w => w.Hwnd != main && w.Title.Length > 0 && !w.IsToolWindow);
            if (dialog != null)
            {
                throw new InvalidOperationException(DialogOpened(closing, dialog.Title));
            }

            await Task.Delay(200);
        }
    }

    /// <summary>te3_layout reopen-pane: the View menu entry named after the pane (see <see cref="PaneReopen"/>).</summary>
    public static Task<object> Reopen(
        AutomationHost host,
        string handle,
        Window window,
        InputTally inputs,
        string title
    ) => PaneReopen.Show(new LayoutViewMenu(host, handle, window, inputs), title);

    /// <summary>What te3_layout_read show-pane says about a hidden pane.</summary>
    public static string HiddenMessage(string title) =>
        PaneReopen.IsMapped(title)
            ? "Pane is hidden; reopen it with te3_layout reopen-pane. No input sent."
            : PaneReopen.UnverifiedLocator;

    public static string DialogOpened(string closing, string dialog) =>
        $"Closing '{closing}' opened \"{dialog}\". Answer it, then run keep/close again.";
}
