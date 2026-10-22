using FlaUI.Mcp.Core;

/// <summary>
/// Closes a dialog without confirming anything: Escape first, then its Cancel button, then the title-bar Close button.
/// Escape can be swallowed by a focused grid (the Layouts dialog has no Cancel button at all).
/// </summary>
internal static class DialogCancel
{
    /// <param name="Escape">Sends Escape to the dialog.</param>
    /// <param name="WaitClosed">Returns once the dialog is gone; throws TimeoutException when it is still open.</param>
    /// <param name="Resolve">The ref of a button in the dialog.</param>
    /// <param name="Click">Clicks a ref.</param>
    public sealed record Steps(
        Func<Task> Escape,
        Func<TimeSpan, Task> WaitClosed,
        Func<ElementSelector, Task<string>> Resolve,
        Func<string, Task> Click
    );

    // Layouts has two title bars and two Close buttons; only the title-bar button has AutomationId "Close".
    public static readonly ElementSelector[] Buttons =
    [
        new(Name: "Cancel", ControlType: "Button"),
        new(Name: "Close", AutomationId: "Close", ControlType: "Button"),
    ];

    /// <summary>
    /// Tries each button only while nothing has been sent: a missing button, or a click Core reports as not
    /// dispatched, moves on to the next one. Once a click may have gone out (completed, partial, pending, or a failure
    /// Core did not report on), nothing else is clicked; if the dialog stays open the click's outcome is reported for
    /// inspection.
    /// </summary>
    public static async Task Run(Steps steps)
    {
        await steps.Escape();
        foreach (var button in Buttons)
        {
            if (await ClosedWithin(steps, TimeSpan.FromSeconds(2)))
            {
                return;
            }

            if (await TryResolve(steps, button) is not { } reference)
            {
                continue;
            }

            try
            {
                await steps.Click(reference);
            }
            catch (Exception error) when (CoreFailure.NotDispatched(error))
            {
                continue;
            }
            catch (Exception error) when (error is InvalidOperationException or AutomationRefusal or TimeoutException)
            {
                await ReportIfStillOpen(steps, button, error);
                return;
            }

            await ReportIfStillOpen(steps, button, null);
            return;
        }

        await steps.WaitClosed(TimeSpan.FromSeconds(5));
    }

    private static async Task<bool> ClosedWithin(Steps steps, TimeSpan timeout)
    {
        try
        {
            await steps.WaitClosed(timeout);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    // A button that cannot be found was never clicked.
    private static async Task<string?> TryResolve(Steps steps, ElementSelector button)
    {
        try
        {
            return await steps.Resolve(button);
        }
        catch (Exception error)
            when (error
                    is ElementNotFoundException
                        or InvalidOperationException
                        or TimeoutException
                        or System.Reflection.AmbiguousMatchException
            )
        {
            return null;
        }
    }

    private static async Task ReportIfStillOpen(Steps steps, ElementSelector button, Exception? clickError)
    {
        if (await ClosedWithin(steps, TimeSpan.FromSeconds(5)))
        {
            return;
        }

        if (clickError == null)
        {
            throw new TimeoutException(
                $"{button.Name} was clicked but the dialog is still open; nothing else was clicked. Inspect it."
            );
        }

        throw new InvalidOperationException(
            $"{button.Name} may have been clicked ({clickError.Message}) and the dialog is still open; "
                + "nothing else was clicked. Inspect it.",
            clickError
        );
    }
}
