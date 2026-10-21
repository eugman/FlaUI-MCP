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
        Assert.Throws<InputRefusedException>(MutationGuard.CheckPermission);

        interaction.Begin("Click Save", userApproved: true);
        MutationGuard.CheckPermission();

        input++;
        Assert.Throws<InputRefusedException>(MutationGuard.CheckPermission);
    }

    [Theory]
    [InlineData("windows_focus")]
    [InlineData("windows_close")]
    public async Task CoresFocusAndCloseAreRefusedWithoutTheGrant(string tool)
    {
        // A real window that is never shown, and a fake desktop: Core's own permission check refuses before any
        // focus or WM_CLOSE, so nothing reaches the desktop, real or fake.
        using var allowAll = new AutomationHost(ProcessPolicy.AllowAll, includeDesktopTools: true);
        using var owner = new Te3Interaction(() => 0);
        Te3McpServer.Install(allowAll, owner);
        using var form = new HiddenForm();
        var handle = Te3Layout.Bind(allowAll, form.Handle, Environment.ProcessId);
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        var result = await allowAll.Tools.ExecuteToolAsync(tool, JsonSerializer.SerializeToElement(new { handle }));

        Assert.True(result.IsError);
        Assert.Contains(Te3InteractionTool.Begin, result.Content[0].Text);
        Assert.Equal(0, result.Outcome?.InputsSent);
        Assert.Equal(0, desktop.FocusRequests + desktop.CloseRequests);
    }

    [Fact]
    public async Task CoresLaunchIsRefusedWithoutTheGrant()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();

        var result = await host.Tools.ExecuteToolAsync(
            "windows_launch",
            JsonSerializer.SerializeToElement(new { app = "TabularEditor3.exe" })
        );

        Assert.True(result.IsError);
        Assert.Contains(Te3InteractionTool.Begin, result.Content[0].Text);
        Assert.Equal(0, desktop.StartRequests);
    }

    [Theory]
    [InlineData("windows_focus")]
    [InlineData("windows_close")]
    [InlineData("windows_launch")]
    public void TheServerKeepsCoresOwnWindowTools(string tool)
    {
        // Core checks the grant in focus, close and launch itself, and its focus and close watch for a pending
        // provider; a TE3 wrapper built from the bare tools would lose that.
        using var plain = new AutomationHost(new ProcessPolicy(["TabularEditor3"]), includeDesktopTools: true);
        var core = plain.Tools.GetToolDefinitions().Single(t => t.Name == tool);

        var served = host.Tools.GetToolDefinitions().Single(t => t.Name == tool);

        Assert.Equal(core.Description, served.Description);
        Assert.Equal(core.Annotations, served.Annotations);
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

    /// <summary>A form that is never shown, owned by its own message loop so UI Automation can answer for it.</summary>
    private sealed class HiddenForm : IDisposable
    {
        private readonly Thread thread;
        private readonly ManualResetEventSlim created = new();
        private System.Windows.Forms.ApplicationContext? context;

        public HiddenForm()
        {
            thread = new Thread(() =>
            {
                using var form = new System.Windows.Forms.Form { Text = "fla grant test" };
                Handle = form.Handle;
                context = new System.Windows.Forms.ApplicationContext();
                created.Set();
                System.Windows.Forms.Application.Run(context);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            created.Wait();
        }

        public nint Handle { get; private set; }

        public void Dispose()
        {
            context?.ExitThread();
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AClickThroughTheRegistryIsRefusedWithoutTheGrant(bool physical)
    {
        // A real ref to a window that is never shown; the grant check comes before any focus or input.
        using var allowAll = new AutomationHost(ProcessPolicy.AllowAll, includeDesktopTools: true);
        using var owner = new Te3Interaction(() => 0);
        Te3McpServer.Install(allowAll, owner);
        using var form = new HiddenForm();
        var handle = Te3Layout.Bind(allowAll, form.Handle, Environment.ProcessId);
        var reference = allowAll.Elements.Register(handle, allowAll.Sessions.GetWindow(handle)!);

        var result = await allowAll.Tools.ExecuteToolAsync(
            "windows_click",
            JsonSerializer.SerializeToElement(new { @ref = reference, physical })
        );

        Assert.True(result.IsError);
        Assert.Contains(Te3InteractionTool.Begin, string.Join(" ", result.Content.Select(c => c.Text)));
    }
}
