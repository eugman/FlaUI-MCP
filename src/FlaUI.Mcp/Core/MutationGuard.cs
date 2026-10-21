namespace FlaUI.Mcp.Core;

/// <summary>Never starts a mutation after cancellation or a failed identity check.</summary>
public static class MutationGuard
{
    public static readonly AsyncLocal<Action?> BeforeMutation = new();

    private static int hookRequired;

    /// <summary>
    /// Declare this host interactive: from now on a mutation with no <see cref="BeforeMutation"/> hook on its flow is
    /// refused (approval-required) instead of allowed, so a path that forgot to install the grant fails closed. A
    /// host calls it once at startup; disposing the result undoes it (for tests).
    /// </summary>
    public static IDisposable RequireHook()
    {
        Interlocked.Increment(ref hookRequired);
        return new Undo();
    }

    private sealed class Undo : IDisposable
    {
        private int done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref done, 1) == 0)
            {
                Interlocked.Decrement(ref hookRequired);
            }
        }
    }

    public static void CheckPermission()
    {
        if (BeforeMutation.Value is { } hook)
        {
            RunHook(hook);
            return;
        }

        if (Volatile.Read(ref hookRequired) > 0)
        {
            throw InputRefusedException.Record(
                new InputRefusal(
                    "approval-required",
                    "This host takes input only under an interaction grant, and none is installed on this path; no input sent."
                )
                {
                    ActionDispatched = false,
                }
            );
        }
    }

    /// <summary>Count one input or mutation against the running operation, just before it is sent.</summary>
    public static void RecordDispatch() => OperationContext.Current.Value?.RecordInput();

    /// <summary>
    /// Run the host's hook. Its refusal keeps its type, and its code is noted on the running operation, so the
    /// outcome carries it even when a tool turns the exception into text; any other exception is noted as
    /// permission-denied.
    /// </summary>
    private static void RunHook(Action hook)
    {
        try
        {
            hook();
        }
        catch (InputRefusedException)
        {
            throw;
        }
        catch (AutomationRefusal refusal)
        {
            Note(new InputRefusal(refusal.Code, refusal.Message) { ActionDispatched = refusal.ActionDispatched });
            throw;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Note(new InputRefusal(RefusalCodes.PermissionDenied, error.Message) { ActionDispatched = false });
            throw;
        }
    }

    private static void Note(InputRefusal refusal)
    {
        if (OperationContext.Current.Value is { } operation)
        {
            operation.Refusal = refusal;
        }
    }

    public static void Execute(Action validateIdentity, Action mutation)
    {
        ExecuteUncounted(
            validateIdentity,
            () =>
            {
                RecordDispatch();
                mutation();
            }
        );
    }

    /// <summary>The checks of <see cref="Execute"/> without the count, for a change counted once it is seen.</summary>
    internal static void ExecuteUncounted(Action validateIdentity, Action mutation)
    {
        OperationContext.Check();
        validateIdentity();
        OperationContext.Check();
        CheckPermission();
        mutation();
    }
}
