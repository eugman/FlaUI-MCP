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
        target.Selector == PreferencesTarget().Selector
        || target.Within == PreferencesTarget().Selector
        || target.Selector.AutomationId == "treePreferences"
        || target.Within?.AutomationId == "treePreferences"
        || target.Selector.AutomationId?.StartsWith("DAX Editor.", StringComparison.Ordinal) == true
        || target.Within?.AutomationId?.StartsWith("DAX Editor.", StringComparison.Ordinal) == true
        || target.Selector.AutomationId?.StartsWith("AI Features.", StringComparison.Ordinal) == true
        || target.Within?.AutomationId?.StartsWith("AI Features.", StringComparison.Ordinal) == true
        || target.Selector.AutomationId?.StartsWith("AI Assistant.", StringComparison.Ordinal) == true
        || target.Within?.AutomationId?.StartsWith("AI Assistant.", StringComparison.Ordinal) == true;

    public async Task CaptureLanguageChoices(string path)
    {
        var language = new Target(
            new(Name: "User interface language", ControlType: "ComboBox", Visible: true),
            new(AutomationId: "Tabular Editor.User Interface"),
            true
        );
        await Expect(language, "English");
        Exception? failure = null;
        nint popupHwnd = 0;
        try
        {
            MarkPopups();
            await Keys("Alt+Down", language);
            await Task.Delay(500);
            var main = host.Sessions.GetInputTarget(handle);
            var preferences = host.Elements.InputForRef(await Reference(PreferencesTarget()));
            var popups = await ObservePopups(main.ProcessId, maxWindows: 5);
            File.WriteAllText(Path.ChangeExtension(path, ".uia.json"), JsonSerializer.Serialize(popups, Indented));
            if (popups.Any(p => !p.Tree.Complete))
            {
                throw new InvalidOperationException("Language popup observation incomplete");
            }

            var matches = popups.Where(p => p.Tree.Elements.Any(e => e.ControlType == "ListItem")).ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException("Expected one language list popup");
            }

            var popup = matches[0];
            popupHwnd = new nint(popup.Hwnd);
            var lists = popup.Tree.Elements.Where(e => e.ControlType == "List").ToArray();
            if (
                lists.Length != 1
                || popup.Tree.Elements.Where(e => e.ControlType == "ListItem").Any(e => e.Depth <= lists[0].Depth)
            )
            {
                throw new InvalidOperationException("Expected one language list with descendant items");
            }

            VerifyLanguageChoices(popup.Tree.Elements.Where(e => e.ControlType == "ListItem").ToArray());
            var bounds = popup.Bounds ?? throw new InvalidOperationException("Language popup bounds unavailable");
            var combo = await Resolve(language);
            var comboBounds = await Task.Run(() => combo.BoundingRectangle).WaitAsync(TimeSpan.FromSeconds(5));
            if (
                Math.Abs(bounds.Left - comboBounds.Left) > 3
                || Math.Abs(bounds.Width - comboBounds.Width) > 6
                || Math.Min(Math.Abs(bounds.Top - comboBounds.Bottom), Math.Abs(bounds.Bottom - comboBounds.Top)) > 3
            )
            {
                throw new InvalidOperationException("Language popup is not adjacent to its combo box");
            }

            var preferencesBounds =
                Win32Desktop.GetWindowBounds(preferences.Hwnd)
                ?? throw new InvalidOperationException("Preferences disappeared");
            var scene = System.Drawing.Rectangle.Union(preferencesBounds, bounds);
            CaptureScene(
                path,
                preferences,
                InputTarget.Capture(popupHwnd, main.ProcessId),
                scene,
                modal: false,
                includeOutsideOwner: true,
                popupShadowPadding: 10
            );
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            try
            {
                await Keys("Escape", language);
                if (popupHwnd != 0)
                {
                    await WaitForAbsence(() => Win32Desktop.GetTopLevelWindows().Any(w => w.Hwnd == popupHwnd));
                }

                await Expect(language, "English");
            }
            catch (Exception cleanup)
            {
                if (failure != null)
                {
                    throw new AggregateException("Language capture and popup cleanup failed", failure, cleanup);
                }

                throw;
            }
        }
    }

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
        if (
            result.Truncated
            || result.Unreadable != 0
            || parent < 0
            || parent + 2 >= rows.Count
            || rows.Count(row => row.Value == "File Formats") != 1
            || rows[parent].Selected != true
            || rows[parent + 1].Value != "General"
            || rows[parent + 2].Value != "Save-to-folder"
        )
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
                return await Task.Run(() =>
                    {
                        // Avoid reattaching the disabled owner and every popup for a
                        // known dialog. Keep the lookup and uniqueness check local.
                        var preferences = IsPreferencesLookup(target);
                        var queryHandle = preferences ? PreferencesHandle() : handle;
                        var within =
                            preferences && target.Within == PreferencesTarget().Selector ? null : target.Within;
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
                    })
                    .WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (System.Reflection.AmbiguousMatchException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException)
            {
                throw;
            } // A hung provider must not spawn retry workers.
            catch (AutomationRefusal refusal) when (refusal is not ElementNotFoundException)
            {
                throw;
            } // Only "not there yet" is polled; a refusal is never relabelled as a timeout.
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(100);
        } while (clock.Elapsed < TimeSpan.FromSeconds(15));
        throw new TimeoutException($"TE3 target {target.Selector}: {last?.Message}", last);
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
                await Expect(
                    new(
                        new(AutomationId: "searchPreferences", ControlType: "Edit"),
                        PreferencesTarget().Selector,
                        true
                    ),
                    ""
                );
                var row = await Resolve(
                    new(
                        new(ControlType: "TreeItem", Value: section, Visible: true),
                        new(AutomationId: "treePreferences"),
                        true
                    )
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
                    throw new InvalidOperationException(
                        "Expected pane content is not inside the visible Preferences dialog."
                    );
                }

                break;
            case { Id: "object" }:
                // Proof from the tree and the status bar; Properties' ObjectType only when the bar names another row
                // (it does not follow every selection on 3.27.1).
                await VerifyObjectArrival();
                break;
            case { Id: "tom-explorer" }:
                if (!await IsOnScreen(TomTreeTarget()))
                {
                    await RequireSelectedTab(TomTabTarget());
                }

                break;
            case { Id: "native-mcp-server" }:
                await VerifyNativeMcpServer();
                break;
            case { Id: "tom-explorer/changes" }:
                if (!await IsOnScreen(TomTreeTarget()) || !await TomChangesOn())
                {
                    throw new InvalidOperationException("TOM Explorer Show changes is not visible and on.");
                }

                break;
            case { Id: "tom-explorer/changes-off" }:
                if (!await IsOnScreen(TomTreeTarget()) || await TomChangesOn())
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
