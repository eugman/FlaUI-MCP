using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// A mutation-permission hook that allows the first checks and then refuses, as a host's observer does when
/// the user starts typing after a guard was built. Dispose to remove it.
/// </summary>
internal sealed class PermissionTrip : IDisposable
{
    public const string Message = "mutation denied: the user is active";

    private int checks;

    private PermissionTrip(int allowed) =>
        MutationGuard.BeforeMutation.Value = () =>
        {
            if (++checks > allowed)
            {
                throw new InvalidOperationException(Message);
            }
        };

    /// <summary>The number of permission checks made so far.</summary>
    public int Checks => checks;

    /// <summary>Allow <paramref name="allowed"/> permission checks, then refuse every later one.</summary>
    public static PermissionTrip DenyAfter(int allowed) => new(allowed);

    public void Dispose() => MutationGuard.BeforeMutation.Value = null;
}
