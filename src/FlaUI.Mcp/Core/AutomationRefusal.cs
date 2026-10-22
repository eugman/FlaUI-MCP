namespace FlaUI.Mcp.Core;

/// <summary>What a refusal means for what went out before it.</summary>
public enum RefusalDisposition
{
    /// <summary>Nothing was sent; the call may be retried once its cause is cleared.</summary>
    NoInput,

    /// <summary>Some input went out before the refusal; observe before continuing, never replay.</summary>
    Partial,

    /// <summary>It is not known whether input went out (a call still pending); never replay.</summary>
    Uncertain,
}

/// <summary>
/// A refusal by the automation layer or a host: the guard refused input, an element was not found, a provider is
/// blocked, the user's grant is missing. It is deliberately not an <see cref="InvalidOperationException"/>, so a poll
/// that catches "not ready yet" cannot swallow a refusal and relabel it as a timeout or "not found". Hosts derive
/// their own refusal types from it.
/// </summary>
public abstract class AutomationRefusal(string code, string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>The refusal code, hyphenated (see <see cref="RefusalCodes"/>).</summary>
    public string Code { get; } = RefusalCodes.Canonical(code);

    /// <summary>The inputs the failing call had sent before this refusal.</summary>
    public int InputsSent { get; init; }

    /// <summary>Whether the failing call had already sent input or a pattern call.</summary>
    public bool ActionDispatched { get; init; }

    public virtual RefusalDisposition Disposition =>
        InputsSent > 0 || ActionDispatched ? RefusalDisposition.Partial : RefusalDisposition.NoInput;
}

/// <summary>
/// The refusal codes in their one canonical spelling, hyphenated. Hosts that spell them with underscores
/// (focus_revoked) are mapped on the way in, so a client compares one form.
/// </summary>
public static class RefusalCodes
{
    public const string ApprovalRequired = "approval-required";
    public const string FocusRevoked = "focus-revoked";
    public const string FocusUnavailable = "focus-unavailable";
    public const string PausedAfterFailure = "paused-after-failure";
    public const string PermissionDenied = "permission-denied";
    public const string DesktopUnavailable = "desktop-unavailable";
    public const string ActivationDenied = "activation-denied";
    public const string IntegrityBlocked = "integrity-blocked";
    public const string PointNotOnTarget = "point-not-on-target";
    public const string ClickPointUnavailable = "click-point-unavailable";
    public const string ClickPointCovered = "click-point-covered";
    public const string ElementNotFound = "element-not-found";
    public const string ProviderBlocked = "provider-blocked";
    public const string PlacementMismatch = "placement-mismatch";

    /// <summary>A code in the canonical form: lower case, words joined by hyphens.</summary>
    public static string Canonical(string code) => code.Replace('_', '-').ToLowerInvariant();
}
