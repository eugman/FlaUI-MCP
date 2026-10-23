using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

/// <summary>Typed TE3 interactions. Locators come from successful 3.26.3 runs.</summary>
public sealed partial class Te3Page(
    AutomationHost host,
    string handle,
    Func<string, object, Task> invoke,
    Action<string>? reportStep = null,
    string? journal = null,
    InputTally? inputs = null
)
{
    internal static bool IsPreferencesLookup(Target target) =>
        InPreferences(target.Selector) || (target.Within is { } within && InPreferences(within));

    // Preferences' own controls: the dialog, its section tree, and the panes whose AutomationIds carry a section prefix.
    private static readonly string[] PreferencesPanePrefixes = ["DAX Editor.", "AI Features.", "AI Assistant."];

    private static bool InPreferences(ElementSelector selector) =>
        selector == PreferencesTarget().Selector
        || selector.AutomationId == "treePreferences"
        || PreferencesPanePrefixes.Any(prefix =>
            selector.AutomationId?.StartsWith(prefix, StringComparison.Ordinal) == true
        );

    public async Task AssertFileFormatsAnnotationRows()
    {
        var result = await Task.Run(() =>
                query.Find(
                    handle,
                    new(ControlType: "TreeItem", Visible: true),
                    new(AutomationId: "treePreferences"),
                    maxResults: 100,
                    includeOwned: true,
                    budget: new SearchBudget(1000, TimeSpan.FromSeconds(3))
                )
            )
            .WaitAsync(TimeSpan.FromSeconds(5));
        var rows = result.Elements;
        var parent = rows.FindIndex(row => row.Value == "File Formats");
        if (!AnnotationRowsInOrder(result, parent))
        {
            throw new InvalidOperationException("File Formats annotation rows are not visible in the expected order");
        }

        var reference = await Reference(PreferencesTarget());
        var dialog = host.Elements.InputForRef(reference);
        await Task.Run(() =>
            {
                // UIA reports the dialog's client bounds; PrintWindow includes its title bar and border.
                dialog.EnsureAlive();
                var origin = (
                    Win32Desktop.GetWindowBounds(dialog.Hwnd)
                    ?? throw new InvalidOperationException("Preferences window disappeared")
                ).Location;
                foreach (var (index, x, y) in new[] { (parent + 1, 125, 718), (parent + 2, 165, 742) })
                {
                    var element =
                        host.Elements.GetElement(rows[index].Ref)
                        ?? throw new InvalidOperationException("Stale annotation row");
                    var bounds = element.BoundingRectangle;
                    bounds.Offset(-origin.X, -origin.Y);
                    if (!bounds.Contains(x, y) || Math.Abs(bounds.Top + bounds.Height / 2 - y) > 1)
                    {
                        throw new InvalidOperationException(
                            $"File Formats annotation {rows[index].Value}: observed {bounds}, expected tip ({x},{y}); review geometry"
                        );
                    }
                }
            })
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    // File Formats, selected and unique, with General and Save-to-folder right below it, in a complete read.
    private static bool AnnotationRowsInOrder(QueryResult result, int parent)
    {
        var rows = result.Elements;
        if (!result.Complete || parent < 0 || parent + 2 >= rows.Count)
        {
            return false;
        }

        return rows.Count(row => row.Value == "File Formats") == 1
            && rows[parent].Selected == true
            && rows[parent + 1].Value == "General"
            && rows[parent + 2].Value == "Save-to-folder";
    }

    private async Task<AutomationElement> Resolve(Target target, string? expected = null)
    {
        reportStep?.Invoke($"Find {target.Selector}; expected value: {expected ?? "(any)"}");
        Exception? last = null;
        var clock = Stopwatch.StartNew();
        do
        {
            OperationContext.Check();
            try
            {
                return await Task.Run(() => ResolveOnce(target, expected)).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) when (Polled(ex))
            {
                last = ex;
            }

            await Task.Delay(100);
        } while (clock.Elapsed < TimeSpan.FromSeconds(15));
        throw new TimeoutException($"TE3 target {target.Selector}: {last?.Message}", last);
    }

    // What the poll waits out: not there yet, or not the expected value yet. An ambiguous match, cancellation, a hung
    // provider (no retry workers), and a refusal other than not-found are never relabelled as a timeout.
    private static bool Polled(Exception ex) =>
        ex is not (System.Reflection.AmbiguousMatchException or OperationCanceledException or TimeoutException)
        && (ex is not AutomationRefusal || ex is ElementNotFoundException);

    private AutomationElement ResolveOnce(Target target, string? expected)
    {
        // Avoid reattaching the disabled owner and every popup for a
        // known dialog. Keep the lookup and uniqueness check local.
        var preferences = IsPreferencesLookup(target);
        var queryHandle = preferences ? PreferencesHandle() : handle;
        var within = preferences && target.Within == PreferencesTarget().Selector ? null : target.Within;
        var element = query.Resolve(
            queryHandle,
            target.Selector,
            within,
            !preferences && target.IncludeOwned,
            budget: new SearchBudget(3000, TimeSpan.FromSeconds(2))
        );
        var actual = element.Patterns.Value.IsSupported
            ? element.Patterns.Value.Pattern.Value.ValueOrDefault
            : element.Properties.Name.ValueOrDefault;
        if (expected != null && actual != expected)
        {
            throw new InvalidOperationException($"Expected '{expected}', observed '{actual}'");
        }

        return element;
    }

    // Preferences open on the section: the search box empty, the section's row selected, and its pane's marker
    // inside the visible dialog.
    private async Task VerifyPreferencesArrival(Te3Destination destination, string section, string marker)
    {
        await Expect(
            new(new(AutomationId: "searchPreferences", ControlType: "Edit"), PreferencesTarget().Selector, true),
            ""
        );
        var row = await Resolve(
            new(new(ControlType: "TreeItem", Value: section, Visible: true), new(AutomationId: "treePreferences"), true)
        );
        if (!row.Patterns.SelectionItem.Pattern.IsSelected.Value)
        {
            throw new InvalidOperationException("Expected Preferences row is not selected.");
        }

        var paneId =
            destination.PaneAutomationId
            ?? throw new InvalidOperationException("Preferences pane locator is not configured.");
        var control = await Resolve(
            new(
                new(Name: marker, ControlType: destination.PaneMarkerControlType, Visible: true),
                new(AutomationId: paneId),
                true
            )
        );
        var dialog = host.Sessions.GetInputTarget(PreferencesHandle());
        var dialogBounds =
            Win32Desktop.GetWindowBounds(dialog.Hwnd)
            ?? throw new InvalidOperationException("Preferences window disappeared.");
        var rect = control.BoundingRectangle;
        var controlBounds = System.Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (rect.IsEmpty || !dialogBounds.Contains(controlBounds))
        {
            throw new InvalidOperationException("Expected pane content is not inside the visible Preferences dialog.");
        }
    }

    /// <summary>Read-only arrival checks shared by companion navigation and capture.</summary>
    public async Task VerifyCompanionDestination(
        Te3Destination destination,
        string? table = null,
        string? objectName = null,
        string? objectType = null
    )
    {
        OperationContext.Check();
        switch (destination)
        {
            case { Section: { } section, PaneMarker: { } marker }:
                await VerifyPreferencesArrival(destination, section, marker);
                break;
            case { Id: "object" }:
                // Proof from the tree and the status bar; Properties' ObjectType only when the bar names another row
                // (it does not follow every selection on 3.27.1).
                await VerifyObjectArrival();
                break;
            case { Id: "tom-explorer" }:
                // Unknown tree visibility needs a selected TOM tab; it may carry the Changed suffix.
                await TomPaneOpen.Verify(
                    () => IsOnScreen(TomTreeTarget()),
                    FindTomTab,
                    title => RequireSelectedTab(TomTabTarget(title))
                );

                break;
            case { Id: "native-mcp-server" }:
                await VerifyNativeMcpServer();
                break;
            case { Id: "tom-explorer/changes" }:
                if (await IsOnScreen(TomTreeTarget()) != true || !await TomChangesOn())
                {
                    throw new InvalidOperationException("TOM Explorer Show changes is not visible and on.");
                }

                break;
            case { Id: "tom-explorer/changes-off" }:
                if (await IsOnScreen(TomTreeTarget()) != true || await TomChangesOn())
                {
                    throw new InvalidOperationException("TOM Explorer Show changes is not visible and off.");
                }

                break;
            case { Id: "expression-editor" }:
                await RequireSelectedTab(ExpressionTabTarget());
                break;
            default:
                throw new ArgumentException("No arrival check for destination " + destination.Id);
        }
    }
}
