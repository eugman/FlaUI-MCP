import assert from 'node:assert/strict';
import { mkdtemp, readFile, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import readline from 'node:readline';
import test from 'node:test';
import { StudyGateway } from './study-gateway.mjs';

const gatewayScript = fileURLToPath(new URL('./study-gateway.mjs', import.meta.url));

// Every child in this suite is a Node fake. No desktop tool is launched.
const backendSource = String.raw`
import { appendFileSync } from 'node:fs';
import readline from 'node:readline';
const [kind, dispatchLog] = process.argv.slice(2);
const inventories = {
  generic: ['safe'], duplicate: ['safe'], launcher: ['safe', 'windows_launch'], companion: ['guide', 'capture']
};
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
      result = { tools: inventories[kind].map(name => ({ name })) };
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

// Callers remove the directory only after their child processes have closed.
async function fixture({ generic = 'generic', companion = null, ...overrides } = {}) {
  const directory = await mkdtemp(join(tmpdir(), 'study-gateway-'));
  const backend = join(directory, 'fake-backend.mjs');
  const dispatchLog = join(directory, 'dispatched.jsonl');
  await writeFile(backend, backendSource);
  const config = {
    genericCommand: [process.execPath, backend, generic, dispatchLog],
    logPath: join(directory, 'calls.jsonl'), ...overrides
  };
  if (companion) config.companionCommand = [process.execPath, backend, companion, dispatchLog];
  return { directory, config, dispatchLog };
}

async function wire(t, options = {}) {
  const files = await fixture(options);
  const configPath = join(files.directory, 'gateway.json');
  await writeFile(configPath, JSON.stringify(files.config));
  const child = spawn(process.execPath, [gatewayScript, configPath], {
    windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'],
    env: { ...process.env, FLAUI_MCP_ALLOWED_APPS: 'UnrelatedApp' }
  });
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
    assert.equal(stderr, '', 'gateway must not crash or emit unhandled errors');
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

for (const companion of [null, 'companion']) {
  test(`${companion ? 'with' : 'without'} companion: tools only, repeated discovery, backend environment`, { timeout: 15000 }, async t => {
    const client = await wire(t, { companion });
    const initialization = await client.request('initialize');
    assert.deepEqual(initialization.result.capabilities, { tools: {} });
    const expectedTools = companion ? ['safe', 'guide', 'capture'] : ['safe'];
    for (let repeat = 0; repeat < 2; repeat++) {
      const discovery = await client.request('tools/list');
      assert.deepEqual(discovery.result.tools.map(tool => tool.name), expectedTools);
    }
    assert.equal((await client.request('resources/list')).error.code, -32601);
    assert.equal((await client.request('resources/read', { uri: 'te3://map/start' })).error.code, -32601);
    for (const name of companion ? ['safe', 'guide'] : ['safe']) {
      const call = await client.request('tools/call', { name });
      assert.equal(JSON.parse(call.result.content[0].text).allowedApps, 'TabularEditor3');
    }
  });
}

test('wire budget rejects call 61 before dispatch, including concurrent calls', { timeout: 15000 }, async t => {
  const client = await wire(t, { companion: 'companion' });
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

test('batch steps count against the budget, and results are summarized', { timeout: 15000 }, async t => {
  const client = await wire(t, { companion: 'companion', maxCalls: 5 });
  await client.request('initialize');
  await client.request('tools/list');
  const batch = steps => client.request('tools/call', {
    name: 'safe', arguments: { actions: Array.from({ length: steps }, () => ({ action: 'click' })) }
  });
  assert.ok((await batch(3)).result);
  assert.match((await batch(3)).error.message, /budget exhausted \(3\/5 used; request needs 3\)/);
  assert.ok((await client.request('tools/call', { name: 'guide' })).result);
  assert.ok((await client.request('tools/call', { name: 'guide' })).result);
  assert.match((await client.request('tools/call', { name: 'guide' })).error.message, /budget exhausted/);
  assert.equal(JSON.parse(await readFile(`${client.config.logPath}.budget.json`, 'utf8')).calls, 5);
  const records = (await readFile(client.config.logPath, 'utf8')).trim().split('\n').map(JSON.parse);
  const results = records.filter(record => record.type === 'result');
  assert.equal(results.length, 3);
  assert.deepEqual(results.map(result => result.content[0].type), ['text', 'text', 'text']);
  assert.deepEqual(records.filter(record => record.type === 'budget-rejected').map(record => record.cost), [3, 1]);
});

test('lifecycle tools are rejected without dispatch or budget use', { timeout: 15000 }, async t => {
  const client = await wire(t, { generic: 'launcher', maxCalls: 1 });
  await client.request('initialize');
  assert.deepEqual((await client.request('tools/list')).result.tools.map(tool => tool.name), ['safe', 'windows_launch']);
  assert.match((await client.request('tools/call', { name: 'windows_launch' })).error.message, /not permitted/);
  assert.ok((await client.request('tools/call', { name: 'safe' })).result);
  assert.equal((await readFile(client.dispatchLog, 'utf8')).trim().split('\n').length, 1);
});

test('a restarted gateway resumes the persisted budget', { timeout: 15000 }, async t => {
  const files = await fixture({ maxCalls: 1 });
  const first = new StudyGateway(files.config);
  await first.initialize({});
  await first.listTools({});
  await first.callTool({ name: 'safe' });
  await first.stop();
  const second = new StudyGateway(files.config);
  t.after(async () => {
    await second.stop();
    await rm(files.directory, { recursive: true, force: true });
  });
  await second.initialize({});
  await second.listTools({});
  await assert.rejects(second.callTool({ name: 'safe' }), /budget exhausted/);
});

test('wire spawn failure settles initialization and later requests', { timeout: 15000 }, async t => {
  const directory = await mkdtemp(join(tmpdir(), 'study-gateway-missing-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const client = await wire(t, {});
  const gateway = new StudyGateway({ ...client.config, genericCommand: [join(directory, 'does-not-exist.exe')] });
  t.after(() => gateway.stop());
  for (const method of ['initialize', 'tools/list']) {
    await assert.rejects(gateway.dispatch(method, {}), /generic backend unavailable/);
  }
});

test('wire backend exit settles outstanding calls and is not replayed', { timeout: 15000 }, async t => {
  const client = await wire(t);
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
  const client = await wire(t, { companion: 'duplicate' });
  await client.request('initialize');
  assert.match((await client.request('tools/list')).error.message, /duplicate tool ownership/);
  assert.match((await client.request('tools/call', { name: 'safe' })).error.message, /unknown tool/);
});

test('observe-only discovery lists tools and rejects dispatch', async t => {
  const files = await fixture({ companion: 'companion', observeOnly: true });
  const gateway = new StudyGateway(files.config);
  t.after(async () => {
    await gateway.stop();
    await rm(files.directory, { recursive: true, force: true });
  });
  await gateway.initialize({});
  assert.equal((await gateway.listTools({})).tools.length, 3);
  await assert.rejects(gateway.callTool({ name: 'safe' }), /observeOnly/);
  await assert.rejects(readFile(files.dispatchLog), { code: 'ENOENT' });
});

test('constructor validates configuration before starting a child', () => {
  const base = { genericCommand: ['unused.exe'], logPath: 'unused' };
  for (const overrides of [{ genericCommand: undefined }, { genericCommand: [] }, { companionCommand: 'x.exe' },
    { maxCalls: 201 }, { maxCalls: 0 }, { observeOnly: 'yes' }]) {
    assert.throws(() => new StudyGateway({ ...base, ...overrides }));
  }
});

test('shutdown reports unconfirmed stream closure within its deadline', async t => {
  const files = await fixture();
  const gateway = new StudyGateway(files.config);
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
