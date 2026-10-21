namespace FlaUI.Mcp.Core;

/// <summary>Never starts a mutation after cancellation or a failed identity check.</summary>
public static class MutationGuard
{
    public static readonly AsyncLocal<Action?> BeforeMutation = new();

    public static void CheckPermission() => BeforeMutation.Value?.Invoke();

    /// <summary>Count one input or mutation against the running operation, just before it is sent.</summary>
    public static void RecordDispatch() => OperationContext.Current.Value?.RecordInput();

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
