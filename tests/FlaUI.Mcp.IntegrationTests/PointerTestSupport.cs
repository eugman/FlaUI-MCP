using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Mcp.Core;

namespace FlaUI.Mcp.IntegrationTests;

/// <summary>Shared steps for the pointer kill tests: resolve controls, register refs, call tools.</summary>
internal static class PointerTestSupport
{
    public static ElementQuery Query(TestAppFixture fixture) =>
        new(fixture.Session, fixture.Elements, new PendingInvokeTracker());

    /// <summary>Show a tab by its AutomationId; controls on hidden tabs are not in the tree.</summary>
    public static void SelectTab(TestAppFixture fixture, string handle, string tabId)
    {
        var tab = Query(fixture).Resolve(handle, new(AutomationId: tabId, ControlType: "TabItem"));
        tab.Patterns.SelectionItem.Pattern.Select();
        if (!WaitFor(() => tab.Patterns.SelectionItem.Pattern.IsSelected.ValueOrDefault))
        {
            throw new InvalidOperationException($"Tab {tabId} was not selected within 3 s.");
        }
    }

    public static AutomationElement Resolve(
        TestAppFixture fixture,
        string handle,
        ElementSelector selector,
        ElementSelector? within = null
    ) => Query(fixture).Resolve(handle, selector, within);

    /// <summary>Resolve a control and register it, as windows_find does, so tools can take its ref.</summary>
    public static (AutomationElement Element, string Ref) Ref(
        TestAppFixture fixture,
        string handle,
        ElementSelector selector,
        ElementSelector? within = null
    )
    {
        var element = Resolve(fixture, handle, selector, within);
        return (element, fixture.Elements.Register(handle, element));
    }

    public static async Task<McpToolResult> Run(ToolBase tool, object args)
    {
        var json = JsonSerializer.Serialize(args, McpProtocol.JsonOptions);
        return await tool.ExecuteAsync(JsonSerializer.Deserialize<JsonElement>(json));
    }

    public static string Text(McpToolResult result) => result.Content.FirstOrDefault()?.Text ?? "";

    /// <summary>The number in a "Click count: N" label.</summary>
    public static int ClickCount(TestAppFixture fixture, string handle) =>
        int.Parse(
            Resolve(fixture, handle, new(AutomationId: "ClickCountLabel"))
                .Properties.Name.Value.Replace("Click count:", "")
                .Trim(),
            System.Globalization.CultureInfo.InvariantCulture
        );

    /// <summary>The generous bound every integration wait uses: only reached when something is broken.</summary>
    public const int WaitBoundMs = 10_000;

    private const int PollMs = 50;

    /// <summary>
    /// The one wait the integration tests use for the real app: poll <paramref name="condition"/> until it holds or the
    /// generous bound passes. Real time is fine here; a fixed sleep is not, since it is either too short or wasted.
    /// </summary>
    public static bool WaitFor(Func<bool> condition, int timeoutMs = WaitBoundMs)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.ElapsedMilliseconds > timeoutMs)
            {
                return false;
            }

            Thread.Sleep(PollMs);
        }

        return true;
    }

    /// <inheritdoc cref="WaitFor(Func{bool}, int)"/>
    public static async Task<bool> WaitForAsync(Func<Task<bool>> condition, int timeoutMs = WaitBoundMs)
    {
        var clock = Stopwatch.StartNew();
        while (!await condition())
        {
            if (clock.ElapsedMilliseconds > timeoutMs)
            {
                return false;
            }

            await Task.Delay(PollMs);
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="condition"/> held throughout <paramref name="windowMs"/>: the check that something did
    /// not happen, such as a refused click that must not press. Only a real-time window can show that.
    /// </summary>
    public static bool StaysTrue(Func<bool> condition, int windowMs = 500)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            if (!condition())
            {
                return false;
            }

            Thread.Sleep(PollMs);
        } while (clock.ElapsedMilliseconds < windowMs);

        return condition();
    }

    /// <summary>Put a window in front without this server's lease, and wait until Windows reports it.</summary>
    public static void Foreground(nint hwnd)
    {
        Win32Desktop.FocusWindow(hwnd);
        Assert.True(WaitFor(() => Win32Desktop.GetForegroundWindow() == hwnd), "Could not bring the window forward.");
    }

    /// <summary>Count inputs the way a tool call does, for the duration of one test step.</summary>
    public static OperationContext Counting()
    {
        var context = new OperationContext { Tool = "kill-test" };
        OperationContext.Current.Value = context;
        return context;
    }

    public static void StopCounting() => OperationContext.Current.Value = null;

    public static int WinFormsProcessId(TestAppFixture fixture) =>
        fixture.Session.GetWindowProcessId(fixture.WinFormsHandle);
}
