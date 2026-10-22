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

    private readonly List<UnverifiedStep> unverified = [];

    /// <summary>The step the run is on, recorded with each unverified call; null outside a run.</summary>
    public string? Step { get; set; }

    /// <summary>
    /// What went out without a proof of its effect, in order: Core calls whose status was unverified (sent, not
    /// proven), and the page's steps that have no read-back. They do not abort the run; the run's manifest lists them.
    /// </summary>
    public IReadOnlyList<UnverifiedStep> Unverified => unverified;

    /// <summary>Notes a step that went out with no read-back of its effect.</summary>
    public void RecordUnverified(string tool, string? step, string? target) => unverified.Add(new(tool, step, target));

    /// <summary>
    /// Runs one Core tool call unless the operation was stopped, and counts it. An error or pending result throws a
    /// <see cref="CoreCallException"/> that carries Core's outcome: completed but pending, partial or not-dispatched.
    /// </summary>
    /// <param name="target">The ref or handle the call acted on, for the unverified list.</param>
    public async Task Invoke(Func<Task<McpToolResult>> call, string? tool = null, string? target = null)
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
            RecordUnverified(tool ?? "(unnamed call)", Step, target);
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

/// <summary>A step that went out without a read-back of its effect: the tool, the run's step, and its ref or handle.</summary>
public sealed record UnverifiedStep(string Tool, string? Step, string? Target);
