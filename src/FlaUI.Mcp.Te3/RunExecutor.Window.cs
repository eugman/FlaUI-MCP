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

    // Asks Windows to focus the run's TE3, then waits for the user to click it if Windows refused.
    private static async Task ActivateOwnedWindow(
        AutomationHost host,
        string handle,
        int processId,
        string label,
        string journal,
        InputTally inputs,
        CancellationToken cancellationToken
    )
    {
        await FocusOwnedWindow(host, handle, journal, inputs);
        var target = host.Sessions.GetInputTarget(handle);
        var activation = Stopwatch.StartNew();
        if (Win32Desktop.GetForegroundWindow() != target.Hwnd)
        {
            Console.WriteLine($"Click the TE3 window containing {label}; waiting 120 seconds.");
        }

        while (Win32Desktop.GetForegroundWindow() != target.Hwnd)
        {
            RunSafety.RequireExclusiveTe3(processId);
            target.EnsureAlive();
            if (activation.Elapsed > TimeSpan.FromSeconds(120))
            {
                throw new TimeoutException(ActivationTimeout);
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    /// <summary>
    /// Asks Windows to focus the run's TE3 window: journaled, through Core's guarded and pending-aware focus, and
    /// counted as input only when it changed the foreground or restored the window.
    /// </summary>
    internal static Task FocusOwnedWindow(AutomationHost host, string handle, string journal, InputTally inputs) =>
        RunJournal.Input(
            journal,
            "te3_focus_window",
            new { handle },
            () => inputs.Dispatch(() => host.Sessions.FocusWindow(handle, host.Pending))
        );
}
