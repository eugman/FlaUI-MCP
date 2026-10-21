using System.Collections.Concurrent;

namespace FlaUI.Mcp.Core;

public sealed record ProcessIdentity(int ProcessId, long StartedTicks);

public sealed class OperationContext
{
    public static readonly AsyncLocal<OperationContext?> Current = new();
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public int ProcessId { get; set; }
    public long ProcessStartedTicks { get; set; }
    public string Tool { get; init; } = "";

    /// <summary>The tool's MCP annotations, echoed by operation status.</summary>
    public McpToolAnnotations? Annotations { get; init; }
    public string? RequestKey { get; init; }
    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    public CancellationTokenSource Stop { get; } = new();
    public volatile bool Finished;
    public string? Error { get; set; }
    public ToolOutcome? ResultOutcome { get; set; }

    /// <summary>The last refusal guarded input raised in this operation.</summary>
    public InputRefusal? Refusal { get; set; }
    private int inputsSent;

    /// <summary>Inputs and mutations this operation has sent so far, nested steps included.</summary>
    public int InputsSent => Volatile.Read(ref inputsSent);

    public void RecordInput() => Interlocked.Increment(ref inputsSent);

    public static void Check() => Current.Value?.Stop.Token.ThrowIfCancellationRequested();
}

public sealed class OperationCoordinator
{
    private readonly object gate = new();
    private readonly ConcurrentDictionary<string, OperationContext> operations = new();

    public OperationContext Begin(string tool, string? requestKey = null, McpToolAnnotations? annotations = null)
    {
        lock (gate)
        {
            foreach (
                var old in operations
                    .Values.Where(o => o.Finished)
                    .OrderByDescending(o => o.StartedUtc)
                    .Skip(100)
                    .ToArray()
            )
            {
                operations.TryRemove(old.Id, out _);
            }

            var op = new OperationContext
            {
                Tool = tool,
                RequestKey = requestKey,
                Annotations = annotations,
            };
            operations[op.Id] = op;
            return op;
        }
    }

    /// <summary>Whether a request with this key ran, or still runs, as an operation that is still retained.</summary>
    public bool Ran(string requestKey) => operations.Values.Any(o => o.RequestKey == requestKey);

    public object Status(string? id = null, PendingInvokeTracker? pending = null, string? requestKey = null) =>
        operations
            .Values.Where(o => (id == null || o.Id == id) && (requestKey == null || o.RequestKey == requestKey))
            .OrderByDescending(o => o.StartedUtc)
            .Select(o =>
            {
                var dispatch = o.ResultOutcome?.Dispatch ?? DispatchSoFar(o);
                var patterns = pending?.ForOperation(o.Id) ?? [];
                // A returned dispatch whose pattern call still holds the provider is not done for the caller.
                return new
                {
                    operationId = o.Id,
                    requestKey = o.RequestKey,
                    finished = o.Finished,
                    o.ProcessId,
                    o.ProcessStartedTicks,
                    o.Tool,
                    annotations = o.Annotations,
                    o.StartedUtc,
                    status = StatusOf(o),
                    dispatch,
                    inputsSent = o.InputsSent,
                    provider = pending == null ? "not-tracked"
                    : patterns.Length > 0 ? "pending"
                    : "idle",
                    pendingPatternIds = patterns,
                    o.Error,
                    resultOutcome = o.ResultOutcome,
                };
            })
            .ToArray();

    /// <summary>ok or failed once finished: the tool's own verdict, not whether input went out.</summary>
    private static string StatusOf(OperationContext o)
    {
        if (o.Finished)
        {
            return o.Error == null ? "ok" : "failed";
        }

        return o.Stop.IsCancellationRequested ? "timed_out_pending" : "running";
    }

    /// <summary>An operation with no result yet: partial once any input went out, otherwise unknown.</summary>
    private static string DispatchSoFar(OperationContext o) => o.InputsSent > 0 ? "partial" : "unknown";
}
