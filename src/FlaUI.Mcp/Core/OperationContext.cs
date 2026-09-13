using System.Collections.Concurrent;

namespace PlaywrightWindows.Mcp.Core;

public sealed class OperationContext
{
    public static readonly AsyncLocal<OperationContext?> Current = new();
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public int ProcessId { get; init; }
    public long ProcessStartedTicks { get; init; }
    public string Tool { get; init; } = "";
    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    public CancellationTokenSource Stop { get; } = new();
    public volatile bool Finished;
    public string? Error { get; set; }
    public string? Action { get; set; }
    public string? Step { get; set; }
    public string? Target { get; set; }
    public static void Check() => Current.Value?.Stop.Token.ThrowIfCancellationRequested();
}

public sealed class OperationCoordinator
{
    private readonly object gate = new();
    private readonly ConcurrentDictionary<string, OperationContext> operations = new();
    public OperationContext Begin(int pid, string tool, long startedTicks = 0)
    {
        lock (gate)
        {
            foreach (var old in operations.Values.Where(o => o.Finished).OrderByDescending(o => o.StartedUtc).Skip(100).ToArray())
                operations.TryRemove(old.Id, out _);
            var op = new OperationContext { ProcessId = pid, ProcessStartedTicks = startedTicks, Tool = tool };
            operations[op.Id] = op;
            return op;
        }
    }
    public object Status(string? id = null, PendingInvokeTracker? pending = null) => operations.Values.Where(o => id == null || o.Id == id)
        .OrderByDescending(o => o.StartedUtc).Select(o =>
        {
            var dispatch = o.Finished ? (o.Error == null ? "completed" : "failed") : (o.Stop.IsCancellationRequested ? "timed_out_pending" : "running");
            var patterns = pending?.ForOperation(o.Id) ?? [];
            return new { operationId = o.Id, o.ProcessId, o.ProcessStartedTicks, o.Tool, o.StartedUtc,
                status = dispatch, dispatch, provider = pending == null ? "not-tracked" : patterns.Length > 0 ? "pending" : "idle",
                pendingPatternIds = patterns, o.Error };
        }).ToArray();
}
