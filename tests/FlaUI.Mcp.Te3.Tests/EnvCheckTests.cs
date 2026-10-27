using System.Text.Json;
using FlaUI.Mcp.Core;
using Xunit;

namespace FlaUI.Mcp.Te3.Tests;

/// <summary>te3_env_check reads which TE3 policies and preferences are set; a temporary folder and a scripted registry.</summary>
public sealed class EnvCheckTests : IDisposable
{
    private readonly string settings = Path.Combine(Path.GetTempPath(), "fla_envcheck_" + Guid.NewGuid().ToString("N"));

    public EnvCheckTests()
    {
        Directory.CreateDirectory(settings);
        File.WriteAllText(
            Path.Combine(settings, "Preferences.json"),
            """{"Mcp":{"Enabled":true,"Port":42100,"AutoStart":false,"AccessToken":"tok-123"},"Other":1}"""
        );
        File.WriteAllText(
            Path.Combine(settings, "UiPreferences.json"),
            """{"RememberWindowState":true,"RememberWindowBounds":true,"MainWindowState":"Normal","ShowWhatsNew":true,"KeyboardCustomizations":{"Custom":{}}}"""
        );
        File.WriteAllText(Path.Combine(settings, "Layouts.json"), """{"Version":2,"Layouts":[]}""");
    }

    public void Dispose() => Directory.Delete(settings, true);

    private static IReadOnlyDictionary<string, object?>? Registry(string key) =>
        key switch
        {
            Te3EnvCheck.UserPolicies => new Dictionary<string, object?>
            {
                ["DisableUpdates"] = 1,
                ["McpAccessTokenValue"] = "tok-456",
            },
            Te3EnvCheck.Licence => new Dictionary<string, object?> { ["LicenseKey"] = "SECRET-KEY", ["User"] = "u" },
            _ => null,
        };

    private async Task<JsonElement> Check()
    {
        var result = await new Te3EnvCheck(settings, Registry).ExecuteAsync(JsonSerializer.SerializeToElement(new { }));
        Assert.NotEqual(true, result.IsError);
        return JsonDocument.Parse(result.Content.Single().Text!).RootElement;
    }

    [Fact]
    public async Task ItReportsThePoliciesThatAreSetAndThoseThatAreAbsent()
    {
        var report = await Check();

        Assert.Equal(1, report.GetProperty("policies").GetProperty("user").GetProperty("DisableUpdates").GetInt32());
        Assert.Equal("not set", report.GetProperty("policies").GetProperty("machine").GetString());
    }

    [Fact]
    public async Task ItReportsTheMcpAndWindowPreferencesItReads()
    {
        var report = await Check();

        Assert.Equal(42100, report.GetProperty("mcp").GetProperty("Port").GetInt32());
        Assert.False(report.GetProperty("mcp").GetProperty("AutoStart").GetBoolean());
        Assert.Equal("Normal", report.GetProperty("window").GetProperty("MainWindowState").GetString());
        Assert.Equal(2, report.GetProperty("layouts").GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task ALicenceIsReportedAsPresentNeverByItsValue()
    {
        var report = await Check();

        Assert.True(report.GetProperty("licence").GetProperty("LicenseKey").GetBoolean());
        Assert.DoesNotContain("SECRET-KEY", report.GetRawText());
    }

    [Fact]
    public void ItIsAReadOnlyTool() => Assert.Equal(McpToolAnnotations.ReadOnly, new Te3EnvCheck().Annotations);

    [Fact]
    public async Task OnlyTheMcpSettingsItNamesAreReportedAndSecretsAreMasked()
    {
        var report = await Check();

        Assert.False(report.GetProperty("mcp").TryGetProperty("AccessToken", out _));
        Assert.Equal(
            "(masked)",
            report.GetProperty("policies").GetProperty("user").GetProperty("McpAccessTokenValue").GetString()
        );
        Assert.DoesNotContain("tok-", report.GetRawText());
    }

    [Fact]
    public async Task ItReadsAllSixPolicyKeysAndReportsTheFirstValueForEachName()
    {
        var visited = new List<string>();
        IReadOnlyDictionary<string, object?>? Read(string path)
        {
            visited.Add(path);
            return path switch
            {
                @"HKLM\Software\Policies\Tabular Editor ApS\TE3" => new Dictionary<string, object?>
                {
                    ["DisableAi"] = 0,
                },
                @"HKLM\Software\Policies\Tabular Editor ApS" => new Dictionary<string, object?>
                {
                    ["disablemcpserver"] = 1,
                    ["McpAccessTokenValue"] = "private-token",
                },
                @"HKLM\Software\Policies\Kapacity\Tabular Editor" => new Dictionary<string, object?>
                {
                    ["DisableTelemetry"] = 1,
                },
                @"HKCU\Software\Policies\Tabular Editor ApS\TE3" => new Dictionary<string, object?>
                {
                    ["DisableAi"] = 1,
                    ["DisableMcpServer"] = 0,
                },
                @"HKCU\Software\Policies\Tabular Editor ApS" => new Dictionary<string, object?>
                {
                    ["DisableTelemetry"] = 0,
                },
                @"HKCU\Software\Policies\Kapacity\Tabular Editor" => new Dictionary<string, object?>
                {
                    ["RequireMcpAccessToken"] = 1,
                },
                _ => null,
            };
        }

        var result = await new Te3EnvCheck(settings, Read).ExecuteAsync(JsonSerializer.SerializeToElement(new { }));
        var report = JsonDocument.Parse(result.Content.Single().Text!).RootElement;
        var policies = report.GetProperty("policies");
        var effective = policies.GetProperty("effective");

        Assert.Equal(6, visited.Count(path => path.Contains(@"\Policies\")));
        Assert.Equal(0, effective.GetProperty("DisableAi").GetProperty("value").GetInt32());
        Assert.Equal(
            @"HKLM\Software\Policies\Tabular Editor ApS\TE3",
            effective.GetProperty("DisableAi").GetProperty("source").GetString()
        );
        Assert.Equal(1, effective.GetProperty("disablemcpserver").GetProperty("value").GetInt32());
        Assert.Equal(
            @"HKLM\Software\Policies\Tabular Editor ApS",
            effective.GetProperty("disablemcpserver").GetProperty("source").GetString()
        );
        Assert.Equal(1, effective.GetProperty("DisableTelemetry").GetProperty("value").GetInt32());
        Assert.Equal(1, effective.GetProperty("RequireMcpAccessToken").GetProperty("value").GetInt32());
        Assert.Equal("(masked)", effective.GetProperty("McpAccessTokenValue").GetProperty("value").GetString());
        Assert.DoesNotContain("private-token", report.GetRawText());
        Assert.Equal(
            1,
            policies.GetProperty("locations").GetProperty("machineShared").GetProperty("disablemcpserver").GetInt32()
        );
    }
}
