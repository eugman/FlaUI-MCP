using System.Diagnostics;
using System.Text.Json;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp;

/// <summary>
/// Registry for MCP tools - maps tool names to handlers
/// </summary>
public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();
    private readonly TimeSpan _toolTimeout;
    private readonly Action? _onToolActivity;
    public OperationCoordinator Operations { get; } = new();
    public Func<JsonElement?, ProcessIdentity?>? ResolveTarget { get; set; }
    public PendingInvokeTracker? Pending { get; set; }

    public ToolRegistry(TimeSpan? toolTimeout = null, Action? onToolActivity = null)
    {
        _toolTimeout = toolTimeout ?? TimeSpan.FromSeconds(30);
        if (_toolTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(toolTimeout), "Tool timeout must be greater than zero.");
        }

        _onToolActivity = onToolActivity;
    }

    public void RegisterTool(ITool tool)
    {
        _tools[tool.Name] = tool;
    }

    public List<McpTool> GetToolDefinitions()
    {
        return _tools.Values.Select(t => t.GetDefinition()).ToList();
    }

    /// <param name="whenStranded">
    /// Called when the call returns before its tool task ended because the caller cancelled. It receives a
    /// task that completes when the tool returns or its timeout would have expired; a serialized caller
    /// holds its slot until then, so a cancel never lets the next tool run beside a still-running one.
    /// </param>
    public async Task<McpToolResult> ExecuteToolAsync(
        string name,
        JsonElement? arguments,
        CancellationToken cancellationToken = default,
        string? requestKey = null,
        Action<Task>? whenStranded = null
    )
    {
        if (!_tools.TryGetValue(name, out var tool))
        {
            return new McpToolResult
            {
                Content = new List<McpContent>
                {
                    new() { Type = "text", Text = $"Unknown tool: {name}" },
                },
                IsError = true,
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            _onToolActivity?.Invoke();
            if (OperationContext.Current.Value != null)
            {
                OperationContext.Check();
                return await tool.ExecuteAsync(arguments);
            }

            var operation = Operations.Begin(name, requestKey);
            using var cancellation = cancellationToken.Register(() => operation.Stop.Cancel());
            var toolTask = Task.Run(async () =>
            {
                OperationContext.Current.Value = operation;
                try
                {
                    // Metadata collection is inside the timeout. Target validation
                    // belongs to the tool, preserving its specific error contract.
                    var target = ResolveTarget?.Invoke(arguments);
                    operation.ProcessId = target?.ProcessId ?? 0;
                    operation.ProcessStartedTicks = target?.StartedTicks ?? 0;
                    OperationContext.Check();
                    var result = await tool.ExecuteAsync(arguments);
                    operation.ResultOutcome = result.Outcome;
                    if (result.IsError == true || result.Outcome?.Dispatch == "failed")
                    {
                        var error = string.Join("; ", result.Content.Select(c => c.Text));
                        operation.Error = string.IsNullOrWhiteSpace(error) ? "Tool reported a failed dispatch." : error;
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    operation.Error = ex.Message;
                    throw;
                }
                finally
                {
                    operation.Finished = true;
                    OperationContext.Current.Value = null;
                }
            });
            var started = Stopwatch.GetTimestamp();
            using var timer = new CancellationTokenSource();
            var timeoutTask = Task.Delay(_toolTimeout, timer.Token);
            var cancelledTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var finished = await Task.WhenAny(toolTask, timeoutTask, cancelledTask);
            // Stop the timer once the tool returns, so a fast call doesn't leave it running for the full timeout.
            await timer.CancelAsync();
            if (finished != toolTask)
            {
                var settled =
                    finished == cancelledTask ? SettledOrTimedOut(toolTask, Stopwatch.GetElapsedTime(started)) : null;
                return Strand(name, operation, toolTask, settled, whenStranded);
            }

            return CompleteResult(await toolTask, operation);
        }
        catch (Exception ex)
        {
            return new McpToolResult
            {
                Content = new List<McpContent>
                {
                    new() { Type = "text", Text = $"Error: {ex.Message}" },
                },
                IsError = true,
            };
        }
    }

    /// <summary>
    /// Result for a call that returned before its tool task ended: the caller cancelled (<paramref name="settled"/>
    /// is set) or the tool timed out. The tool may still be running and may have partially executed.
    /// </summary>
    private McpToolResult Strand(
        string name,
        OperationContext operation,
        Task<McpToolResult> toolTask,
        Task? settled,
        Action<Task>? whenStranded
    )
    {
        operation.Stop.Cancel();
        if (settled != null)
        {
            whenStranded?.Invoke(settled);
        }

        // A stranded call can still hold this app's UIA provider. Fail later ref tools fast until it returns.
        if (Pending != null && operation.ProcessId != 0)
        {
            var stranded = Pending.Begin(
                operation.ProcessId,
                settled != null ? $"cancelled {name}" : $"timed-out {name}"
            );
            _ = toolTask.ContinueWith(_ => Pending.Complete(stranded), TaskScheduler.Default);
        }

        _ = toolTask.ContinueWith(
            task =>
            {
                _ = task.Exception;
            },
            TaskContinuationOptions.OnlyOnFaulted
        );

        return new McpToolResult
        {
            Content = new List<McpContent>
            {
                new() { Type = "text", Text = PendingMessage(name, settled != null) },
            },
            Outcome = new ToolOutcome("timed_out_pending", "unknown", OperationId: operation.Id),
            IsError = true,
        };
    }

    /// <summary>Completes when the tool returns, or when its timeout would have expired had it not been cancelled.</summary>
    private Task SettledOrTimedOut(Task toolTask, TimeSpan elapsed)
    {
        var remaining = _toolTimeout - elapsed;
        return Task.WhenAny(toolTask, Task.Delay(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero));
    }

    private string PendingMessage(string name, bool cancelled) =>
        (
            cancelled
                ? $"Tool '{name}' cancellation requested. "
                : $"Tool '{name}' timed out after {(int)_toolTimeout.TotalMilliseconds}ms. "
        )
        + "A modal dialog or blocked UI Automation provider may still be running in the background. "
        + "Inspect the target and operation status before continuing. The request may have partially executed; do not blindly replay it.";

    private static McpToolResult CompleteResult(McpToolResult result, OperationContext operation)
    {
        var outcome = result.Outcome;
        if (outcome == null)
        {
            outcome = new ToolOutcome(result.IsError == true ? "failed" : "completed", "not-tracked")
            {
                Verification = "not-established",
            };
        }

        return result with
        {
            Outcome = outcome with { OperationId = outcome.OperationId ?? operation.Id },
        };
    }
}

/// <summary>
/// Interface for MCP tools
/// </summary>
public interface ITool
{
    string Name { get; }
    McpTool GetDefinition();
    Task<McpToolResult> ExecuteAsync(JsonElement? arguments);
}

/// <summary>
/// Base class for tools with common utilities
/// </summary>
public abstract class ToolBase : ITool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract object InputSchema { get; }

    public McpTool GetDefinition() =>
        new()
        {
            Name = Name,
            Description = Description,
            InputSchema = InputSchema,
        };

    public abstract Task<McpToolResult> ExecuteAsync(JsonElement? arguments);

    protected static McpToolResult TextResult(string text) =>
        new()
        {
            Content = new List<McpContent>
            {
                new() { Type = "text", Text = text },
            },
        };

    protected static McpToolResult ErrorResult(string message) =>
        new()
        {
            Content = new List<McpContent>
            {
                new() { Type = "text", Text = message },
            },
            IsError = true,
        };

    protected static McpToolResult BlockedResult(PendingInvokeInfo pending) =>
        ErrorResult(PendingInvokeTracker.DescribeBlocked(pending)) with
        {
            Outcome = new ToolOutcome(
                "failed",
                "pending",
                pending.OperationId,
                pending.ModalTitle,
                pending.ParentOperationId
            ),
        };

    protected static McpToolResult ImageResult(byte[] imageData, string mimeType = "image/png") =>
        new()
        {
            Content = new List<McpContent>
            {
                new()
                {
                    Type = "image",
                    Data = Convert.ToBase64String(imageData),
                    MimeType = mimeType,
                },
            },
        };

    protected T? GetArgument<T>(JsonElement? arguments, string name)
    {
        if (arguments == null)
        {
            return default;
        }

        if (!arguments.Value.TryGetProperty(name, out var prop))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(prop.GetRawText(), McpProtocol.JsonOptions);
    }

    protected string? GetStringArgument(JsonElement? arguments, string name)
    {
        if (arguments == null)
        {
            return null;
        }

        if (!arguments.Value.TryGetProperty(name, out var prop))
        {
            return null;
        }

        return prop.GetString();
    }

    protected bool GetBoolArgument(JsonElement? arguments, string name, bool defaultValue = false)
    {
        if (arguments == null)
        {
            return defaultValue;
        }

        if (!arguments.Value.TryGetProperty(name, out var prop))
        {
            return defaultValue;
        }

        return prop.GetBoolean();
    }
}
