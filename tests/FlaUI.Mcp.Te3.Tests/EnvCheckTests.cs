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
}
