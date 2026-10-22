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
