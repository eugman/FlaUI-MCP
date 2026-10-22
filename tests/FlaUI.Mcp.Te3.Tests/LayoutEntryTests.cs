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

    /// <summary>A docked layout: panes close when their Close is pressed, and maximize flips the button.</summary>
    private sealed class FakeSurface : ILayoutSurface
    {
        private readonly InputTally inputs;
        private readonly List<string> panes;

        public FakeSurface(InputTally inputs, params string[] panes)
        {
            this.inputs = inputs;
            this.panes = [.. panes];
        }

        /// <summary>Open document tabs; Close removes one unless it is in <see cref="Stuck"/>.</summary>
        public List<string> Documents { get; } = [];

        public List<string> Presses { get; } = [];

        /// <summary>Extra top-level windows of the TE3 process, read at each check.</summary>
        public List<Win32WindowInfo> Extra { get; } = [];

        /// <summary>Runs after each press.</summary>
        public Action<string>? AfterPress { get; set; }

        /// <summary>When false, Maximize and Restore are pressed but the title bar keeps its button.</summary>
        public bool ButtonsFlip { get; set; } = true;

        /// <summary>Panes Close does not remove.</summary>
        public HashSet<string> Stuck { get; } = [];

        /// <summary>A press that fails after its input went out.</summary>
        public string? FailingPress { get; set; }

        private readonly HashSet<string> maximized = [];

        public Rectangle Origin => new(0, 30, 1920, 1110);

        public nint MainWindow => Main;

        public IReadOnlyList<LayoutPart> Parts() =>
            [
                .. panes.Select((t, i) => new LayoutPart(t, "pane", new Rectangle(0, 61 + i * 100, 500, 100))),
                .. Documents.Select(t => new LayoutPart(t, "document", new Rectangle(680, 61, 760, 947))),
            ];

        public Rectangle? BoundsOf(nint hwnd) => new Rectangle(200, 200, 400, 600);

        /// <summary>Class and owner of the extra windows; the floating Dependencies window is unowned WinForms.</summary>
        public Dictionary<nint, WindowShape> Shapes { get; } = [];

        public WindowShape? ShapeOf(nint hwnd) => Shapes.TryGetValue(hwnd, out var shape) ? shape : null;

        public bool ButtonShown(string title, string button) =>
            Documents.Contains(title) ? button == "Close" : PaneShows(title, button);

        private bool PaneShows(string title, string button) =>
            panes.Contains(title) && (button == "Close" || (button == "Restore") == maximized.Contains(title));

        public async Task Press(string title, string button)
        {
            await inputs.Invoke(
                () => Task.FromResult(Sent($"pressed {button}", FailingPress == title)),
                "windows_click"
            );
            Presses.Add($"{button} {title}");
            if (button == "Close" && !Stuck.Contains(title))
            {
                panes.Remove(title);
                Documents.Remove(title);
            }
            else if (ButtonsFlip && button == "Maximize")
            {
                maximized.Add(title);
            }
            else if (ButtonsFlip && button == "Restore")
            {
                maximized.Remove(title);
            }

            AfterPress?.Invoke(title);
            readsSincePress = readsSincePress < 0 ? 0 : readsSincePress;
        }

        /// <summary>A window that opens after the first press, once this many window reads followed it.</summary>
        public (Win32WindowInfo Window, int AfterReads)? LateDialog { get; set; }

        private int readsSincePress = -1;

        public IReadOnlyList<Win32WindowInfo> Windows()
        {
            var late =
                LateDialog is { } dialog && readsSincePress >= 0 && ++readsSincePress > dialog.AfterReads
                    ? new[] { dialog.Window }
                    : [];
            return [new(Main, "Model - Tabular Editor 3", 7, true, false, false), .. Extra, .. late];
        }

        public Task Delay(int ms) => Task.CompletedTask;

        /// <summary>Floating windows whose own Close leaves them open.</summary>
        public HashSet<nint> StuckWindows { get; } = [];

        public async Task CloseWindow(nint hwnd)
        {
            var window = Extra.Single(w => w.Hwnd == hwnd);
            await inputs.Invoke(() => Task.FromResult(Sent("closed", false)), "windows_close");
            Presses.Add("CloseWindow " + window.Title);
            if (!StuckWindows.Contains(hwnd))
            {
                Extra.Remove(window);
            }
        }
    }

    // A Core click result that sent one input; failed when asked, after the input went out.
    private static McpToolResult Sent(string text, bool failed) =>
        new()
        {
            IsError = failed,
            Content = [new() { Text = text }],
            Outcome = new ToolOutcome(ToolOutcome.DispatchFor(failed, 1), "not-tracked")
            {
                Status = failed ? "failed" : "ok",
                InputsSent = 1,
            },
        };

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
        Assert.Equal("paused_after_failure", refused.Outcome?.Failure?.Code);
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
