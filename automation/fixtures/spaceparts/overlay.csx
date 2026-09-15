// Adds the objects backlog screenshots need that the public SpaceParts template lacks.
// Only adds or sets new properties; the template's intentional mess (warnings, BPA findings) stays.
// Run by build-offline.ps1: te script <template copy> --script overlay.csx --save-to spaceparts-offline.bim

// User-defined functions need Power BI compatibility level 1702; the template is 1600.
// The SSAS copy is built from the unmodified template, so this only affects the offline fixture.
if (Model.Database.CompatibilityLevel < 1702) Model.Database.CompatibilityLevel = 1702;

// Identity, so a run manifest can tell this fixture apart from the small study fixture.
Model.SetAnnotation("FlaUIFixture", "spaceparts-backlog-1");

// Object-level security on two existing roles.
var accountManagers = Model.Roles["Account Managers"];
var territoryManagers = Model.Roles["Territory Managers"];
Model.Tables["Budget"].ObjectLevelSecurity[territoryManagers] = MetadataPermission.None;
Model.Tables["Invoices"].Columns["Net Invoice COGS"].ObjectLevelSecurity[accountManagers] = MetadataPermission.None;
Model.Tables["Invoices"].Columns["Net Invoice COGS"].ObjectLevelSecurity[territoryManagers] = MetadataPermission.Read;

// Incremental refresh policy on Invoices, filtering the same view the partition reads.
var invoices = Model.Tables["Invoices"];
invoices.EnableRefreshPolicy = true;
invoices.RollingWindowGranularity = RefreshGranularityType.Year;
invoices.RollingWindowPeriods = 5;
invoices.IncrementalGranularity = RefreshGranularityType.Day;
invoices.IncrementalPeriods = 10;
invoices.SourceExpression =
@"let
    Source = Sql.Database(#""SqlEndpoint"", #""Database""),
    Data = Source{[Schema=""Factview"",Item=""Invoices""]}[Data],
    Filtered = Table.SelectRows(Data, each [Billing Date] >= RangeStart and [Billing Date] < RangeEnd)
in
    Filtered";

// Detail rows on a table and on a measure; KPI on the same measure.
var orders = Model.Tables["Orders"];
orders.DefaultDetailRowsExpression =
    "SELECTCOLUMNS ( Orders, \"Order\", Orders[Sales Order Document Number], \"Customer\", Orders[Customer Key], \"Value\", Orders[Net Order Value] )";
var kpiMeasure = Model.AllMeasures.Where(m => m.Table.Name == "__Measures").OrderBy(m => m.Name).First();
kpiMeasure.DetailRowsExpression = "SELECTCOLUMNS ( Invoices, \"Invoice\", Invoices[Billing Document Number], \"Value\", Invoices[Net Invoice Value] )";
var kpi = kpiMeasure.AddKPI();
kpi.TargetExpression = "1";
kpi.StatusExpression = "IF ( [" + kpiMeasure.Name + "] >= 1, 1, -1 )";

// An import aggregation table whose columns are AlternateOf the Invoices detail columns.
var aggregates = Model.AddTable("Invoice Aggregates");
aggregates.Partitions[0].Delete();
aggregates.AddMPartition("Invoice Aggregates",
@"let
    Source = Sql.Database(#""SqlEndpoint"", #""Database""),
    Data = Source{[Schema=""Factview"",Item=""Invoices""]}[Data],
    Grouped = Table.Group(Data, {""Customer Key""}, {{""Net Invoice Value"", each List.Sum([Net Invoice Value]), type number}})
in
    Grouped");
var aggregateCustomer = aggregates.AddDataColumn("Customer Key", "Customer Key", null, DataType.String);
var aggregateValue = aggregates.AddDataColumn("Net Invoice Value", "Net Invoice Value", null, DataType.Double);
aggregateCustomer.AddAlternateOf(invoices.Columns["Customer Key"], SummarizationType.GroupBy);
aggregateValue.AddAlternateOf(invoices.Columns["Net Invoice Value"], SummarizationType.Sum);
aggregates.IsHidden = true;

// One DirectQuery and one Dual partition.
Model.Tables["Forecast"].Partitions[0].Mode = ModeType.DirectQuery;
Model.Tables["Customers"].Partitions[0].Mode = ModeType.Dual;

// A Power Query (structured) data source, for the data source screenshots.
var source = Model.AddStructuredDataSource("SpaceParts SQL");
source.Protocol = "tds";
source.Server = "localhost";
source.Database = "fla_spaceparts";

// A user-defined function (needs the Power BI compatibility level that supports functions).
var function = Model.AddFunction("fla_Double");
function.Expression = "( amount : NUMERIC ) => amount * 2";
