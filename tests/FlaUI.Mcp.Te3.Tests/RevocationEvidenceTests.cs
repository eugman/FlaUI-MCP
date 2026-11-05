using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

/// <summary>How the grant ended reaches a failed run's manifest and the interaction tool's reply.</summary>
public sealed class RevocationEvidenceTests
{
    private long input;

    [Fact]
    public void AFailedRunNotesHowTheGrantEndedButNotTheRunsOwnNormalEnd()
    {
        using var interaction = new Te3Interaction(() => input);
        interaction.Begin("run", userApproved: true);
        input++;
        Assert.Throws<InputRefusedException>(interaction.Require);
        var manifest = new RunManifest();

        RunExecutor.RecordFailure(manifest, new OperationCanceledException(), interaction.LastRevocation);

        var note = Assert.Single(manifest.DiagnosticNotes);
        Assert.Contains("grant revoked (focus-revoked, user-input)", note);

        interaction.Begin("run", userApproved: true);
        interaction.End();
        var ended = new RunManifest();
        RunExecutor.RecordFailure(ended, new InvalidOperationException("failed"), interaction.LastRevocation);
        Assert.Empty(ended.DiagnosticNotes);
    }

    [Fact]
    public async Task BeginRepliesWithHowThePreviousBatchEnded()
    {
        using var interaction = new Te3Interaction(() => input);
        interaction.Begin("first", userApproved: true);
        input++;
        Assert.Throws<InputRefusedException>(interaction.Require);

        var begun = await new Te3InteractionTool(interaction, Te3InteractionTool.Begin).ExecuteAsync(
            JsonSerializer.SerializeToElement(new { batch = "second", userApproved = true })
        );

        using var reply = JsonDocument.Parse(begun.Content[0].Text!);
        Assert.True(reply.RootElement.GetProperty("active").GetBoolean());
        Assert.Contains("user-input", reply.RootElement.GetProperty("lastRevocation").GetString());
    }
}
