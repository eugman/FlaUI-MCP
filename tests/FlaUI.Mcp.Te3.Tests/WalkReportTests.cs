using System.Text.Json;
using Xunit;

/// <summary>The walk account te3_navigate object returns (live vte3g walk 7: a miss reported no objectType).</summary>
public sealed class WalkReportTests
{
    private static JsonElement Json(object report) => JsonSerializer.SerializeToElement(report);

    [Fact]
    public void AMissReportsTheRequestedTypeAndAHitTheConfirmedOne()
    {
        var request = new WalkRequest("Brands", "Brand", "Column", "2. Something");
        var miss = new SelectResult(false, null, 22, 24, 0, "not in the folder", false);
        var hit = new SelectResult(true, new TreeRow(3, "Brand", 4), 22, 24, 1, null, false, ConfirmedType: "Column");

        Assert.Equal("Column", Json(WalkAccount.Rows(miss, request)).GetProperty("objectType").GetString());
        Assert.Equal("Column", Json(WalkAccount.Rows(hit, request)).GetProperty("objectType").GetString());
        Assert.Equal(
            "Column",
            Json(WalkAccount.Keys(miss, request, new WalkResult(false, null, 1, "end")))
                .GetProperty("objectType")
                .GetString()
        );
    }

    [Fact]
    public void AnUntypedMissSaysNoTypeAndTheTableTryNamesItsStrategy()
    {
        var request = new WalkRequest("Brands", "Brand", "");
        var byTable = new SelectResult(true, null, 3, 24, 2, null, false, ConfirmedType: "Column") { ByTable = true };

        Assert.Equal(
            JsonValueKind.Null,
            Json(WalkAccount.Rows(new SelectResult(false, null, 0, null, 0, "x", false), request))
                .GetProperty("objectType")
                .ValueKind
        );
        Assert.Equal("rows-by-table", Json(WalkAccount.Rows(byTable, request)).GetProperty("strategy").GetString());
    }
}
