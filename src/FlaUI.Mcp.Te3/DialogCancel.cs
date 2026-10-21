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

    public static async Task Run(Steps steps)
    {
        await steps.Escape();
        foreach (var button in Buttons)
        {
            try
            {
                await steps.WaitClosed(TimeSpan.FromSeconds(2));
                return;
            }
            catch (TimeoutException) { }

            try
            {
                await steps.Click(await steps.Resolve(button));
            }
            catch (Exception error)
                when (error
                        is InvalidOperationException
                            or TimeoutException
                            or System.Reflection.AmbiguousMatchException
                ) { }
        }

        await steps.WaitClosed(TimeSpan.FromSeconds(5));
    }
}
