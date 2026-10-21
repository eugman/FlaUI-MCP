using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

/// <summary>The consent grant the TE3 desktop tools require; physical input is simulated, nothing is hooked.</summary>
public sealed class InteractionTests
{
    private long input;

    private Te3Interaction Grant() => new(() => input);

    [Fact]
    public void InputIsRefusedUntilTheUserApprovesABatch()
    {
        using var interaction = Grant();
        var error = Assert.Throws<InvalidOperationException>(interaction.Require);
        Assert.Contains(Te3InteractionTool.Begin, error.Message);
        Assert.Contains("has not approved a batch", error.Message);
        var refusal = Assert.Throws<ArgumentException>(() =>
            interaction.Begin("Open Preferences", userApproved: false)
        );
        Assert.Contains("Ask the user to clear the machine", refusal.Message);
        Assert.Throws<ArgumentException>(() => interaction.Begin(" ", userApproved: true));
        Assert.False(interaction.Active);
    }

    [Fact]
    public void TheUsersOwnInputRevokesTheGrant()
    {
        using var interaction = Grant();
        interaction.Begin("Open Preferences", userApproved: true);
        interaction.Require();

        input++;

        var error = Assert.Throws<InvalidOperationException>(interaction.Require);
        Assert.Contains("keyboard or mouse", error.Message);
        Assert.False(interaction.Active);
        // Revoked for good: the count settling again does not bring it back.
        Assert.Throws<InvalidOperationException>(interaction.Require);
    }

    [Fact]
    public void EndingTheBatchRevokesIt()
    {
        using var interaction = Grant();
        interaction.Begin("Open Preferences", userApproved: true);
        interaction.End();
        Assert.Throws<InvalidOperationException>(interaction.Require);
        interaction.Begin("Open TOM Explorer", userApproved: true);
        Assert.True(interaction.Active);
    }

    [Fact]
    public void TheGuardChecksTheGrantBeforeEveryInputAndOnlyWhileHeld()
    {
        using var interaction = Grant();
        interaction.Begin("Resize panes", userApproved: true);
        var before = MutationGuard.BeforeMutation.Value;
        using (interaction.Guard())
        {
            MutationGuard.CheckPermission();
            input++;
            Assert.Throws<InvalidOperationException>(MutationGuard.CheckPermission);
        }

        Assert.Equal(before, MutationGuard.BeforeMutation.Value);
    }

    [Fact]
    public async Task NavigateRefusesBeforeAttachingWithoutAGrant()
    {
        var result = await new Te3Companion(null!, Te3Companion.Navigate, Grant()).ExecuteAsync(
            JsonSerializer.SerializeToElement(new { destination = "tom-explorer" })
        );
        Assert.True(result.IsError);
        var response = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal("permission", response.GetProperty("phase").GetString());
        Assert.Contains(Te3InteractionTool.Begin, response.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("close")]
    [InlineData("keep")]
    [InlineData("hide-pane")]
    [InlineData("apply-preset")]
    [InlineData("maximize-pane")]
    [InlineData("restore-pane")]
    [InlineData("resize")]
    public async Task LayoutChangesRefuseBeforeAttachingWithoutAGrant(string action)
    {
        var result = await new Te3Layout(null!, Grant()).ExecuteAsync(
            JsonSerializer.SerializeToElement(
                new
                {
                    action,
                    titles = new[] { "Properties" },
                    title = "Properties",
                    preset = FlaUI.Mcp.Te3.Te3PanePlan.AssistantModelReview,
                    width = 300,
                }
            )
        );
        Assert.True(result.IsError);
        Assert.Contains(Te3InteractionTool.Begin, result.Content[0].Text);
    }

    [Fact]
    public async Task TheBeginAndEndToolsDriveTheGrant()
    {
        using var interaction = Grant();
        var begin = new Te3InteractionTool(interaction, Te3InteractionTool.Begin);
        var refused = await begin.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { batch = "Open Preferences", userApproved = false })
        );
        Assert.True(refused.IsError);
        Assert.False(interaction.Active);

        await begin.ExecuteAsync(
            JsonSerializer.SerializeToElement(new { batch = "Open Preferences", userApproved = true })
        );
        Assert.True(interaction.Active);

        await new Te3InteractionTool(interaction, Te3InteractionTool.End).ExecuteAsync(null);
        Assert.False(interaction.Active);
        var error = Assert.Throws<InvalidOperationException>(interaction.Require);
        Assert.Contains("ended by te3_interaction_end.", error.Message);
    }

    [Fact]
    public void TheDesktopToolsSayWhatTheyRequire()
    {
        Assert.Contains(Te3InteractionTool.Begin, new Te3Companion(null!, Te3Companion.Navigate).Description);
        Assert.Contains(Te3InteractionTool.Begin, new Te3Layout(null!).Description);
        Assert.DoesNotContain("handoff", new Te3Companion(null!, Te3Companion.Navigate).Description);
    }

    [Fact]
    public void ASecondBatchIsRefusedWhileOneIsHeld()
    {
        using var interaction = Grant();
        interaction.Begin("Open Preferences", userApproved: true);

        var error = Assert.Throws<InvalidOperationException>(() =>
            interaction.Begin("Resize panes", userApproved: true)
        );

        Assert.Contains("End the current batch", error.Message);
        Assert.True(interaction.Active);
    }

    [Fact]
    public void AnObserverThatStoppedRevokesTheGrant()
    {
        var stopped = false;
        using var interaction = new Te3Interaction(() =>
            stopped ? throw new InvalidOperationException("hook gone") : input
        );
        interaction.Begin("Open Preferences", userApproved: true);
        interaction.Require();

        stopped = true;

        Assert.Throws<InvalidOperationException>(interaction.Require);
        Assert.False(interaction.Active);
    }

    [Fact]
    public async Task BeginWithoutAnExplicitApprovalIsRefused()
    {
        using var interaction = Grant();

        var result = await new Te3InteractionTool(interaction, Te3InteractionTool.Begin).ExecuteAsync(
            JsonSerializer.SerializeToElement(new { batch = "Open Preferences" })
        );

        Assert.True(result.IsError);
        Assert.Contains(Te3InteractionTool.Begin, result.Content[0].Text);
        Assert.False(interaction.Active);
    }

    [Fact]
    public void StoppingTheHostEndsTheGrant()
    {
        var interaction = Grant();
        interaction.Begin("Open Preferences", userApproved: true);

        interaction.Dispose();

        Assert.False(interaction.Active);
        var error = Assert.Throws<InvalidOperationException>(interaction.Require);
        Assert.Contains("the host stopped", error.Message);
    }

    // The test process's own id is never TE3, so each call stops at attach and nothing reaches the desktop.
    private static JsonElement Own(object arguments)
    {
        var node = JsonSerializer.SerializeToNode(arguments)!.AsObject();
        node["processId"] = Environment.ProcessId;
        return JsonSerializer.SerializeToElement(node);
    }

    private static string Phase(FlaUI.Mcp.McpToolResult result) =>
        JsonDocument.Parse(result.Content[0].Text!).RootElement.GetProperty("phase").GetString()!;

    [Fact]
    public async Task TheModelStateReadNeedsNoGrant()
    {
        var result = await new Te3Companion(null!, Te3Companion.ModelState, Grant()).ExecuteAsync(Own(new { }));

        Assert.True(result.IsError);
        Assert.Equal("attach", Phase(result));
        Assert.Contains("processId must identify TabularEditor3", result.Content[0].Text);
    }

    [Fact]
    public async Task NavigateUsesTheGrantItWasGiven()
    {
        using var interaction = Grant();
        interaction.Begin("Open TOM Explorer", userApproved: true);

        var result = await new Te3Companion(null!, Te3Companion.Navigate, interaction).ExecuteAsync(
            Own(new { destination = "tom-explorer" })
        );

        Assert.True(result.IsError);
        Assert.Equal("attach", Phase(result));
    }

    [Theory]
    [InlineData("list")]
    [InlineData("show-pane")]
    public async Task ReadOnlyLayoutActionsNeedNoGrant(string action)
    {
        var result = await new Te3Layout(null!, Grant(), readOnly: true).ExecuteAsync(
            Own(new { action, title = "Properties" })
        );

        Assert.True(result.IsError);
        Assert.DoesNotContain(Te3InteractionTool.Begin, result.Content[0].Text);
        Assert.Contains("Expected one visible TE3 document window", result.Content[0].Text);
    }

    [Fact]
    public async Task LayoutChangesUseTheGrantTheyWereGiven()
    {
        using var interaction = Grant();
        interaction.Begin("Resize panes", userApproved: true);

        var result = await new Te3Layout(null!, interaction).ExecuteAsync(
            Own(new { action = "hide-pane", title = "Properties" })
        );

        Assert.True(result.IsError);
        Assert.Contains("Expected one visible TE3 document window", result.Content[0].Text);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData(100, null, true)]
    [InlineData(null, 100, true)]
    public async Task APresetPlanUsesTheGivenWindowSizeOr1080p(int? width, int? height, bool tooSmall)
    {
        var arguments = new System.Text.Json.Nodes.JsonObject
        {
            ["action"] = "plan-preset",
            ["preset"] = FlaUI.Mcp.Te3.Te3PanePlan.AssistantModelReview,
        };
        if (width != null)
        {
            arguments["windowWidth"] = width;
        }

        if (height != null)
        {
            arguments["windowHeight"] = height;
        }

        var result = await new Te3Layout(null!, Grant(), readOnly: true).ExecuteAsync(
            JsonSerializer.SerializeToElement(arguments)
        );

        Assert.Equal(tooSmall, result.IsError == true);
    }
}
