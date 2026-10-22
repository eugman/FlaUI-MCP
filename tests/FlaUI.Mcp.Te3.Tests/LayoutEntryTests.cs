using System.Drawing;
using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>
/// te3_layout through its entry point on a scripted layout: every press is counted, nothing reaches a desktop. These
/// fail when the tool stops calling its checks, not only when a check itself is wrong.
/// </summary>
public sealed class LayoutEntryTests : IDisposable
{
    private const nint Main = 0x10;

    private readonly Te3Interaction interaction = new(() => 0);

    public LayoutEntryTests() => interaction.Begin("arrange TE3", userApproved: true);

    public void Dispose() => interaction.Dispose();

    private FakeSurface? surface;

    private Te3Layout Tool(Func<InputTally, FakeSurface> layout, bool readOnly = false) =>
        new(
            null!,
            interaction,
            readOnly,
            (_, inputs) =>
            {
                surface = layout(inputs);
                return surface;
            }
        );

    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);

    private static FakeSurface Standard(InputTally inputs) => new(inputs, "AI Assistant", "TOM Explorer", "Properties");

    [Fact]
    public async Task KeepWithATypoPressesNothing()
    {
        var result = await Tool(Standard).ExecuteAsync(Args(new { action = "keep", titles = new[] { "TOM Explorr" } }));

        Assert.Equal(true, result.IsError);
        Assert.Empty(surface!.Presses);
        Assert.Equal(0, result.Outcome?.InputsSent);
    }

    [Fact]
    public async Task KeepClosesEveryOtherPane()
    {
        var result = await Tool(Standard)
            .ExecuteAsync(Args(new { action = "keep", titles = new[] { "TOM Explorer" } }));

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(["Close AI Assistant", "Close Properties"], surface!.Presses);
    }

    [Fact]
    public async Task AMaximizeThatChangedNothingFailsAfterItsOneInput()
    {
        var tool = Tool(inputs => new FakeSurface(inputs, "Properties") { ButtonsFlip = false });

        var result = await tool.ExecuteAsync(Args(new { action = "maximize-pane", title = "Properties" }));

        Assert.Equal(true, result.IsError);
        Assert.Contains("still shows Maximize", result.Content.Single().Text);
        Assert.Equal(1, result.Outcome?.InputsSent);
    }

    [Fact]
    public async Task AMaximizeIsReadBack()
    {
        var result = await Tool(inputs => new FakeSurface(inputs, "Properties"))
            .ExecuteAsync(Args(new { action = "maximize-pane", title = "Properties" }));

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("readBack", result.Content.Single().Text);
    }

    [Fact]
    public async Task AFloatingPaneOpenBeforeTheCallIsNotTakenForADialog()
    {
        var tool = Tool(inputs =>
        {
            var layout = Standard(inputs);
            layout.Extra.Add(new(0x30, "Dependencies", 7, true, false, false));
            layout.Shapes[0x30] = new WindowShape("WindowsForms10.Window.8.app.0.1e2bef7_r3_ad1", 0);
            return layout;
        });

        var result = await tool.ExecuteAsync(Args(new { action = "close", titles = new[] { "AI Assistant" } }));

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(["Close AI Assistant"], surface!.Presses);
    }

    [Fact]
    public async Task AnUnknownWindowAlreadyOpenIsRefusedBeforeAnyInput()
    {
        var tool = Tool(inputs =>
        {
            var layout = Standard(inputs);
            layout.Extra.Add(new(0x40, "Save changes?", 7, true, false, false));
            return layout;
        });

        var result = await tool.ExecuteAsync(Args(new { action = "close", titles = new[] { "AI Assistant" } }));

        Assert.Equal(true, result.IsError);
        Assert.Contains("Save changes?", result.Content.Single().Text);
        Assert.Empty(surface!.Presses);
    }

    [Fact]
    public async Task ADialogTheFirstCloseOpenedLateStopsTheNextClose()
    {
        // The dialog appears only after the first close's one-second watch ended. The call's one snapshot still
        // shows it as new before the second click, so that click is never sent.
        var tool = Tool(inputs =>
        {
            var layout = Standard(inputs);
            layout.LateDialog = (new(0x40, "Save changes?", 7, true, false, false), 6);
            return layout;
        });

        var result = await tool.ExecuteAsync(Args(new { action = "keep", titles = new[] { "TOM Explorer" } }));

        Assert.Equal(true, result.IsError);
        Assert.Contains("Save changes?", result.Content.Single().Text);
        Assert.Equal(["Close AI Assistant"], surface!.Presses);
    }

    [Fact]
    public async Task AFailureAfterInputPausesUntilTheLayoutIsReadAgain()
    {
        var tool = Tool(inputs => new FakeSurface(inputs, "AI Assistant", "Properties")
        {
            FailingPress = "AI Assistant",
        });
        var read = Tool(Standard, readOnly: true);

        var failed = await tool.ExecuteAsync(Args(new { action = "close", titles = new[] { "AI Assistant" } }));
        var refused = await tool.ExecuteAsync(Args(new { action = "close", titles = new[] { "Properties" } }));

        Assert.Equal(true, failed.IsError);
        Assert.Equal(true, refused.IsError);
        Assert.Equal("paused-after-failure", refused.Outcome?.Failure?.Code);
        Assert.Empty(surface!.Presses);

        Assert.NotEqual(true, (await read.ExecuteAsync(Args(new { action = "list" }))).IsError);
        var resumed = await tool.ExecuteAsync(Args(new { action = "close", titles = new[] { "Properties" } }));

        Assert.NotEqual(true, resumed.IsError);
        Assert.Equal(["Close Properties"], surface!.Presses);
    }

    [Fact]
    public async Task AFailureBeforeAnyInputDoesNotPause()
    {
        var tool = Tool(Standard);

        await tool.ExecuteAsync(Args(new { action = "keep", titles = new[] { "TOM Explorr" } }));
        var next = await tool.ExecuteAsync(Args(new { action = "close", titles = new[] { "AI Assistant" } }));

        Assert.NotEqual(true, next.IsError);
    }

    [Fact]
    public void WhilePausedTheGrantRefusesWithItsOwnCodeAndAnObservationLiftsIt()
    {
        interaction.Pause("an earlier call failed after sending input");

        var error = Assert.Throws<InputRefusedException>(interaction.Require);

        Assert.Equal(Te3Interaction.PausedAfterFailure, error.Refusal.Code);
        interaction.Observed();
        interaction.Require();
    }

    private static FakeSurface WithDocuments(InputTally inputs)
    {
        var layout = Standard(inputs);
        layout.Documents.AddRange(["Expression Editor", "DAX Query 1"]);
        return layout;
    }

    [Fact]
    public async Task CloseDocumentPressesTheTabsCloseAndReadsItBack()
    {
        var result = await Tool(WithDocuments)
            .ExecuteAsync(Args(new { action = "close-document", title = "DAX Query 1" }));

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(["Close DAX Query 1"], surface!.Presses);
    }

    [Fact]
    public async Task CloseDocumentRefusesAPaneOrAnUnknownTitleBeforeInput()
    {
        var pane = await Tool(WithDocuments)
            .ExecuteAsync(Args(new { action = "close-document", title = "Properties" }));
        Assert.Equal(true, pane.IsError);
        Assert.Empty(surface!.Presses);

        var unknown = await Tool(WithDocuments)
            .ExecuteAsync(Args(new { action = "close-document", title = "DAX Query 9" }));
        Assert.Equal(true, unknown.IsError);
        Assert.Contains("DAX Query 1", unknown.Content.Single().Text);
        Assert.Empty(surface!.Presses);
    }

    [Fact]
    public async Task ADocumentThatStaysOpenIsAFailure()
    {
        var tool = Tool(inputs =>
        {
            var layout = WithDocuments(inputs);
            layout.Stuck.Add("DAX Query 1");
            return layout;
        });

        var result = await tool.ExecuteAsync(Args(new { action = "close-document", title = "DAX Query 1" }));

        Assert.Equal(true, result.IsError);
        Assert.Contains("still open", result.Content.Single().Text);
    }

    [Fact]
    public async Task ListShowsFloatingWindowsBesideTheDockedParts()
    {
        // The floating Dependencies pane is a top-level window of its own (owner 0), not inside the main window.
        var tool = Tool(
            inputs =>
            {
                var layout = Standard(inputs);
                layout.Extra.Add(new(0x30, "Dependencies", 7, true, false, false));
                layout.Shapes[0x30] = new WindowShape("WindowsForms10.Window.8.app.0.1e2bef7_r3_ad1", 0);
                layout.Extra.Add(new(0x40, "Save changes?", 7, true, false, false));
                return layout;
            },
            readOnly: true
        );

        var listed = JsonDocument.Parse(
            (await tool.ExecuteAsync(Args(new { action = "list" }))).Content.Single().Text!
        );

        Assert.Contains(
            listed.RootElement.EnumerateArray(),
            p => p.GetProperty("title").GetString() == "Dependencies" && p.GetProperty("kind").GetString() == "floating"
        );
        // A dialog is a window of its own too, but not a floating pane.
        Assert.Contains(
            listed.RootElement.EnumerateArray(),
            p => p.GetProperty("title").GetString() == "Save changes?" && p.GetProperty("kind").GetString() == "dialog"
        );
    }

    [Fact]
    public async Task AnUnexpectedErrorThatEscapesTheCallStillPauses()
    {
        // Respond turns the usual failures into a reply; anything else (a COM error mid-call) escaped before the
        // pause, after input may have gone out.
        var tool = new Te3Layout(
            null!,
            interaction,
            attach: (_, inputs) =>
                throw new System.Runtime.InteropServices.COMException("provider gone", unchecked((int)0x80040201))
        );

        await Assert.ThrowsAsync<System.Runtime.InteropServices.COMException>(() =>
            tool.ExecuteAsync(Args(new { action = "close", titles = new[] { "AI Assistant" } }))
        );

        Assert.Equal(
            Te3Interaction.PausedAfterFailure,
            Assert.Throws<InputRefusedException>(interaction.Require).Refusal.Code
        );
    }

    [Fact]
    public async Task CloseClosesAFloatingPaneThroughItsOwnWindowAndReadsItBack()
    {
        // Live: close ["Dependencies"] first ignored the floating window, then only pointed at windows_close.
        var tool = Tool(inputs =>
        {
            var layout = Standard(inputs);
            layout.Extra.Add(new(0x30, "Dependencies", 7, true, false, false));
            layout.Shapes[0x30] = new WindowShape("WindowsForms10.Window.8.app.0.1e2bef7_r3_ad1", 0);
            return layout;
        });

        var result = await tool.ExecuteAsync(Args(new { action = "close", titles = new[] { "Dependencies" } }));
        var reply = JsonDocument.Parse(result.Content.Single().Text!).RootElement;

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(["CloseWindow Dependencies"], surface!.Presses);
        Assert.Contains("Dependencies", reply.GetProperty("closed").EnumerateArray().Select(e => e.GetString()));
        Assert.Empty(reply.GetProperty("unresolved").EnumerateArray());
        Assert.Contains("windows_close", reply.GetProperty("floatingClosedWith").GetString());
    }

    [Fact]
    public async Task AFloatingPaneThatStaysOpenIsUnresolved()
    {
        var tool = Tool(inputs =>
        {
            var layout = Standard(inputs);
            layout.Extra.Add(new(0x30, "Dependencies", 7, true, false, false));
            layout.Shapes[0x30] = new WindowShape("WindowsForms10.Window.8.app.0.1e2bef7_r3_ad1", 0);
            layout.StuckWindows.Add(0x30);
            return layout;
        });

        var result = await tool.ExecuteAsync(Args(new { action = "close", titles = new[] { "Dependencies" } }));
        var reply = JsonDocument.Parse(result.Content.Single().Text!).RootElement;

        Assert.Contains("Dependencies", reply.GetProperty("unresolved").EnumerateArray().Select(e => e.GetString()));
    }
}
