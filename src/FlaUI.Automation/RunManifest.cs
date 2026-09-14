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
    public int ProcessId { get; set; }
    public long ProcessStartedTicks { get; set; }
    public string? Te3Version { get; set; }
    public string? SettingsBackup { get; set; }
    public bool SettingsRestored { get; set; } = true;
    public bool NeedsRecovery { get; set; }
    public string? CleanupError { get; set; }
    public bool Passed { get; set; }
    public string? Error { get; set; }
    public string? CurrentStep { get; set; }
    public string? ErrorDetails { get; set; }
    public List<TestResult> Tests { get; set; } = [];
    public Dictionary<string, string> Screenshots { get; set; } = [];
    public Dictionary<string, string> ScreenshotHashes { get; set; } = [];
    public string? CaptureVariant { get; set; }
    public int? ExpectedDpi { get; set; }
    public Dictionary<string, CaptureEnvironment> CaptureEnvironments { get; set; } = [];
    public Dictionary<string, CompositionSpec> Compositions { get; set; } = [];
    public Dictionary<string, string> SourceHashes { get; set; } = [];
    public Dictionary<string, string> ModelChecks { get; set; } = [];
}
