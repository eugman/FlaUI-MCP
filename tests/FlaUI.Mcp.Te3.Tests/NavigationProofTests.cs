using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// te3_navigate's proof: an object navigation compares the selection TE3 names afterwards (name, type, table) with
/// the request; the begin and end tools prove the grant's state by reading it back.
/// </summary>
public sealed class NavigationProofTests
{
    [Fact]
    public void TheSelectionTe3NamesIsComparedWithTheRequest()
    {
        var proof = NavigationProof.Object(
            "Brands",
            "Brand",
            "Column",
            new("Column \"Brand\"", null, null, null),
            "Brands"
        );

        Assert.Equal(Proof.Verified, proof.Verdict);
        Assert.Equal("Brand (Column) in Brands", proof.Observed);
        Assert.True(proof.Independent);
    }

    [Fact]
    public void AnotherTypeOrNameIsAMismatchAndAnUnreadBarIsUnavailable()
    {
        Assert.Equal(
            Proof.Mismatch,
            NavigationProof
                .Object("Brands", "Brand", "Column", new("Hierarchy \"Brand\"", null, null, null), "Brands")
                .Verdict
        );
        Assert.Equal(
            Proof.Unavailable,
            NavigationProof.Object("Brands", "Brand", "Column", new(null, null, null, null), "Brands").Verdict
        );
    }

    [Fact]
    public void ATableNoReadGaveLeavesTheProofUnavailableNotEchoedFromTheRequest()
    {
        var proof = NavigationProof.Object(
            "Brands",
            "Brand",
            "Column",
            new("Column \"Brand\"", null, null, null),
            null
        );

        Assert.Equal(Proof.Unavailable, proof.Verdict);
        Assert.Contains("table not read", proof.Observed);
        Assert.DoesNotContain("in Brands", proof.Observed);
    }

    [Fact]
    public void APreviousWalksTableCannotProveTheCurrentSelection()
    {
        // The current tree can be scrolled past the table row while a same-named object is selected elsewhere.
        // Historical walk state must never turn that incomplete read into an independent proof.
        var proof = NavigationProof.ObjectFromRead(
            "Invoices",
            "Gross Sales (Quantity)",
            "Measure",
            new("Measure \"Gross Sales (Quantity)\"", null, null, null),
            new SelectionPlace(null, false),
            "Invoices"
        );

        Assert.Equal(Proof.Unavailable, proof.Verdict);
        Assert.DoesNotContain("in Invoices", proof.Observed);
    }

    [Fact]
    public void AnUntypedRequestTakesTheTypeTe3NamesAndACalculatedColumnIsAColumn()
    {
        Assert.Equal(
            Proof.Verified,
            NavigationProof.Object("Brands", "Brand", null, new("Column \"Brand\"", null, null, null), "Brands").Verdict
        );
        Assert.Equal(
            Proof.Verified,
            NavigationProof
                .Object("Sales", "Margin", "Column", new("Calculated Column \"Margin\"", null, null, null), "Sales")
                .Verdict
        );
    }

    [Fact]
    public async Task BeginAndEndProveTheGrantStateTheyReadBack()
    {
        using var interaction = new Te3Interaction(() => 0);
        var begun = await new Te3InteractionTool(interaction, Te3InteractionTool.Begin).ExecuteAsync(
            JsonSerializer.SerializeToElement(new { batch = "Open Preferences", userApproved = true })
        );
        var ended = await new Te3InteractionTool(interaction, Te3InteractionTool.End).ExecuteAsync(null);

        Assert.Equal(Proof.Verified, begun.Outcome?.Proof?.Verdict);
        Assert.Equal("active", begun.Outcome?.Proof?.Observed);
        Assert.Equal("inactive", ended.Outcome?.Proof?.Observed);
    }
}
