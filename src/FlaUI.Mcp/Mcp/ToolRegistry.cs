using System.Text.Json;
using PlaywrightWindows.Mcp.Core;

namespace PlaywrightWindows.Mcp;

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

    public async Task<McpToolResult> ExecuteToolAsync(string name, JsonElement? arguments)
    {
        if (!_tools.TryGetValue(name, out var tool))
        {
            return new McpToolResult
            {
                Content = new List<McpContent>
                {
                    new() { Type = "text", Text = $"Unknown tool: {name}" }
                },
                IsError = true
            };
        }

        try
        {
            _onToolActivity?.Invoke();
            // Batch actions resolve independently: upstream batches may span windows/processes.
            var target = arguments is { ValueKind: JsonValueKind.Object } a && a.TryGetProperty("actions", out _)
                ? null : ResolveTarget?.Invoke(arguments);
            if (OperationContext.Current.Value != null)
            {
                OperationContext.Check();
                var current = OperationContext.Current.Value;
                if (target != null && current.ProcessId != 0 && (target.ProcessId != current.ProcessId || target.StartedTicks != current.ProcessStartedTicks))
                    throw new ArgumentException("Nested operation target identity mismatch.");
                return await tool.ExecuteAsync(arguments);
            }
            var operation = Operations.Begin(target?.ProcessId ?? 0, name, target?.StartedTicks ?? 0);
            var toolTask = Task.Run(async () =>
            {
                OperationContext.Current.Value = operation;
                try
                {
                    var result = await tool.ExecuteAsync(arguments);
                    if (result.IsError == true) operation.Error = string.Join("; ", result.Content.Select(c => c.Text));
                    return result;
                }
                catch (Exception ex) { operation.Error = ex.Message; throw; }
                finally { operation.Finished = true; OperationContext.Current.Value = null; }
            });
            var timeoutTask = Task.Delay(_toolTimeout);

            if (await Task.WhenAny(toolTask, timeoutTask) == timeoutTask)
            {
                operation.Stop.Cancel();
                _ = toolTask.ContinueWith(
                    task => { _ = task.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted);

                return new McpToolResult
                {
                    Content = new List<McpContent>
                    {
                        new()
                        {
                            Type = "text",
                            Text = $"Tool '{name}' timed out after {(int)_toolTimeout.TotalMilliseconds}ms. " +
                                "A modal dialog or blocked UI Automation provider may still be running in the background. " +
                                "Inspect the target and operation status before continuing. The request may have partially executed; do not blindly replay it."
                        }
                    },
                    Outcome = new ToolOutcome("timed_out_pending", "unknown", OperationId: operation.Id),
                    IsError = true
                };
            }

            return await toolTask;
        }
        catch (Exception ex)
        {
            return new McpToolResult
            {
                Content = new List<McpContent>
                {
                    new() { Type = "text", Text = $"Error: {ex.Message}" }
                },
                IsError = true
            };
        }
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

    public McpTool GetDefinition() => new()
    {
        Name = Name,
        Description = Description,
        InputSchema = InputSchema
    };

    public abstract Task<McpToolResult> ExecuteAsync(JsonElement? arguments);

    protected static McpToolResult TextResult(string text) => new()
    {
        Content = new List<McpContent>
        {
            new() { Type = "text", Text = text }
        }
    };

    protected static McpToolResult ErrorResult(string message) => new()
    {
        Content = new List<McpContent>
        {
            new() { Type = "text", Text = message }
        },
        IsError = true
    };

    protected static McpToolResult BlockedResult(PendingInvokeInfo pending) =>
        ErrorResult(PendingInvokeTracker.DescribeBlocked(pending)) with
        {
            Outcome = new ToolOutcome("failed", "pending", pending.OperationId,
                pending.ModalTitle, pending.ParentOperationId)
        };

    protected static McpToolResult ImageResult(byte[] imageData, string mimeType = "image/png") => new()
    {
        Content = new List<McpContent>
        {
            new() 
            { 
                Type = "image", 
                Data = Convert.ToBase64String(imageData),
                MimeType = mimeType
            }
        }
    };

    protected T? GetArgument<T>(JsonElement? arguments, string name)
    {
        if (arguments == null) return default;
        if (!arguments.Value.TryGetProperty(name, out var prop)) return default;
        return JsonSerializer.Deserialize<T>(prop.GetRawText(), McpProtocol.JsonOptions);
    }

    protected string? GetStringArgument(JsonElement? arguments, string name)
    {
        if (arguments == null) return null;
        if (!arguments.Value.TryGetProperty(name, out var prop)) return null;
        return prop.GetString();
    }

    protected bool GetBoolArgument(JsonElement? arguments, string name, bool defaultValue = false)
    {
        if (arguments == null) return defaultValue;
        if (!arguments.Value.TryGetProperty(name, out var prop)) return defaultValue;
        return prop.GetBoolean();
    }
}
