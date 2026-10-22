using System.Text.Json.Serialization;

namespace FlaUI.Mcp.Core;

/// <summary>
/// How a tool checked its effect: the verdict, what it observed and expected, where it read it (a pattern, the
/// window rectangle, the foreground), and whether that read is independent of the channel that acted. Status follows
/// the verdict: ok only for verified or no-op, failed for mismatch or not-applied, unverified otherwise. Hosts build
/// theirs with <see cref="Compare"/> and must not hard-code verified.
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

    /// <summary>Verified when <paramref name="observed"/> equals <paramref name="expected"/>, unavailable when unread.</summary>
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
