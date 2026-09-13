public sealed record RecoveryResult(bool NeedsRecovery, string[] Errors);

/// <summary>One shutdown/reset/restore sequence for normal runs and explicit recovery.</summary>
public static class RecoveryCoordinator
{
    public static Task<RecoveryResult> Recover(RunManifest manifest, bool restoreSettings, Action save,
        SettingsLease? currentSettings = null)
    {
        async Task Close()
        {
            RunSafety.RequireExclusiveTe3(manifest.ProcessId);
            await RunSafety.CloseOwned(manifest.ProcessId, manifest.ProcessStartedTicks, manifest.Te3Path);
            RunSafety.RequireExclusiveTe3();
        }
        Action? restore = null;
        if (restoreSettings && !manifest.SettingsRestored)
        {
            if (currentSettings != null) restore = currentSettings.Restore;
            else if (manifest.SettingsBackup != null) restore = () => SettingsLease.RestoreBackup(manifest.SettingsBackup);
        }
        return Complete(manifest, Close, () => ResetResource(manifest), restore, save);
    }

    // Side-effect seams keep lifecycle ordering testable without UI or a database.
    internal static async Task<RecoveryResult> Complete(RunManifest manifest, Func<Task> close,
        Func<Task> resetResource, Action? restoreSettings, Action save)
    {
        var errors = new List<string>();
        var safe = false;
        try
        {
            try { await close(); safe = true; }
            catch (Exception ex) { errors.Add("Process shutdown: " + ex.Message); }
            if (safe)
            {
                try { await resetResource(); }
                catch (Exception ex) { errors.Add("Database: " + ex.Message); }
                // A database failure must not prevent restoration of the user's preferences.
                if (restoreSettings != null && !manifest.SettingsRestored)
                {
                    try { restoreSettings(); manifest.SettingsRestored = true; }
                    catch (Exception ex) { errors.Add("Preferences: " + ex.Message); }
                }
            }
        }
        finally
        {
            manifest.RecoveryErrors.AddRange(errors);
            manifest.NeedsRecovery = !safe || errors.Count > 0 || !manifest.SettingsRestored || manifest.ResourceNeedsRecovery;
            if (manifest.NeedsRecovery)
            {
                manifest.Passed = false;
                manifest.CleanupError = errors.Count > 0 ? string.Join("; ", errors) : "Resource or preference restoration remains incomplete";
            }
            // Keep previous failure evidence on a successful recovery; recovery is not a test rerun.
            save();
        }
        return new(manifest.NeedsRecovery, errors.ToArray());
    }

    private static async Task ResetResource(RunManifest manifest)
    {
        if (manifest.Slot is not { } slot)
        {
            return;
        }
        if (manifest.SlotReset) return;
        var registry = manifest.SlotRegistry ?? throw new InvalidOperationException("Missing fixed-slot registry");
        var slots = new FixtureSlots(registry, new CalculatedFixtureBackend(manifest.Te, new CliRunner()));
        var registered = slots.Status().SingleOrDefault(s => s.Database == slot.Database);
        // An old run must never overwrite a newer run's fixture registration.
        if (registered == null || registered with { Dirty = true, PendingOperation = null } != slot with { Dirty = true, PendingOperation = null })
            throw new InvalidOperationException("Slot registration changed; use fixture-reset for the current registration");
        await slots.Reset(slot);
        manifest.SlotReset = true;
        manifest.CleanupOutcome = "ResetToBaseline";
    }
}
