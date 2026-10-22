using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

/// <summary>The Preferences language-list capture: open the popup, check and capture it, close it again.</summary>
public sealed partial class Te3Page
{
    private static Target LanguageTarget() =>
        new(
            new(Name: "User interface language", ControlType: "ComboBox", Visible: true),
            new(AutomationId: "Tabular Editor.User Interface"),
            true
        );

    public async Task CaptureLanguageChoices(string path)
    {
        var language = LanguageTarget();
        await Expect(language, "English");
        await PopupScope.Run(
            () => OpenLanguagePopup(language, path),
            scene => CaptureLanguagePopup(language, path, scene),
            (popupHwnd, failure) => CloseLanguagePopup(language, popupHwnd, failure)
        );
    }

    private sealed record LanguageScene(PopupObservation Popup, InputTarget Main, InputTarget Preferences);

    // Opens the language list and finds its one popup; the handle is kept before the popup's contents are checked.
    private async Task<(LanguageScene, nint)> OpenLanguagePopup(Target language, string path)
    {
        MarkPopups();
        await Keys("Alt+Down", language);
        var main = host.Sessions.GetInputTarget(handle);
        var preferences = host.Elements.InputForRef(await Reference(PreferencesTarget()));
        // Read until a complete observation shows the list's items, not once after a fixed pause.
        var popups = await SettlePoll.Until(
            () => ObservePopups(main.ProcessId, maxWindows: 5),
            seen =>
                seen.All(p => p.Tree.Complete) && seen.Any(p => p.Tree.Elements.Any(e => e.ControlType == "ListItem"))
        );
        File.WriteAllText(Path.ChangeExtension(path, ".uia.json"), JsonSerializer.Serialize(popups, Indented));
        var popup = LanguagePopup(popups);
        return (new LanguageScene(popup, main, preferences), new nint(popup.Hwnd));
    }

    private async Task CaptureLanguagePopup(Target language, string path, LanguageScene scene)
    {
        VerifyLanguageList(scene.Popup);
        var bounds = scene.Popup.Bounds ?? throw new InvalidOperationException("Language popup bounds unavailable");
        var combo = await Resolve(language);
        var comboBounds = await Task.Run(() => combo.BoundingRectangle).WaitAsync(TimeSpan.FromSeconds(5));
        RequireAdjacent(bounds, comboBounds);

        var preferencesBounds =
            Win32Desktop.GetWindowBounds(scene.Preferences.Hwnd)
            ?? throw new InvalidOperationException("Preferences disappeared");
        CaptureScene(
            path,
            scene.Preferences,
            InputTarget.Capture(new nint(scene.Popup.Hwnd), scene.Main.ProcessId),
            System.Drawing.Rectangle.Union(preferencesBounds, bounds),
            modal: false,
            includeOutsideOwner: true,
            popupShadowPadding: 10
        );
    }

    // The one popup holding the language list, from a complete observation.
    private static PopupObservation LanguagePopup(PopupObservation[] popups)
    {
        if (popups.Any(p => !p.Tree.Complete))
        {
            throw new InvalidOperationException("Language popup observation incomplete");
        }

        var matches = popups.Where(p => p.Tree.Elements.Any(e => e.ControlType == "ListItem")).ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("Expected one language list popup");
        }

        return matches[0];
    }

    // The popup holds one list with its items below it, and they are the expected choices.
    private static void VerifyLanguageList(PopupObservation popup)
    {
        var lists = popup.Tree.Elements.Where(e => e.ControlType == "List").ToArray();
        if (
            lists.Length != 1
            || popup.Tree.Elements.Where(e => e.ControlType == "ListItem").Any(e => e.Depth <= lists[0].Depth)
        )
        {
            throw new InvalidOperationException("Expected one language list with descendant items");
        }

        VerifyLanguageChoices(popup.Tree.Elements.Where(e => e.ControlType == "ListItem").ToArray());
    }

    private static void RequireAdjacent(System.Drawing.Rectangle bounds, System.Drawing.Rectangle comboBounds)
    {
        if (
            Math.Abs(bounds.Left - comboBounds.Left) > 3
            || Math.Abs(bounds.Width - comboBounds.Width) > 6
            || Math.Min(Math.Abs(bounds.Top - comboBounds.Bottom), Math.Abs(bounds.Bottom - comboBounds.Top)) > 3
        )
        {
            throw new InvalidOperationException("Language popup is not adjacent to its combo box");
        }
    }

    // Escape closes the popup, which must then be gone and leave English chosen; a cleanup failure after a capture
    // failure reports both.
    private async Task CloseLanguagePopup(Target language, nint popupHwnd, Exception? failure)
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
