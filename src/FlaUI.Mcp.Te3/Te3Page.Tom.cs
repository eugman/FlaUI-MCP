using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

/// <summary>Te3Page's popup baseline, its fill and placement helpers, and the TOM Explorer Show changes toggle.</summary>
public sealed partial class Te3Page
{
    // The windows open just before the input that opens a popup; ObservePopups considers only windows opened since.
    private IReadOnlyCollection<nint>? popupBaseline;

    private void MarkPopups() => popupBaseline = PopupWindows.Baseline(host.Sessions.GetInputTarget(handle).ProcessId);

    private async Task<PopupObservation[]> ObservePopups(int processId, int? maxWindows)
    {
        var windows = PopupWindows.New(Win32Desktop.GetTopLevelWindows(processId), popupBaseline);
        if (windows.Length > maxWindows)
        {
            throw new InvalidOperationException($"Popup discovery incomplete: {windows.Length} candidate windows");
        }

        return await Task.Run(() =>
                windows
                    .Select(w => new PopupObservation(
                        w.Hwnd.ToInt64(),
                        w.Title,
                        Win32Desktop.GetWindowBounds(w.Hwnd),
                        query.Find(
                            host.Sessions.RegisterNativeWindow(w.Hwnd, processId),
                            new(Visible: true),
                            maxDepth: 5,
                            maxResults: 100,
                            budget: new SearchBudget(1000, TimeSpan.FromSeconds(3))
                        )
                    ))
                    .ToArray()
            )
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    // verifyFocus: Core's keyboard fallback (Ctrl+A, Backspace, text) first checks that focus reached the field and
    // refuses otherwise: true for the field itself, FocusRules.SelfOrDescendantName for it or a child (Scintilla).
    private async Task Fill(Target target, string value, object verifyFocus)
    {
        var reference = await Reference(target);
        reportStep?.Invoke($"Fill {target.Selector}");
        await invoke(
            "windows_fill",
            new
            {
                @ref = reference,
                value,
                verifyFocus,
            }
        );
    }

    // A placement Core reports as not exact, after the window moved, is read back and reported (PlacementReadBack).
    private Task<System.Drawing.Rectangle> PlaceWindow(
        string reference,
        nint hwnd,
        System.Drawing.Rectangle requested
    ) =>
        PlacementReadBack.Place(
            () =>
                invoke(
                    "windows_place_window",
                    new
                    {
                        @ref = reference,
                        placement = new
                        {
                            x = requested.X,
                            y = requested.Y,
                            width = requested.Width,
                            height = requested.Height,
                        },
                    }
                ),
            () => Win32Desktop.GetWindowBounds(hwnd),
            requested,
            reportStep
        );

    public async Task OpenTomChanges()
    {
        await OpenTom();
        if (await TomChangesOn())
        {
            return;
        }

        await Click(TomChangesTarget());
        if (!await TomChangesOn())
        {
            throw new InvalidOperationException("Show changes did not turn on in TOM Explorer.");
        }
    }

    /// <summary>
    /// Turns TOM Explorer's Show changes filter off (the Toggle CheckBox; the bindable command is
    /// TomExplorer.ToggleChangedOnly). Properties' own Show changes has no bindable command and is not driven here.
    /// </summary>
    public async Task CloseTomChanges()
    {
        await OpenTom();
        if (await TomChangesState() is null)
        {
            throw new InvalidOperationException("TOM Explorer shows no Show changes checkbox; no input sent.");
        }

        await ShowChangesGate.TurnOff(TomChangesState, () => Click(TomChangesTarget()));
    }

    // Null when the checkbox is not on screen (builds without it, or the TOM Explorer toolbar hidden).
    private async Task<bool?> TomChangesState() => await IsOnScreen(TomChangesTarget()) ? await TomChangesOn() : null;

    private async Task<bool> TomChangesOn()
    {
        var control = await Resolve(TomChangesTarget());
        if (!control.Patterns.Toggle.IsSupported)
        {
            throw new InvalidOperationException("TOM Explorer Show changes has no Toggle pattern.");
        }

        var state = await Task.Run(() => control.Patterns.Toggle.Pattern.ToggleState.Value)
            .WaitAsync(TimeSpan.FromSeconds(5));
        return state == ToggleState.On;
    }

    /// <summary>One lookup, no retries: whether the target exists and is not offscreen; a failed read throws.</summary>
    private async Task<bool> IsOnScreen(Target target) =>
        await Task.Run(() =>
            ScreenPresence.Read(
                () =>
                    !query
                        .Resolve(
                            handle,
                            target.Selector,
                            target.Within,
                            target.IncludeOwned,
                            budget: new SearchBudget(3000, TimeSpan.FromSeconds(2))
                        )
                        .Properties.IsOffscreen.ValueOrDefault,
                target.Selector.ToString()
            )
        );
}
