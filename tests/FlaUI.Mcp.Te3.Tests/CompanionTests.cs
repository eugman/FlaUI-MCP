using System.Text.Json;
using Xunit;

public sealed class CompanionTests
{
    [Fact]
    public void PreferencesLookupsStayLocalWithoutReroutingMenusOrOtherDialogs()
    {
        Assert.True(Te3Page.IsPreferencesLookup(Te3Page.PreferencesTarget()));
        Assert.True(
            Te3Page.IsPreferencesLookup(
                new(new(AutomationId: "searchPreferences"), Te3Page.PreferencesTarget().Selector, true)
            )
        );
        Assert.True(
            Te3Page.IsPreferencesLookup(
                new(new(ControlType: "TreeItem", Value: "Auto Formatting"), new(AutomationId: "treePreferences"), true)
            )
        );
        Assert.True(
            Te3Page.IsPreferencesLookup(
                new(new(Name: "Auto format code as you type"), new(AutomationId: "DAX Editor.Auto Formatting"), true)
            )
        );
        Assert.False(Te3Page.IsPreferencesLookup(new(new(Name: "Preferences...", ControlType: "Button"), null, true)));
        Assert.False(Te3Page.IsPreferencesLookup(Te3Page.OpenFileNameTarget()));
    }

    [Fact]
    // Navigation plus a read-only status-bar observation. Nothing that edits, runs or dismisses.
    public void OnlyNavigationAndObservationAreExposed() =>
        Assert.Equal(["te3_navigate", "te3_model_state"], Te3Companion.Names);

    [Fact]
    public void FolderSchemaAndMissMessagePointToDisplayFolders()
    {
        var schema = JsonSerializer.SerializeToElement(new Te3Companion(null!, "te3_navigate").InputSchema);
        Assert.Contains(
            "display folder",
            schema.GetProperty("properties").GetProperty("folder").GetProperty("description").GetString()
        );
        Assert.Contains("pass folder", Te3Page.ObjectNotFoundMessage("Sales", "Total Amount"));
    }

    [Fact]
    public void NavigationSchemaAndValidationShareTheRegistry()
    {
        var schema = JsonSerializer.SerializeToElement(new Te3Companion(null!, "te3_navigate").InputSchema);
        var ids = schema
            .GetProperty("properties")
            .GetProperty("destination")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToArray();
        Assert.Equal(Te3Destinations.Ids, ids);
        foreach (var id in ids)
        {
            Te3Companion.Validate(
                JsonSerializer.SerializeToElement(
                    new
                    {
                        processId = 1,
                        destination = id,
                        table = "Sales",
                        objectName = "Amount",
                        objectType = "Column",
                    }
                ),
                Te3Companion.Navigate
            );
        }
    }

    [Fact]
    public void PreferencesDestinationsNameTheirSectionAndPaneMarker() =>
        Assert.All(
            Te3Destinations.All.Where(d => d.Id.StartsWith("preferences/", StringComparison.Ordinal)),
            d =>
            {
                Assert.NotNull(d.Section);
                Assert.NotNull(d.PaneMarker);
            }
        );

    [Fact]
    public void MatchedSceneDoesNotClaimVisualCompleteness()
    {
        var assessment = JsonSerializer.SerializeToElement(Te3Companion.Assessment(true));
        Assert.Equal("matched", assessment.GetProperty("sceneIdentity").GetString());
        Assert.Equal("not-assessed", assessment.GetProperty("visualCompleteness").GetString());
        Assert.False(assessment.GetProperty("docsApproved").GetBoolean());
    }

    [Theory]
    [InlineData("attach")]
    [InlineData("navigate")]
    [InlineData("verify-arrival")]
    public void FailuresDoNotClaimTheSceneOrReplayInput(string phase)
    {
        var result = Te3Companion.Failure(phase, "provider call", "UIA Timeout");
        Assert.True(result.IsError);
        var response = JsonDocument.Parse(result.Content[0].Text!).RootElement;
        Assert.Equal(phase, response.GetProperty("phase").GetString());
        Assert.Equal("not-assessed", response.GetProperty("assessment").GetProperty("sceneIdentity").GetString());
        Assert.Contains("no uncertain input", response.GetProperty("next").GetString());
    }

    [Theory]
    [InlineData(
        "Failed to click: activation-denied: Windows kept 'WindowsTerminal' (PID 7) in the foreground.",
        "activation-denied",
        "title bar"
    )]
    [InlineData("desktop-unavailable: Windows reports no foreground window.", "desktop-unavailable", "locked")]
    public void ActivationBlockersNameTheBlockerInsteadOfSuggestingReplay(string error, string blocker, string guidance)
    {
        var response = JsonDocument
            .Parse(Te3Companion.Failure("navigate", "Open Model menu", error).Content[0].Text!)
            .RootElement;
        Assert.Equal(blocker, response.GetProperty("blocker").GetString());
        Assert.Contains(guidance, response.GetProperty("next").GetString());
        Assert.Contains("earlier steps may have", response.GetProperty("next").GetString());
    }

    [Fact]
    public async Task ValidationFailureIsReportedBeforeAnyAttachment()
    {
        var result = await new Te3Companion(null!, "te3_navigate").ExecuteAsync(
            JsonSerializer.SerializeToElement(new { })
        );
        Assert.True(result.IsError);
        Assert.Equal(
            "validate",
            JsonDocument.Parse(result.Content[0].Text!).RootElement.GetProperty("phase").GetString()
        );
    }

    [Fact]
    public void MainWindowIgnoresUntitledPopupsAndPreferences()
    {
        FlaUI.Mcp.Core.Win32WindowInfo[] windows =
        [
            new(11, "", 123, true, false, false),
            new(12, "Preferences", 123, true, false, false),
            new(13, "fla_model.bim - Tabular Editor 3.26.3 - Enterprise Edition", 123, false, false, false),
        ];
        Assert.Equal((nint)13, Te3Companion.MainWindow(windows));
        Assert.Throws<InvalidOperationException>(() => Te3Companion.MainWindow(windows[..2]));
        Assert.Throws<InvalidOperationException>(() =>
            Te3Companion.MainWindow([windows[2], windows[2] with { Hwnd = 14 }])
        );
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"processId\":0,\"destination\":\"object\"}")]
    [InlineData("{\"processId\":123,\"destination\":\"execute-script\"}")]
    [InlineData("{\"processId\":123,\"destination\":\"object\",\"objectName\":\"Amount\"}")]
    public void InvalidNavigationRejectedBeforeAttach(string json) =>
        Assert.Throws<ArgumentException>(() =>
            Te3Companion.Validate(JsonDocument.Parse(json).RootElement, Te3Companion.Navigate)
        );

    [Theory]
    [InlineData("Amount", "Data Column", "Comparison")]
    [InlineData("Amount", "Measure", "Sales")]
    [InlineData("Amount", null, "Sales")]
    [InlineData("Total", "Data Column", "Sales")]
    [InlineData("Amount", "Data Column", null)]
    public void WrongOrUnprovenArrivalRejected(string value, string? type, string? walkedTable) =>
        Assert.Throws<InvalidOperationException>(() =>
            Te3Page.VerifyArrival("Sales", "Amount", "Column", new(value, type), walkedTable)
        );

    [Theory]
    [InlineData("Table", "Table", true)]
    [InlineData("Table", "Calculated Table", true)]
    [InlineData("Table", "Calculated Table Column", false)]
    [InlineData("Column", "Calculated Table Column", true)]
    [InlineData("Column", "Level", false)]
    [InlineData("Measure", null, false)]
    public void SameNamedRowsArePickedByObjectType(string requested, string? observed, bool expected) =>
        Assert.Equal(expected, Te3Page.ObjectTypeMatches(requested, observed));

    [Theory]
    [InlineData("Calculated Table \"Date\"", "Calculated Table")]
    [InlineData("Calculated Table Column \"Calenc", "Calculated Table Column")]
    [InlineData("Hierarchy \"Date Hierarchy\"", "Hierarchy")]
    [InlineData("(Nothing selected)", null)]
    [InlineData(null, null)]
    public void StatusBarSelectionGivesTheObjectType(string? selection, string? expected) =>
        Assert.Equal(expected, Te3Page.SelectionType(selection));

    [Fact]
    public void ColumnArrivalAccepted() =>
        Te3Page.VerifyArrival("Sales", "Amount", "Column", new("Amount", "Data Column"), "Sales");

    [Fact]
    public void TableArrivalUsesTheTableName() =>
        Te3Page.VerifyArrival("Date", "Date", "Table", new("Date", "Calculated Table"), "Date");

    [Fact]
    public void ProcessIdIsOptionalWhenOneTe3IsRunning()
    {
        var withoutId = JsonDocument.Parse("""{"destination":"tom-explorer"}""").RootElement;
        Te3Companion.Validate(withoutId, Te3Companion.Navigate);

        Assert.Equal(42, Te3Companion.ResolveProcessId(withoutId, () => [42]));
        Assert.Throws<InvalidOperationException>(() => Te3Companion.ResolveProcessId(withoutId, () => []));
        var several = Assert.Throws<ArgumentException>(() => Te3Companion.ResolveProcessId(withoutId, () => [1, 2]));
        Assert.Contains("pass processId", several.Message);
    }

    [Fact]
    public void ExplicitProcessIdWinsOverDiscovery()
    {
        var withId = JsonDocument.Parse("""{"processId":7,"destination":"tom-explorer"}""").RootElement;
        Assert.Equal(7, Te3Companion.ResolveProcessId(withId, () => [1, 2]));
    }

    [Fact]
    public void NonNumericProcessIdIsRejectedWithAClearMessage()
    {
        var text = JsonDocument.Parse("""{"processId":"123","destination":"tom-explorer"}""").RootElement;
        var error = Assert.Throws<ArgumentException>(() => Te3Companion.Validate(text, Te3Companion.Navigate));
        Assert.Contains("positive integer", error.Message);
    }

    [Fact]
    public void NullProcessIdMeansNotGiven()
    {
        var nullId = JsonDocument.Parse("""{"processId":null,"destination":"tom-explorer"}""").RootElement;
        Te3Companion.Validate(nullId, Te3Companion.Navigate);
        Assert.Equal(42, Te3Companion.ResolveProcessId(nullId, () => [42]));
    }
}
