import assert from 'node:assert/strict';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawn } from 'node:child_process';
import readline from 'node:readline';
import test from 'node:test';
import { StudyGateway } from './study-gateway.mjs';

// Every child in this suite is a Node fake. No desktop tool is launched.
const backendSource = String.raw`
import { appendFileSync } from 'node:fs';
import readline from 'node:readline';
const [kind, dispatchLog] = process.argv.slice(2);
let calls = 0;
readline.createInterface({ input: process.stdin }).on('line', line => {
  const message = JSON.parse(line);
  if (message.id === undefined) return;
  let result;
  switch (message.method) {
    case 'initialize':
      result = {
        protocolVersion: '2025-03-26',
        capabilities: { tools: { listChanged: true }, resources: { subscribe: true }, prompts: {} }
      };
      break;
    case 'tools/list':
      result = { tools: kind === 'generic' || kind === 'duplicate'
        ? [{ name: 'safe' }]
        : kind === 'launcher' ? [{ name: 'safe' }, { name: 'windows_launch' }]
        : kind === 'C' ? [{ name: 'guide' }] : [{ name: 'guide' }, { name: 'capture' }] };
      break;
    case 'resources/list':
      result = { resources: [{ uri: 'te3://map/start' }] };
      break;
    case 'resources/read':
      result = { contents: [{ uri: message.params.uri, text: 'map' }] };
      break;
    case 'tools/call':
      calls++;
      appendFileSync(dispatchLog, JSON.stringify({ kind, call: calls }) + '\n');
      if (message.params.arguments?.exit) process.exit(7);
      result = { content: [{ type: 'text', text: JSON.stringify({
        calls, kind, allowedApps: process.env.FLAUI_MCP_ALLOWED_APPS
      }) }] };
      break;
    default:
      throw new Error('unexpected fake method: ' + message.method);
  }
  process.stdout.write(JSON.stringify({ jsonrpc: '2.0', id: message.id, result }) + '\n');
});
`;

const gatewaySource = `
import readline from 'node:readline';
import { StudyGateway } from ${JSON.stringify(new URL('./study-gateway.mjs', import.meta.url).href)};
const { config, commands } = JSON.parse(process.argv[2]);
const gateway = new StudyGateway(config, { backendCommands: commands });
const input = readline.createInterface({ input: process.stdin });
input.on('line', line => { void gateway.handle(JSON.parse(line)); });
input.on('close', () => { void gateway.stop(); });
`;

async function fixture(arm, overrides = {}) {
  const directory = await mkdtemp(join(tmpdir(), 'study-gateway-'));
  const backend = join(directory, 'fake-backend.mjs');
  const harness = join(directory, 'fake-gateway.mjs');
  const dispatchLog = join(directory, 'dispatched.jsonl');
  await writeFile(backend, backendSource);
  await writeFile(harness, gatewaySource);
  const config = { arm, buildDirectory: directory, logPath: join(directory, 'calls.jsonl'), ...overrides };
  const commands = {
    generic: [process.execPath, backend, 'generic', dispatchLog],
    companion: [process.execPath, backend, arm, dispatchLog]
  };
  // Callers remove this directory only after their child processes have closed.
  return { directory, harness, config, commands, dispatchLog };
}

async function wire(t, arm, overrides = {}, changeCommands = () => {}) {
  const files = await fixture(arm, overrides);
  changeCommands(files.commands, files);
  const child = spawn(process.execPath, [files.harness, JSON.stringify({
    config: files.config, commands: files.commands
  })], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'],
    env: { ...process.env, FLAUI_MCP_ALLOWED_APPS: 'UnrelatedApp' } });
  let stderr = '';
  child.stderr.on('data', chunk => { stderr += chunk; });
  const closed = new Promise(resolveClosed => child.once('close', resolveClosed));
  const pending = new Map();
  let nextId = 0;
  const lines = readline.createInterface({ input: child.stdout });
  lines.on('line', line => {
    const reply = JSON.parse(line);
    const request = pending.get(reply.id);
    if (!request) return;
    pending.delete(reply.id);
    clearTimeout(request.timer);
    request.resolve(reply);
  });
  t.after(async () => {
    child.stdin.end();
    const timer = setTimeout(() => child.kill(), 3000);
    await closed;
    clearTimeout(timer);
    lines.close();
    for (const request of pending.values()) clearTimeout(request.timer);
    await rm(files.directory, { recursive: true, force: true });
    assert.equal(stderr, '', 'fake gateway must not crash or emit unhandled errors');
  });
  return {
    ...files,
    request(method, params = {}) {
      const id = ++nextId;
      return new Promise((resolveRequest, reject) => {
        const timer = setTimeout(() => reject(new Error(`wire response timeout: ${method}; ${stderr}`)), 5000);
        pending.set(id, { resolve: resolveRequest, timer });
        child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, params }) + '\n');
      });
    }
  };
}

for (const arm of ['A', 'B', 'C', 'D']) {
  test(`${arm}: wire capabilities, repeated discovery, resources, and backend environment`, { timeout: 15000 }, async t => {
    const client = await wire(t, arm);
    const initialization = await client.request('initialize');
    const withCompanion = arm === 'C' || arm === 'D';
    assert.deepEqual(initialization.result.capabilities, withCompanion ? { tools: {}, resources: {} } : { tools: {} });
    const expectedTools = arm === 'D' ? ['safe', 'guide', 'capture'] : arm === 'C' ? ['safe', 'guide'] : ['safe'];
    for (let repeat = 0; repeat < 2; repeat++) {
      const discovery = await client.request('tools/list');
      assert.deepEqual(discovery.result.tools.map(tool => tool.name), expectedTools);
    }
    const resources = await client.request('resources/list');
    if (withCompanion) {
      assert.equal(resources.result.resources[0].uri, 'te3://map/start');
      const resource = await client.request('resources/read', { uri: 'te3://map/start' });
      assert.equal(resource.result.contents[0].text, 'map');
    } else {
      assert.equal(resources.error.code, -32601);
    }
    for (const name of withCompanion ? ['safe', 'guide'] : ['safe']) {
      const call = await client.request('tools/call', { name });
      assert.equal(JSON.parse(call.result.content[0].text).allowedApps, 'TabularEditor3');
    }
  });
}

test('wire budget rejects call 61 before dispatch, including concurrent calls', { timeout: 15000 }, async t => {
  const client = await wire(t, 'D');
  await client.request('initialize');
  await client.request('tools/list');
  const replies = await Promise.all(Array.from({ length: 61 }, (_, index) => client.request('tools/call', {
    name: index % 2 ? 'guide' : 'safe',
    arguments: index === 0 ? { image: 'secret', actions: [{ action: 'click', name: 'misleading', text: 'private' }] } : {}
  })));
  assert.equal(replies.filter(reply => reply.result).length, 60);
  assert.match(replies[60].error.message, /budget exhausted/);
  const dispatched = (await readFile(client.dispatchLog, 'utf8')).trim().split('\n');
  assert.equal(dispatched.length, 60);
  const log = await readFile(client.config.logPath, 'utf8');
  const batch = log.trim().split('\n').map(JSON.parse).filter(record => record.type === 'batch-action-requested');
  assert.equal(batch.length, 1);
  assert.equal(batch[0].action, 'click');
  assert.doesNotMatch(log, /secret|private|misleading/);
});

test('batch steps and resource reads share the budget, and results are summarized', { timeout: 15000 }, async t => {
  const client = await wire(t, 'C', { maxCalls: 5 });
  await client.request('initialize');
  await client.request('tools/list');
  const batch = steps => client.request('tools/call', {
    name: 'safe', arguments: { actions: Array.from({ length: steps }, () => ({ action: 'click' })) }
  });
  assert.ok((await batch(3)).result);
  assert.match((await batch(3)).error.message, /budget exhausted \(3\/5 used; request needs 3\)/);
  assert.ok((await client.request('resources/read', { uri: 'te3://map/start' })).result);
  assert.ok((await client.request('tools/call', { name: 'guide' })).result);
  assert.match((await client.request('tools/call', { name: 'guide' })).error.message, /budget exhausted/);
  assert.equal(JSON.parse(await readFile(`${client.config.logPath}.budget.json`, 'utf8')).calls, 5);
  const records = (await readFile(client.config.logPath, 'utf8')).trim().split('\n').map(JSON.parse);
  const results = records.filter(record => record.type === 'result');
  assert.equal(results.length, 3);
  assert.deepEqual(results.map(result => result.content[0].type), ['text', null, 'text']);
  assert.deepEqual(records.filter(record => record.type === 'budget-rejected').map(record => record.cost), [3, 1]);
});

test('lifecycle tools are rejected without dispatch or budget use', { timeout: 15000 }, async t => {
  const client = await wire(t, 'A', { maxCalls: 1 }, commands => { commands.generic[2] = 'launcher'; });
  await client.request('initialize');
  assert.deepEqual((await client.request('tools/list')).result.tools.map(tool => tool.name), ['safe', 'windows_launch']);
  assert.match((await client.request('tools/call', { name: 'windows_launch' })).error.message, /not permitted/);
  assert.ok((await client.request('tools/call', { name: 'safe' })).result);
  assert.equal((await readFile(client.dispatchLog, 'utf8')).trim().split('\n').length, 1);
});

test('a restarted gateway resumes the persisted budget', { timeout: 15000 }, async t => {
  const files = await fixture('A', { maxCalls: 1 });
  const first = new StudyGateway(files.config, { backendCommands: files.commands });
  await first.initialize({});
  await first.listTools({});
  await first.callTool({ name: 'safe' });
  await first.stop();
  const second = new StudyGateway(files.config, { backendCommands: files.commands });
  t.after(async () => {
    await second.stop();
    await rm(files.directory, { recursive: true, force: true });
  });
  await second.initialize({});
  await second.listTools({});
  await assert.rejects(second.callTool({ name: 'safe' }), /budget exhausted/);
});

test('wire spawn failure settles initialization and later requests', { timeout: 15000 }, async t => {
  const client = await wire(t, 'A', {}, (commands, files) => {
    commands.generic = [join(files.directory, 'does-not-exist.exe')];
  });
  for (const method of ['initialize', 'tools/list']) {
    const response = await client.request(method);
    assert.match(response.error.message, /generic backend unavailable/);
  }
});

test('wire backend exit settles outstanding calls and is not replayed', { timeout: 15000 }, async t => {
  const client = await wire(t, 'A');
  await client.request('initialize');
  await client.request('tools/list');
  const responses = await Promise.all([
    client.request('tools/call', { name: 'safe', arguments: { exit: true } }),
    client.request('tools/call', { name: 'safe' })
  ]);
  for (const response of responses) assert.match(response.error.message, /backend unavailable/);
  const later = await client.request('tools/call', { name: 'safe' });
  assert.match(later.error.message, /backend unavailable/);
  assert.equal((await readFile(client.dispatchLog, 'utf8')).trim().split('\n').length, 1);
});

test('wire duplicate tools fail without publishing partial ownership', { timeout: 15000 }, async t => {
  const client = await wire(t, 'C', {}, commands => { commands.companion[2] = 'duplicate'; });
  await client.request('initialize');
  assert.match((await client.request('tools/list')).error.message, /duplicate tool ownership/);
  assert.match((await client.request('tools/call', { name: 'safe' })).error.message, /unknown tool/);
});

test('constructor API preserves observe-only discovery and rejects dispatch', async t => {
  const files = await fixture('C', { observeOnly: true });
  const gateway = new StudyGateway(files.config, { backendCommands: files.commands });
  t.after(async () => {
    await gateway.stop();
    await rm(files.directory, { recursive: true, force: true });
  });
  await gateway.initialize({});
  await gateway.listTools({});
  assert.equal((await gateway.request('resources/list', {}, 'companion')).resources.length, 1);
  await assert.rejects(gateway.callTool({ name: 'safe' }), /observeOnly/);
  await assert.rejects(readFile(files.dispatchLog), { code: 'ENOENT' });
});

test('constructor validates configuration before starting a child', () => {
  const base = { arm: 'A', buildDirectory: 'unused', logPath: 'unused' };
  for (const overrides of [{ arm: 'E' }, { maxCalls: 61 }, { maxCalls: 0 }, { observeOnly: 'yes' }]) {
    assert.throws(() => new StudyGateway({ ...base, ...overrides }));
  }
});

test('shutdown reports unconfirmed stream closure within its deadline', async t => {
  const files = await fixture('A');
  const gateway = new StudyGateway(files.config, { backendCommands: files.commands });
  await gateway.initialize({});
  gateway.backends.generic.child.kill();
  await gateway.backends.generic.closed;
  // Simulate a close notification that never arrives, without leaving a process.
  gateway.backends.generic.closed = new Promise(() => {});
  t.mock.timers.enable({ apis: ['setTimeout'] });
  const stopped = gateway.stop();
  const rejected = assert.rejects(stopped, /shutdown was not confirmed/);
  t.mock.timers.tick(5001);
  await rejected;
  t.mock.timers.reset();
  await rm(files.directory, { recursive: true, force: true });
});
