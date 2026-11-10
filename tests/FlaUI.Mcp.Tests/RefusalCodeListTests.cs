using System.Reflection;
using System.Text.RegularExpressions;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Every refusal code the server emits is a constant in <see cref="RefusalCodes"/>, so a host, the broker contract
/// and a client compare against one list. A code written as a string literal where a refusal is built would be
/// missing from that list or free to drift from it.
/// </summary>
public sealed partial class RefusalCodeListTests
{
    private static readonly string[] Codes = typeof(RefusalCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToArray();

    // A refusal built from a literal: the record itself, or a helper that takes the code first (Refused("x-y", ...),
    // Refusal("x-y", ...), ErrorResult("x-y", ...)).
    [GeneratedRegex("""new InputRefusal\(\s*"([^"]*)"|\b(?:Refus\w*|ErrorResult)\(\s*"([a-z]+(?:[-_][a-z]+)+)"\s*,""")]
    private static partial Regex LiteralCode();

    // The same sites written the right way, for the positive control.
    [GeneratedRegex("""(?:new InputRefusal|\bRefus\w*|ErrorResult)\(\s*RefusalCodes\.\w+""")]
    private static partial Regex ListedCode();

    [Theory]
    [InlineData("""new InputRefusal("x-y", message)""")]
    [InlineData("new InputRefusal(\n    \"x-y\",\n    message)")]
    [InlineData("""throw Refused("x-y", "why");""")]
    [InlineData("""Refusal("x_y", message, false)""")]
    [InlineData("""ErrorResult("x-y", "Missing argument")""")]
    public void TheScanSeesALiteralCodeWhereverARefusalIsBuilt(string source) => Assert.Matches(LiteralCode(), source);

    [Theory]
    [InlineData("""ErrorResult("Provide either chord or keys.")""")]
    [InlineData("""ErrorResult($"Failed to {action}: {error.Message}")""")]
    [InlineData("""new InputRefusal(RefusalCodes.TargetStale, "Stale")""")]
    [InlineData("""throw Refused(ActivationPending, "why");""")]
    public void TheScanLeavesMessagesAndNamedCodesAlone(string source) => Assert.DoesNotMatch(LiteralCode(), source);

    [Fact]
    public void TheScanReadsTheSourceWhereRefusalsAreBuilt()
    {
        // A scan that reads nothing finds nothing: it must see the sites that name a listed code (59 when written).
        var sites = Sources().Sum(file => ListedCode().Matches(File.ReadAllText(file)).Count);

        Assert.True(sites >= 50, $"only {sites} refusal sites found");
    }

    [GeneratedRegex(
        """
            "([a-z]+(?:-[a-z]+)+)"
            """,
        RegexOptions.IgnorePatternWhitespace
    )]
    private static partial Regex HyphenatedLiteral();

    [Fact]
    public void TheListIsCanonicalAndHasNoDuplicates()
    {
        Assert.All(Codes, code => Assert.Equal(RefusalCodes.Canonical(code), code));
        Assert.Equal(Codes.Length, Codes.Distinct().Count());
    }

    [Theory]
    [InlineData("lease-unavailable")]
    [InlineData("window-baseline-unreadable")]
    [InlineData("launch-handoff")]
    [InlineData("launch-pending")]
    [InlineData("activation-pending")]
    [InlineData("pointer-moved")]
    public void CodesThatWereOnlyLiteralsAreListed(string code) => Assert.Contains(code, Codes);

    [Fact]
    public void TheGrantsOwnNamesAreTheListedCodes()
    {
        Assert.Equal(RefusalCodes.ApprovalRequired, InteractionGrant.ApprovalRequired);
        Assert.Equal(RefusalCodes.FocusRevoked, InteractionGrant.FocusRevoked);
        Assert.Equal(RefusalCodes.FocusUnavailable, InteractionGrant.FocusUnavailable);
        Assert.Equal(RefusalCodes.PausedAfterFailure, InteractionGrant.PausedAfterFailure);
        Assert.Equal(RefusalCodes.ActivationPending, InteractionGrant.ActivationPending);
    }

    [Fact]
    public void NoRefusalIsBuiltFromALiteralCode()
    {
        var literal = Sources()
            .SelectMany(file =>
                LiteralCode()
                    .Matches(File.ReadAllText(file))
                    .Select(match => $"{Path.GetFileName(file)}: {match.Value}")
            );

        Assert.Empty(literal);
    }

    [Fact]
    public void NoListedCodeIsSpelledOutAgainOutsideTheList()
    {
        var repeated = Sources()
            .SelectMany(file =>
                File.ReadLines(file)
                    .Select((line, index) => (line, index))
                    .Where(e => !e.line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    .SelectMany(e =>
                        HyphenatedLiteral()
                            .Matches(e.line)
                            .Where(match => Codes.Contains(match.Groups[1].Value))
                            .Select(match => $"{Path.GetFileName(file)}:{e.index + 1} {match.Value}")
                    )
            );

        Assert.Empty(repeated);
    }

    private static IEnumerable<string> Sources()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "FlaUI.Mcp");
            if (Directory.Exists(Path.Combine(candidate, "Tools")))
            {
                return Directory
                    .GetFiles(candidate, "*.cs", SearchOption.AllDirectories)
                    .Where(file =>
                        !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && Path.GetFileName(file) != "AutomationRefusal.cs"
                    );
            }
        }

        throw new DirectoryNotFoundException("src/FlaUI.Mcp");
    }
}
