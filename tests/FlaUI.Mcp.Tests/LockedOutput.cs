namespace FlaUI.Mcp.Tests;

/// <summary>
/// A transport's output that a test can read while requests are still writing to it. A plain StringWriter is not
/// safe to read during a write, and polling it from another thread threw out of StringBuilder.ToString.
/// </summary>
internal sealed class LockedOutput : StringWriter
{
    private readonly object gate = new();

    public override void Write(char value)
    {
        lock (gate)
        {
            base.Write(value);
        }
    }

    public override void Write(string? value)
    {
        lock (gate)
        {
            base.Write(value);
        }
    }

    public override void WriteLine(string? value)
    {
        lock (gate)
        {
            base.WriteLine(value);
        }
    }

    public override Task WriteLineAsync(string? value)
    {
        WriteLine(value);
        return Task.CompletedTask;
    }

    public override string ToString()
    {
        lock (gate)
        {
            return base.ToString();
        }
    }
}
