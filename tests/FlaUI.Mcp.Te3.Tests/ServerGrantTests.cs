using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// In the TE3 server the user's grant gates every tool that changes the desktop, not only the TE3 ones. Nothing is
/// sent: each mutating call here is refused before it runs.
/// </summary>
public sealed class ServerGrantTests : IDisposable
{
    private readonly AutomationHost host = new(new ProcessPolicy(["TabularEditor3"]), includeDesktopTools: true);
    private long input;
    private readonly Te3Interaction interaction;

    public ServerGrantTests()
    {
        interaction = new Te3Interaction(() => input);
        Te3McpServer.Install(host, interaction);
    }

    public void Dispose()
    {
        MutationGuard.BeforeMutation.Value = null;
        interaction.Dispose();
        host.Dispose();
    }

    [Fact]
    public void EveryCoreMutationNeedsTheGrantAndTheUsersInputRevokesIt()
    {
        Assert.Throws<InvalidOperationException>(MutationGuard.CheckPermission);

        interaction.Begin("Click Save", userApproved: true);
        MutationGuard.CheckPermission();

        input++;
        Assert.Throws<InvalidOperationException>(MutationGuard.CheckPermission);
    }

    [Theory]
    [InlineData("windows_focus")]
    [InlineData("windows_close")]
    [InlineData("windows_launch")]
    public async Task WindowToolsThatSkipCoresCheckAreRefusedWithoutTheGrant(string tool)
    {
        var result = await host.Tools.ExecuteToolAsync(
            tool,
            JsonSerializer.SerializeToElement(new { handle = "w-none", app = "fla-test-no-such-app.exe" })
        );

        Assert.True(result.IsError);
        Assert.Contains(Te3InteractionTool.Begin, result.Content[0].Text);
    }

    [Fact]
    public async Task ReadingNeedsNoGrant()
    {
        var result = await host.Tools.ExecuteToolAsync(
            "windows_list_windows",
            JsonSerializer.SerializeToElement(new { })
        );

        Assert.DoesNotContain(Te3InteractionTool.Begin, string.Join(" ", result.Content.Select(c => c.Text)));
    }
}
