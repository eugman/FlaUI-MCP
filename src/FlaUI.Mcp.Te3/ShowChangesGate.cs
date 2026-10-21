/// <summary>
/// Object navigation walks TOM Explorer's filtered tree, and with the Show changes filter on that tree holds only
/// changed objects (empty when nothing is unsaved), so a walk would report the table missing. The filter is turned off
/// first; tom-explorer/changes is the only route that turns it on.
/// </summary>
internal static class ShowChangesGate
{
    public const string StillActive =
        "TOM Explorer show-changes filter active: it did not turn off, so object navigation did not walk the tree. "
        + "Next: clear the Show changes CheckBox in the TOM Explorer toolbar, then navigate again.";

    /// <summary>Turns the filter off when it is on. True when it clicked; a missing checkbox (null) is left alone.</summary>
    /// <param name="isOn">The checkbox state, or null when it is not on screen.</param>
    /// <param name="click">Clicks the checkbox once.</param>
    public static async Task<bool> TurnOff(Func<Task<bool?>> isOn, Func<Task> click)
    {
        if (await isOn() != true)
        {
            return false;
        }

        await click();
        if (await isOn() == true)
        {
            throw new InvalidOperationException(StillActive);
        }

        return true;
    }
}
