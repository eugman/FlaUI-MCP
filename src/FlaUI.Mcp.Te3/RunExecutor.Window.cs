using System.Diagnostics;
using FlaUI.Mcp.Core;

/// <summary>The run's own TE3 window: finding it after launch, and bringing it to the front.</summary>
public static partial class RunExecutor
{
    // The run's TE3 window: the only top-level window titled with the run's model or database.
    private static async Task<string> WaitForOwnedWindow(
        AutomationHost host,
        Process app,
        string marker,
        CancellationToken cancellationToken
    )
    {
        var startup = Stopwatch.StartNew();
        while (startup.Elapsed < TimeSpan.FromSeconds(90))
        {
            var windows = Win32Desktop
                .GetTopLevelWindows(app.Id)
                .Where(w => !w.IsToolWindow && w.Title.Contains(marker) && w.Title.Contains("Tabular Editor 3"))
                .ToArray();
            if (windows.Length == 1)
            {
                return host.Sessions.RegisterNativeWindow(windows[0].Hwnd, app.Id);
            }

            if (app.HasExited)
            {
                throw new InvalidOperationException("TE3 exited during startup");
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException("TE3 did not open the owned model");
    }

    internal const string ActivationTimeout =
        "Manual activation not received within 120 seconds. The run asked Windows to focus TE3; nothing was typed or clicked.";

    /// <summary>
    /// Launches the run's TE3 through Core's guarded start: the input lease, the mutation check (the run's grant) and
    /// the offline switch, which a raw Process.Start skipped.
    /// </summary>
    internal static Process LaunchTe3(RunManifest manifest, Action save, ProcessStartInfo start)
    {
        RunGrant.RequireInstalled();
        return RunJournal.Launch(manifest, save, start, GuardedDesktop.StartProcess);
    }

    // Asks Windows to focus the run's TE3, then waits for the user to click it if Windows refused; after that wait the
    // grant continues only when a click, and no typing, brought TE3 forward.
    private static async Task ActivateOwnedWindow(
        AutomationHost host,
        string handle,
        string label,
        string journal,
        InputTally inputs,
        Te3Interaction interaction,
        CancellationToken cancellationToken
    )
    {
        await FocusOwnedWindow(host, handle, journal, inputs);
        var target = host.Sessions.GetInputTarget(handle);
        var activation = Stopwatch.StartNew();
        var waited = Win32Desktop.GetForegroundWindow() != target.Hwnd;
        // The user's keys and clicks when asked: only a click may bring TE3 forward (Te3Interaction.Rebaseline).
        var prompt = waited ? interaction.Mark() : null;
        if (waited)
        {
            Console.WriteLine(
                $"Click the TE3 window containing {label}; waiting 120 seconds. Then leave the keyboard and mouse "
                    + "alone: input after that click stops the run."
            );
        }

        while (Win32Desktop.GetForegroundWindow() != target.Hwnd)
        {
            RunSafety.RequireExclusiveTe3(target.ProcessId);
            target.EnsureAlive();
            if (activation.Elapsed > TimeSpan.FromSeconds(120))
            {
                throw new TimeoutException(ActivationTimeout);
            }

            await Task.Delay(250, cancellationToken);
        }

        if (waited)
        {
            interaction.Rebaseline(prompt);
        }
    }

    /// <summary>
    /// Asks Windows to focus the run's TE3 window: journaled, through Core's guarded focus, and counted as input only
    /// when it changed the foreground or restored the window. Refused first, with nothing journaled or sent, while an
    /// earlier call still holds TE3's provider (Core's native-handle focus does not consult the pending tracker), and
    /// without the run's grant.
    /// </summary>
    internal static Task FocusOwnedWindow(AutomationHost host, string handle, string journal, InputTally inputs)
    {
        RunGrant.RequireInstalled();
        if (host.Pending.TryGetPending(host.Sessions.GetInputTarget(handle).ProcessId, out var held))
        {
            throw new ProviderBlockedException(held);
        }

        return RunJournal.Input(
            journal,
            "te3_focus_window",
            new { handle },
            () => inputs.Dispatch(() => host.Sessions.FocusWindow(handle, host.Pending))
        );
    }
}
