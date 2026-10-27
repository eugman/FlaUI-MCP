using System.Text.Json.Serialization;

namespace FlaUI.Mcp.Core;

/// <summary>
/// How a tool checked its effect: the verdict, what it observed and expected, where it read it (a pattern, the
/// window rectangle, the foreground), and whether that read is independent of the channel that acted. Status follows
/// the verdict: ok only for verified or no-op, failed for mismatch or not-applied, unverified otherwise. Hosts build
/// theirs with <see cref="Compare"/> where appropriate; callers must account for whether the read is independent
/// before reporting a verified effect. This record does not enforce that distinction by itself.
/// </summary>
public sealed record Proof(
    [property: JsonPropertyName("verdict")] string Verdict,
    [property: JsonPropertyName("observed")] string? Observed,
    [property: JsonPropertyName("expected")] string? Expected,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("independent")] bool Independent
)
{
    public const string Verified = "verified";
    public const string NoOp = "no-op";
    public const string Mismatch = "mismatch";
    public const string NotApplied = "not-applied";
    public const string Unavailable = "unavailable";

    /// <summary>
    /// The channel that acted reports the effect itself (a Value pattern read back after its own SetValue): the
    /// read agrees, but nothing independent confirmed it, so the status is unverified, never ok.
    /// </summary>
    public const string SelfReported = "self-reported";

    /// <summary>
    /// The state read back right after the call agreed, but a dialog opened a moment later, so it may not stand;
    /// the status is unverified until the dialog is handled and the state observed again.
    /// </summary>
    public const string Unsettled = "unsettled";

    /// <summary>Compares values, returning verified when equal or unavailable when unread; it does not downgrade a same-channel read.</summary>
    public static Proof Compare(string? expected, string? observed, string source, bool independent)
    {
        string verdict;
        if (observed == null)
        {
            verdict = Unavailable;
        }
        else
        {
            verdict = observed == expected ? Verified : Mismatch;
        }

        return new(verdict, observed, expected, source, independent);
    }

    /// <summary>The status a verdict gives: ok, failed or unverified.</summary>
    public static string StatusFor(string verdict) =>
        verdict switch
        {
            Verified or NoOp => "ok",
            Mismatch or NotApplied => "failed",
            _ => "unverified",
        };
}
