using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;

/// <summary>
/// The proof each te3_layout change carries (Core's ToolOutcome.proof), built from the read-back the action already
/// makes: the panes and floating windows left after a close, the title-bar button after maximize or restore, the
/// pane size after a drag, the document tabs after close-document, and the SHA-256 of each file a snapshot or
/// restore copied. A change whose read-back failed has already thrown, so this names what was read.
/// </summary>
internal static class LayoutProof
{
    private const string Layout = "TE3 layout read-back (docked parts and floating windows)";
    private const string Sizes = "pane bounds read back after the drag";

    /// <summary>The result with its proof; the status follows the verdict (ok, failed or unverified).</summary>
    public static McpToolResult Attach(McpToolResult result, Proof proof) =>
        result with
        {
            Outcome = (result.Outcome ?? new ToolOutcome("completed", "not-tracked")).WithProof(proof),
        };

    /// <summary>The proof for one action's reply.</summary>
    public static Proof For(string action, JsonElement reply) =>
        action switch
        {
            "close" or "keep" => Closed(reply),
            "hide-pane" => Changed(reply, "hidden", reply.GetProperty("visible").GetBoolean() ? "visible" : "hidden"),
            "reopen-pane" => Changed(
                reply,
                "showing",
                reply.GetProperty("visible").GetBoolean() ? "showing" : "hidden"
            ),
            "maximize-pane" => Button(reply, "Restore"),
            "restore-pane" => Button(reply, "Maximize"),
            "resize" => Step(reply),
            "apply-preset" => Preset(reply),
            "close-document" => Proof.Compare("closed", "closed", "TE3 document tabs read back after Close", true),
            "snapshot" => Files(reply.GetProperty("files"), true),
            "restore" => Files(reply.GetProperty("restored"), reply.GetProperty("verified").GetBoolean()),
            _ => new Proof(Proof.Unavailable, null, null, "no read-back for " + action, false),
        };

    private static Proof Closed(JsonElement reply)
    {
        var unresolved = reply.GetProperty("unresolved").EnumerateArray().Select(e => e.GetString()).ToList();
        return Proof.Compare(
            "all resolved",
            unresolved.Count == 0 ? "all resolved" : "unresolved: " + string.Join(", ", unresolved),
            Layout,
            true
        );
    }

    // A pane already in the asked state was left alone: a no-op, proven by the same read.
    private static Proof Changed(JsonElement reply, string expected, string observed)
    {
        if (reply.GetProperty("changed").GetBoolean())
        {
            return Proof.Compare(expected, observed, Layout, true);
        }

        return new Proof(observed == expected ? Proof.NoOp : Proof.Mismatch, observed, expected, Layout, true);
    }

    private static Proof Button(JsonElement reply, string expected) =>
        Proof.Compare(
            expected,
            reply.GetProperty("readBack").GetString(),
            "the pane's title-bar button read back (Maximize and Restore are one button renamed)",
            true
        );

    private static Proof Step(JsonElement step)
    {
        var requested = step.GetProperty("requested").GetInt32();
        var reached = step.GetProperty("reached").GetInt32();
        var expected = $"{requested} px ±{LayoutChecks.SizeTolerance}";
        if (step.TryGetProperty("skipped", out var skipped) && skipped.GetBoolean())
        {
            return new Proof(Proof.NoOp, $"{reached} px", expected, Sizes, true);
        }

        var within = Math.Abs(reached - requested) <= LayoutChecks.SizeTolerance;
        return Proof.Compare(expected, within ? expected : $"{reached} px", Sizes, true);
    }

    private static Proof Preset(JsonElement reply)
    {
        var steps = reply.GetProperty("changes").EnumerateArray().Select(Step).ToList();
        if (steps.Find(p => p.Verdict != Proof.Verified && p.Verdict != Proof.NoOp) is { } miss)
        {
            return miss;
        }

        var verdict = steps.TrueForAll(p => p.Verdict == Proof.NoOp) ? Proof.NoOp : Proof.Verified;
        var summary = $"{steps.Count} pane(s) within ±{LayoutChecks.SizeTolerance} px";
        return new Proof(verdict, summary, summary, Sizes, true);
    }

    private static Proof Files(JsonElement hashes, bool verified)
    {
        var expected = $"{hashes.EnumerateObject().Count()} file(s) with the snapshot's SHA-256";
        return Proof.Compare(
            expected,
            verified ? expected : "a file differs",
            "SHA-256 read-back of each copied file",
            true
        );
    }
}
