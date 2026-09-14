// Discovery only: never dispatch tools/call, launch TE3, or change preferences.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { StudyGateway } from './study-gateway.mjs';
import { companionTools } from './ladder-study.mjs';

export function checkToolInventory(names, rung) {
  if (new Set(names).size !== names.length) throw new Error('Duplicate tool names');
  const unexpected = names.filter(name => !name.startsWith('windows_') && !name.startsWith('te3_'));
  if (unexpected.length) throw new Error(`Unexpected tools: ${unexpected.join(', ')}`);
  const generic = names.filter(name => name.startsWith('windows_'));
  if (!generic.length) throw new Error('No generic windows_ tools');
  const te3 = names.filter(name => name.startsWith('te3_')).sort();
  if (rung === '3') {
    if (JSON.stringify(te3) !== JSON.stringify([...companionTools].sort())) {
      throw new Error(`Rung 3 must expose exactly ${companionTools.join(', ')}; observed ${te3.join(', ') || 'none'}`);
    }
  } else if (te3.length) {
    throw new Error(`Rung ${rung} must not expose te3_* tools; observed ${te3.join(', ')}`);
  }
  return { genericTools: generic.length, companionTools: te3.length };
}

async function discover(gateway) {
  await gateway.dispatch('initialize', {
    protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'ladder-tool-preflight', version: '1' }
  });
  await gateway.handle({ jsonrpc: '2.0', method: 'notifications/initialized' });
  const first = await gateway.dispatch('tools/list', {});
  const second = await gateway.dispatch('tools/list', {});
  if (JSON.stringify(first) !== JSON.stringify(second)) throw new Error('Tool discovery changed between calls');
  return first.tools;
}

export async function observeTools(studyDirectory, outputDirectory) {
  const root = path.resolve(studyDirectory);
  const study = JSON.parse(fs.readFileSync(path.join(root, 'study.json'), 'utf8'));
  const trialConfig = JSON.parse(fs.readFileSync(path.join(root, study.trials[0].id, 'gateway.json'), 'utf8'));
  const output = path.resolve(outputDirectory);
  fs.mkdirSync(output); // Never replace previous observations.
  const gateway = new StudyGateway({ ...trialConfig, logPath: path.join(output, 'calls.jsonl'), observeOnly: true });
  let timer;
  let tools;
  try {
    tools = await Promise.race([
      discover(gateway),
      new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('Tool discovery timed out')), 15000); })
    ]);
  } finally {
    clearTimeout(timer);
    await gateway.stop();
  }
  fs.writeFileSync(path.join(output, 'tools.json'), JSON.stringify(tools, null, 2) + '\n', { flag: 'wx' });
  const names = tools.map(tool => tool.name);
  const summary = {
    observedAt: new Date().toISOString(), study: root, rung: study.rung,
    ...checkToolInventory(names, study.rung), tools: names,
    scope: 'Gateway/backend discovery only; the model-visible tool list is in each trial transcript init event.'
  };
  fs.writeFileSync(path.join(output, 'summary.json'), JSON.stringify(summary, null, 2) + '\n', { flag: 'wx' });
  return summary;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 4) throw new Error('node tool-preflight.mjs STUDY_DIRECTORY NEW_OUTPUT_DIRECTORY');
    console.log(JSON.stringify(await observeTools(process.argv[2], process.argv[3]), null, 2));
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
