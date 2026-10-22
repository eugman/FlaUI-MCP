using System.Text.Json;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp;

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

    /// <summary>MCP annotations for tools/list; null leaves a client to assume the tool may mutate.</summary>
    public virtual McpToolAnnotations? Annotations => null;

    /// <summary>Reads the app's state, so a successful call is evidence of what is on screen.</summary>
    protected virtual bool Observation => false;

    /// <summary>Usable while an app's UI Automation provider is blocked.</summary>
    protected virtual bool Recovery => false;

    /// <summary>Arguments that make an otherwise observing call change the desktop.</summary>
    protected virtual IReadOnlyList<string> MutatingWhenArgument => [];

    /// <summary>The tool's facts in one record; its definition and the README table are built from it.</summary>
    public ToolFacts Facts =>
        new(
            Name,
            Description,
            FactAnnotations(),
            InputSchema,
            InputFree: Annotations?.ReadOnlyHint == true,
            Observation,
            Recovery,
            MutatingWhenArgument
        );

    // The MCP annotations with the fact hints added; null (no hints) when the tool declares none.
    private McpToolAnnotations? FactAnnotations()
    {
        bool? Hint(bool value) => value ? true : null;
        if (Annotations is not { } annotations)
        {
            // No MCP hints declared, but the fact hints still reach the client and the broker.
            if (!Observation && !Recovery && MutatingWhenArgument.Count == 0)
            {
                return null;
            }

            annotations = new McpToolAnnotations();
        }

        return annotations with
        {
            ObservationHint = Hint(Observation),
            RecoveryHint = Hint(Recovery),
            MutatingWhenArgument = MutatingWhenArgument.Count > 0 ? MutatingWhenArgument : null,
        };
    }

    public McpTool GetDefinition() => Facts.Definition();

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

    /// <summary>
    /// A failure the tool found before any input, with a typed refusal (<paramref name="code"/>, such as
    /// invalid-arguments) so a client reads the code instead of the text; nothing was dispatched.
    /// </summary>
    protected static McpToolResult ErrorResult(string code, string message) =>
        ErrorResult(message) with
        {
            Outcome = new ToolOutcome("not-dispatched", "not-tracked")
            {
                Status = "failed",
                Failure = new InputRefusal(code, message) { ActionDispatched = false },
            },
        };

    /// <summary>
    /// The reply for a UI Automation focus or close that has not returned, or null when it completed (or ran
    /// natively). Its outcome says whether the request was sent yet.
    /// </summary>
    protected static McpToolResult? StillPending(PatternCallResult? result, string request)
    {
        if (result is null || result.Outcome == PatternCallOutcome.Completed)
        {
            return null;
        }

        var text = result.Dispatched
            ? $"{request} was sent; the app's UI Automation provider has not returned."
            : $"{request} is still waiting on the app's UI Automation provider and may not have been sent.";
        return TextResult(
            $"{text} Check windows_operation_status before continuing; do not repeat it.{result.WaitNote}"
        ) with
        {
            Outcome = ToolOutcome.FromPattern(result),
        };
    }

    protected static McpToolResult BlockedResult(PendingInvokeInfo pending) =>
        ErrorResult(PendingInvokeTracker.DescribeBlocked(pending)) with
        {
            Outcome = new ToolOutcome(
                "not-dispatched",
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
