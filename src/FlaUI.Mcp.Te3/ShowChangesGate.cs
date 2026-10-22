/// <summary>
/// Object navigation walks TOM Explorer's filtered tree, and with the Show changes filter on that tree holds only
/// changed objects (empty when nothing is unsaved), so a walk would report the table missing. The filter is turned off
/// first; tom-explorer/changes turns it on and tom-explorer/changes-off turns it off.
/// </summary>
internal static class ShowChangesGate
{
    public const string StillActive =
        "TOM Explorer show-changes filter active: it did not turn off, so object navigation did not walk the tree. "
        + "Next: clear the Show changes CheckBox in the TOM Explorer toolbar, then navigate again.";

    public const string Vanished =
        "TOM Explorer's Show changes checkbox vanished after the click, so whether the filter is off is unknown; "
        + "object navigation did not walk the tree. Observe TOM Explorer before navigating again.";

    /// <summary>Turns the filter off when it is on. True when it clicked; a missing checkbox (null) is left alone.</summary>
    /// <param name="isOn">The checkbox state, or null when it is not on screen.</param>
    /// <param name="click">Clicks the checkbox once.</param>
    /// <param name="waitMs">How long the checkbox may still read on after the click.</param>
    /// <param name="pollMs">Pause between reads.</param>
    /// <param name="delay">The pause, Task.Delay when null; tests pass one that does not sleep.</param>
    public static async Task<bool> TurnOff(
        Func<Task<bool?>> isOn,
        Func<Task> click,
        int waitMs = SettleMs,
        int pollMs = PollMs,
        Func<int, Task>? delay = null
    )
    {
        if (await isOn() != true)
        {
            return false;
        }

        await click();
        for (var waited = 0; ; waited += pollMs)
        {
            var now = await isOn();
            if (now == null)
            {
                throw new FlaUI.Mcp.Core.ElementNotFoundException(Vanished);
            }

            if (now == false)
            {
                return true;
            }

            if (waited >= waitMs)
            {
                throw new InvalidOperationException(StillActive);
            }

            await (delay ?? Task.Delay)(pollMs);
        }
    }

    private const int SettleMs = 1000;
    private const int PollMs = 200;
}
