using FlaUI.Mcp;
using FlaUI.Mcp.Core;

/// <summary>
/// The inputs one TE3 tool call or run has sent, as Core counted them: each Core tool call adds its outcome's
/// inputsSent, and a direct send adds what Core recorded while it ran (activation, focus and each GuardedInput send).
/// Nothing is inferred from a tool's text.
/// </summary>
public sealed class InputTally
{
    public int Sent { get; private set; }

    private readonly List<string> unverified = [];

    /// <summary>
    /// The Core calls that went out without a proof of their effect (status unverified): sent, not proven. They do
    /// not abort the run; the caller reads its own state back, or reports the step as unverified.
    /// </summary>
    public IReadOnlyList<string> Unverified => unverified;

    /// <summary>
    /// Runs one Core tool call unless the operation was stopped, and counts it. An error or pending result throws a
    /// <see cref="CoreCallException"/> that carries Core's outcome: completed but pending, partial or not-dispatched.
    /// </summary>
    public async Task Invoke(Func<Task<McpToolResult>> call, string? tool = null)
    {
        OperationContext.Check();
        // Inside a running operation (a te3_* tool call), Core counts the step against it; otherwise the call is
        // its own operation and reports its count in the outcome.
        var parent = OperationContext.Current.Value;
        var before = parent?.InputsSent ?? 0;
        McpToolResult? result = null;
        try
        {
            result = await call();
        }
        finally
        {
            Sent += parent != null ? parent.InputsSent - before : result?.Outcome?.InputsSent ?? 0;
        }

        if (result.IsError == true || result.Outcome?.Status == "failed" || result.Outcome?.IsPending == true)
        {
            throw new CoreCallException(tool, result);
        }

        if (result.Outcome?.Status == "unverified")
        {
            unverified.Add(tool ?? "(unnamed call)");
        }
    }

    /// <summary>
    /// Runs input the host sends itself through GuardedInput, and counts what Core recorded while it ran, also when
    /// it then throws. Outside a running operation it runs in one of its own, so Core has somewhere to count.
    /// </summary>
    public void Dispatch(Action send)
    {
        var parent = OperationContext.Current.Value;
        var counter = parent ?? new OperationContext { Tool = "te3_direct_input" };
        var before = counter.InputsSent;
        OperationContext.Current.Value = counter;
        try
        {
            send();
        }
        finally
        {
            OperationContext.Current.Value = parent;
            Sent += counter.InputsSent - before;
        }
    }
}
