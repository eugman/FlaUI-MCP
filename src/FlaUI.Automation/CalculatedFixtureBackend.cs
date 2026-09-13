using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Reset transport for source-free calculated fixtures only. Never use metadata
/// replacement for a processed SpaceParts copy: that requires a backup adapter.
/// </summary>
public sealed class CalculatedFixtureBackend(string executable, ICliRunner cli) : IFixtureSlotBackend
{
    public async Task Reset(SlotRegistration slot) => await Execute(slot, "reset");
    public async Task Verify(SlotRegistration slot) => await Execute(slot, "verify");
    public async Task Remove(SlotRegistration slot) => await Execute(slot, "remove");
    private async Task Execute(SlotRegistration slot, string operation)
    {
        FixtureSlots.Validate(slot);
        var result = await cli.Run(executable, new[] { "script", slot.BaselinePath, "-e", BuildScript(slot, operation) });
        CliProtocol.Require(result, "FLA_SLOT:" + slot.Owner + ":" + operation);
    }
    public static string BuildScript(SlotRegistration slot, string operation)
    {
        FixtureSlots.Validate(slot);
        if (operation is not ("reset" or "verify" or "remove")) throw new ArgumentException("Unknown slot operation");
        var db = JsonNode.Parse(File.ReadAllText(slot.BaselinePath))!.AsObject();
        var model = db["model"]?.AsObject() ?? throw new InvalidOperationException("Baseline must be a BIM database");
        if ((model["dataSources"]?.AsArray().Count ?? 0) != 0 || (model["roles"]?.AsArray().Count ?? 0) != 0)
            throw new InvalidOperationException("Calculated fixture cannot contain sources or roles");
        var tables = model["tables"]?.AsArray() ?? throw new InvalidOperationException("Baseline requires tables");
        if (tables.Count == 0 || tables.Any(t => t?["partitions"] is not JsonArray parts || parts.Count == 0 ||
            parts.Any(p => p?["source"]?["type"]?.GetValue<string>() != "calculated")))
            throw new InvalidOperationException("Only source-free calculated partitions can use this reset adapter");
        db["name"] = slot.Database; db["id"] = slot.Database;
        var annotations = model["annotations"] as JsonArray ?? new JsonArray();
        if (model["annotations"] == null) model["annotations"] = annotations;
        foreach (var name in new[] { "FlaUISlotOwner", "FlaUIBaselineHash" })
            foreach (var old in annotations.Where(a => a?["name"]?.GetValue<string>() == name).ToArray()) annotations.Remove(old);
        annotations.Add(new JsonObject { ["name"] = "FlaUISlotOwner", ["value"] = slot.Owner });
        annotations.Add(new JsonObject { ["name"] = "FlaUIBaselineHash", ["value"] = slot.BaselineHash });
        var payload = new JsonObject { ["createOrReplace"] = new JsonObject {
            ["object"] = new JsonObject { ["database"] = slot.Database }, ["database"] = db } }.ToJsonString();
        string Q(string value) => JsonSerializer.Serialize(value);
        var connection = new System.Data.Common.DbConnectionStringBuilder();
        connection["Data Source"] = slot.Server;
        var mutation = operation switch
        {
            "reset" => $$"""
                var result = server.Execute({{Q(payload)}});
                if (result.ContainsErrors) throw new Exception("Fixture replacement failed");
                // Reconnect to reload metadata after executing TMSL. DatabaseCollection
                // has no Refresh method in the TOM version shipped with TE CLI.
                server.Disconnect();
                server.Connect({{Q(connection.ConnectionString)}});
                db = server.Databases.Find({{Q(slot.Database)}});
                if (db == null || db.Name != {{Q(slot.Database)}} || db.Model == null ||
                    db.Model.Annotations.Find("FlaUISlotOwner")?.Value != {{Q(slot.Owner)}})
                    throw new Exception("Slot ownership mismatch after replacement; refusing refresh");
                db.Model.RequestRefresh(Microsoft.AnalysisServices.Tabular.RefreshType.Calculate);
                db.Model.SaveChanges();
                """,
            "remove" => "if (db != null) db.Drop();",
            _ => $$"""
                if (db == null || db.Model.Annotations.Find("FlaUIBaselineHash")?.Value != {{Q(slot.BaselineHash)}} || db.Model.Tables.Count != {{tables.Count}})
                    throw new Exception("Fixture baseline verification failed");
                foreach (var table in db.Model.Tables)
                    foreach (var partition in table.Partitions)
                        if (partition.State != Microsoft.AnalysisServices.Tabular.ObjectState.Ready)
                            throw new Exception("Fixture partition is not ready");
                """
        };
        return $$"""
            using (var server = new Microsoft.AnalysisServices.Tabular.Server()) {
                server.Connect({{Q(connection.ConnectionString)}});
                var db = server.Databases.Find({{Q(slot.Database)}});
                var named = server.Databases.FindByName({{Q(slot.Database)}});
                if (named != null && named.ID != {{Q(slot.Database)}})
                    throw new Exception("Slot name belongs to another database ID; refusing mutation");
                if (db != null && (db.ID != {{Q(slot.Database)}} || db.Name != {{Q(slot.Database)}} || db.Model == null ||
                    db.Model.Annotations.Find("FlaUISlotOwner")?.Value != {{Q(slot.Owner)}}))
                    throw new Exception("Slot ownership mismatch; refusing mutation");
                {{mutation}}
                Output({{Q("FLA_SLOT:" + slot.Owner + ":" + operation)}});
                server.Disconnect();
            }
            """;
    }
}
