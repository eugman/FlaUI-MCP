/// <summary>
/// The end of every run, passed or failed: close the run's own TE3, restore the leased settings, compare the settings
/// folder, release the host and write the results. A stage that fails is recorded and the later stages still run.
/// </summary>
internal sealed class RunCleanup(RunManifest manifest, string settingsDirectory, Action save)
{
    /// <summary>The run's settings lease; null when the run stopped before taking one.</summary>
    public SettingsLease? Settings { get; init; }

    /// <summary>The settings folder as it was before the run; null when the run stopped before reading it.</summary>
    public Dictionary<string, string>? SettingsBefore { get; init; }

    /// <summary>Closes the TE3 the run launched; true when it had to be killed.</summary>
    public required Func<Task<bool>> CloseOwned { get; init; }

    /// <summary>Throws while any TE3 is still running.</summary>
    public required Action RequireNoTe3 { get; init; }

    /// <summary>Releases the run's process and automation host.</summary>
    public Action Release { get; init; } = () => { };

    /// <summary>Writes the run's index page; the real one when null.</summary>
    public Action? WriteIndex { get; init; }

    public async Task Run()
    {
        var errors = new List<string>();
        var closed = await Close(errors);
        if (closed && Settings != null)
        {
            Restore(Settings, errors);
        }

        CompareSettings();
        try
        {
            Release();
        }
        catch (Exception error)
        {
            errors.Add("Disposal: " + error.Message);
        }

        Conclude(closed, errors);
        RunExecutor.FinalizeArtifacts(manifest, save, WriteIndex ?? (() => ArtifactFiles.WriteIndex(manifest)));
        // The backup is needed only while the saved manifest still says recovery is required.
        if (Settings != null && RunOptions.SafeToContinue(manifest) && Settings.DiscardBackup())
        {
            NoteBackupRemoved();
        }
    }

    private void NoteBackupRemoved()
    {
        manifest.SettingsBackupRemoved = true;
        try
        {
            save();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The saved manifest still names the backup; recover on it says it was already removed.
        }
    }

    internal const string KilledNote =
        "TE3 did not exit after its close request and was killed (a save prompt or a busy model can hold it).";

    private async Task<bool> Close(List<string> errors)
    {
        try
        {
            if (await CloseOwned())
            {
                manifest.DiagnosticNotes.Add(KilledNote);
            }

            RequireNoTe3();
            return true;
        }
        catch (Exception error)
        {
            errors.Add("TE3 shutdown: " + error.Message);
            return false;
        }
    }

    private void Restore(SettingsLease settings, List<string> errors)
    {
        try
        {
            settings.Restore();
            manifest.SettingsRestored = true;
        }
        catch (Exception error)
        {
            errors.Add("Preferences: " + error.Message);
        }
    }

    // Compared whether or not TE3 closed cleanly. A run that had to be killed is when settings are most likely to
    // have been left changed, and skipping the comparison there recorded nothing about the case that matters most.
    private void CompareSettings()
    {
        if (SettingsBefore == null)
        {
            return;
        }

        try
        {
            manifest.SettingsChanges = SettingsChanges.Compare(
                SettingsBefore,
                SettingsChanges.Snapshot(settingsDirectory)
            );
        }
        catch (Exception error)
        {
            manifest.SettingsChanges = ["snapshot failed: " + error.Message];
        }
    }

    // Only a leased file left changed is a recovery condition: the restore did not take and a backup exists to put
    // it back. Every other file in the folder is churn TE3 writes on a normal launch, which nothing restores; making
    // those block the run left it permanently "needs recovery" with no action that could clear it, and
    // ArtifactFiles.Promote refuses every capture from such a run.
    private void Conclude(bool closed, List<string> errors)
    {
        var unrestored = SettingsChanges.Unrestored(manifest.SettingsChanges);
        manifest.NeedsRecovery = !closed || !manifest.SettingsRestored || errors.Count > 0 || unrestored.Count > 0;
        if (!manifest.NeedsRecovery)
        {
            return;
        }

        manifest.Passed = false;
        var what =
            errors.Count > 0
                ? string.Join("; ", errors)
                : $"Leased settings left changed after the restore ({string.Join(", ", unrestored)})";
        manifest.CleanupError =
            manifest.SettingsBackup == null
                ? what
                : $"{what.TrimEnd('.')}. Retained settings backup: {manifest.SettingsBackup}";
    }
}
