using System.Text.Json;
using Xunit;

public sealed class CompanionTests
{
    [Fact]
    public void PreferencesLookupsStayLocalWithoutReroutingMenusOrOtherDialogs()
    {
        Assert.True(Te3Page.IsPreferencesLookup(Te3Page.PreferencesTarget()));
        Assert.True(Te3Page.IsPreferencesLookup(new(new(AutomationId: "searchPreferences"), Te3Page.PreferencesTarget().Selector, true)));
        Assert.True(Te3Page.IsPreferencesLookup(new(new(ControlType: "TreeItem", Value: "Auto Formatting"), new(AutomationId: "treePreferences"), true)));
        Assert.True(Te3Page.IsPreferencesLookup(new(new(Name: "Auto format code as you type"), new(AutomationId: "DAX Editor.Auto Formatting"), true)));
        Assert.False(Te3Page.IsPreferencesLookup(new(new(Name: "Preferences...", ControlType: "Button"), null, true)));
        Assert.False(Te3Page.IsPreferencesLookup(Te3Page.OpenFileNameTarget()));
    }

    [Fact]
    public void InspectSchemaAsksOnlyForProcessAndTopic()
    {
        var schema = JsonSerializer.SerializeToElement(new Te3Companion(null!, "te3_inspect").InputSchema);
        var properties = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(["processId", "topic"], properties);
    }

    [Theory]
    [InlineData("te3_navigate", "destination")]
    [InlineData("te3_capture", "scene")]
    public void DestinationSchemasAndValidationShareTheRegistry(string tool, string key)
    {
        var schema = JsonSerializer.SerializeToElement(new Te3Companion(null!, tool).InputSchema);
        var ids = schema.GetProperty("properties").GetProperty(key).GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Equal(Te3Destinations.Ids, ids);
        foreach (var id in ids)
            Te3Companion.Validate(tool, JsonSerializer.SerializeToElement(new { processId = 1, destination = id, scene = id,
                table = "Sales", objectName = "Amount", objectType = "Column", savePath = "C:/Temp/capture.png" }));
    }

    [Fact]
    public void PreferencesDestinationsNameTheirSectionAndPaneMarker()
        => Assert.All(Te3Destinations.All.Where(d => d.Id.StartsWith("preferences/", StringComparison.Ordinal)),
            d => { Assert.NotNull(d.Section); Assert.NotNull(d.PaneMarker); });

    [Theory]
    [InlineData(null, "not-saved")]
    [InlineData("C:/Temp/fla_capture.png", "saved")]
    public void MatchedSceneDoesNotClaimVisualCompleteness(string? path, string artifact)
    {
        var assessment = JsonSerializer.SerializeToElement(Te3Companion.Assessment(true, path));
        Assert.Equal("matched", assessment.GetProperty("sceneIdentity").GetString());
        Assert.Equal(artifact, assessment.GetProperty("artifact").GetString());
        Assert.Equal(path, assessment.GetProperty("path").GetString());
        Assert.Equal("not-assessed", assessment.GetProperty("visualCompleteness").GetString());
        Assert.False(assessment.GetProperty("docsApproved").GetBoolean());
        Assert.False(assessment.TryGetProperty("verified", out _));
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("verify-captured-scene")]
    public void PostCaptureFailureRetainsSavedArtifactWithoutClaimingSceneMismatch(string phase)
    {
        const string path = "C:/Temp/fla_capture.png";
        var result = Te3Companion.Failure(phase, "check display or scene", "provider changed", null, path);
        Assert.True(result.IsError);
        var response = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        var assessment = response.GetProperty("assessment");
        Assert.Equal("saved", assessment.GetProperty("artifact").GetString());
        Assert.Equal(path, assessment.GetProperty("path").GetString());
        Assert.Equal("not-assessed", assessment.GetProperty("sceneIdentity").GetString());
        Assert.Equal("not-assessed", assessment.GetProperty("visualCompleteness").GetString());
        Assert.False(assessment.GetProperty("docsApproved").GetBoolean());
    }

    [Theory]
    [InlineData("Expected one element; found 0.")]
    [InlineData("UIA Timeout")]
    public void CapturePreconditionErrorPreservesCauseAndGivesConditionalNavigation(string error)
    {
        var request = JsonSerializer.SerializeToElement(new { processId = 123, scene = "object",
            table = "Comparison", objectName = "Amount", objectType = "Column", folder = "Example",
            savePath = "C:/Temp/not-created.png", includeImage = true });
        var result = Te3Companion.Failure("verify-prepared-scene", "Find Name row", error, request);
        Assert.True(result.IsError);
        var response = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal("not-assessed", response.GetProperty("assessment").GetProperty("sceneIdentity").GetString());
        Assert.Equal("not-saved", response.GetProperty("assessment").GetProperty("artifact").GetString());
        Assert.Equal("verify-prepared-scene", response.GetProperty("phase").GetString());
        Assert.Equal("Find Name row", response.GetProperty("lastStep").GetString());
        Assert.Equal(error, response.GetProperty("error").GetString());
        var navigation = response.GetProperty("navigationArguments");
        Te3Companion.Validate("te3_navigate", navigation);
        Assert.Equal("object", navigation.GetProperty("destination").GetString());
        Assert.Equal("Comparison", navigation.GetProperty("table").GetString());
        Assert.Equal("Example", navigation.GetProperty("folder").GetString());
        Assert.False(navigation.TryGetProperty("savePath", out _));
        Assert.False(navigation.TryGetProperty("includeImage", out _));
        Assert.Contains("provider failure does not prove missing selection", response.GetProperty("next").GetString());
    }

    [Theory]
    [InlineData("attach")]
    [InlineData("navigate")]
    [InlineData("capture")]
    [InlineData("verify-captured-scene")]
    public void OtherFailuresDoNotSuggestNavigationReplay(string phase)
    {
        var result = Te3Companion.Failure(phase, "provider call", "UIA Timeout", null);
        var response = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal(phase, response.GetProperty("phase").GetString());
        Assert.False(response.TryGetProperty("navigationArguments", out var navigation) && navigation.ValueKind != JsonValueKind.Null);
    }

    [Theory]
    [InlineData("Failed to click: activation-denied: Windows kept 'WindowsTerminal' (PID 7) in the foreground.", "activation-denied", "title bar")]
    [InlineData("desktop-unavailable: Windows reports no foreground window.", "desktop-unavailable", "locked")]
    public void ActivationBlockersNameTheBlockerInsteadOfSuggestingReplay(string error, string blocker, string guidance)
    {
        var result = Te3Companion.Failure("navigate", "Open Model menu", error, null);

        var response = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal(blocker, response.GetProperty("blocker").GetString());
        Assert.Contains(guidance, response.GetProperty("next").GetString());
        Assert.Contains("earlier steps may have", response.GetProperty("next").GetString());
    }

    [Fact]
    public async Task ValidationFailureIsReportedBeforeAnyAttachment()
    {
        var result = await new Te3Companion(null!, "te3_capture").ExecuteAsync(JsonSerializer.SerializeToElement(new { }));
        Assert.True(result.IsError);
        var response = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal("validate", response.GetProperty("phase").GetString());
    }

    [Fact]
    public void MainWindowIgnoresUntitledPopupsAndPreferences()
    {
        PlaywrightWindows.Mcp.Core.Win32WindowInfo[] windows = [
            new(11, "", 123, true, false, false),
            new(12, "Preferences", 123, true, false, false),
            new(13, "fla_model.bim - Tabular Editor 3.26.3 - Enterprise Edition", 123, false, false, false)];
        Assert.Equal((nint)13, Te3Companion.MainWindow(windows));
        Assert.Throws<InvalidOperationException>(() => Te3Companion.MainWindow(windows[..2]));
        Assert.Throws<InvalidOperationException>(() => Te3Companion.MainWindow([windows[2], windows[2] with { Hwnd = 14 }]));
    }

    [Fact]
    public void NormalWindowClippingIsNotHiddenByCropping()
        => Assert.Throws<InvalidOperationException>(() => Te3Companion.FrameForWindow(new(-8, 0, 1000, 700), new(0, 0, 1920, 1160), false));

    [Theory]
    [InlineData(100, 80, 0, 0)]
    [InlineData(-1820, 80, -1920, 0)]
    public void ContainedNormalWindowIsNotCropped(int x, int y, int monitorX, int monitorY)
        => Assert.Null(Te3Companion.FrameForWindow(new(x, y, 1155, 714), new(monitorX, monitorY, 1920, 1080), false));

    [Fact]
    public void MaximizedBorderCropSupportsNegativeMonitorCoordinates()
    {
        var frame = Te3Companion.FrameForWindow(new(-1928, -8, 1936, 1176), new(-1920, 0, 1920, 1160), true)!;
        Assert.Equal(8, frame.X); Assert.Equal(8, frame.Y);
        Assert.Equal(1920, frame.Width); Assert.Equal(1160, frame.Height);
        frame.Validate();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"processId\":0,\"destination\":\"object\"}")]
    [InlineData("{\"processId\":123,\"destination\":\"execute-script\"}")]
    [InlineData("{\"processId\":123,\"destination\":\"object\",\"objectName\":\"Amount\"}")]
    public void InvalidNavigationRejectedBeforeAttach(string json)
        => Assert.Throws<ArgumentException>(() => Te3Companion.Validate("te3_navigate", JsonDocument.Parse(json).RootElement));

    [Theory]
    [InlineData("Column", "'Comparison'[Amount]")]
    [InlineData("Measure", "'Sales'[Amount]")]
    [InlineData("Column", null)]
    public void WrongOrUnprovenObjectIdentityRejected(string type, string? dax)
        => Assert.Throws<InvalidOperationException>(() => Te3Page.VerifyObjectIdentity("Sales", "Amount", "Column", "Amount", type, dax));

    [Fact]
    public void QualifiedColumnIdentityAccepted()
        => Te3Page.VerifyObjectIdentity("Sales", "Amount", "Column", "Amount", "Data Column", "'Sales'[Amount]");

    [Fact]
    public void EscapedIdentifiersAreComparedExactly()
        => Te3Page.VerifyObjectIdentity("O'Brien", "A]B", "Measure", "A]B", "Measure", "'O''Brien'[A]]B]");
}
