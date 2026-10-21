using FlaUI.Mcp;

/// <summary>
/// The inputs one TE3 tool call or run has sent, each counted once it went out: a Core tool call that completed or is
/// pending, a failed one whose text does not prove a refusal before input, and a direct send whose checks passed. A refusal
/// before input (activation denied, no desktop, no grant, a stale ref, a covered point, a failed intent write)
/// counts nothing. TODO(core): switch to Core's inputsSent after the rebase.
/// </summary>
public sealed class InputTally
{
    public int Sent { get; private set; }

    /// <summary>Runs one Core tool call unless the operation was stopped, and counts it; throws on an error or pending result.</summary>
    public async Task Invoke(Func<Task<McpToolResult>> call, bool readOnly = false, string? tool = null)
    {
        FlaUI.Mcp.Core.OperationContext.Check();
        McpToolResult result;
        try
        {
            result = await call();
        }
        catch (Exception error)
        {
            Count(readOnly, error.Message, tool);
            throw;
        }

        var text = string.Join("; ", result.Content.Select(c => c.Text));
        Count(readOnly, result.IsError == true ? text : null, tool);
        if (result.IsError == true || result.Outcome?.IsPending == true)
        {
            throw new InvalidOperationException(text);
        }
    }

    /// <summary>
    /// Dispatches one direct input, counted first: call it once every check before the input has passed (inside
    /// GuardedInput.Send), so a click that throws after the button went down still counts.
    /// </summary>
    public void Dispatch(Action send)
    {
        Sent++;
        send();
    }

    // failure: null when the call returned normally; otherwise its text, which may prove nothing was sent.
    private void Count(bool readOnly, string? failure, string? tool)
    {
        if (!readOnly && (failure == null || !InputProof.NotSent(failure, tool)))
        {
            Sent++;
        }
    }
}
