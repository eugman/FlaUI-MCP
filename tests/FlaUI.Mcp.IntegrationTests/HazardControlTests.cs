using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit.Abstractions;
using static FlaUI.Mcp.IntegrationTests.PointerTestSupport;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// The WinForms app's Hazards tab: controls where the wrong input does damage. Each test checks the app's own
/// state, not only the reply, so a tool that sends input to the wrong place fails here.
/// </summary>
[Collection("TestApps")]
public class HazardControlTests(TestAppFixture fixture, ITestOutputHelper output)
{
    private string Handle => fixture.WinFormsHandle;

    private (AutomationElement Element, string Ref) Hazard(ElementSelector selector, ElementSelector? within = null)
    {
        SelectTab(fixture, Handle, "HazardsTab");
        return Ref(fixture, Handle, selector, within);
    }

    private string Label(string automationId) =>
        Resolve(fixture, Handle, new(AutomationId: automationId)).Properties.Name.ValueOrDefault ?? "";

    [Fact]
    public async Task ClickingTheRenameTreeNeverStartsARename()
    {
        // A click on the selected row of a LabelEdit tree starts inline rename, and the next key renames the row.
        // windows_click selects through SelectionItem, and leaves an already-selected row alone.
        var (_, row) = Hazard(new(Name: "Rename Me", ControlType: "TreeItem"), new(AutomationId: "RenameTreeView"));
        var click = new ClickTool(fixture.Elements);

        var first = await fixture.CallTool(click, new { @ref = row });
        var second = await fixture.CallTool(click, new { @ref = row });
        output.WriteLine($"{first}\n{second}");

        Assert.Equal("Rename Me is already selected; nothing was clicked", second);
        // The tree starts the edit a double-click time after the click, so watch for longer than that.
        Assert.True(
            StaysTrue(() => Label("RenameStatusLabel") == "Not renaming", windowMs: 1500),
            $"The tree started a rename: {Label("RenameStatusLabel")}"
        );
    }

    [Fact]
    public async Task ATreeThatIgnoresSelectIsNotReportedAsSelected()
    {
        var (_, row) = Hazard(
            new(Name: "Ignores Select", ControlType: "TreeItem"),
            new(AutomationId: "StubbornTreeView")
        );

        var reply = await fixture.CallTool(new ClickTool(fixture.Elements), new { @ref = row });
        output.WriteLine(reply);

        Assert.DoesNotContain("Selected Ignores Select", reply);
        Assert.Contains("Select changed nothing", reply);
        Assert.NotEqual("Selected: Ignores Select", Label("StubbornStatusLabel"));
    }

    [Fact]
    public async Task AClickUnderASiblingOverlayIsRefusedAndPressesNothing()
    {
        // The label sits over the whole button, so the press would land on the label at any point of it.
        var (_, button) = Hazard(new(AutomationId: "CoveredButton"));
        var before = Label("CoveredClicksLabel");

        var result = await Run(new ClickTool(fixture.Elements), new { @ref = button, physical = true });
        output.WriteLine(Text(result));

        Assert.True(result.IsError);
        Assert.Contains("is covered by", Text(result));
        Assert.Contains("no input sent", Text(result));
        Assert.True(StaysTrue(() => Label("CoveredClicksLabel") == before), "The covered button was pressed.");
    }

    [Fact]
    public async Task ADragThatWouldDropUnderASiblingOverlayIsRefused()
    {
        // The drop point is hit-tested like a press: the label over the button would take the release.
        var (_, start) = Hazard(new(AutomationId: "DecoyBox"));
        var (_, covered) = Hazard(new(AutomationId: "CoveredButton"));
        var before = Label("CoveredClicksLabel");

        var result = await Run(
            new MouseTool(fixture.Elements, new PendingInvokeTracker(), fixture.Session),
            new
            {
                action = "drag",
                handle = Handle,
                @ref = start,
                toRef = covered,
            }
        );
        output.WriteLine(Text(result));

        Assert.True(result.IsError);
        Assert.Contains("is covered by", Text(result));
        Assert.True(StaysTrue(() => Label("CoveredClicksLabel") == before), "The covered button was pressed.");
    }

    [Fact]
    public async Task FillRefusesAControlWithNoValuePatternThatDoesNotTakeFocus()
    {
        // Focus starts in the decoy: a fill that sent Ctrl+A and the text anyway would replace the decoy's text.
        var (decoy, decoyRef) = Hazard(new(AutomationId: "DecoyBox"));
        await fixture.CallTool(new ClickTool(fixture.Elements), new { @ref = decoyRef, physical = true });
        Assert.True(WaitFor(() => decoy.Properties.HasKeyboardFocus.ValueOrDefault), "The decoy did not take focus.");
        var (pane, paneRef) = Hazard(new(AutomationId: "NoValuePane"));
        Assert.False(pane.Patterns.Value.IsSupported, "Precondition: the pane must have no Value pattern.");

        var result = await Run(new FillTool(fixture.Elements), new { @ref = paneRef, value = "typed" });
        output.WriteLine(Text(result));

        Assert.True(result.IsError);
        Assert.Contains("did not receive verified keyboard focus", Text(result));
        Assert.True(
            StaysTrue(() => decoy.Patterns.Value.Pattern.Value.ValueOrDefault == "keep me"),
            "The fill's keys reached the decoy."
        );
    }

    [Fact]
    public async Task FillReachesAControlWithNoValuePatternOnlyThroughVerifiedFocus()
    {
        var (box, boxRef) = Hazard(new(AutomationId: "KeysOnlyBox"));
        Assert.False(box.Patterns.Value.IsSupported, "Precondition: the box must have no Value pattern.");

        var reply = await fixture.CallTool(new FillTool(fixture.Elements), new { @ref = boxRef, value = "typed" });
        output.WriteLine(reply);

        // The box has nothing to read back, so the fill is sent but not verified; the app shows what arrived.
        Assert.StartsWith("Sent \"typed\" to", reply);
        Assert.True(WaitFor(() => Label("KeysOnlyTextLabel") == "Keys: typed"), Label("KeysOnlyTextLabel"));
    }

    [Fact]
    public async Task ParkingThePointerBesideAWindowOverEveryScreenIsRefused()
    {
        var (_, open) = Hazard(new(AutomationId: "OpenFullScreenButton"));
        var processId = WinFormsProcessId(fixture);
        Assert.Contains("Invoked", await fixture.CallTool(new ClickTool(fixture.Elements), new { @ref = open }));
        Win32WindowInfo? FullScreen() =>
            Win32Desktop.GetTopLevelWindows(processId).SingleOrDefault(w => w.Title == "Full Screen Window");
        Assert.True(WaitFor(() => FullScreen() != null), "The full-screen window did not open.");
        var handle = fixture.Session.RegisterNativeWindow(FullScreen()!.Hwnd, processId);
        try
        {
            var pointer = InputDesktop.Current.PointerPosition();
            var shot = await Run(
                new ScreenshotTool(fixture.Session, fixture.Elements),
                new
                {
                    handle,
                    parkPointer = true,
                    allowOccludedPixels = true,
                }
            );
            var texts = string.Join("\n", shot.Content.Select(c => c.Text).OfType<string>());
            output.WriteLine(texts);

            Assert.Contains("parkPointer was refused", texts);
            Assert.Contains("covers its screen", texts);
            Assert.Equal(pointer, InputDesktop.Current.PointerPosition());
            // The refused park is a detail of a capture that succeeded, not the capture's proof.
            Assert.Equal(Proof.Unavailable, shot.Outcome?.Details?["park"].GetProperty("verdict").GetString());
        }
        finally
        {
            fixture.Session.CloseWindow(handle);
            Assert.True(WaitFor(() => FullScreen() == null), "The full-screen window did not close.");
        }
    }
}
