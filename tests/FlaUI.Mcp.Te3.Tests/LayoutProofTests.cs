using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// Every te3_layout change carries Core's proof: the verdict of the read-back it already makes (the panes left, the
/// title-bar button, the document gone), so status says verified, no-op, or why not.
/// </summary>
public sealed class LayoutProofTests : IDisposable
{
    private readonly Te3Interaction interaction = new(() => 0);

    public LayoutProofTests() => interaction.Begin("arrange TE3", userApproved: true);

    public void Dispose() => interaction.Dispose();

    private Task<McpToolResult> Run(object args, Func<InputTally, FakeSurface>? layout = null) =>
        new Te3Layout(
            null!,
            interaction,
            attach: (_, inputs) =>
                layout?.Invoke(inputs) ?? new FakeSurface(inputs, "AI Assistant", "TOM Explorer", "Properties")
        ).ExecuteAsync(JsonSerializer.SerializeToElement(args));

    private static Proof ProofOf(McpToolResult result)
    {
        Assert.NotEqual(true, result.IsError);
        return result.Outcome?.Proof ?? throw new Xunit.Sdk.XunitException("No proof on " + result.Content[0].Text);
    }

    [Fact]
    public async Task AClosedPaneIsVerifiedByTheLayoutReadBack()
    {
        var proof = ProofOf(await Run(new { action = "close", titles = new[] { "Properties" } }));

        Assert.Equal(Proof.Verified, proof.Verdict);
        Assert.True(proof.Independent);
        Assert.Contains("read-back", proof.Source);
    }

    private static List<string?> Listed(McpToolResult result, string property) =>
        JsonDocument
            .Parse(result.Content.Single().Text!)
            .RootElement.GetProperty(property)
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

    [Fact]
    public async Task KeepWithAPaneThatStaysOpenIsNotVerified()
    {
        var result = await Run(
            new { action = "keep", titles = new[] { "TOM Explorer" } },
            inputs =>
            {
                var layout = new FakeSurface(inputs, "AI Assistant", "TOM Explorer", "Properties");
                layout.Stuck.Add("Properties");
                return layout;
            }
        );

        Assert.Equal(["Properties"], Listed(result, "unresolved"));
        Assert.Equal(["AI Assistant"], Listed(result, "closed"));
        Assert.Equal(Proof.Mismatch, result.Outcome?.Proof?.Verdict);
        Assert.Contains("Properties", result.Outcome?.Proof?.Observed);
    }

    [Theory]
    [InlineData("keep", "TOM Explorer")]
    [InlineData("close", "Properties")]
    public async Task APaneThatTakesAMomentToLeaveIsWaitedForNotCalledStuck(string action, string title)
    {
        FakeSurface? layout = null;
        var result = await Run(
            new { action, titles = new[] { title } },
            inputs =>
            {
                layout = new FakeSurface(inputs, "TOM Explorer", "Properties");
                // Still listed on the reads straight after its Close, gone a few reads later.
                layout.SlowToClose["Properties"] = 3;
                return layout;
            }
        );

        Assert.Empty(Listed(result, "unresolved"));
        Assert.Equal(["Properties"], Listed(result, "closed"));
        Assert.Equal(Proof.Verified, ProofOf(result).Verdict);
        Assert.NotEmpty(layout!.Delays);
    }

    [Fact]
    public async Task AStuckPaneIsJudgedAfterABoundedWait()
    {
        var stuck = new LayoutPart("Properties", "pane", default);
        var waited = 0;

        var parts = await LayoutChecks.PartsOnceGone(
            () => [stuck],
            new HashSet<string> { "Properties" },
            ms =>
            {
                waited += ms;
                return Task.CompletedTask;
            }
        );

        Assert.Equal([stuck], parts);
        Assert.Equal(LayoutChecks.StableMs, waited);
    }

    [Fact]
    public async Task KeepWithAFloatingPaneThatStaysOpenIsNotVerified()
    {
        var result = await Run(
            new { action = "keep", titles = new[] { "TOM Explorer" } },
            inputs =>
            {
                var layout = new FakeSurface(inputs, "TOM Explorer");
                layout.Extra.Add(new(0x30, "Dependencies", 7, true, false, false));
                layout.Shapes[0x30] = new WindowShape("WindowsForms10.Window.8.app.0.1e2bef7_r3_ad1", 0);
                layout.StuckWindows.Add(0x30);
                return layout;
            }
        );

        Assert.Equal(["Dependencies"], Listed(result, "unresolved"));
        Assert.Equal(Proof.Mismatch, result.Outcome?.Proof?.Verdict);
    }

    [Theory]
    [InlineData("DAX Query 1")]
    [InlineData("Dependencies")]
    public async Task KeepingADocumentOrAFloatingPaneIsVerifiedWhenEveryOtherPaneClosed(string kept)
    {
        var result = await Run(
            new { action = "keep", titles = new[] { kept } },
            inputs =>
            {
                var layout = new FakeSurface(inputs, "AI Assistant", "Properties");
                layout.Documents.Add("DAX Query 1");
                layout.Extra.Add(new(0x30, "Dependencies", 7, true, false, false));
                layout.Shapes[0x30] = new WindowShape("WindowsForms10.Window.8.app.0.1e2bef7_r3_ad1", 0);
                return layout;
            }
        );

        Assert.Empty(Listed(result, "unresolved"));
        Assert.Empty(Listed(result, "remaining"));
        Assert.Equal(Proof.Verified, ProofOf(result).Verdict);
    }

    [Fact]
    public async Task AHiddenPaneIsVerifiedAndOneAlreadyHiddenIsANoOp()
    {
        Assert.Equal(Proof.Verified, ProofOf(await Run(new { action = "hide-pane", title = "Properties" })).Verdict);

        var again = await Run(
            new { action = "hide-pane", title = "Properties" },
            inputs => new FakeSurface(inputs, "TOM Explorer")
        );
        Assert.Equal(Proof.NoOp, ProofOf(again).Verdict);
        Assert.Equal("ok", Proof.StatusFor(ProofOf(again).Verdict));
    }

    [Fact]
    public async Task AMaximizeIsProvenByTheButtonItNowShows()
    {
        var proof = ProofOf(await Run(new { action = "maximize-pane", title = "Properties" }));

        Assert.Equal(Proof.Verified, proof.Verdict);
        Assert.Equal("Restore", proof.Observed);
    }

    [Fact]
    public async Task AClosedDocumentIsProvenGone()
    {
        var proof = ProofOf(
            await Run(
                new { action = "close-document", title = "DAX Query 1" },
                inputs =>
                {
                    var layout = new FakeSurface(inputs, "TOM Explorer");
                    layout.Documents.Add("DAX Query 1");
                    return layout;
                }
            )
        );

        Assert.Equal(Proof.Verified, proof.Verdict);
        Assert.Equal("closed", proof.Observed);
    }

    [Fact]
    public void ResizeStepsAreVerifiedWithinTheToleranceAndSkippedOnesAreNoOps()
    {
        var moved = JsonSerializer.SerializeToElement(
            new
            {
                title = "Properties",
                requested = 300,
                reached = 302,
            }
        );
        var skipped = JsonSerializer.SerializeToElement(
            new
            {
                title = "Properties",
                requested = 300,
                reached = 299,
                skipped = true,
            }
        );

        Assert.Equal(Proof.Verified, LayoutProof.For("resize", moved).Verdict);
        Assert.Equal(Proof.NoOp, LayoutProof.For("resize", skipped).Verdict);
        Assert.Equal(
            Proof.NoOp,
            LayoutProof
                .For("apply-preset", JsonSerializer.SerializeToElement(new { changes = new[] { skipped } }))
                .Verdict
        );
        Assert.Equal(
            Proof.Verified,
            LayoutProof
                .For(
                    "apply-preset",
                    JsonSerializer.SerializeToElement(new { changes = new object[] { skipped, moved } })
                )
                .Verdict
        );
    }

    [Fact]
    public void AProofCarriesWhatTheReadBackSawNotAConstant()
    {
        // Review of 00898fc: close-document and snapshot/restore proved "verified" from constants.
        var stillOpen = JsonSerializer.SerializeToElement(
            new { closedDocument = "DAX Query 1", openDocuments = new[] { "DAX Query 1" } }
        );
        var changed = JsonSerializer.SerializeToElement(
            new
            {
                files = new Dictionary<string, string> { ["Layouts.json"] = "AA" },
                readBack = new Dictionary<string, string> { ["Layouts.json"] = "BB" },
            }
        );

        Assert.Equal(Proof.Mismatch, LayoutProof.For("close-document", stillOpen).Verdict);
        Assert.Equal("open", LayoutProof.For("close-document", stillOpen).Observed);
        var files = LayoutProof.For("snapshot", changed);
        Assert.Equal(Proof.Mismatch, files.Verdict);
        Assert.Contains("BB", files.Observed);
    }

    [Fact]
    public void AnArrivalCheckThatOnlyPassedIsNotAnIndependentProof() =>
        Assert.False(NavigationProof.Arrived("tom-explorer").Independent);
}
