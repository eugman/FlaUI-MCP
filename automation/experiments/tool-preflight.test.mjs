import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { checkToolInventory, observeTools } from './tool-preflight.mjs';

const generic = ['windows_find', 'windows_screenshot'];
const companion = ['te3_inspect', 'te3_navigate', 'te3_capture'];

test('generic-only rungs pass with windows_ tools and reject te3_ tools', () => {
  for (const rung of ['0a', '0b', '1', '2']) {
    assert.deepEqual(checkToolInventory(generic, rung), { genericTools: 2, companionTools: 0 });
    assert.throws(() => checkToolInventory([...generic, 'te3_capture'], rung), /must not expose te3_/);
  }
});

test('rung 3 requires exactly the three companion tools', () => {
  assert.deepEqual(checkToolInventory([...generic, ...companion], '3'), { genericTools: 2, companionTools: 3 });
  assert.throws(() => checkToolInventory([...generic, 'te3_inspect'], '3'), /exactly/);
  assert.throws(() => checkToolInventory([...generic, ...companion, 'te3_catalog'], '3'), /exactly/);
});

test('unexpected, duplicate or missing generic tools fail', () => {
  assert.throws(() => checkToolInventory([...generic, 'Bash'], '0b'), /Unexpected tools: Bash/);
  assert.throws(() => checkToolInventory([generic[0], generic[0]], '0b'), /Duplicate/);
  assert.throws(() => checkToolInventory([], '0b'), /No generic/);
});

// Node fake backend; no desktop tool is launched.
const backendSource = String.raw`
import readline from 'node:readline';
const names = process.argv.slice(2);
readline.createInterface({ input: process.stdin }).on('line', line => {
  const message = JSON.parse(line);
  if (message.id === undefined) return;
  const result = message.method === 'tools/list'
    ? { tools: names.map(name => ({ name, description: 'fake' })) }
    : { protocolVersion: '2025-03-26', capabilities: { tools: {} } };
  process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: message.id, result }) + '\n');
});
`;

test('observeTools lists the first trial gateway tools twice and writes a summary', { timeout: 15000 }, async t => {
  const root = await mkdtemp(join(tmpdir(), 'ladder-preflight-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const backend = join(root, 'backend.mjs');
  await writeFile(backend, backendSource);
  await mkdir(join(root, '3-sonnet-object-01'));
  await writeFile(join(root, 'study.json'), JSON.stringify({ rung: '3', trials: [{ id: '3-sonnet-object-01' }] }));
  await writeFile(join(root, '3-sonnet-object-01', 'gateway.json'), JSON.stringify({
    genericCommand: [process.execPath, backend, ...generic],
    companionCommand: [process.execPath, backend, ...companion],
    logPath: join(root, 'unused.jsonl'), maxCalls: 60
  }));
  const output = join(root, 'preflight');
  const summary = await observeTools(root, output);
  assert.equal(summary.genericTools, 2);
  assert.equal(summary.companionTools, 3);
  assert.deepEqual(summary.tools, [...generic, ...companion]);
  assert.equal(JSON.parse(readFileSync(join(output, 'tools.json'), 'utf8')).length, 5);
  await assert.rejects(observeTools(root, output), { code: 'EEXIST' });
});
