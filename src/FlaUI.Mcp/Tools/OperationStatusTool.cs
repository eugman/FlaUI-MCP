using System.Text.Json;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tools;

public sealed class OperationStatusTool(OperationCoordinator operations, PendingInvokeTracker pending) : ToolBase
{
    /// <summary>Read-only and never blocked: it is how a client inspects the calls that are.</summary>
    public const string ToolName = "windows_operation_status";

    public override string Name => ToolName;

    protected override bool Recovery => true;

    public override McpToolAnnotations Annotations => McpToolAnnotations.Status;
    public override string Description =>
        "Read-only status for running, timed-out, and completed operations. status is ok or failed once finished (running or timed_out_pending before); "
        + "dispatch says how much input went out: completed, partial, not-dispatched, timed_out_pending (still running; never replay) or unknown, with inputsSent. "
        + "provider is pending while a click it started still blocks UI Automation on that app (usually an open menu or dialog). No UI Automation calls.";
    public override object InputSchema => ToolSchema.For<Arguments>();

    /// <summary>The tool's arguments; its schema is generated from them (published with an empty required list).</summary>
    [ToolSchemaRequiredList]
    internal sealed record Arguments(
        [property: ToolDoc("An operationId from a tool's structuredContent; omit it for every retained operation.")]
            string? OperationId = null
    );

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments) =>
        Task.FromResult(
            TextResult(
                JsonSerializer.Serialize(
                    new
                    {
                        operations = operations.Status(GetStringArgument(arguments, "operationId"), pending),
                        pendingPatterns = pending.Status(),
                    }
                )
            )
        );
}
