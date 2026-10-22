using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A refusal that is caught and handled leaves no note on the operation, so a later, unrelated failure in the
/// same call is not reported under its code.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class HandledRefusalTests
{
    [Fact]
    public void AStaleWindowThatIsForgottenLeavesNoRefusalOnTheOperation()
    {
        using var installed = new FakeDesktop().Install();
        using var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, FakeDesktop.TargetHwnd, FakeDesktop.TargetPid, FakeDesktop.Target);
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            var window = sessions.GetWindow(
                handle,
                _ => null,
                _ =>
                    throw InputRefusedException.Record(
                        new InputRefusal(RefusalCodes.TargetStale, "Stale process/window identity.")
                    )
            );

            Assert.Null(window);
            Assert.Null(operation.Refusal);
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }

    [Fact]
    public void ForgettingAHandledRefusalKeepsANewerOne()
    {
        var operation = new OperationContext();
        OperationContext.Current.Value = operation;
        try
        {
            var older = InputRefusedException.Record(new InputRefusal(RefusalCodes.TargetStale, "older"));
            var newer = InputRefusedException.Record(new InputRefusal(RefusalCodes.FocusLost, "newer"));

            InputRefusedException.Handled(older);

            Assert.Same(newer.Refusal, operation.Refusal);
        }
        finally
        {
            OperationContext.Current.Value = null;
        }
    }
}
