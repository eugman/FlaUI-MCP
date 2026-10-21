using System.Text.RegularExpressions;

/// <summary>What a failure's text proves about input: whether it was refused before anything was sent.</summary>
internal static class InputProof
{
    // Core's wording when a tool that sends several chords or lines stopped part-way: something already went out,
    // whatever refusal follows. This always wins.
    private static readonly Regex PartlySent = new(
        @"\bCompleted [1-9]\d* chord|\bTyped [1-9]\d* of\b|may be partly typed|may have partially executed",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)
    );

    // A refusal that says nothing was sent. "No further input sent" is not one: it follows an input that went out.
    private static readonly Regex NothingSent = new(
        @"\bno input (?:was )?sent\b|\bsent no input\b|\bno keyboard input was dispatched\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)
    );

    // Checked before a single input goes out: the grant, a stale ref or identity, a window Core cannot own.
    private static readonly Regex RefusedBeforeInput = new(
        "Desktop input needs the user's approval|Stale element reference|Stale process/window identity"
            + "|Cannot establish window ownership",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)
    );

    // Tools that send a single input, checked first, so a grant or stale-ref refusal from them proves nothing went
    // out. Every other tool may send several (send_keys, type, fill, a mouse drag or scroll) and is never proven.
    private static readonly HashSet<string> SingleInput = ["windows_click"];

    // Core's GuardedInput text, at the start of the error or after a tool's "Failed to ...: " prefix.
    // TODO(core): report activation-denied as a structured outcome instead of matching its text.
    private static readonly Regex Blockers = new(
        "(?:^|: )(?<blocker>activation-denied): Windows kept |(?:^|: )(?<blocker>desktop-unavailable): Windows reports no foreground window",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)
    );

    /// <summary>
    /// Whether the failure proves no input went out. A partial-input marker always means something was sent. A grant
    /// or stale-ref refusal proves nothing was sent only for a tool on the single-input allowlist.
    /// </summary>
    /// <param name="tool">The Core tool that failed, when known.</param>
    internal static bool NotSent(string message, string? tool = null)
    {
        if (PartlySent.IsMatch(message))
        {
            return false;
        }

        if (NothingSent.IsMatch(message) || Blocker(message) != null)
        {
            return true;
        }

        return tool != null && SingleInput.Contains(tool) && RefusedBeforeInput.IsMatch(message);
    }

    /// <summary>The desktop condition that refused the input, when retrying cannot help: activation-denied or desktop-unavailable.</summary>
    internal static string? Blocker(string message) =>
        Blockers.Match(message) is { Success: true } match ? match.Groups["blocker"].Value : null;
}
