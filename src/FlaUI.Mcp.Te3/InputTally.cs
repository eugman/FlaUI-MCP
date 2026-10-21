using FlaUI.Mcp;

/// <summary>
/// The inputs one TE3 tool call or run has sent. TODO(core): switch to Core's inputsSent after the rebase.
/// </summary>
public sealed class InputTally
{
    public int Sent { get; private set; }

    /// <summary>Runs one Core tool call and counts it; throws on an error or pending result.</summary>
    public async Task Invoke(Func<Task<McpToolResult>> call, bool readOnly = false)
    {
        if (!readOnly)
        {
            Sent++;
        }

        var result = await call();
        if (result.IsError == true || result.Outcome?.IsPending == true)
        {
            throw new InvalidOperationException(string.Join("; ", result.Content.Select(c => c.Text)));
        }
    }

    /// <summary>Sends one direct input and counts it.</summary>
    public void Send(Action send)
    {
        Sent++;
        send();
    }
}
