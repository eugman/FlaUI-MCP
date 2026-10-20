/// <summary>
/// The user's consent for the TE3 tools that take the desktop: an in-memory grant for one described batch.
/// </summary>
public sealed class Te3Interaction : IDisposable
{
    public Te3Interaction(Func<long>? physicalInput = null) => _ = physicalInput;

    public bool Active => throw new NotImplementedException();

    public void Begin(string description, bool userApproved) => throw new NotImplementedException();

    public void End(string why = "Batch ended") => throw new NotImplementedException();

    public void Require() => throw new NotImplementedException();

    public IDisposable Guard() => throw new NotImplementedException();

    public void Dispose()
    {
        // Nothing to release yet.
    }
}
