namespace FlaUI.Mcp.Core;

/// <summary>
/// The user's consent for one described batch of desktop input, held in memory and never serialized. The user's
/// own keyboard or mouse input revokes it, so they take the machine back by using it; so do a failed observation, a
/// foreground that stays unavailable past a short grace, and the host stopping. <see cref="Guard"/> installs it as
/// <see cref="MutationGuard.BeforeMutation"/>, so every input the core tools send requires it. Shared by every host.
/// </summary>
/// <remarks>
/// <see cref="Token"/> ends with the grant; <see cref="SafetyToken"/> ends only on a safety event (the user's
/// input, a failed observation, a lost foreground, the host stopping), not on a normal <see cref="End"/>, so a
/// recording can outlive its batch while the user's input still stops it at once.
/// </remarks>
public sealed partial class InteractionGrant : IDisposable
{
    public const string ApprovalRequired = "approval-required";
    public const string FocusRevoked = "focus-revoked";
    public const string FocusUnavailable = "focus-unavailable";
    public const string PausedAfterFailure = "paused-after-failure";

    /// <summary>How long the foreground may be missing (a window switch in progress) before the grant is revoked.</summary>
    public const long MissingForegroundGraceMilliseconds = 500;

    /// <summary>Refused while the user is asked to click the host's window (<see cref="BeginPrompt"/>).</summary>
    public const string ActivationPending = "activation-pending";

    // Camtasia's reason texts, kept word for word: the broker's text fallback and the contract's safety reasons
    // match them. The typed code travels beside them.
    private const string UserInput = "Non-injected keyboard or mouse input observed; ask the user before resuming";

    private readonly Func<(nint Window, int Pid)> foreground;
    private readonly object sync = new();
    private readonly HashSet<int> targets = [];
    private readonly Func<long>? physicalInput;
    private readonly Func<InputCounts>? counts;
    private readonly Func<IInputObserver> createObserver;
    private readonly bool observerInjected;
    private readonly IDisposable interactiveHost;
    private readonly Func<long> clock;
    private readonly List<CancellationTokenSource> retired = [];
    private System.Threading.Timer? monitor;
    private IInputObserver? inputObserver;
    private long inputAtApproval;
    private long? missingForegroundSince;
    private CancellationTokenSource lifetime = new();
    private CancellationTokenSource safety = new();
    private bool watching;
    private bool disposed;
    private string? paused;
    private string code = ApprovalRequired;

    /// <param name="foreground">The foreground window and its process; the real desktop by default.</param>
    /// <param name="physicalInput">Counts the user's input; null starts a <see cref="PhysicalInputObserver"/>.</param>
    /// <param name="clock">Milliseconds for the missing-foreground grace; Environment.TickCount64 by default.</param>
    /// <param name="createObserver">Starts an observer once a batch begins.</param>
    /// <param name="counts">Keys and button-downs for <see cref="Mark"/> when <paramref name="physicalInput"/> is injected.</param>
    /// <remarks>Constructing a grant declares the host interactive (<see cref="MutationGuard.RequireHook"/>).</remarks>
    public InteractionGrant(
        Func<(nint Window, int Pid)>? foreground = null,
        Func<long>? physicalInput = null,
        Func<long>? clock = null,
        Func<IInputObserver>? createObserver = null,
        Func<InputCounts>? counts = null
    )
    {
        interactiveHost = MutationGuard.RequireHook();
        this.counts = counts;
        observerInjected = createObserver != null;
        this.createObserver = createObserver ?? (() => new PhysicalInputObserver());
        this.clock = clock ?? (() => Environment.TickCount64);
        this.foreground =
            foreground
            ?? (
                () =>
                {
                    var hwnd = InputDesktop.Current.Foreground();
                    return (hwnd, hwnd == 0 ? 0 : InputDesktop.Current.ProcessOf(hwnd));
                }
            );
        this.physicalInput = physicalInput;
    }

    /// <summary>Cancelled when the grant ends, normally or not.</summary>
    public CancellationToken Token
    {
        get
        {
            lock (sync)
            {
                return lifetime.Token;
            }
        }
    }

    /// <summary>Cancelled only by a safety event; a normal end leaves it running while input is still watched.</summary>
    public CancellationToken SafetyToken
    {
        get
        {
            lock (sync)
            {
                return safety.Token;
            }
        }
    }

    public string? Batch { get; private set; }

    public string Reason { get; private set; } = "User has not approved an interactive batch";

    /// <summary>The refusal code <see cref="Require"/> would give now (hyphenated).</summary>
    public string Code
    {
        get
        {
            lock (sync)
            {
                return code;
            }
        }
    }

    public bool Active
    {
        get
        {
            lock (sync)
            {
                return Batch != null;
            }
        }
    }

    /// <summary>Approve one described batch the user cleared the machine for.</summary>
    public void Begin(string batch, bool userApproved, bool monitorChanges = true)
    {
        if (!userApproved || string.IsNullOrWhiteSpace(batch))
        {
            throw new ArgumentException("Ask the user to clear the machine for a described batch first.");
        }

        lock (sync)
        {
            if (Batch != null)
            {
                throw new InvalidOperationException("End the current batch before beginning another.");
            }

            ObjectDisposedException.ThrowIf(disposed, this);
            if (physicalInput == null && !monitorChanges && !observerInjected)
            {
                // No monitor and no input source would leave the batch unwatched: never fail open.
                throw new ArgumentException("A batch without monitoring needs an injected input source.");
            }

            if (foreground().Window == 0)
            {
                throw new InvalidOperationException("No interactive foreground window.");
            }

            Renew();
            if (physicalInput == null)
            {
                inputObserver = Live(inputObserver) ?? createObserver();
            }

            inputAtApproval = InputSequence();
            targets.Clear();
            missingForegroundSince = null;
            paused = null;
            promptUntil = null;
            (Batch, Reason, code, watching) = (batch, "Approved", ApprovalRequired, true);
            monitor?.Dispose();
            monitor = monitorChanges ? new System.Threading.Timer(_ => Observe(), null, 100, 100) : null;
        }
    }

    // A new grant generation: the old tokens are kept until disposal, so a revocation racing it cancels the old one.
    private void Renew()
    {
        retired.Add(lifetime);
        lifetime = new CancellationTokenSource();
        if (safety.IsCancellationRequested)
        {
            retired.Add(safety);
            safety = new CancellationTokenSource();
        }
    }

    /// <summary>Note a process the batch owns (launched or attached); the reason then says when it is not in front.</summary>
    public void AddTarget(int pid)
    {
        Require();
        lock (sync)
        {
            if (Batch == null)
            {
                throw Refused(ApprovalRequired, "Approval revoked.");
            }

            targets.Add(pid);
        }
    }

    /// <summary>Launch a process under the grant and note it as a target.</summary>
    public T Launch<T>(Func<T> launch, Func<T, int> processId)
    {
        Require();
        T result;
        lock (sync)
        {
            if (Batch == null)
            {
                throw Refused(ApprovalRequired, "Approval revoked.");
            }

            result = launch();
            targets.Add(processId(result));
        }

        Observe();
        return result;
    }

    /// <summary>Throws a typed refusal unless a batch is approved, unpaused, and the foreground is available.</summary>
    public void Require()
    {
        Observe();
        lock (sync)
        {
            if (Batch == null)
            {
                throw Refused(code, "Interaction approval required: " + Reason);
            }

            if (promptUntil != null)
            {
                throw Refused(
                    ActivationPending,
                    "The user is being asked to click the window; no input until they have."
                );
            }

            if (paused != null)
            {
                throw Refused(PausedAfterFailure, $"Desktop input is paused: {paused}. Observe the app, then retry.");
            }

            if (foreground().Window == 0)
            {
                throw Refused(FocusUnavailable, "Foreground is transitioning; no input permitted");
            }
        }
    }

    /// <summary>Wait until the foreground is available again, refusing as soon as the grant ends.</summary>
    public async Task WaitForForeground(CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            Observe();
            lock (sync)
            {
                if (Batch == null)
                {
                    throw Refused(code, "Interaction approval required: " + Reason);
                }

                if (foreground().Window != 0)
                {
                    return;
                }
            }

            await Task.Delay(25, token);
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

    /// <summary>Refuse input until <see cref="Observed"/>: a call failed after sending input.</summary>
    public void Pause(string why)
    {
        lock (sync)
        {
            paused = why;
        }
    }

    /// <summary>The app's state after a failure has been looked at; input may resume.</summary>
    public void Observed()
    {
        lock (sync)
        {
            paused = null;
        }
    }

    /// <summary>The user's keys and clicks now; null without a counting source.</summary>
    public InputCounts? Mark()
    {
        lock (sync)
        {
            return counts?.Invoke() ?? inputObserver?.Counts;
        }
    }

    /// <summary>
    /// Ends the grant. A normal end keeps <see cref="SafetyToken"/> alive while physical input is still watched; a
    /// safety end (host stop) also cancels it.
    /// </summary>
    /// <param name="code">The refusal later input gets: approval-required, or activation-denied for a failed prompt.</param>
    public void End(string reason = "Batch finished", bool safetyEvent = false, string code = ApprovalRequired)
    {
        CancellationTokenSource stop,
            stopSafety;
        lock (sync)
        {
            stop = RevokeLocked(reason, code, safetyEvent);
            stopSafety = safety;
        }

        Cancel(stop);
        if (safetyEvent)
        {
            Cancel(stopSafety);
        }
    }

    // A refusal noted on the running operation so its outcome carries the code; the refusal itself sent nothing.
    private static InputRefusedException Refused(string code, string message) =>
        InputRefusedException.Record(new InputRefusal(code, message) { ActionDispatched = false });

    private static void Cancel(CancellationTokenSource? source)
    {
        try
        {
            source?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A completed shutdown already revoked this generation.
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        End("Host stopped", safetyEvent: true);
        lock (sync)
        {
            monitor?.Dispose();
            monitor = null;
        }

        inputObserver?.Dispose();
        foreach (var source in retired)
        {
            source.Dispose();
        }

        lifetime.Dispose();
        safety.Dispose();
        interactiveHost.Dispose();
    }
}
