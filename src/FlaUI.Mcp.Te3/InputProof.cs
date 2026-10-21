using System.Text.RegularExpressions;

/// <summary>What a failure's text proves about input: whether it was refused before anything was sent.</summary>
internal static class InputProof
{
    // Core and this host end a refusal that sent nothing with "no input sent", "no further input sent" or
    // "sent no input"; a grant refusal, a stale ref or identity, and a window Core cannot own also come before input.
    private static readonly Regex NotSentText = new(
        @"\bno (?:further )?input (?:was )?sent\b|\bsent no input\b|Desktop input needs the user's approval"
            + "|Stale element reference|Stale process/window identity|Cannot establish window ownership",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)
    );

    // Core's GuardedInput text, at the start of the error or after a tool's "Failed to ...: " prefix.
    // TODO(core): report activation-denied as a structured outcome instead of matching its text.
    private static readonly Regex Blockers = new(
        "(?:^|: )(?<blocker>activation-denied): Windows kept |(?:^|: )(?<blocker>desktop-unavailable): Windows reports no foreground window",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)
    );

    /// <summary>Whether the failure says no input went out.</summary>
    /// <param name="tool">The Core tool that failed, when known.</param>
    internal static bool NotSent(string message, string? tool = null) =>
        NotSentText.IsMatch(message) || Blocker(message) != null;

    /// <summary>The desktop condition that refused the input, when retrying cannot help: activation-denied or desktop-unavailable.</summary>
    internal static string? Blocker(string message) =>
        Blockers.Match(message) is { Success: true } match ? match.Groups["blocker"].Value : null;
}
