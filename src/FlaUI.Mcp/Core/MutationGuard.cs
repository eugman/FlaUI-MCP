namespace PlaywrightWindows.Mcp.Core;

/// <summary>Never starts a mutation after cancellation or a failed identity check.</summary>
public static class MutationGuard
{
    public static void Execute(Action validateIdentity, Action mutation)
    {
        OperationContext.Check();
        validateIdentity();
        OperationContext.Check();
        mutation();
    }
}
