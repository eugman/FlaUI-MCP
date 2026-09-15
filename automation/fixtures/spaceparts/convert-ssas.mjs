// Converts the public SpaceParts template model into a BIM that standalone SSAS can process.
// Standalone SSAS rejects inline Sql.Database M partitions, so each Dimview/Factview partition becomes a
// query partition over a legacy provider data source (the pattern used by llm-pbi-precon/rig/sql-ssas).
// Usage: node convert-ssas.mjs TEMPLATE_MODEL.bim OUTPUT.bim [SQL_DATABASE]
import fs from 'node:fs';

export function convertToSsas(bim, sqlDatabase) {
  if (!/^fla_[A-Za-z0-9_]+$/.test(sqlDatabase)) throw new Error(`SQL database must start with fla_: ${sqlDatabase}`);
  const result = structuredClone(bim);
  const model = result.model;
  const changes = [];
  if (model.defaultPowerBIDataSourceVersion) {
    delete model.defaultPowerBIDataSourceVersion;
    changes.push('removed defaultPowerBIDataSourceVersion');
  }
  model.dataSources = [{
    name: 'SqlSpaceParts',
    connectionString: `Provider=MSOLEDBSQL;Data Source=localhost;Initial Catalog=${sqlDatabase};Integrated Security=SSPI;Persist Security Info=false`,
    impersonationMode: 'impersonateServiceAccount'
  }];
  for (const table of model.tables) {
    for (const partition of table.partitions ?? []) {
      if (partition.source?.type !== 'm') continue;
      const expression = [].concat(partition.source.expression ?? '').join('\n');
      const view = expression.match(/Schema\s*=\s*"([^"]+)"\s*,\s*Item\s*=\s*"([^"]+)"/);
      if (view) {
        partition.source = { type: 'query', dataSource: 'SqlSpaceParts', query: `SELECT * FROM [${view[1]}].[${view[2]}]` };
        changes.push(`${table.name}: query partition over ${view[1]}.${view[2]}`);
      } else if (!/DateTimeZone\.FixedLocalNow\(\)/.test(expression)) {
        // Only the Last Refresh timestamp may stay M; anything else would silently change the model's data.
        throw new Error(`Unsupported M partition in ${table.name}`);
      }
    }
  }
  return { bim: result, changes };
}

if (process.argv[1] && import.meta.url.endsWith(process.argv[1].replaceAll('\\', '/').split('/').pop())) {
  const [input, output, sqlDatabase = 'fla_spaceparts'] = process.argv.slice(2);
  if (!input || !output) throw new Error('Usage: node convert-ssas.mjs TEMPLATE_MODEL.bim OUTPUT.bim [SQL_DATABASE]');
  const { bim, changes } = convertToSsas(JSON.parse(fs.readFileSync(input, 'utf8').replace(/^﻿/, '')), sqlDatabase);
  fs.writeFileSync(output, JSON.stringify(bim, null, 2) + '\n');
  for (const change of changes) console.log(change);
  console.log(`wrote ${output}`);
}
