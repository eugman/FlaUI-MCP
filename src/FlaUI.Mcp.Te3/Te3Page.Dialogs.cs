using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

public sealed partial class Te3Page
{
    /// <summary>Opens a ComboBox in Preferences with Alt+Down. Without a name there must be exactly one visible ComboBox.</summary>
    public async Task OpenPreferencesDropdown(string? name = null)
    {
        var preferences = PreferencesHandle();
        var found = await Task.Run(() =>
                query.Find(
                    preferences,
                    new(ControlType: "ComboBox", Name: name, Visible: true),
                    maxResults: 10,
                    budget: new SearchBudget(3000, TimeSpan.FromSeconds(3))
                )
            )
            .WaitAsync(TimeSpan.FromSeconds(5));
        if (found.Elements.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected one Preferences ComboBox{(name == null ? "" : $" named {name}")}; found: "
                    + string.Join(", ", found.Elements.Select(e => $"'{e.Name}'"))
            );
        }

        await invoke(
            "windows_send_keys",
            new
            {
                handle = preferences,
                @ref = found.Elements[0].Ref,
                chord = "Alt+Down",
            }
        );
        await Task.Delay(500);
    }

    public Task SendKeysToPreferences(string chord) => SendKeysTo(PreferencesHandle(), chord);

    public sealed record Dialog(string Handle, nint Hwnd, string Title);

    /// <summary>Selects a RadioButton in a dialog through its SelectionItem pattern.</summary>
    public async Task ChooseOption(Dialog dialog, string name) =>
        await invoke("windows_click", new { @ref = await DialogRef(dialog, name, "RadioButton") });

    public async Task<Dialog> OpenDialog(params string[] menuPath)
    {
        var before = TitledWindows().Select(w => w.Hwnd).ToArray();
        await OpenMenuPath(menuPath);
        var (dialogHandle, window) = await WaitForNewWindow(before, TimeSpan.FromSeconds(15));
        return new(dialogHandle, window.Hwnd, window.Title);
    }

    /// <summary>Closes a dialog without confirming anything; see <see cref="DialogCancel"/>.</summary>
    public Task CancelDialog(Dialog dialog) =>
        DialogCancel.Run(
            new(
                () => SendKeysTo(dialog.Handle, "Escape"),
                timeout => WaitForWindowClosed(dialog.Hwnd, timeout),
                button => DialogRef(dialog, button),
                reference => invoke("windows_click", new { @ref = reference })
            )
        );

    public async Task StageField(Dialog dialog, string name, string controlType, string value) =>
        await invoke(
            "windows_fill",
            new
            {
                @ref = await DialogRef(dialog, name, controlType),
                value,
                verifyFocus = true,
            }
        );

    public async Task SetDialogChoice(Dialog dialog, string name, string value)
    {
        var reference = await DialogRef(
            dialog,
            new(Name: name, ControlType: "ComboBox", Pattern: "Value", Visible: true)
        );
        await invoke(
            "windows_fill",
            new
            {
                @ref = reference,
                value,
                verifyFocus = true,
            }
        );
        var observed = (
            await ResolveDialog(dialog, new(Name: name, ControlType: "ComboBox", Pattern: "Value", Visible: true))
        )
            .Patterns
            .Value
            .Pattern
            .Value
            .ValueOrDefault;
        if (observed != value)
        {
            throw new InvalidOperationException($"{name} expected {value}, observed {observed}");
        }
    }

    public async Task<string> ReadDialogValue(Dialog dialog, string automationId)
    {
        var element = await ResolveDialog(dialog, new(AutomationId: automationId, Pattern: "Value", Visible: true));
        return element.Patterns.Value.Pattern.Value.ValueOrDefault ?? "";
    }

    public async Task ClickDialogButton(Dialog dialog, string automationId)
    {
        var reference = await DialogRef(dialog, new(AutomationId: automationId, ControlType: "Button", Visible: true));
        await invoke("windows_click", new { @ref = reference });
    }

    public async Task<string> ReadDialogButtonName(Dialog dialog, string automationId) =>
        (await ResolveDialog(dialog, new(AutomationId: automationId, ControlType: "Button", Visible: true)))
            .Properties
            .Name
            .ValueOrDefault
        ?? "";

    public async Task WaitForDialogButton(Dialog dialog, string automationId, string name, TimeSpan timeout)
    {
        var appeared = await Poll.Until(
            async () =>
            {
                var found = await Task.Run(() =>
                        query.Find(
                            dialog.Handle,
                            new(AutomationId: automationId, Name: name, ControlType: "Button", Visible: true),
                            maxResults: 2,
                            budget: new SearchBudget(200, TimeSpan.FromSeconds(1)),
                            register: false
                        )
                    )
                    .WaitAsync(TimeSpan.FromSeconds(3));
                return found.Elements.Count == 1;
            },
            timeout
        );
        if (!appeared)
        {
            throw new TimeoutException($"Dialog button {automationId} did not become {name}");
        }
    }

    public async Task OpenDropdown(Dialog dialog, string name) =>
        await invoke(
            "windows_send_keys",
            new
            {
                handle = dialog.Handle,
                @ref = await DialogRef(
                    dialog,
                    new(Name: name, ControlType: "ComboBox", Visible: true, Pattern: "Value")
                ),
                chord = "Alt+Down",
            }
        );

    public Task MapDialog(Dialog dialog, string path) => MapWindow(dialog.Handle, path);

    private Task<string> DialogRef(Dialog dialog, string name, string controlType) =>
        DialogRef(dialog, new(Name: name, ControlType: controlType));

    private async Task<string> DialogRef(Dialog dialog, ElementSelector selector)
    {
        reportStep?.Invoke($"Find {selector} in {dialog.Title}");
        var element = await ResolveDialog(dialog, selector);
        return host.Elements.Register(dialog.Handle, element);
    }

    private async Task<AutomationElement> ResolveDialog(Dialog dialog, ElementSelector selector) =>
        await Task.Run(() =>
                query.Resolve(
                    dialog.Handle,
                    selector,
                    null,
                    false,
                    budget: new SearchBudget(2000, TimeSpan.FromSeconds(3))
                )
            )
            .WaitAsync(TimeSpan.FromSeconds(5));

    private HashSet<nint> WindowHandles() =>
        Win32Desktop.GetTopLevelWindows(host.Sessions.GetWindowProcessId(handle)).Select(w => w.Hwnd).ToHashSet();

    private async Task ClickThroughPopups(HashSet<nint> before, IEnumerable<string> items)
    {
        var processId = host.Sessions.GetWindowProcessId(handle);
        var opened = await NewWindows(before, required: true);
        foreach (var name in items)
        {
            var command = await ResolveIn(opened, processId, name);
            var beforeClick = WindowHandles();
            await ClickPopupCommand(command, name);
            // The last item may open a dialog instead of a popup, so a new window is not required here.
            opened = await NewWindows(beforeClick, required: false);
        }
    }

    private async Task<nint[]> NewWindows(HashSet<nint> before, bool required)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var opened = Win32Desktop
                .GetTopLevelWindows(host.Sessions.GetWindowProcessId(handle))
                .Where(w => !w.IsCloaked && !before.Contains(w.Hwnd))
                .Select(w => w.Hwnd)
                .ToArray();
            // Give a popup a moment to finish building before it is searched.
            if (opened.Length > 0 && clock.ElapsedMilliseconds >= 300)
            {
                return opened;
            }

            if (clock.Elapsed > TimeSpan.FromSeconds(required ? 5 : 1))
            {
                return required ? throw new TimeoutException("Menu popup did not open") : opened;
            }

            await Task.Delay(100);
        }
    }

    private async Task<AutomationElement> ResolveIn(IEnumerable<nint> windows, int processId, string name)
    {
        reportStep?.Invoke($"Find '{name}' in the open popup");
        var matches = new List<AutomationElement>();
        foreach (var hwnd in windows)
        {
            string popup;
            try
            {
                popup = host.Sessions.RegisterNativeWindow(hwnd, processId);
            }
            catch (Exception error)
                when (error is InvalidOperationException or ArgumentException || CoreFailure.IsGone(error))
            {
                // Closed meanwhile: Core's identity refusal (target-stale, window-ownership-unknown) is dealt with.
                InputRefusedException.Handled(error);
                continue;
            }

            var found = await Task.Run(() =>
                    query.Find(
                        popup,
                        new(Name: name, Visible: true),
                        maxResults: 3,
                        budget: new SearchBudget(1000, TimeSpan.FromSeconds(2))
                    )
                )
                .WaitAsync(TimeSpan.FromSeconds(4));
            matches.AddRange(
                found.Elements.Select(element => host.Elements.GetElement(element.Ref)).OfType<AutomationElement>()
            );
        }

        return matches.Count == 1
            ? matches[0]
            : throw new InvalidOperationException($"Expected one '{name}' in the open popup; found {matches.Count}");
    }

    private async Task<nint> MenuPopup(AutomationElement element)
    {
        var owner = host.Sessions.GetInputTarget(handle);
        var popup = await Task.Run(() =>
            {
                var menu = false;
                for (var current = element; current != null; current = current.Parent)
                {
                    menu |= current.Properties.ControlType.ValueOrDefault == FlaUI.Core.Definitions.ControlType.Menu;
                    if (current.Properties.ControlType.ValueOrDefault != FlaUI.Core.Definitions.ControlType.Window)
                    {
                        continue;
                    }

                    var hwnd = current.Properties.NativeWindowHandle.ValueOrDefault;
                    if (hwnd != 0 && menu)
                    {
                        return Win32Desktop.GetAncestor(hwnd, 2);
                    }
                }

                throw new InvalidOperationException("Command is not in the expected native menu");
            })
            .WaitAsync(TimeSpan.FromSeconds(5));
        if (popup == owner.Hwnd || Win32Desktop.GetProcessId(popup) != owner.ProcessId)
        {
            throw new InvalidOperationException("Unexpected TE3 popup identity");
        }

        return popup;
    }
}
