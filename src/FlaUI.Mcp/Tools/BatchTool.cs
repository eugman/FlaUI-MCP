using System.Text.Json;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

public sealed class BatchTool : ToolBase
{
    // Steps dispatch through the same registry, so each keeps its tool's guards.
    private readonly ToolRegistry registry;

    internal BatchTool(ToolRegistry registry) => this.registry = registry;

    public override string Name => "windows_batch";

    public override McpToolAnnotations Annotations => McpToolAnnotations.Input(destructive: true);
    public override string Description =>
        "Execute click/type/fill/wait/snapshot actions in order, returning numbered results and structured step outcomes. Stops on errors by default; always stops when an action is still pending. Never replay a pending action.";
    public override object InputSchema => ToolSchema.For<Arguments>();

    /// <summary>The tool's arguments; its schema is generated from them.</summary>
    internal sealed record Arguments
    {
        [ToolSchemaFrom(nameof(ActionsSchema))]
        public required object Actions { get; init; }

        [ToolDoc("Stop executing if an action fails (default: true)")]
        public bool? StopOnError { get; init; }
    }

    public static object ActionsSchema =>
        new
        {
            type = "array",
            items = new
            {
                type = "object",
                properties = new
                {
                    action = new { type = "string", @enum = new[] { "click", "type", "fill", "wait", "snapshot" } },
                    @ref = new { type = "string" },
                    text = new { type = "string" },
                    value = new { type = "string" },
                    ms = new
                    {
                        type = "integer",
                        minimum = 0,
                        description = "Milliseconds to wait (default: 100)",
                    },
                    handle = new { type = "string", description = "Window handle for snapshot or input" },
                },
                required = new[] { "action" },
            },
            description = "The steps to run in order, at most 100: each an action and its arguments.",
        };

    public override async Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        if (arguments is not { } args || !args.TryGetProperty("actions", out var actions))
        {
            return ErrorResult(RefusalCodes.InvalidArguments, "Missing actions");
        }

        var rows = actions.EnumerateArray().ToArray();
        if (rows.Length > 100)
        {
            return ErrorResult("At most 100 steps per batch");
        }

        return await ExecuteRows(
            rows,
            (tool, row) => registry.ExecuteToolAsync(tool, row),
            !args.TryGetProperty("stopOnError", out var stop) || stop.GetBoolean()
        );
    }

    internal static async Task<McpToolResult> ExecuteRows(
        JsonElement[] rows,
        Func<string, JsonElement, Task<McpToolResult>> invoke,
        bool stopOnError = true
    )
    {
        var batch = new BatchRun(invoke, stopOnError, rows.Length);
        foreach (var (row, index) in rows.Select((row, index) => (row, index)))
        {
            OperationContext.Check();
            if (!await batch.Run(row, index + 1))
            {
                break;
            }
        }

        return batch.Result();
    }

    /// <summary>The trace and step outcomes of one batch, filled in step by step.</summary>
    private sealed class BatchRun(Func<string, JsonElement, Task<McpToolResult>> invoke, bool stopOnError, int rowCount)
    {
        private readonly List<string> trace = [];
        private readonly List<ToolStepOutcome> steps = [];
        private ToolOutcome? pending;
        private int pendingNumber;

        /// <summary>Run one step; false when the batch must stop after it.</summary>
        public async Task<bool> Run(JsonElement row, int number)
        {
            try
            {
                return await RunStep(row, number);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                trace.Add($"{number}. ERROR: {ex.Message}");
                steps.Add(new(number, null, true, new("not-dispatched", "unknown")));
                if (!stopOnError)
                {
                    return true;
                }

                trace.Add($"Stopped at action {number} due to error");
                return false;
            }
        }

        private async Task<bool> RunStep(JsonElement row, int number)
        {
            var op = row.GetProperty("action").GetString();
            if (op == "wait")
            {
                await Wait(row, number);
                return true;
            }

            if (op == "snapshot" && row.TryGetProperty("ref", out _))
            {
                throw new ArgumentException(
                    "Snapshot does not support ref; use handle or omit both for the focused window"
                );
            }

            var tool = op switch
            {
                "click" => "windows_click",
                "fill" => "windows_fill",
                "type" => "windows_type",
                "snapshot" => "windows_snapshot",
                _ => throw new ArgumentException($"Unknown action: {op}"),
            };
            var result = await invoke(tool, ToolArguments(row));
            trace.Add($"{number}. {op}: {string.Join("\n", result.Content.Select(c => c.Text ?? ""))}");
            steps.Add(new(number, op, result.IsError == true, result.Outcome));
            if (result.Outcome?.IsPending == true)
            {
                pending = result.Outcome;
                pendingNumber = number;
                trace.Add(
                    $"Stopped at action {number}: operation still pending; inspect state before continuing, do not replay."
                );
                return false;
            }

            if (result.IsError == true && stopOnError)
            {
                trace.Add($"Stopped at action {number} due to error");
                return false;
            }

            return true;
        }

        /// <summary>
        /// The row without its batch-only keys. "action" names the batch step; passed on, it would reach
        /// windows_click as its own action argument and force a mouse click on every click row.
        /// </summary>
        private static JsonElement ToolArguments(JsonElement row)
        {
            var arguments = System.Text.Json.Nodes.JsonNode.Parse(row.GetRawText())!.AsObject();
            arguments.Remove("action");
            return JsonSerializer.SerializeToElement(arguments);
        }

        private async Task Wait(JsonElement row, int number)
        {
            var ms = row.TryGetProperty("ms", out var delay) ? delay.GetInt32() : 100;
            if (ms < 0)
            {
                throw new ArgumentException("Wait must be nonnegative");
            }

            await Task.Delay(
                TimeSpan.FromMilliseconds(ms),
                InputDesktop.Current.Time,
                OperationContext.Current.Value?.Stop.Token ?? CancellationToken.None
            );
            trace.Add($"{number}. wait: Waited {ms}ms");
            steps.Add(new(number, "wait", false, new("completed", "returned")));
        }

        /// <summary>
        /// A batch stopped at a pending step after earlier steps sent input is partial, with the pending marker
        /// kept. Reporting the pending step's own "not-dispatched" would invite a replay of input already sent.
        /// </summary>
        private static ToolOutcome PendingOutcome(ToolOutcome pending, int sent, bool stoppedEarly)
        {
            if (sent == 0 || pending.Dispatch == "completed" && !stoppedEarly)
            {
                return pending;
            }

            return pending with
            {
                Dispatch = "partial",
                Provider = "pending",
            };
        }

        // Failed wins; a step that could not check its effect makes the batch unverified, never ok.
        private string? RolledUp(bool failed)
        {
            if (failed)
            {
                return "failed";
            }

            return steps.Any(step => step.Outcome?.Status == "unverified") ? "unverified" : null;
        }

        /// <summary>
        /// The numbered trace as text, and the structured outcome beside it. A step that fails after completed ones
        /// makes the batch partial: earlier input reached the app. IsError mirrors status: failed, so a client
        /// that reads only isError does not take a failed batch for a successful one.
        /// </summary>
        public McpToolResult Result()
        {
            var failed = steps.Any(s => s.IsError);
            var sent = steps.Sum(s => s.Outcome?.InputsSent ?? 0);
            var outcome = pending is null
                ? new(ToolOutcome.DispatchFor(failed, sent), "returned")
                : PendingOutcome(pending, sent, pendingNumber < rowCount);
            return TextResult(string.Join("\n", trace)) with
            {
                IsError = failed ? true : null,
                Outcome = outcome with { Steps = steps, InputsSent = sent, Status = RolledUp(failed) },
            };
        }
    }
}
