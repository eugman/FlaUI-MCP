// Synthetic, deterministic fixture: no external data source or credentials.
// The two test tables are replaced so this script is also the reset operation for repeated runs.
Model.Tables.FirstOrDefault(t => t.Name == "Sales")?.Delete();
Model.Tables.FirstOrDefault(t => t.Name == "Comparison")?.Delete();
foreach (var measure in Model.AllMeasures.Where(m => m.Name == "UI Revenue" || m.Name == "Sales").ToList()) measure.Delete();
var sales = Model.AddCalculatedTable("Sales", "DATATABLE(\"Amount\", INTEGER, {{10}, {20}, {30}})");
sales.Description = "Three synthetic sales amounts for UI automation.";
sales.AddMeasure("Total Amount", "SUM('Sales'[Amount])", "Smoke tests").FormatString = "#,##0";
sales.Measures["Total Amount"].Description = "Sum of the three synthetic amounts.";
sales.AddMeasure("UI Revenue", "[Total Amount] + 1");
var other = Model.AddCalculatedTable("Comparison", "DATATABLE(\"Amount\", INTEGER, {{1}})");
other.Description = "Disconnected test-only table to verify duplicate column-name navigation.";
// The BPA loader reads this exact annotation key (not BestPracticeAnalyzer_Rules).
Model.SetAnnotation("BestPracticeAnalyzer", @"[{""ID"":""FLA_UI_DESCRIPTION"",""Name"":""fla_ UI description required"",""Category"":""FlaUI"",""Description"":""Pinned test-only rule"",""Severity"":1,""Scope"":""Measure"",""Expression"":""Name = \""UI Revenue\"" and Description = \""\"""",""CompatibilityLevel"":1500}]");
Model.SetAnnotation("FlaUIFixture", "v2");
