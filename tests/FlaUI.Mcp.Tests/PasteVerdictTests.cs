using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>A paste's read-back verdict is a structured field, not only a sentence in the reply text.</summary>
[Collection(InputLeaseCollection.Name)]
public sealed class PasteVerdictTests
{
    [Theory]
    [InlineData("", "Total Sales", "Total Sales", "verified")]
    [InlineData("Total Sales", "Total Sales", "Sales", "unchanged")]
    [InlineData("Total Sales", "Total Sales", "Margin", "not-applied")]
    [InlineData("", "something else", "Total Sales", "mismatch")]
    [InlineData(null, "Total Sales", "Total Sales", "not-established")]
    [InlineData("existing", "existing", "", "not-established")]
    [InlineData("", null, "Total Sales", "unavailable")]
    public void EachReadBackHasAVerdict(string? before, string? observed, string expected, string verification)
    {
        var verdict = PasteTool.Judge(before, observed, expected);

        Assert.Equal(verification, verdict.Verification);
        Assert.Equal(PasteTool.DescribeEvidence(before, observed, expected), verdict.Sentence);
    }

    [Fact]
    public void LineEndingsDoNotDecideTheVerdict()
    {
        // The clipboard holds CRLF; an edit control may report LF (or the reverse).
        Assert.Equal("verified", PasteTool.Judge("", "line1\nline2", "line1\r\nline2").Verification);
        Assert.Equal("unchanged", PasteTool.Judge("a\r\nb", "a\nb", "a\r\nb").Verification);
    }

    [Fact]
    public void ThePasteReplyCarriesItsVerdict()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var tool = new PasteTool(new ElementRegistry(), new PendingInvokeTracker(), ProcessPolicy.AllowAll, null);
        using var input = new GuardedInput(FakeDesktop.Target);

        var result = tool.Paste(input, null, "text", "focused element");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("not-established", result.Outcome!.Verification);
        Assert.Contains("Not verified", result.Content[0].Text);
        Assert.Equal("text", desktop.ClipboardWritten);
        Assert.Equal(1, desktop.Dispatched);
    }
}
