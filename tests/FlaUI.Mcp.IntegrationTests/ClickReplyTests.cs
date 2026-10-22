using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;
using FlaUI.Mcp.Tools;
using Xunit.Abstractions;
using static FlaUI.Mcp.IntegrationTests.PointerTestSupport;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>
/// windows_click replies and the input they stand for, checked against the real test apps. Each test names
/// the mutation-testing survivors it kills (docs/mutation.md, pointer-integration part).
/// </summary>
[Collection("TestApps")]
public class ClickReplyTests(TestAppFixture fixture, ITestOutputHelper output)
{
    private ClickTool Click() => new(fixture.Elements);

    private (AutomationElement Element, string Ref) WinFormsClickMe()
    {
        SelectTab(fixture, fixture.WinFormsHandle, "ButtonsTab");
        return Ref(fixture, fixture.WinFormsHandle, new(AutomationId: "ClickMeButton"));
    }

    [Fact]
    public async Task ExplicitInvokePressesTheButtonOnce()
    {
        // Kills ClickTool:259 String ("invoke" emptied in PatternActions).
        var (_, button) = WinFormsClickMe();
        var before = ClickCount(fixture, fixture.WinFormsHandle);
        var reply = await fixture.CallTool(Click(), new { @ref = button, action = "invoke" });
        Assert.Equal("Invoked Click Me", reply);
        Assert.True(WaitFor(() => ClickCount(fixture, fixture.WinFormsHandle) == before + 1));
    }

    [Fact]
    public async Task PhysicalClickOnAPlainButtonReportsOnlyTheClick()
    {
        // Kills ClickTool:82 Conditional(false) and String ("physical" never read), ClickTool:155 Null coalescing
        // (requested physical ignored), ClickTool:163 Boolean (selectionFallback starts true), ClickTool:230
        // Logical (physical || ellipsis, and ... || processId != 0: both wait 2 s for a window).
        var (_, button) = WinFormsClickMe();
        var before = ClickCount(fixture, fixture.WinFormsHandle);
        var clock = Stopwatch.StartNew();
        var reply = await fixture.CallTool(Click(), new { @ref = button, physical = true });
        clock.Stop();
        output.WriteLine($"{reply} ({clock.ElapsedMilliseconds} ms)");
        Assert.Equal("Clicked Click Me", reply);
        Assert.True(WaitFor(() => ClickCount(fixture, fixture.WinFormsHandle) == before + 1));
    }

    [Theory]
    [InlineData("right")]
    [InlineData("middle")]
    public async Task NonLeftButtonClickDoesNotPressTheButton(string mouseButton)
    {
        // Kills ClickTool:80 Null coalescing and String ("button" never read: a left click), ClickTool:218 String
        // ("right" -> left click), ClickTool:219 String ("middle" -> left click), ClickTool:230 Logical
        // (physical && ellipsis || processId != 0 waits for a window after a plain right click).
        var (_, button) = WinFormsClickMe();
        var before = ClickCount(fixture, fixture.WinFormsHandle);
        var reply = await fixture.CallTool(Click(), new { @ref = button, button = mouseButton });
        Assert.Equal("Clicked Click Me", reply);
        Assert.True(StaysTrue(() => ClickCount(fixture, fixture.WinFormsHandle) == before), "The button was pressed.");
    }

    [Fact]
    public async Task DoubleClickIsReportedAsADoubleClick()
    {
        // Kills ClickTool:81 String ("doubleClick" never read: the click becomes an Invoke).
        var (_, button) = WinFormsClickMe();
        var before = ClickCount(fixture, fixture.WinFormsHandle);
        var reply = await fixture.CallTool(Click(), new { @ref = button, doubleClick = true });
        Assert.Equal("Double-clicked Click Me", reply);
        Assert.True(WaitFor(() => ClickCount(fixture, fixture.WinFormsHandle) > before));
    }

    [Fact]
    public async Task RefFromAnotherWindowIsRefused()
    {
        // Kills ClickTool:100 String ("handle" never read, so the window check is skipped).
        var (_, button) = WinFormsClickMe();
        var before = ClickCount(fixture, fixture.WinFormsHandle);
        var result = await Run(Click(), new { @ref = button, handle = fixture.WpfHandle });
        Assert.True(result.IsError);
        Assert.Contains($"belongs to window {fixture.WinFormsHandle}, not {fixture.WpfHandle}", Text(result));
        Assert.True(StaysTrue(() => ClickCount(fixture, fixture.WinFormsHandle) == before), "The button was pressed.");
    }

    [Fact]
    public async Task ExplicitToggleReportsTheStateChange()
    {
        // Kills ClickTool:101 String ("action" never read: WinForms checkboxes then Invoke), ClickTool:259 String
        // ("toggle" emptied), ClickTool:345 String (Supports "toggle"), ClickTool:368 Conditional(false), Equality
        // and String (toggleBefore never read), ClickTool:379 Equality and String (no wait for the new state),
        // ClickTool:393 String (toggle reply falls through to "Collapsed").
        SelectTab(fixture, fixture.WinFormsHandle, "ButtonsTab");
        var (checkbox, cbRef) = Ref(fixture, fixture.WinFormsHandle, new(AutomationId: "EnableCheckbox"));
        var before = checkbox.Patterns.Toggle.Pattern.ToggleState.Value;
        var after = before == ToggleState.On ? ToggleState.Off : ToggleState.On;
        try
        {
            var reply = await fixture.CallTool(Click(), new { @ref = cbRef, action = "toggle" });
            Assert.Equal($"Enable the button below: {before} -> {after}", reply);
        }
        finally
        {
            if (checkbox.Patterns.Toggle.Pattern.ToggleState.Value != before)
            {
                checkbox.Patterns.Toggle.Pattern.Toggle();
            }
        }
    }

    [Fact]
    public async Task DefaultClickTogglesAWpfCheckbox()
    {
        // Kills ClickTool:299 String ("toggle" emptied in the default order: a WPF checkbox has no Invoke, so the
        // mutant falls back to a mouse click).
        SelectTab(fixture, fixture.WpfHandle, "ButtonsTab");
        var (checkbox, cbRef) = Ref(fixture, fixture.WpfHandle, new(AutomationId: "EnableCheckbox"));
        Assert.False(checkbox.Patterns.Invoke.IsSupported);
        var before = checkbox.Patterns.Toggle.Pattern.ToggleState.Value;
        var after = before == ToggleState.On ? ToggleState.Off : ToggleState.On;
        try
        {
            var reply = await fixture.CallTool(Click(), new { @ref = cbRef });
            Assert.Equal($"Enable the button below: {before} -> {after}", reply);
        }
        finally
        {
            if (checkbox.Patterns.Toggle.Pattern.ToggleState.Value != before)
            {
                checkbox.Patterns.Toggle.Pattern.Toggle();
            }
        }
    }

    [Fact]
    public async Task DefaultClickSelectsASelectOnlyTab()
    {
        // Kills ClickTool:299 String ("select" emptied in the default order: a WPF tab supports only
        // SelectionItem, so the mutant falls back to a mouse click).
        SelectTab(fixture, fixture.WpfHandle, "ButtonsTab");
        var (tab, tabRef) = Ref(fixture, fixture.WpfHandle, new(AutomationId: "FormsTab", ControlType: "TabItem"));
        Assert.False(tab.Patterns.Invoke.IsSupported);
        Assert.False(tab.Patterns.Toggle.IsSupported);
        try
        {
            var reply = await fixture.CallTool(Click(), new { @ref = tabRef });
            Assert.Equal("Selected Forms", reply);
            Assert.True(tab.Patterns.SelectionItem.Pattern.IsSelected.Value);
        }
        finally
        {
            SelectTab(fixture, fixture.WpfHandle, "ButtonsTab");
        }
    }

    [Fact]
    public async Task DefaultClickOnATabUsesInvokeOrToggleBeforeSelect()
    {
        // Kills ClickTool:294 Logical (TreeItem/ListItem || supports("select") makes every selectable control
        // select) when WinForms tab items expose Invoke; otherwise the reply is Selected either way.
        SelectTab(fixture, fixture.WinFormsHandle, "ButtonsTab");
        var (tab, tabRef) = Ref(fixture, fixture.WinFormsHandle, new(AutomationId: "GridTab", ControlType: "TabItem"));
        var expected = "Selected Grid";
        if (tab.Patterns.Invoke.IsSupported)
        {
            expected = "Invoked Grid";
        }
        else if (tab.Patterns.Toggle.IsSupported)
        {
            expected = "Grid: ";
        }

        output.WriteLine($"Expecting: {expected}");
        try
        {
            var reply = await fixture.CallTool(Click(), new { @ref = tabRef });
            Assert.StartsWith(expected, reply);
        }
        finally
        {
            SelectTab(fixture, fixture.WinFormsHandle, "ButtonsTab");
        }
    }

    [Fact]
    public async Task DefaultClickOnAControlWithoutPatternsFallsBackToTheMouse()
    {
        // Kills ClickTool:299 Linq (FirstOrDefault -> First throws when no pattern fits).
        SelectTab(fixture, fixture.WpfHandle, "ButtonsTab");
        var (label, labelRef) = Ref(fixture, fixture.WpfHandle, new(AutomationId: "ClickCountLabel"));
        Assert.False(label.Patterns.Invoke.IsSupported);
        Assert.False(label.Patterns.Toggle.IsSupported);
        Assert.False(label.Patterns.SelectionItem.IsSupported);
        var name = label.Properties.Name.Value;
        var reply = await fixture.CallTool(Click(), new { @ref = labelRef });
        Assert.Equal($"Clicked {name}", reply);
    }

    [Fact]
    public async Task SelectingAListRowAgainSelectsIt()
    {
        // Kills ClickTool:169 Logical ((select && TreeItem) || selected), ClickTool:170 Equality (!= TreeItem):
        // both call an already selected list row "already selected". Also ClickTool:259 String ("select"
        // emptied), ClickTool:346 String (Supports "select"), ClickTool:397 String (case and reply text).
        SelectTab(fixture, fixture.WinFormsHandle, "TreesTab");
        var (row, rowRef) = Ref(
            fixture,
            fixture.WinFormsHandle,
            new(Name: "Photo.jpg", ControlType: "ListItem"),
            new(AutomationId: "TestListView")
        );
        var first = await fixture.CallTool(Click(), new { @ref = rowRef, action = "select" });
        output.WriteLine(first);
        Assert.StartsWith("Selected Photo.jpg", first);
        Assert.True(WaitFor(() => row.Patterns.SelectionItem.Pattern.IsSelected.Value));
        var again = await fixture.CallTool(Click(), new { @ref = rowRef, action = "select" });
        Assert.Equal("Selected Photo.jpg", again);
    }

    [Fact]
    public async Task ClickingTheSelectedTreeRowAgainClicksNothing()
    {
        // Kills ClickTool:169 Equality (action != "select") and String ("select" emptied): both select again
        // instead of answering "already selected".
        SelectTab(fixture, fixture.WpfHandle, "TreesTab");
        var tree = new ElementSelector(AutomationId: "TestTreeView");
        var (_, cherry) = Ref(fixture, fixture.WpfHandle, new(Name: "Cherry", ControlType: "TreeItem"), tree);
        var (_, banana) = Ref(fixture, fixture.WpfHandle, new(Name: "Banana", ControlType: "TreeItem"), tree);
        Assert.Equal("Selected Cherry", await fixture.CallTool(Click(), new { @ref = cherry }));
        Assert.Equal("Selected Banana", await fixture.CallTool(Click(), new { @ref = banana }));
        Assert.Equal(
            "Banana is already selected; nothing was clicked",
            await fixture.CallTool(Click(), new { @ref = banana })
        );
    }

    [Fact]
    public async Task CollapseAndExpandWorkOnTheSelectedTreeRow()
    {
        // Kills ClickTool:169 Logical ((select || TreeItem) && selected answers "already selected" for a collapse
        // of the selected row), ClickTool:259 String ("expand", "collapse" emptied), ClickTool:347 String (Supports
        // "expand", "collapse"), ClickTool:398 String (expand case falls through to "Collapsed").
        SelectTab(fixture, fixture.WpfHandle, "TreesTab");
        var tree = new ElementSelector(AutomationId: "TestTreeView");
        var (_, cherry) = Ref(fixture, fixture.WpfHandle, new(Name: "Cherry", ControlType: "TreeItem"), tree);
        var (fruits, fruitsRef) = Ref(fixture, fixture.WpfHandle, new(Name: "Fruits", ControlType: "TreeItem"), tree);
        Assert.Equal("Selected Cherry", await fixture.CallTool(Click(), new { @ref = cherry }));
        Assert.Equal("Selected Fruits", await fixture.CallTool(Click(), new { @ref = fruitsRef }));
        try
        {
            Assert.Equal(
                "Collapsed Fruits",
                await fixture.CallTool(Click(), new { @ref = fruitsRef, action = "collapse" })
            );
            Assert.Equal(
                ExpandCollapseState.Collapsed,
                fruits.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value
            );
            Assert.Equal(
                "Expanded Fruits",
                await fixture.CallTool(Click(), new { @ref = fruitsRef, action = "expand" })
            );
            Assert.Equal(
                ExpandCollapseState.Expanded,
                fruits.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value
            );
        }
        finally
        {
            if (fruits.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value != ExpandCollapseState.Expanded)
            {
                fruits.Patterns.ExpandCollapse.Pattern.Expand();
            }
        }
    }

    [Fact]
    public async Task DefaultClickOnAWinFormsTreeRowSelectsIt()
    {
        // Kills ClickTool:294 Equality (type is not TreeItem or ListItem sends a tree row down the
        // invoke/toggle/select order) when WinForms tree rows expose Invoke.
        SelectTab(fixture, fixture.WinFormsHandle, "TreesTab");
        var tree = new ElementSelector(AutomationId: "TestTreeView");
        Resolve(fixture, fixture.WinFormsHandle, new(Name: "Banana", ControlType: "TreeItem"), tree)
            .Patterns.SelectionItem.Pattern.Select();
        var (apple, appleRef) = Ref(fixture, fixture.WinFormsHandle, new(Name: "Apple", ControlType: "TreeItem"), tree);
        output.WriteLine($"Apple supports Invoke: {apple.Patterns.Invoke.IsSupported}");
        var reply = await fixture.CallTool(Click(), new { @ref = appleRef });
        Assert.StartsWith("Selected Apple", reply);
    }

    [Fact]
    public async Task EllipsisClickNamesTheDialogItOpenedAndAFailedClickNamesTheModal()
    {
        // Kills ClickTool:230 Equality (processId == 0), ClickTool:231 Conditional(false) (no "before" list, so
        // the main window counts as new), ClickTool:244 Equality (waited > 2000: no wait), ClickTool:247 Block
        // removal, ClickTool:253 Statement (no sleep: 20 polls before the dialog exists), ClickToolReplies:70
        // Logical, LogicalNot and Equality (Length < 0), ClickToolReplies:43 Conditional(false) and Linq
        // (Any -> All), ClickToolReplies:46 Conditional(true).
        var processId = WinFormsProcessId(fixture);
        var sendKeys = new SendKeysTool(fixture.Elements, new PendingInvokeTracker(), sessions: fixture.Session);
        SelectTab(fixture, fixture.WinFormsHandle, "DialogsTab");
        var (_, ellipsis) = Ref(fixture, fixture.WinFormsHandle, new(AutomationId: "ChooseOptionsButton"));
        var (_, modeless) = Ref(fixture, fixture.WinFormsHandle, new(AutomationId: "OpenModelessButton"));
        try
        {
            var reply = await fixture.CallTool(Click(), new { @ref = ellipsis });
            Assert.Equal(
                "Clicked Choose Options...; window \"Test Modal Dialog\" opened. Get its handle from windows_list_windows.",
                reply
            );

            // A click that fails while the modal is up names it; the provider stays free after a physical click.
            var failed = await Run(Click(), new { @ref = modeless, action = "bogus" });
            Assert.True(failed.IsError);
            Assert.Equal(
                $"Failed to click {modeless}: action must be invoke, select, expand, collapse, toggle or click. "
                    + "Window \"Test Modal Dialog\" appears to be a modal dialog blocking input; handle it first (see windows_list_windows).",
                Text(failed)
            );
        }
        finally
        {
            await DismissModal(processId, sendKeys);
        }
    }

    [Fact]
    public async Task FailedClickWithoutAModalGivesOnlyTheReason()
    {
        // Kills ClickToolReplies:43 Conditional(true), Logical (!enabled || !tool) and LogicalNot (enabled &&
        // !tool): each names the enabled main window as a modal. Also ClickToolReplies:36 String (the message
        // replaced by the exception type).
        SelectTab(fixture, fixture.WinFormsHandle, "DialogsTab");
        var (_, modeless) = Ref(fixture, fixture.WinFormsHandle, new(AutomationId: "OpenModelessButton"));
        var failed = await Run(Click(), new { @ref = modeless, action = "bogus" });
        Assert.True(failed.IsError);
        Assert.Equal(
            $"Failed to click {modeless}: action must be invoke, select, expand, collapse, toggle or click",
            Text(failed)
        );
    }

    [Fact]
    public async Task EllipsisClickThatOpensNothingWaitsTwoSeconds()
    {
        // Kills ClickTool:244 AddAssignment (waited -= 100 never ends: timeout), ClickTool:244 Equality
        // (waited > 2000 returns at once) and ClickTool:253 Statement (no sleep: the 2 s wait takes no time).
        SelectTab(fixture, fixture.WinFormsHandle, "DialogsTab");
        var (_, button) = Ref(fixture, fixture.WinFormsHandle, new(AutomationId: "DoNothingButton"));
        var clock = Stopwatch.StartNew();
        var reply = await fixture.CallTool(Click(), new { @ref = button });
        clock.Stop();
        Assert.Equal("Clicked Do Nothing...; no new window appeared within 2 seconds.", reply);
        Assert.InRange(clock.ElapsedMilliseconds, 1900, 10000);
    }

    private async Task DismissModal(int processId, SendKeysTool sendKeys)
    {
        Win32WindowInfo? dialog = null;
        WaitFor(
            () =>
            {
                dialog = Win32Desktop
                    .GetTopLevelWindows(processId)
                    .SingleOrDefault(w => w.Title == "Test Modal Dialog");
                return dialog != null;
            },
            1000
        );
        if (dialog != null)
        {
            var handle = fixture.Session.RegisterNativeWindow(dialog.Hwnd, processId);
            Assert.Contains("Sent keys", await fixture.CallTool(sendKeys, new { handle, chord = "Enter" }));
            Assert.True(
                WaitFor(() => Win32Desktop.GetTopLevelWindows(processId).All(w => w.Title != "Test Modal Dialog"))
            );
        }
    }
}
