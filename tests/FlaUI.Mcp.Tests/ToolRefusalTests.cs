using System.Runtime.CompilerServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit;

namespace FlaUI.Mcp.Tests;

/// <summary>
/// Input tools refuse before any input when arguments are wrong, the provider is blocked or the target is
/// not allowed, and report how far a partial dispatch got.
/// </summary>
[Collection(InputLeaseCollection.Name)]
public sealed class ToolRefusalTests
{
    private const string Ref = "w1e1";

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static AutomationElement Element() =>
        (AutomationElement)RuntimeHelpers.GetUninitializedObject(typeof(AutomationElement));

    private static (SessionManager Sessions, string Handle) Window()
    {
        var sessions = new SessionManager(null, ProcessPolicy.AllowAll);
        var handle = sessions.PublishWindow(null, FakeDesktop.TargetHwnd, FakeDesktop.TargetPid, FakeDesktop.Target);
        return (sessions, handle);
    }

    /// <summary>A registry holding <see cref="Ref"/> for a process whose provider is still blocked.</summary>
    private static ElementRegistry BlockedRef(out PendingInvokeTracker tracker)
    {
        var refs = new ElementRegistry();
        Assert.Equal(Ref, refs.Register("w1", Element()));
        refs.SetWindowProcessId("w1", FakeDesktop.TargetPid);
        tracker = new PendingInvokeTracker();
        tracker.Begin(FakeDesktop.TargetPid, "Invoke on 'Open...'");
        return refs;
    }

    private static void AssertBlocked(McpToolResult result)
    {
        Assert.True(result.IsError);
        Assert.Contains("Invoke on 'Open...'", result.Content[0].Text);
        Assert.Equal("not-dispatched", result.Outcome!.Dispatch);
        Assert.Equal("pending", result.Outcome.Provider);
    }

    private static Task<McpToolResult> Run(ITool tool, object? arguments)
    {
        var registry = new ToolRegistry();
        registry.RegisterTool(tool);
        return registry.ExecuteToolAsync(tool.Name, arguments == null ? null : Args(arguments));
    }

    [Theory]
    [InlineData(null, null, "Provide either chord or keys.")]
    [InlineData("Enter", "Tab", "Provide either chord or keys, not both.")]
    public async Task SendKeysNeedsExactlyOneOfChordOrKeys(string? chord, string? key, string message)
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var keys = key == null ? null : new[] { key };

        var result = await Run(new SendKeysTool(new ElementRegistry()), new { chord, keys });

        Assert.True(result.IsError);
        Assert.Equal(message, result.Content[0].Text);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task SendKeysToAWindowReportsTheChordSent()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;

        var result = await Run(
            new SendKeysTool(new ElementRegistry(), sessions: sessions),
            new { handle, chord = "Enter" }
        );

        Assert.NotEqual(true, result.IsError);
        Assert.StartsWith("Sent keys Enter to focused element", result.Content[0].Text);
        Assert.Equal(1, desktop.Dispatched);
    }

    [Fact]
    public async Task SendKeysToARefWhoseProviderIsBlockedIsRefused()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var refs = BlockedRef(out var tracker);

        AssertBlocked(await Run(new SendKeysTool(refs, tracker), new { @ref = Ref, chord = "Enter" }));
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task SendKeysToADeniedWindowIsRefused()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;
        var policy = new ProcessPolicy(["some-app-that-is-not-running"]);

        var result = await Run(
            new SendKeysTool(new ElementRegistry(), processPolicy: policy, sessions: sessions),
            new { handle, chord = "Enter" }
        );

        Assert.True(result.IsError);
        Assert.Contains("Target process", result.Content[0].Text);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task TypeNeedsText()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var result = await Run(new TypeTool(new ElementRegistry()), new { });

        Assert.True(result.IsError);
        Assert.Equal("Missing required argument: text", result.Content[0].Text);
    }

    [Fact]
    public async Task TypeIntoAWindowSendsEachLineThenSubmits()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;

        var result = await Run(
            new TypeTool(new ElementRegistry(), sessions: sessions),
            new
            {
                handle,
                text = "a\nb",
                submit = true,
            }
        );

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("Typed and submitted \"a\nb\" into focused element", result.Content[0].Text);
        // Line, Enter, line, submitting Enter.
        Assert.Equal(4, desktop.Dispatched);
    }

    [Fact]
    public async Task TypeThatLosesFocusAfterTheFirstLineSaysWhichLineMayBePartlyTyped()
    {
        var desktop = new FakeDesktop { FocusLostAfter = 1 };
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;

        var result = await Run(new TypeTool(new ElementRegistry(), sessions: sessions), new { handle, text = "a\nb" });

        Assert.True(result.IsError);
        Assert.EndsWith("Typed 1 of 2 line(s); line 2 may be partly typed.", result.Content[0].Text);
        Assert.Equal(1, desktop.Dispatched);
        Assert.Equal("partial", result.Outcome!.Dispatch);
    }

    [Fact]
    public async Task TypeRefusedBeforeAnyInputSaysNothingIsPartlyTyped()
    {
        var desktop = new FakeDesktop { ForegroundWindow = FakeDesktop.OtherHwnd, ActivationAllowed = false };
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;

        var result = await Run(new TypeTool(new ElementRegistry(), sessions: sessions), new { handle, text = "a\nb" });

        Assert.True(result.IsError);
        Assert.EndsWith("Typed 0 of 2 line(s).", result.Content[0].Text);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task TypeIntoARefWhoseProviderIsBlockedIsRefused()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var refs = BlockedRef(out var tracker);

        AssertBlocked(await Run(new TypeTool(refs, tracker), new { @ref = Ref, text = "a" }));
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task TypeIntoADeniedWindowIsRefused()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;
        var policy = new ProcessPolicy(["some-app-that-is-not-running"]);

        var result = await Run(
            new TypeTool(new ElementRegistry(), processPolicy: policy, sessions: sessions),
            new { handle, text = "a" }
        );

        Assert.True(result.IsError);
        Assert.Contains("Target process", result.Content[0].Text);
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public async Task TypeByHandleNeedsASessionManager()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var result = await Run(new TypeTool(new ElementRegistry()), new { handle = "w1", text = "a" });

        Assert.True(result.IsError);
        Assert.Equal("Window-handle input requires a session manager.", result.Content[0].Text);
    }

    [Theory]
    [InlineData(null, "x", "Missing required argument: ref")]
    [InlineData(Ref, null, "Missing required argument: value")]
    public async Task FillNeedsARefAndAValue(string? reference, string? value, string message)
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var result = await Run(new FillTool(new ElementRegistry()), new { @ref = reference, value });

        Assert.True(result.IsError);
        Assert.Equal(message, result.Content[0].Text);
    }

    [Fact]
    public async Task FillOfARefWhoseProviderIsBlockedIsRefused()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var refs = BlockedRef(out var tracker);

        AssertBlocked(await Run(new FillTool(refs, tracker), new { @ref = Ref, value = "x" }));
    }

    [Fact]
    public async Task ClickNeedsARef()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var result = await Run(new ClickTool(new ElementRegistry()), new { });

        Assert.True(result.IsError);
        Assert.Equal("Missing required argument: ref", result.Content[0].Text);
    }

    [Fact]
    public async Task ClickOfARefWhoseProviderIsBlockedIsRefused()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        var refs = BlockedRef(out var tracker);

        AssertBlocked(await Run(new ClickTool(refs, tracker), new { @ref = Ref }));
    }

    [Fact]
    public async Task MouseNeedsAHandleOrRef()
    {
        using var offlineDesktop = new FakeDesktop().Install();
        using var sessions = Window().Sessions;

        var result = await Run(
            new MouseTool(new ElementRegistry(), new PendingInvokeTracker(), sessions),
            new { action = "park" }
        );

        Assert.True(result.IsError);
        Assert.Equal("handle or ref required", result.Content[0].Text);
    }

    [Fact]
    public async Task MouseOnAWindowWhoseProviderIsBlockedIsRefused()
    {
        var desktop = new FakeDesktop();
        using var installed = desktop.Install();
        var (sessions, handle) = Window();
        using var _ = sessions;
        var tracker = new PendingInvokeTracker();
        tracker.Begin(FakeDesktop.TargetPid, "Invoke on 'Open...'");

        AssertBlocked(
            await Run(new MouseTool(new ElementRegistry(), tracker, sessions), new { action = "park", handle })
        );
        Assert.Equal(0, desktop.Dispatched);
    }

    [Fact]
    public void AnUnknownOrMalformedRefHasNoWindow()
    {
        var refs = new ElementRegistry();
        refs.Register("w1", Element());
        // Registered under an empty window name, so only the separator check refuses "e1".
        Assert.Equal("e1", refs.Register("", Element()));

        Assert.Throws<ArgumentException>(() => refs.WindowForRef("w1e2"));
        Assert.Throws<ArgumentException>(() => refs.WindowForRef("e1"));
        Assert.Equal("w1", refs.WindowForRef(Ref));
    }

    [Theory]
    [InlineData("e1")]
    [InlineData("w1")]
    [InlineData("")]
    public void AMalformedRefHasNoProcess(string reference)
    {
        var refs = new ElementRegistry();
        refs.SetWindowProcessId("w1", FakeDesktop.TargetPid);
        // A ref with nothing before its separator must not resolve to the window named "".
        refs.SetWindowProcessId("", FakeDesktop.TargetPid);

        Assert.Equal(0, refs.GetProcessIdForRef(reference));
    }

    [Fact]
    public void InputThroughARefWithoutProcessIdentityIsRefused()
    {
        var refs = new ElementRegistry();
        refs.Register("w1", Element());
        refs.SetWindowProcessId("w1", FakeDesktop.TargetPid);

        var error = Assert.Throws<InvalidOperationException>(() => refs.InputForRef(Ref));

        Assert.Contains("establish input ownership", error.Message);
    }
}
