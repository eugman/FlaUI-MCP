// Discovery only: never dispatch tools/call, launch TE3, or change preferences.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { StudyGateway } from './study-gateway.mjs';

export function checkArmInventories(arms) {
  const names = arm => arms[arm].tools.map(tool => tool.name).sort();
  const same = (actual, expected) => JSON.stringify(actual) === JSON.stringify(expected.sort());
  for (const arm of ['A', 'B', 'C', 'D']) {
    if (!Array.isArray(arms[arm]?.tools) || !Array.isArray(arms[arm]?.resources)) {
      throw new Error(`Missing ${arm} discovery observations`);
    }
    const tools = names(arm);
    if (tools.some(name => typeof name !== 'string') || new Set(tools).size !== tools.length) {
      throw new Error(`Invalid or duplicate ${arm} tool names`);
    }
    const resources = arms[arm].resources.map(resource => resource?.uri);
    if (resources.some(uri => typeof uri !== 'string' || !uri.startsWith('te3://map/')) ||
        new Set(resources).size !== resources.length) {
      throw new Error(`Invalid or duplicate ${arm} map resource URIs`);
    }
  }
  const generic = names('A');
  if (!generic.includes('windows_find') || !generic.includes('windows_screenshot') ||
      generic.some(name => !name.startsWith('windows_'))) {
    throw new Error('Arm A must contain only generic Windows tools, including find and screenshot');
  }
  if (!same(names('B'), generic)) throw new Error('A/B generic tool inventories differ');
  if (!same(names('C'), [...generic, 'te3_catalog'])) throw new Error('C must add only catalog access');
  if (!same(names('D'), [...generic, 'te3_catalog', 'te3_inspect', 'te3_navigate', 'te3_capture'])) {
    throw new Error('D companion tool inventory differs from the planned treatment');
  }
  if (arms.A.resources.length || arms.B.resources.length) throw new Error('A/B expose map resources');
  const resources = arm => arms[arm].resources.map(resource => resource.uri).sort();
  if (!resources('C').length || !same(resources('C'), resources('D'))) {
    throw new Error('C/D must expose the same nonempty map resource inventory');
  }
  return { genericTools: generic.length, mapResources: resources('C').length };
}

// Agents read the maps embedded in the exe, while the study freezes map/*.md separately.
export function compareMapResources(texts, mapDirectory) {
  const strip = text => text.replace(/^﻿/, '');
  const files = fs.readdirSync(mapDirectory).filter(name => name.endsWith('.md')).sort();
  const expected = files.map(name => `te3://map/${name.slice(0, -3)}`);
  if (JSON.stringify(Object.keys(texts).sort()) !== JSON.stringify(expected)) {
    throw new Error('Embedded map topics differ from the frozen map directory');
  }
  for (const name of files) {
    if (strip(texts[`te3://map/${name.slice(0, -3)}`]) !== strip(fs.readFileSync(path.join(mapDirectory, name), 'utf8'))) {
      throw new Error(`Embedded map topic differs from frozen file: ${name}`);
    }
  }
  return files.length;
}

export async function observeTools(buildDirectory, outputDirectory,
  mapDirectory = path.join(path.dirname(path.resolve(buildDirectory)), 'map')) {
  const output = path.resolve(outputDirectory);
  fs.mkdirSync(output); // Never replace previous observations.
  const arms = {};
  const mapTexts = {};
  for (const arm of ['A', 'B', 'C', 'D']) {
    const gateway = new StudyGateway({
      arm, buildDirectory: path.resolve(buildDirectory),
      logPath: path.join(output, `${arm}.calls.jsonl`), observeOnly: true
    });
    let timer;
    try {
      arms[arm] = await Promise.race([
        (async () => {
          const initialized = await gateway.dispatch('initialize', {
            protocolVersion: '2024-11-05', capabilities: {},
            clientInfo: { name: 'four-arm-tool-preflight', version: '1' }
          });
          await gateway.handle({ jsonrpc: '2.0', method: 'notifications/initialized' });
          const first = await gateway.dispatch('tools/list', {});
          const second = await gateway.dispatch('tools/list', {});
          if (JSON.stringify(first) !== JSON.stringify(second)) throw new Error(`${arm} discovery changed between calls`);
          const resources = initialized.capabilities.resources
            ? (await gateway.dispatch('resources/list', {})).resources : [];
          if (arm === 'C') {
            for (const { uri } of resources) mapTexts[uri] = (await gateway.dispatch('resources/read', { uri })).contents[0].text;
          }
          return { tools: first.tools, resources };
        })(),
        new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(`${arm} discovery timed out`)), 15000); })
      ]);
    } finally {
      clearTimeout(timer);
      await gateway.stop();
    }
    fs.writeFileSync(path.join(output, `${arm}.json`), JSON.stringify(arms[arm], null, 2) + '\n', { flag: 'wx' });
  }
  const result = {
    observedAt: new Date().toISOString(), ...checkArmInventories(arms),
    frozenMapTopicsMatched: fs.existsSync(mapDirectory) ? compareMapResources(mapTexts, mapDirectory) : 'not-run: no sibling map directory',
    scope: 'Gateway/backend discovery only; not the model-visible CLI tool surface.',
    cliIsolation: 'not-verified', modelEffort: 'not-verified', livePermission: false
  };
  fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify(result, null, 2) + '\n', { flag: 'wx' });
  return result;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 4) throw new Error('node tool-preflight.mjs BUILD_DIRECTORY NEW_OUTPUT_DIRECTORY');
    console.log(JSON.stringify(await observeTools(process.argv[2], process.argv[3]), null, 2));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
