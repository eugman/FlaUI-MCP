using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Mcp.Core;

public sealed partial class Te3Page
{
    /// <summary>
    /// Sends input this page dispatches itself rather than through a Core tool, journaled like tool calls. The
    /// GuardedInput (whose constructor activates the target) and <paramref name="send"/>, which sends through it, run
    /// inside the journaled call, and the tally counts what Core recorded: the activation, when there was one, and
    /// each send.
    /// </summary>
    private Task SendInput(string action, object arguments, InputTarget target, Action<GuardedInput> send) =>
        RunJournal.Input(
            journal,
            action,
            arguments,
            () =>
                Inputs.Dispatch(() =>
                {
                    using var input = new GuardedInput(target);
                    send(input);
                })
        );

    /// <summary>
    /// Moves the pointer to <paramref name="point"/> once the target is in front and the point lies on
    /// <paramref name="over"/>; refuses, sending nothing more, when another window covers it.
    /// </summary>
    private Task MovePointer(string what, InputTarget target, System.Drawing.Point point, nint over) =>
        SendInput(
            "te3_pointer_move",
            new
            {
                target = what,
                point.X,
                point.Y,
            },
            target,
            input =>
            {
                input.Check(() =>
                {
                    if (Win32Desktop.WindowAt(point) != over)
                    {
                        throw Obscured(target, what + " is obscured");
                    }
                });
                input.Send(() => FlaUI.Core.Input.Mouse.MoveTo(point));
            }
        );

    /// <summary>The refusal for a pointer target another window covers, as Core's structured point-not-on-target.</summary>
    internal static InputRefusedException Obscured(InputTarget target, string what) =>
        InputRefusedException.Record(
            new InputRefusal("point-not-on-target", what + "; no input sent.")
            {
                Target = new ProcessRef(null, target.ProcessId),
            }
        );

    /// <summary>
    /// Clicks a command in TE3's own non-activating menu popup through GuardedInput.Click: Core reads the element's
    /// point under the input lease, checks it with ClickPoint, and hit-tests it against the popup.
    /// </summary>
    private Task ClickInPopup(string label, AutomationElement element, nint popup) =>
        SendInput(
            "te3_click",
            new { target = label },
            host.Sessions.GetInputTarget(handle) with
            {
                HitHwnd = popup,
            },
            input => input.Click(element)
        );

    /// <summary>Inputs sent for this page: the caller's tool calls (when it shares the tally) and the page's own.</summary>
    public InputTally Inputs { get; } = inputs ?? new();

    private readonly ElementQuery query = new(host.Sessions, host.Elements, host.Pending);
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    // Popups are separate top-level windows; searching them directly avoids spending the budget on the editor.
    // UIA can report keyboard focus shortly after the key press, so poll briefly before failing.
    private async Task RequireKeyboardFocus(Target target, string failure)
    {
        var focused = await Poll.Until(
            async () =>
            {
                var element = await Resolve(target);
                return await Task.Run(() => element.Properties.HasKeyboardFocus.Value)
                    .WaitAsync(TimeSpan.FromSeconds(5));
            },
            TimeSpan.FromSeconds(2)
        );
        if (!focused)
        {
            throw new InvalidOperationException(failure);
        }
    }

    private async Task Click(Target target, bool physical = true)
    {
        var reference = await Reference(target);
        reportStep?.Invoke($"Click {target.Selector}");
        await invoke("windows_click", new { @ref = reference, physical });
    }

    private async Task Keys(string chord, Target? target = null)
    {
        var reference = target == null ? null : await Reference(target);
        reportStep?.Invoke($"Keys {chord}; target: {target?.Selector.ToString() ?? handle}");
        await invoke("windows_send_keys", KeysArguments(handle, reference, chord, target));
    }

    /// <summary>
    /// The windows_send_keys arguments. Keys to a Scintilla editor (daxscilla) ask for focus on it or inside it
    /// explicitly ("self-or-descendant"), since UIA may report focus on a child; other sends keep Core's default.
    /// </summary>
    internal static Dictionary<string, object?> KeysArguments(
        string window,
        string? reference,
        string chord,
        Target? target
    )
    {
        var arguments = new Dictionary<string, object?>
        {
            ["handle"] = window,
            ["ref"] = reference,
            ["chord"] = chord,
        };
        if (reference != null && target?.Selector.AutomationId == "daxscilla")
        {
            arguments["verifyFocus"] = FocusRules.SelfOrDescendantName;
        }

        return arguments;
    }

    /// <summary>
    /// What this page sent without a read-back of its effect, in order, in the same list as the Core calls Core
    /// reported unverified (the page's tally): none is claimed as verified.
    /// </summary>
    public IReadOnlyList<UnverifiedStep> UnverifiedSteps => Inputs.Unverified;

    private void Unverified(string step)
    {
        Inputs.RecordUnverified("te3_page", step, handle);
        reportStep?.Invoke($"{step}: sent, not read back (unverified)");
    }

    /// <summary>
    /// Where to park the pointer: the middle of the title bar, always inside the window. Clamping to
    /// screen zero instead put the point on a different monitor when the window sat on one arranged
    /// above or left of the primary, where the obscured check then aborted a recipe whose selection
    /// had already succeeded.
    /// </summary>
    /// <param name="bounds">The window's screen rectangle.</param>
    /// <returns>A point inside that rectangle.</returns>
    internal static System.Drawing.Point ParkPoint(System.Drawing.Rectangle bounds)
    {
        // A degenerate rectangle contains no point at all, so there is nothing honest to return:
        // clamping into it yields a pixel above or left of the window.
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException($"Window rectangle {bounds} has no interior to park the pointer in.");
        }

        return new(bounds.Left + bounds.Width / 2, Math.Clamp(bounds.Top + 12, bounds.Top, bounds.Bottom - 1));
    }

    // A pointer left on a tree row raises a hover tooltip that covers later menus and captures; park it on the title bar.
    private async Task ParkPointer()
    {
        var owner = host.Sessions.GetInputTarget(handle);
        var bounds =
            Win32Desktop.GetWindowBounds(owner.Hwnd) ?? throw new InvalidOperationException("TE3 window disappeared");
        await MovePointer("TE3 title bar", owner, ParkPoint(bounds), owner.Hwnd);
        await Task.Delay(200);
    }
}
