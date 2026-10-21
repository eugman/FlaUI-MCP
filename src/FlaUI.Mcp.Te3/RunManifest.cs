public record TestResult(string Name, string Status, long DurationMs, string DocsPage);

/// <summary>Results and durable information required to undo acquired resources.</summary>
public class RunManifest
{
    public int SchemaVersion { get; set; } = 3;
    public string RunId { get; set; } = "";
    public string? RequestedScenario { get; set; }
    public string Database { get; set; } = "";
    public string Server { get; set; } = "";
    public string Te { get; set; } = "";
    public string Te3Path { get; set; } = "";
    public string Output { get; set; } = "";
    public string? DocsRoot { get; set; }

    /// <summary>UTC ticks when the runner was about to start TE3; with ProcessId 0, a TE3 may be running unrecorded.</summary>
    public long LaunchRequestedTicks { get; set; }
    public int ProcessId { get; set; }
    public long ProcessStartedTicks { get; set; }
    public string? Te3Version { get; set; }
    public string? SettingsBackup { get; set; }
    public bool SettingsRestored { get; set; } = true;
    public bool NeedsRecovery { get; set; }
    public string? CleanupError { get; set; }

    /// <summary>Why the failing call's outcome is missing from actions.jsonl; null when the journal has it.</summary>
    public string? JournalError { get; set; }

    /// <summary>Settings files changed by the run beyond the restored four; warnings, not failures.</summary>
    public List<string> SettingsChanges { get; set; } = [];
    public bool Passed { get; set; }
    public string? Error { get; set; }
    public string? CurrentStep { get; set; }
    public string? ErrorDetails { get; set; }

    /// <summary>Why a failure capture (failure.png, windows_find, failure-windows.json) is missing, one line each.</summary>
    public List<string> DiagnosticNotes { get; set; } = [];
    public List<TestResult> Tests { get; set; } = [];
    public Dictionary<string, string> Screenshots { get; set; } = [];
    public Dictionary<string, string> ScreenshotHashes { get; set; } = [];
    public string? CaptureVariant { get; set; }
    public int? ExpectedDpi { get; set; }
    public Dictionary<string, CaptureEnvironment> CaptureEnvironments { get; set; } = [];
    public Dictionary<string, CompositionSpec> Compositions { get; set; } = [];
    public Dictionary<string, AnnotationSpec> Annotations { get; set; } = [];
    public Dictionary<string, string> SourceHashes { get; set; } = [];
    public Dictionary<string, string> ModelChecks { get; set; } = [];

    /// <summary>Fields this version does not know, kept so rewriting a newer runner's manifest loses nothing.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Unknown { get; set; }
}
