using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

public sealed partial class Te3Page
{
    public CaptureEnvironment ObserveCaptureEnvironment() =>
        CaptureEnvironment.Observe(host.Sessions.GetInputTarget(handle).Hwnd);

    public async Task Capture(string path, Target? target = null, CaptureFrame? frame = null)
    {
        var reference = target == null ? null : await Reference(target);
        reportStep?.Invoke($"Capture {Path.GetFileName(path)}");
        await invoke(
            "windows_screenshot",
            new
            {
                handle,
                @ref = reference,
                savePath = path,
                includeImage = false,
                background = target == null || target.Selector.RootOnly,
                frame,
            }
        );
    }

    public async Task CaptureScalarScene(string path)
    {
        var owner = host.Sessions.GetInputTarget(handle);
        owner.EnsureAlive();
        var ownerBounds =
            Win32Desktop.GetWindowBounds(owner.Hwnd) ?? throw new InvalidOperationException("Owner bounds unavailable");
        var reference = await Reference(ScriptOutputTarget());
        var dialog = host.Elements.InputForRef(reference);
        var requested = new System.Drawing.Rectangle(ownerBounds.Left + 96, ownerBounds.Top + 192, 555, 254);
        reportStep?.Invoke("Place scalar output dialog beside its source");
        await PlaceWindow(reference, dialog.Hwnd, requested);
        // Native bounds change before DevExpress finishes repainting its resized controls.
        await Task.Delay(500);
        var observed =
            Win32Desktop.GetWindowBounds(dialog.Hwnd)
            ?? throw new InvalidOperationException("Output dialog bounds unavailable");
        // Keep OS minimum sizes; never squeeze or stretch pixels to match an old version.
        var scene = ScalarSceneBounds(ownerBounds, observed, Screen.FromHandle(owner.Hwnd).WorkingArea);
        CaptureScene(path, owner, dialog, scene, modal: true);
    }

    private void CaptureScene(
        string path,
        InputTarget owner,
        InputTarget dialog,
        System.Drawing.Rectangle scene,
        bool modal,
        bool includeOutsideOwner = false,
        int? popupShadowPadding = null
    )
    {
        var ownerBounds =
            Win32Desktop.GetWindowBounds(owner.Hwnd) ?? throw new InvalidOperationException("Owner disappeared");
        var observed =
            Win32Desktop.GetWindowBounds(dialog.Hwnd) ?? throw new InvalidOperationException("Popup disappeared");
        var spec = new SceneCheck.Spec(
            owner.Hwnd,
            dialog,
            scene,
            ownerBounds,
            observed,
            modal,
            includeOutsideOwner,
            popupShadowPadding
        );
        void VerifyScene()
        {
            owner.EnsureAlive();
            dialog.EnsureAlive();
            SceneCheck.Verify(
                spec,
                new(
                    Win32Desktop.GetForegroundWindow(),
                    Win32Desktop.GetTopLevelWindows(),
                    Win32Desktop.GetWindowBounds,
                    Screen.AllScreens.Select(s => s.WorkingArea).ToList()
                )
            );
        }

        reportStep?.Invoke("Capture unobscured owner/popup scene (actual screen pixels)");
        VerifyScene();
        using var capture = FlaUI.Core.Capturing.Capture.Rectangle(scene);
        VerifyScene();
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        capture.Bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
    }

    public async Task CaptureCalculationGroupMenu(string path)
    {
        var modelMenu = new Target(
            new(Name: "Model", ControlType: "MenuItem"),
            new(Name: "Main menu", ControlType: "MenuBar")
        );
        var ownerIdentity = await HoverModelMenu(modelMenu);
        // Invoke can remain pending for TE3 menu popups; use the guarded input path.
        MarkPopups();
        await Click(modelMenu);
        // Read until the menu's popup has opened, not once after a fixed pause.
        var discovery = await SettlePoll.Until(
            () => ObservePopups(ownerIdentity.ProcessId, maxWindows: null),
            seen => seen.Length > 0
        );
        File.WriteAllText(Path.ChangeExtension(path, ".uia.json"), JsonSerializer.Serialize(discovery, Indented));
        var target = new Target(new(Name: "Add Calculation Group", ControlType: "Button"), null, true);
        var element = await Resolve(target);
        var popup = await MenuPopup(element);
        var owner = host.Sessions.GetInputTarget(handle);
        // Keep the pointer on the menu bar, outside the popup, so tooltips cannot hide rows.
        await WalkModelMenuToCalculationGroup();
        var ownerBounds =
            Win32Desktop.GetWindowBounds(owner.Hwnd) ?? throw new InvalidOperationException("Owner disappeared");
        var bounds = Win32Desktop.GetWindowBounds(popup) ?? throw new InvalidOperationException("Menu disappeared");
        await Task.Delay(500);
        await RequireKeyboardFocus(target, "Calculation group menu item is not highlighted/focused");
        var workArea = Screen.FromHandle(owner.Hwnd).WorkingArea;
        var x = Math.Max(ownerBounds.Left, workArea.Left);
        var y = Math.Max(ownerBounds.Top, workArea.Top);
        var scene = new System.Drawing.Rectangle(
            x,
            y,
            Math.Max(1018, bounds.Right - x + 16),
            Math.Max(665, bounds.Bottom - y + 16)
        );
        CaptureScene(path, owner, InputTarget.Capture(popup, owner.ProcessId), scene, modal: false);
        await Keys("Escape");
        await Absent(target);
    }

    // Parks the pointer on the Model menu-bar item, so the popup opens under it and no tooltip covers its rows.
    private async Task<InputTarget> HoverModelMenu(Target modelMenu)
    {
        var menuBarItem = await Resolve(modelMenu);
        var ownerIdentity = host.Sessions.GetInputTarget(handle);
        // Refused before activation when the item cannot be pointed at; chosen again, and hit-tested, under the
        // lease right before the move.
        await Task.Run(() => ClickPoint.Choose(ClickGeometry.Read(menuBarItem))).WaitAsync(TimeSpan.FromSeconds(5));
        reportStep?.Invoke("Open Model menu");
        await MovePointer("Model menu", ownerIdentity, () => MenuBarPoint(menuBarItem), ownerIdentity.Hwnd);
        return ownerIdentity;
    }

    // Only where the item is visible, as Core's pointer tools choose it, and hit-tested as they do.
    private static System.Drawing.Point MenuBarPoint(AutomationElement item)
    {
        var point = ClickPoint.Choose(ClickGeometry.Read(item));
        ClickPoint.Verify(point, item);
        return point;
    }

    // The observed enabled-item order of the Model menu; every step is asserted instead of a blind key count.
    private async Task WalkModelMenuToCalculationGroup()
    {
        await Keys("Home");
        foreach (
            var name in new[]
            {
                "Deploy...",
                "Serialization options...",
                "Import tables...",
                "Script DAX",
                "Refresh model",
                "Add Table",
                "Add Calculated Table",
                "Add Calculation Group",
            }
        )
        {
            if (name != "Deploy...")
            {
                await Keys("Down");
            }

            await RequireKeyboardFocus(new(new(Name: name), null, true), "Expected focused menu item: " + name);
        }
    }

    public static System.Drawing.Rectangle ScalarSceneBounds(
        System.Drawing.Rectangle owner,
        System.Drawing.Rectangle dialog,
        System.Drawing.Rectangle workArea
    )
    {
        // Maximized windows have invisible borders beyond the monitor (-9 on this desktop).
        // Anchor the source strip inside the visible work area, not that invisible border.
        var x = Math.Max(owner.Left + 8, workArea.Left);
        var y = Math.Max(owner.Top + 100, workArea.Top);
        return new(x, y, Math.Max(704, dialog.Right - x + 15), Math.Max(361, dialog.Bottom - y + 15));
    }

    public static bool IsPopupShadow(
        Win32WindowInfo window,
        System.Drawing.Rectangle? bounds,
        int processId,
        System.Drawing.Rectangle dialog,
        bool modal = true,
        int? padding = null
    )
    {
        // Observed TE3 3.26.3 shadows: modal/language list 10 px, menu 7 px.
        var margin = padding ?? (modal ? 10 : 7);
        return window.ProcessId == processId
            && window.Title.Length == 0
            && bounds == System.Drawing.Rectangle.Inflate(dialog, margin, margin);
    }
}
