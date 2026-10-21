using FlaUI.Mcp.Core;

/// <summary>
/// The user's consent for the TE3 tools that take the desktop: an in-memory grant for one described batch.
/// The user's own keyboard or mouse input revokes it, so they take the machine back by using it. Never serialized.
/// </summary>
public sealed class Te3Interaction : IDisposable
{
    private readonly object sync = new();
    private readonly Func<long>? physicalInput;
    private readonly Func<IInputObserver>? createObserver;
    private IInputObserver? observer;
    private long inputAtApproval;
    private string? batch;
    private string reason = "the user has not approved a batch.";

    /// <param name="physicalInput">Counts non-injected input; null hooks the real keyboard and mouse once a batch begins.</param>
    public Te3Interaction(Func<long>? physicalInput = null)
    {
        this.physicalInput = physicalInput;
        createObserver = physicalInput == null ? () => new PhysicalInputObserver() : null;
    }

    /// <param name="createObserver">Starts an observer once a batch begins.</param>
    internal Te3Interaction(Func<IInputObserver> createObserver) => this.createObserver = createObserver;

    public bool Active
    {
        get
        {
            lock (sync)
            {
                return batch != null;
            }
        }
    }

    public void Begin(string description, bool userApproved)
    {
        if (!userApproved || string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "Ask the user to clear the machine for a described batch, then begin it with userApproved."
            );
        }

        lock (sync)
        {
            if (batch != null)
            {
                throw new InvalidOperationException("End the current batch before beginning another.");
            }

            if (createObserver != null)
            {
                observer = Live(observer) ?? createObserver();
            }

            inputAtApproval = InputSequence();
            batch = description;
            reason = "approved.";
        }
    }

    public void End(string why = "the batch ended.")
    {
        lock (sync)
        {
            batch = null;
            reason = why;
        }
    }

    /// <summary>Throws unless a batch is approved and the user has not touched the keyboard or mouse since.</summary>
    public void Require()
    {
        lock (sync)
        {
            if (batch != null && UserInputSinceApproval())
            {
                batch = null;
                reason = "keyboard or mouse input from the user was observed; ask them before resuming.";
            }

            if (batch == null)
            {
                throw new InvalidOperationException(
                    $"Desktop input needs the user's approval: {reason} Ask the user, then call {Te3InteractionTool.Begin}."
                );
            }
        }
    }

    /// <summary>
    /// Counts the user's input from now on, keeping the batch: for the run, after the user clicked TE3 because Windows
    /// refused to focus it, as the run asked. A grant already revoked stays revoked.
    /// </summary>
    internal void Rebaseline()
    {
        lock (sync)
        {
            if (batch == null)
            {
                throw new InvalidOperationException($"No batch to continue: {reason}");
            }

            inputAtApproval = InputSequence();
        }
    }

    /// <summary>Requires the grant now and before every input the core tools send, until disposed.</summary>
    public IDisposable Guard()
    {
        Require();
        var previous = MutationGuard.BeforeMutation.Value;
        MutationGuard.BeforeMutation.Value = Require;
        return new Restore(previous);
    }

    private sealed class Restore(Action? previous) : IDisposable
    {
        public void Dispose() => MutationGuard.BeforeMutation.Value = previous;
    }

    private bool UserInputSinceApproval()
    {
        try
        {
            return InputSequence() != inputAtApproval;
        }
        catch (InvalidOperationException)
        {
            // An observer that stopped cannot vouch that the user is away.
            return true;
        }
    }

    // An observer that can no longer vouch for the user's input is replaced, never reused.
    private static IInputObserver? Live(IInputObserver? current)
    {
        if (current == null)
        {
            return null;
        }

        try
        {
            _ = current.Sequence;
            return current;
        }
        catch (InvalidOperationException)
        {
            current.Dispose();
            return null;
        }
    }

    private long InputSequence() => physicalInput?.Invoke() ?? observer?.Sequence ?? 0;

    public void Dispose()
    {
        End("the host stopped.");
        observer?.Dispose();
    }
}
