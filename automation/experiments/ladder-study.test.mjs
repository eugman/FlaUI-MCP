import assert from 'node:assert/strict';
import test from 'node:test';
import { readFileSync, existsSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import {
  tasks, holdoutTasks, conditionRows, schedule, promptFor, validateConfig, usageFrom, verifyFrozenInputs, prepare, run,
  runTrialSession, claudeCommand, assertSameAssemblies, readEvents, summarize, groupRows
} from './ladder-study.mjs';
import { composeSkill } from '../skills/build-skill.mjs';

test('twelve distinct tasks, the last three held out', () => {
  assert.equal(Object.keys(tasks).length, 12);
  assert.deepEqual(holdoutTasks, ['bpa-view', 'dax-query', 'model-properties']);
  assert.deepEqual(Object.keys(tasks).slice(-3), holdoutTasks);
});

test('schedule runs each task once and shuffles deterministically by seed', () => {
  const base = { rung: '1', model: 'sonnet', tasks: Object.keys(tasks), trialsPerTask: 1 };
  const first = schedule({ ...base, seed: 7 });
  assert.equal(first.length, 12);
  assert.deepEqual(first.map(trial => trial.id).sort(), Object.keys(tasks).map(task => `1-sonnet-${task}-01`).sort());
  assert.deepEqual(schedule({ ...base, seed: 7 }), first);
  assert.notDeepEqual(schedule({ ...base, seed: 8 }).map(trial => trial.id), first.map(trial => trial.id));
  assert.equal(first.find(trial => trial.task === 'bpa-view').holdout, true);
  assert.equal(first.find(trial => trial.task === 'column').holdout, false);
});

test('only the script-run task gets the supplied source and the execution authorization', () => {
  const edit = promptFor('script-edit', '', 'C:\\study\\hello-world.csx', 'C:\\trial\\result.png');
  assert.ok(!edit.includes('Supplied source'));
  assert.ok(!edit.includes('authorized to execute'));
  assert.ok(promptFor('script-run', '', 'C:\\study\\hello-world.csx', 'y').includes('authorized to execute'));
});

test('prompt appends guidance only when the skill has text', () => {
  const bare = promptFor('script-run', '', 'C:\\study\\hello-world.csx', 'C:\\trial\\result.png');
  const guided = promptFor('script-run', '- rule', 'C:\\study\\hello-world.csx', 'C:\\trial\\result.png');
  assert.ok(bare.includes('The owned TE3 processId is {{PID}}. Save the final PNG to C:\\trial\\result.png.'));
  assert.ok(bare.includes('Supplied source: C:\\study\\hello-world.csx.'));
  assert.ok(bare.includes('Stop within 60 MCP tool calls or seven minutes.'));
  assert.ok(!bare.includes('Guidance'));
  assert.equal(guided, bare.trimEnd() + '\n\nGuidance:\n- rule\n');
  assert.ok(!promptFor('column', '', 'x', 'y').includes('Supplied source'));
});

const result = usage => ({ type: 'result', is_error: false, num_turns: 4, total_cost_usd: 0.12, session_id: 's-1', usage });

test('extended-budget reruns select exact cells, label their ids and state the raised limits', () => {
  const trials = schedule({ rung: '0a', model: 'sonnet', tasks: ['formatting', 'code-actions'], trialsPerTask: 3, seed: 1,
    onlyTrials: ['formatting-02', 'code-actions-01'], label: 'extended' });
  assert.deepEqual(trials.map(trial => trial.id).sort(), ['0a-sonnet-code-actions-01-extended', '0a-sonnet-formatting-02-extended']);
  assert.ok(trials.every(trial => trial.label === 'extended'));
  assert.ok(promptFor('formatting', '', 'x', 'y', { maxCalls: 120, agentMinutes: 15 }).includes('Stop within 120 MCP tool calls or 15 minutes.'));
  assert.ok(promptFor('formatting', '', 'x', 'y').includes('Stop within 60 MCP tool calls or seven minutes.'));
});

test('usage sums all four Claude counters and treats missing cache fields as zero', () => {
  const full = usageFrom([result({
    input_tokens: 10, cache_creation_input_tokens: 200, cache_read_input_tokens: 3000, output_tokens: 40
  })]);
  assert.equal(full.status, 'final');
  assert.equal(full.usage.total_tokens, 3250);
  assert.equal(full.usage.uncached_input_tokens, 210);
  assert.equal(full.usage.session_id, 's-1');
  assert.equal(full.usage.total_cost_usd, 0.12);
  const partial = usageFrom([result({ input_tokens: 10, output_tokens: 5 })]);
  assert.equal(partial.usage.total_tokens, 15);
  assert.equal(partial.usage.cache_read_input_tokens, 0);
  assert.match(usageFrom([]).reason, /observed 0/);
  assert.match(usageFrom([result({ input_tokens: 1, output_tokens: 1 }), result({ input_tokens: 1, output_tokens: 1 })]).reason, /observed 2/);
  assert.match(usageFrom([result({ input_tokens: -1, output_tokens: 1 })]).reason, /Invalid token counters/);
  assert.match(usageFrom([{ type: 'result' }]).reason, /no usage/);
  assert.match(usageFrom([result({ input_tokens: 1, output_tokens: 1 })], 1).reason, /malformed/);
});

test('readEvents counts malformed JSON without admitting it as evidence', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ladder-events-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const log = join(root, 'agent.jsonl');
  await writeFile(log, '{bad json\n{"type":"result"}\n{"noType":true}\n');
  assert.deepEqual(readEvents(log), { events: [{ type: 'result' }], malformedLines: 2 });
});

test('claude command runs headless with only the study MCP server', () => {
  const args = claudeCommand('C:\\bin\\claude.exe', 'opus', 'C:\\trial\\mcp.json');
  assert.equal(args[0], 'C:\\bin\\claude.exe');
  const value = flag => args[args.indexOf(flag) + 1];
  assert.equal(args[1], '-p');
  assert.equal(value('--model'), 'opus');
  assert.equal(value('--output-format'), 'stream-json');
  assert.equal(value('--mcp-config'), 'C:\\trial\\mcp.json');
  assert.equal(value('--tools'), '');
  assert.equal(value('--allowedTools'), 'mcp__study__*');
  assert.equal(value('--permission-mode'), 'dontAsk');
  assert.equal(value('--setting-sources'), 'local');
  for (const flag of ['--verbose', '--no-session-persistence', '--strict-mcp-config', '--disable-slash-commands']) {
    assert.ok(args.includes(flag), flag);
  }
  assert.ok(!args.includes('--dangerously-skip-permissions'));
  assert.deepEqual(claudeCommand('cli.js', 'sonnet', 'm.json').slice(0, 2), [process.execPath, 'cli.js']);
});

const digest = value => createHash('sha256').update(value).digest('hex');
async function writeStudy(directory, value) {
  const text = JSON.stringify(value);
  await writeFile(join(directory, 'study.json'), text);
  await writeFile(join(directory, 'study.json.sha256'), digest(text));
}

test('frozen inputs and recorded runtimes reject missing or changed files', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ladder-freeze-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const input = join(root, 'input.txt');
  const runtime = join(root, 'runtime.bin');
  await writeFile(input, 'frozen input');
  await writeFile(runtime, 'runtime bytes');
  await writeStudy(root, { frozen: {}, runtimes: {} });
  assert.throws(() => verifyFrozenInputs(root), /Frozen input inventory must be a nonempty object/);
  const runtimeRecord = { path: runtime, sha256: digest('runtime bytes') };
  const valid = {
    frozen: { 'input.txt': digest('frozen input') },
    runtimes: { node: runtimeRecord, claude: runtimeRecord, te3: runtimeRecord, te: runtimeRecord }
  };
  await writeStudy(root, valid);
  assert.deepEqual(verifyFrozenInputs(root), { inputs: 1, runtimes: 4 });
  await writeFile(join(root, 'study.json'), JSON.stringify({ ...valid, trials: [] }));
  assert.throws(() => verifyFrozenInputs(root), /seal missing or study.json changed/);
  await writeStudy(root, valid);
  await writeFile(input, 'changed input');
  assert.throws(() => verifyFrozenInputs(root), /Frozen input changed: input.txt/);
  await writeFile(input, 'frozen input');
  await rm(runtime);
  assert.throws(() => verifyFrozenInputs(root), /Runtime missing: node/);
  await writeFile(runtime, 'runtime bytes');
  await writeStudy(root, { frozen: { 'input.txt': digest('frozen input') }, runtimes: { node: runtimeRecord } });
  assert.throws(() => verifyFrozenInputs(root), /Runtime inventory missing or invalid: claude/);
});

test('controller assemblies must match the compared build', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ladder-assemblies-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  for (const directory of ['build', 'controller']) {
    await mkdir(join(root, directory));
    for (const name of ['FlaUI.Mcp.dll', 'FlaUI.Automation.dll']) await writeFile(join(root, directory, name), name);
  }
  assert.doesNotThrow(() => assertSameAssemblies(join(root, 'build'), join(root, 'controller')));
  await writeFile(join(root, 'controller', 'FlaUI.Automation.dll'), 'stale');
  assert.throws(() => assertSameAssemblies(join(root, 'build'), join(root, 'controller')), /differs from the study build/);
  await rm(join(root, 'build', 'FlaUI.Mcp.dll'));
  assert.throws(() => assertSameAssemblies(join(root, 'build'), join(root, 'controller')), /Missing FlaUI.Mcp.dll/);
});

async function preparationInputs(t, overrides = {}) {
  const root = await mkdtemp(join(tmpdir(), 'ladder-prepare-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const files = {
    'generic/FlaUI.Mcp.exe': 'old generic', 'generic/extra.dll': 'x',
    'companion/FlaUI.Automation.exe': 'runner', 'companion/FlaUI.Mcp.dll': 'mcp', 'companion/FlaUI.Automation.dll': 'auto',
    'controller/Controller.exe': 'controller', 'controller/FlaUI.Mcp.dll': 'mcp', 'controller/FlaUI.Automation.dll': 'auto',
    'baseline.bim': '{}', 'TabularEditor3.exe': 'te3', 'TabularEditor.exe': 'te', 'claude.exe': 'claude'
  };
  for (const [name, text] of Object.entries(files)) {
    await mkdir(join(root, name, '..'), { recursive: true });
    await writeFile(join(root, name), text);
  }
  const config = {
    rung: '5', model: 'sonnet', seed: 42,
    genericBuild: join(root, 'generic'), companionBuild: join(root, 'companion'),
    controller: join(root, 'controller', 'Controller.exe'), baseline: join(root, 'baseline.bim'),
    te3: join(root, 'TabularEditor3.exe'), te: join(root, 'TabularEditor.exe'), claudeEntry: join(root, 'claude.exe'),
    ...overrides
  };
  for (const key of Object.keys(config)) if (config[key] === undefined) delete config[key];
  const configPath = join(root, 'config.json');
  await writeFile(configPath, JSON.stringify(config));
  return { root, config, configPath };
}

test('config validation limits companion tools to condition 5 and models to sonnet/opus', async t => {
  const { config } = await preparationInputs(t);
  assert.equal(validateConfig(config).trialsPerTask, 1);
  assert.deepEqual(validateConfig(config).genericCommand, ['FlaUI.Mcp.exe', 'mcp']);
  assert.throws(() => validateConfig({ ...config, rung: '2' }), /only for condition 5/);
  assert.throws(() => validateConfig({ ...config, companionBuild: undefined }), /requires companionBuild/);
  assert.throws(() => validateConfig({ ...config, model: 'haiku' }), /sonnet or opus/);
  assert.throws(() => validateConfig({ ...config, seed: 'x' }), /seed/);
  assert.throws(() => validateConfig({ ...config, tasks: ['unknown'] }), /known tasks/);
  assert.throws(() => validateConfig({ ...config, genericCommand: ['Missing.exe'] }), /Missing Missing.exe/);
});

test('prepare freezes a condition 5 study with skill, gateways and MCP configs', async t => {
  const { root, configPath } = await preparationInputs(t);
  const out = join(root, 'study');
  assert.deepEqual(await prepare(configPath, out), { directory: out, trials: 12 });
  const study = JSON.parse(readFileSync(join(out, 'study.json'), 'utf8'));
  const skill = composeSkill('5');
  assert.equal(study.skill.sha256, skill.sha256);
  assert.deepEqual(study.skill.layers, skill.layers);
  assert.equal(readFileSync(join(out, 'skill.md'), 'utf8'), skill.text);
  assert.deepEqual(Object.keys(study.runtimes).sort(), ['claude', 'node', 'te', 'te3']);
  assert.deepEqual(verifyFrozenInputs(out), { inputs: Object.keys(study.frozen).length, runtimes: 4 });
  const trial = study.trials[0];
  const dir = join(out, trial.id);
  const gateway = JSON.parse(readFileSync(join(dir, 'gateway.json'), 'utf8'));
  assert.deepEqual(gateway.genericCommand, [join(out, 'build', 'FlaUI.Mcp.exe'), 'mcp']);
  assert.deepEqual(gateway.companionCommand, [join(out, 'companion', 'FlaUI.Automation.exe'), 'mcp']);
  const mcp = JSON.parse(readFileSync(join(dir, 'mcp.json'), 'utf8'));
  assert.deepEqual(mcp.mcpServers.study.args, [join(out, 'study-gateway.mjs'), join(dir, 'gateway.json')]);
  assert.equal(readFileSync(join(dir, 'prompt-template.txt'), 'utf8').includes('Guidance:'), skill.text.length > 0);
  await assert.rejects(prepare(configPath, out), /fresh study directory/);
});

test('prepare for a generic rung has no companion and checks a supplied current build', async t => {
  const { root, configPath } = await preparationInputs(t, {
    rung: '1', companionBuild: undefined, genericCommand: ['FlaUI.Mcp.exe', 'serve', '--stdio'], tasks: ['column'], trialsPerTask: 2
  });
  const out = join(root, 'study');
  await prepare(configPath, out);
  const study = JSON.parse(readFileSync(join(out, 'study.json'), 'utf8'));
  assert.deepEqual(study.trials.map(trial => trial.id).sort(), ['1-sonnet-column-01', '1-sonnet-column-02']);
  assert.equal(readFileSync(join(out, 'skill.md'), 'utf8'), '');
  assert.equal(existsSync(join(out, 'companion')), false);
  const gateway = JSON.parse(readFileSync(join(out, '1-sonnet-column-01', 'gateway.json'), 'utf8'));
  assert.deepEqual(gateway.genericCommand, [join(out, 'build', 'FlaUI.Mcp.exe'), 'serve', '--stdio']);
  assert.equal(gateway.companionCommand, undefined);

  const mismatched = JSON.parse(readFileSync(configPath, 'utf8'));
  mismatched.currentBuild = join(root, 'generic');
  await writeFile(configPath, JSON.stringify(mismatched));
  await assert.rejects(prepare(configPath, join(root, 'other')), /Missing FlaUI.Mcp.dll/);
});

// These subprocesses only write local fixture files. No Claude or desktop app runs.
const fakeController = String.raw`
import fs from 'node:fs';
import path from 'node:path';
const [directory, mode] = process.argv.slice(2);
const manifestPath = path.join(directory, 'manifest.json');
function finish() {
  fs.writeFileSync(manifestPath, JSON.stringify({
    passed: mode !== 'hold-failed', error: mode === 'hold-failed' ? 'Test TE3 exited' : null,
    settingsRestored: mode !== 'restore-failure', needsRecovery: mode === 'restore-failure'
  }));
  console.log(JSON.stringify({ ManifestPath: manifestPath }));
  process.exit(mode === 'nonzero' ? 3 : 0);
}
fs.writeFileSync(path.join(directory, 'ready.json'), JSON.stringify({ processId: process.pid }));
const timer = setInterval(() => {
  if (mode === 'early-exit' && fs.existsSync(path.join(directory, 'agent-started'))) finish();
  if (fs.existsSync(path.join(directory, 'done'))) {
    clearInterval(timer);
    if (mode === 'slow-cleanup') setTimeout(finish, 200);
    else finish();
  }
}, 10);
`;

const fakeClaude = String.raw`
import fs from 'node:fs';
import path from 'node:path';
const [directory, mode] = process.argv.slice(2);
fs.writeFileSync(path.join(directory, 'agent-started'), String(process.pid));
fs.writeFileSync(path.join(directory, 'agent-cwd'), process.cwd());
const emit = event => console.log(JSON.stringify(event));
emit({ type: 'system', subtype: 'init', session_id: 'fake-session', model: 'claude-sonnet-fake', tools: ['mcp__study__windows_find'] });
emit({ type: 'assistant', message: { content: [{ type: 'text', text: 'working' }] } });
if (mode === 'hang') setInterval(() => {}, 100);
else {
  if (mode === 'malformed') console.log('{bad JSON');
  emit({ type: 'result', subtype: 'success', is_error: mode === 'failure', num_turns: 3, total_cost_usd: 0.01,
    session_id: 'fake-session', usage: {
      input_tokens: mode === 'invalid' ? -1 : 100, cache_creation_input_tokens: 20, cache_read_input_tokens: 300, output_tokens: 10
    } });
  process.exitCode = mode === 'failure' ? 4 : 0;
}
`;

async function waitForFakeProcessExit(pid, label, timeoutMs = 5000) {
  assert.ok(Number.isInteger(pid) && pid > 0, `Invalid owned ${label} PID`);
  const deadline = Date.now() + timeoutMs;
  while (true) {
    try {
      process.kill(pid, 0); // Read-only existence probe; never signal another process.
    } catch (error) {
      if (error.code === 'ESRCH') return;
      throw error;
    }
    if (Date.now() >= deadline) {
      throw new Error(`Owned fake ${label} PID ${pid} did not exit; fixture retained`);
    }
    await new Promise(resolve => setTimeout(resolve, 25));
  }
}

async function sessionFixture(t, controllerMode = 'success', agentMode = 'success') {
  const root = await mkdtemp(join(tmpdir(), 'ladder-unit-'));
  const directory = join(root, 'trial');
  await mkdir(directory);
  const controller = join(root, 'controller.mjs');
  const agent = join(root, 'claude.mjs');
  await writeFile(controller, fakeController);
  await writeFile(agent, fakeClaude);
  t.after(async () => {
    // The controller may still be restoring after the deliberately short timeout.
    // Confirm both owned fakes have exited before deleting their working directory.
    const readyPath = join(directory, 'ready.json');
    const agentPath = join(directory, 'agent-started');
    if (existsSync(readyPath)) {
      const ready = JSON.parse(readFileSync(readyPath, 'utf8'));
      await waitForFakeProcessExit(ready.processId, 'controller');
    }
    if (existsSync(agentPath)) {
      await waitForFakeProcessExit(Number(readFileSync(agentPath, 'utf8')), 'agent');
    }
    await rm(root, { recursive: true, force: true });
  });
  return {
    directory, trial: { id: '0b-sonnet-formatting-01', rung: '0b', model: 'sonnet', task: 'formatting' },
    controllerCommand: [process.execPath, controller, directory, controllerMode],
    agentCommand: [process.execPath, agent, directory, agentMode],
    readyMs: 3000, agentMs: 3000, cleanupMs: 3000, stopMs: 3000,
    lock: join(root, 'active'), usagePath: join(directory, 'usage.json')
  };
}

test('session saves final accounting and transcript facts after verified restoration', { timeout: 15000 }, async t => {
  const fixture = await sessionFixture(t);
  fixture.agentCwd = join(fixture.directory, 'empty-agent-cwd');
  await mkdir(fixture.agentCwd);
  const record = await runTrialSession(fixture);
  assert.equal(readFileSync(join(fixture.directory, 'agent-cwd'), 'utf8'), fixture.agentCwd);
  assert.equal(record.lifecycleOutcome, 'completed');
  assert.equal(record.cleanup.status, 'verified');
  assert.match(record.review, /^pending/);
  assert.equal(record.status, 'final');
  assert.equal(record.usage.total_tokens, 430);
  assert.equal(record.usage.uncached_input_tokens, 120);
  assert.equal(record.sessionId, 'fake-session');
  assert.equal(record.agentModel, 'claude-sonnet-fake');
  assert.deepEqual(record.visibleTools, ['mcp__study__windows_find']);
  assert.equal(record.transcriptPath, join(fixture.directory, 'agent.jsonl'));
  assert.equal(existsSync(fixture.lock), false);
  await assert.rejects(runTrialSession(fixture), /already attempted/);
});

for (const agentMode of ['malformed', 'invalid']) {
  test(`${agentMode} agent accounting stays unknown and is persisted`, { timeout: 15000 }, async t => {
    const fixture = await sessionFixture(t, 'success', agentMode);
    const record = await runTrialSession(fixture);
    assert.equal(record.status, 'unknown');
    assert.equal(record.usage, null);
    assert.ok(record.reason);
    assert.equal(JSON.parse(readFileSync(fixture.usagePath)).status, 'unknown');
  });
}

test('controller exit during an agent call stops and confirms the agent', { timeout: 15000 }, async t => {
  const fixture = await sessionFixture(t, 'early-exit', 'hang');
  await assert.rejects(runTrialSession(fixture), /stopped: controller/);
  const record = JSON.parse(readFileSync(fixture.usagePath));
  assert.equal(record.cleanup.status, 'verified');
  assert.equal(record.status, 'unknown');
  assert.ok(record.agentProcess);
  const pid = Number(readFileSync(join(fixture.directory, 'agent-started')));
  assert.throws(() => process.kill(pid, 0), { code: 'ESRCH' });
  assert.equal(existsSync(fixture.lock), false);
});

for (const controllerMode of ['restore-failure', 'nonzero']) {
  test(`${controllerMode} preserves agent failure and blocks another trial`, { timeout: 15000 }, async t => {
    const fixture = await sessionFixture(t, controllerMode, 'failure');
    const sigintListeners = process.listenerCount('SIGINT');
    await assert.rejects(runTrialSession(fixture), error => {
      assert.ok(error instanceof AggregateError);
      assert.equal(error.errors.length, 2);
      return true;
    });
    const record = JSON.parse(readFileSync(fixture.usagePath));
    assert.match(record.originalError, /Agent process failed/);
    assert.equal(record.cleanupErrors.length, 1);
    assert.equal(record.cleanup.status, 'unverified');
    assert.equal(existsSync(fixture.lock), true);
    assert.equal(process.listenerCount('SIGINT'), sigintListeners);
  });
}

test('a failed hold with verified restoration releases the lock and records the hold result', { timeout: 15000 }, async t => {
  const fixture = await sessionFixture(t, 'hold-failed');
  const record = await runTrialSession(fixture);
  assert.equal(record.cleanup.status, 'verified');
  assert.equal(record.cleanup.holdPassed, false);
  assert.equal(record.cleanup.holdError, 'Test TE3 exited');
  assert.equal(existsSync(fixture.lock), false);
});

test('agent deadline awaits termination and restoration', { timeout: 15000 }, async t => {
  const fixture = await sessionFixture(t, 'success', 'hang');
  fixture.agentMs = 250;
  await assert.rejects(runTrialSession(fixture), /stopped: timeout/);
  const record = JSON.parse(readFileSync(fixture.usagePath));
  assert.equal(record.timedOut, true);
  assert.equal(record.cleanup.status, 'verified');
  assert.equal(existsSync(fixture.lock), false);
});

test('cancellation wakes the session and awaits termination', { timeout: 15000 }, async t => {
  const fixture = await sessionFixture(t, 'success', 'hang');
  const cancellation = new AbortController();
  const timer = setInterval(() => {
    if (existsSync(join(fixture.directory, 'agent-started'))) cancellation.abort();
  }, 10);
  try {
    await assert.rejects(runTrialSession({ ...fixture, signal: cancellation.signal }), /stopped: interrupted/);
  } finally { clearInterval(timer); }
  const record = JSON.parse(readFileSync(fixture.usagePath));
  assert.equal(record.interrupted, true);
  assert.equal(record.cleanup.status, 'verified');
});

test('unconfirmed controller restoration retains its lock and durable outcome', { timeout: 15000 }, async t => {
  const fixture = await sessionFixture(t, 'slow-cleanup');
  fixture.cleanupMs = 30;
  await assert.rejects(runTrialSession(fixture), /Controller restoration timed out/);
  assert.equal(existsSync(fixture.lock), true);
  assert.equal(JSON.parse(readFileSync(fixture.usagePath)).cleanup.status, 'unverified');
});

test('agent spawn failure still persists accounting and cleans the controller', { timeout: 15000 }, async t => {
  const fixture = await sessionFixture(t);
  fixture.agentCommand = [join(fixture.directory, 'missing.exe')];
  await assert.rejects(runTrialSession(fixture), /Agent process failed/);
  const record = JSON.parse(readFileSync(fixture.usagePath));
  assert.equal(record.status, 'unknown');
  assert.match(record.originalError, /ENOENT/);
  assert.equal(record.cleanup.status, 'verified');
});

test('live CLI requires handoff and rejects unknown trials before starting processes', async t => {
  const fixture = await sessionFixture(t);
  const input = join(fixture.directory, 'input.txt');
  const runtime = join(fixture.directory, 'runtime.bin');
  await writeFile(input, 'frozen');
  await writeFile(runtime, 'runtime');
  const runtimeRecord = { path: runtime, sha256: digest('runtime') };
  await writeStudy(fixture.directory, {
    frozen: { 'input.txt': digest('frozen') },
    runtimes: { node: runtimeRecord, claude: runtimeRecord, te3: runtimeRecord, te: runtimeRecord }
  });
  await assert.rejects(run(fixture.directory, '0b-sonnet-formatting-01', false), /Fresh desktop handoff required/);
  await assert.rejects(run(fixture.directory, '0b-sonnet-formatting-01', true), /Unknown trial/);
  assert.equal(existsSync(join(fixture.directory, 'agent-started')), false);
});

test('summarize reports trials with review fields and rung/task/model groups', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ladder-summary-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const trial = async (id, task, files) => {
    await mkdir(join(root, id));
    const usage = { id, rung: '1', model: 'sonnet', task, lifecycleOutcome: 'completed', cleanup: { status: 'verified' },
      status: 'final', usage: { input_tokens: 10, cache_creation_input_tokens: 10, cache_read_input_tokens: 70,
        output_tokens: 10, uncached_input_tokens: 20, total_tokens: 100 } };
    await writeFile(join(root, id, 'usage.json'), JSON.stringify(usage));
    for (const [name, text] of Object.entries(files)) await writeFile(join(root, id, name), text);
  };
  const review = (passed, claimedSuccess) => JSON.stringify({
    passed, claimedSuccess, rubric: { 'section selected': passed }, notes: 'checked'
  });
  await trial('1-sonnet-formatting-01', 'formatting', {
    'calls.jsonl': [
      { type: 'tool-requested', call: 1, tool: 'windows_snapshot' },
      { type: 'tool-requested', call: 4, cost: 3, tool: 'windows_batch' },
      { type: 'tool-rejected', tool: 'windows_launch', reason: 'controller-owned lifecycle' },
      { type: 'budget-rejected', tool: 'windows_find', cost: 1, error: 'call budget exhausted (60/60 used; request needs 1)' }
    ].map(record => JSON.stringify(record)).join('\n'),
    'review.json': review(true, true)
  });
  await trial('1-sonnet-formatting-02', 'formatting', { 'review.json': review(false, true) });
  await trial('1-sonnet-bpa-view-01', 'bpa-view', {});
  await mkdir(join(root, '1-sonnet-object-01'));

  const rows = summarize(root);
  assert.equal(rows.length, 3);
  const scored = rows.find(row => row.id === '1-sonnet-formatting-01');
  assert.equal(scored.kind, 'trial');
  assert.equal(scored.rung, '1');
  assert.equal(scored.totalTokens, 100);
  assert.equal(scored.uncachedInputTokens, 20);
  assert.equal(scored.dispatchedCalls, 4);
  assert.equal(scored.toolRejections, 1);
  assert.equal(scored.budgetRejections, 1);
  assert.equal(scored.passed, true);
  assert.deepEqual(scored.rubric, { 'section selected': true });
  const holdout = rows.find(row => row.task === 'bpa-view');
  assert.equal(holdout.holdout, true);
  assert.equal(holdout.reviewed, false);
  assert.equal(holdout.dispatchedCalls, null);

  const groups = groupRows(rows);
  const formatting = groups.find(group => group.task === 'formatting');
  assert.deepEqual({ ...formatting }, {
    kind: 'group', rung: '1', task: 'formatting', model: 'sonnet', label: null, holdout: false, n: 2, passes: 1, passAll: false,
    falseClaims: 1, meanDispatchedCalls: 4, meanTotalTokens: 100, meanUncachedInputTokens: 20, unreviewed: 0
  });
  assert.equal(groups.find(group => group.task === 'bpa-view').unreviewed, 1);
  assert.deepEqual(conditionRows(rows), [{
    kind: 'condition', rung: '1', model: 'sonnet', label: null, passes: 1, n: 3,
    trained: { passes: 1, n: 2 }, holdout: { passes: 0, n: 1 },
    falseClaims: 1, medianDispatchedCalls: 4, meanTotalTokens: 100, unreviewed: 1
  }]);
  assert.deepEqual(conditionRows([...rows, { ...rows[0], task: 'relationship', passed: false }]), conditionRows(rows));

  await writeFile(join(root, '1-sonnet-bpa-view-01', 'review.json'), JSON.stringify({ passed: 'yes' }));
  assert.throws(() => summarize(root), /boolean passed/);
});

test('summarize keeps the hold-out status a trial was prepared with', async t => {
  const root = await mkdtemp(join(tmpdir(), 'ladder-holdout-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  await mkdir(join(root, '1-sonnet-dax-general-01'));
  await writeFile(join(root, 'study.json'), JSON.stringify({ trials: [{ id: '1-sonnet-dax-general-01', task: 'dax-general', holdout: true }] }));
  await writeFile(join(root, '1-sonnet-dax-general-01', 'usage.json'), JSON.stringify({ id: '1-sonnet-dax-general-01', rung: '1', task: 'dax-general' }));
  assert.equal(summarize(root)[0].holdout, true);
});
