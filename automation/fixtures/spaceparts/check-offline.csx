// Fails if the offline SpaceParts fixture lacks an object the wave 2 recipes rely on.
var missing = new List<string>();
void Require(bool present, string what) { if (!present) missing.Add(what); }

Require(Model.GetAnnotation("FlaUIFixture") == "spaceparts-backlog-1", "FlaUIFixture annotation");
Require(Model.GetAnnotation("TabularEditor_SerializeOptions") != null, "SerializeOptions annotation");
Require(Model.Roles.Count >= 2 && Model.Roles.Any(r => r.TablePermissions.Any()), "roles with table permissions");
Require(Model.Tables["Budget"].ObjectLevelSecurity[Model.Roles["Territory Managers"]] == MetadataPermission.None, "table OLS");
Require(Model.Tables["Invoices"].Columns["Net Invoice COGS"].ObjectLevelSecurity[Model.Roles["Account Managers"]] == MetadataPermission.None, "column OLS");
Require(Model.Tables["Invoices"].EnableRefreshPolicy, "refresh policy");
Require(Model.Tables.OfType<CalculationGroupTable>().Any(), "calculation groups");
Require(Model.Relationships.Count > 0, "relationships");
Require(Model.Perspectives.Count > 0, "perspectives");
Require(Model.Expressions.Any(e => e.Name == "RangeStart") && Model.Expressions.Any(e => e.Name == "RangeEnd"), "RangeStart/RangeEnd");
Require(Model.AllPartitions.OfType<MPartition>().Any(), "M partitions");
Require(Model.AllPartitions.Any(p => p.Mode == ModeType.DirectQuery), "DirectQuery partition");
Require(Model.AllPartitions.Any(p => p.Mode == ModeType.Dual), "Dual partition");
Require(!string.IsNullOrEmpty(Model.Tables["Orders"].DefaultDetailRowsExpression), "table detail rows");
Require(Model.AllMeasures.Any(m => !string.IsNullOrEmpty(m.DetailRowsExpression)), "measure detail rows");
Require(Model.AllMeasures.Any(m => m.KPI != null), "KPI");
Require(Model.AllColumns.Any(c => c.AlternateOf != null), "AlternateOf");
Require(Model.DataSources.OfType<StructuredDataSource>().Any(), "structured data source");
Require(Model.Functions.Any(f => f.Name == "fla_Double"), "function");
Require(Model.AllMeasures.Any(m => !string.IsNullOrEmpty(m.DisplayFolder)), "display folders");

if (missing.Count > 0) throw new Exception("Fixture is missing: " + string.Join(", ", missing));
("Fixture complete: " + Model.Tables.Count + " tables, " + Model.AllMeasures.Count() + " measures").Output();
