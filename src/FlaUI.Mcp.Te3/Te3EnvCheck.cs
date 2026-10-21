using System.Text.Json;
using FlaUI.Mcp;
using FlaUI.Mcp.Core;

/// <summary>
/// te3_env_check: which TE3 policies and preferences are set, read from the registry and %LocalAppData%\TabularEditor3
/// without touching TE3 or the desktop. Values that change a run (MCP port and auto-start, remembered window state,
/// layouts) are reported; a licence is reported only as present or absent.
/// </summary>
/// <param name="settings">TE3's settings folder; null is the user's.</param>
/// <param name="registry">Reads a registry key's values (HKCU or HKLM path), or null when absent; null reads the real one.</param>
public sealed class Te3EnvCheck(
    string? settings = null,
    Func<string, IReadOnlyDictionary<string, object?>?>? registry = null
) : ToolBase
{
    public const string ToolName = "te3_env_check";
    public const string UserPolicies = @"HKCU\Software\Policies\Tabular Editor ApS\TE3";
    public const string MachinePolicies = @"HKLM\Software\Policies\Tabular Editor ApS\TE3";
    public const string Licence = @"HKCU\Software\Kapacity\Tabular Editor 3";

    private static readonly string[] WindowKeys =
    [
        "RememberWindowState",
        "RememberWindowBounds",
        "MainWindowState",
        "MainWindowRectangle",
        "ShowWhatsNew",
        "ModelSourceDetail",
    ];

    // Said outright: the reply leaves nulls out, and "no policy key" is the finding.
    private const string NotSet = "not set";

    public override string Name => ToolName;

    public override McpToolAnnotations Annotations => McpToolAnnotations.ReadOnly;

    public override string Description =>
        "Read which TE3 policies (HKCU/HKLM Software\\Policies\\Tabular Editor ApS\\TE3) and preferences are set: MCP "
        + "server (enabled, port, auto-start), remembered window state, layouts and keyboard customizations. A licence "
        + "is reported as present or absent only. Reads files and the registry; no TE3 window or input.";

    public override object InputSchema =>
        new
        {
            type = "object",
            properties = new { },
            additionalProperties = false,
        };

    public override Task<McpToolResult> ExecuteAsync(JsonElement? arguments)
    {
        var folder = settings ?? SettingsLease.DefaultDirectory;
        var read = registry ?? RealRegistry.Read;
        var preferences = ReadJson(Path.Combine(folder, "Preferences.json"));
        var ui = ReadJson(Path.Combine(folder, "UiPreferences.json"));
        var layouts = ReadJson(Path.Combine(folder, "Layouts.json"));
        var report = new
        {
            settings = folder,
            policies = new
            {
                user = (object?)read(UserPolicies) ?? NotSet,
                machine = (object?)read(MachinePolicies) ?? NotSet,
            },
            licence = read(Licence)?.ToDictionary(v => v.Key, _ => true),
            mcp = Property(preferences, "Mcp"),
            window = ui is { } u
                ? WindowKeys.Where(k => u.TryGetProperty(k, out _)).ToDictionary(k => k, k => u.GetProperty(k))
                : null,
            keyboardCustomizations = Property(ui, "KeyboardCustomizations"),
            layouts = layouts is { } l
                ? new { version = Property(l, "Version"), count = Property(l, "Layouts")?.GetArrayLength() }
                : null,
        };
        return Task.FromResult(TextResult(JsonSerializer.Serialize(report, McpProtocol.JsonOptions)));
    }

    private static JsonElement? ReadJson(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? Property(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var value) ? value : null;

    private static class RealRegistry
    {
        public static IReadOnlyDictionary<string, object?>? Read(string path)
        {
            var hive = path.StartsWith("HKLM", StringComparison.Ordinal)
                ? Microsoft.Win32.Registry.LocalMachine
                : Microsoft.Win32.Registry.CurrentUser;
            using var key = hive.OpenSubKey(path[5..]);
            return key?.GetValueNames().ToDictionary(n => n, n => key.GetValue(n));
        }
    }
}
