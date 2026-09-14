import assert from 'node:assert/strict';
import test from 'node:test';
import {readFileSync, existsSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {mkdtemp, mkdir, writeFile, rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {schedule, promptFor, validateEvidence, usageFrom, verifyFrozenInputs, run, runTrialSession, agentArguments, assertSameAssemblies, findRollout, summarize} from './four-arm-study.mjs';

test('pilot covers every model/effort, task and arm exactly once', () => {
  const trials = schedule();
  assert.equal(trials.length, 36);
  assert.equal(new Set(trials.map(trial => trial.id)).size, 36);
  assert.deepEqual([...new Set(trials.map(trial => `${trial.model}/${trial.effort}`))], [
    'gpt-5.6-luna/low', 'gpt-5.6-terra/medium', 'gpt-5.6-sol/low'
  ]);
  for (const task of ['formatting', 'object', 'script']) {
    for (const model of ['gpt-5.6-luna', 'gpt-5.6-terra', 'gpt-5.6-sol']) {
      const arms = trials.filter(trial => trial.task === task && trial.model === model)
        .map(trial => trial.arm).sort();
      assert.deepEqual(arms, ['A', 'B', 'C', 'D']);
    }
  }
});

test('only the specified treatment is added to otherwise identical prompts', () => {
  const make = arm => promptFor({task:'script', arm}, 'GENERIC-SKILL', 'MAP-BOOTSTRAP',
    'C:\\trial\\hello-world.csx', 'C:\\trial\\result.png');
  assert.ok(!make('A').includes('GENERIC-SKILL'));
  assert.ok(!make('A').includes('MAP-BOOTSTRAP'));
  assert.ok(make('B').includes('GENERIC-SKILL'));
  assert.ok(!make('B').includes('MAP-BOOTSTRAP'));
  assert.equal(make('C'), make('D'));
  assert.ok(make('C').startsWith(make('B').trimEnd()));
  assert.ok(make('B').startsWith(make('A').trimEnd()));
  assert.ok(make('A').includes('C:\\trial\\hello-world.csx'));
});

test('evidence requires full references from three different trials', () => {
  const evidence = JSON.parse(readFileSync(new URL('./generic-skill-evidence.json', import.meta.url)));
  assert.doesNotThrow(() => validateEvidence(evidence));
  const repeated = structuredClone(evidence);
  repeated.rules[0].traces.forEach(trace => trace.trial = 'one-trial');
  assert.throws(() => validateEvidence(repeated), /three distinct trials/);
  const missing = structuredClone(evidence);
  delete missing.rules[0].traces[0].event;
  assert.throws(() => validateEvidence(missing), /missing event/);
});

test('usage does not double-count cached input or reasoning output', () => {
  const result = usageFrom([{type:'turn.completed', usage:{
    input_tokens:1000, cached_input_tokens:800, output_tokens:100, reasoning_output_tokens:40
  }}]);
  assert.equal(result.usage.total_tokens, 1100);
  assert.equal(result.usage.uncached_input_tokens, 200);
  assert.equal(usageFrom([]).usage, null);
  assert.match(usageFrom([{type:'turn.completed', usage:{
    input_tokens:10, cached_input_tokens:20, output_tokens:1
  }}]).reason, /Invalid token counters/);
});

const digest = value => createHash('sha256').update(value).digest('hex');
async function writeStudy(directory, value) {
  const text = JSON.stringify(value);
  await writeFile(join(directory, 'study.json'), text);
  await writeFile(join(directory, 'study.json.sha256'), digest(text));
}

test('frozen inputs and recorded runtimes reject missing or changed files', async t => {
  const root = await mkdtemp(join(tmpdir(), 'four-arm-freeze-'));
  const input = join(root, 'input.txt');
  const runtime = join(root, 'runtime.bin');
  await writeFile(input, 'frozen input');
  await writeFile(runtime, 'runtime bytes');
  await writeStudy(root, { frozen: {}, runtimes: {} });
  assert.throws(() => verifyFrozenInputs(root), /Frozen input inventory must be a nonempty object/);
  const runtimeRecord = { path: runtime, sha256: digest('runtime bytes') };
  const valid = {
    frozen: { 'input.txt': digest('frozen input') },
    runtimes: { node: runtimeRecord, codexLauncher: runtimeRecord, codexNative: runtimeRecord, te3: runtimeRecord, te: runtimeRecord }
  };
  await writeStudy(root, valid);
  t.after(() => rm(root, { recursive: true, force: true }));

  assert.deepEqual(verifyFrozenInputs(root), { inputs: 1, runtimes: 5 });
  await writeFile(join(root, 'study.json'), JSON.stringify({ ...valid, trials: [] }));
  assert.throws(() => verifyFrozenInputs(root), /seal missing or study.json changed/);
  await writeStudy(root, valid);
  await writeFile(input, 'changed input');
  assert.throws(() => verifyFrozenInputs(root), /Frozen input changed: input.txt/);
  await writeFile(input, 'frozen input');
  await rm(runtime);
  assert.throws(() => verifyFrozenInputs(root), /Runtime missing: node/);
  await writeFile(runtime, 'runtime bytes');
  await writeStudy(root, { frozen: { 'input.txt': 'not-a-digest' }, runtimes: { node: runtimeRecord } });
  assert.throws(() => verifyFrozenInputs(root), /Invalid frozen input digest/);
  await writeStudy(root, { frozen: { 'input.txt': digest('frozen input') }, runtimes: { node: runtimeRecord } });
  assert.throws(() => verifyFrozenInputs(root), /Runtime inventory missing or invalid: codexLauncher/);
});

test('controller assemblies must match the study build', async t => {
  const root = await mkdtemp(join(tmpdir(), 'four-arm-assemblies-'));
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

// These subprocesses only write local fixture files. No Codex or desktop app runs.
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

const fakeAgent = String.raw`
import fs from 'node:fs';
import path from 'node:path';
const [directory, mode] = process.argv.slice(2);
fs.writeFileSync(path.join(directory, 'agent-started'), String(process.pid));
fs.writeFileSync(path.join(directory, 'agent-cwd'), process.cwd());
console.log(JSON.stringify({ type: 'thread.started', thread_id: 'fake-thread' }));
if (mode === 'hang') setInterval(() => {}, 100);
else {
  if (mode === 'malformed') console.log('{bad JSON');
  console.log(JSON.stringify({ type: 'turn.completed', usage: {
    input_tokens: 100, cached_input_tokens: mode === 'invalid' ? 101 : 80, output_tokens: 10
  }}));
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
  const root = await mkdtemp(join(tmpdir(), 'four-arm-unit-'));
  const directory = join(root, 'trial');
  await mkdir(directory);
  const controller = join(root, 'controller.mjs');
  const agent = join(root, 'agent.mjs');
  await writeFile(controller, fakeController);
  await writeFile(agent, fakeAgent);
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
    directory, trial: { id: 'fake-A', model: 'fake', effort: 'low', arm: 'A' },
    controllerCommand: [process.execPath, controller, directory, controllerMode],
    agentCommand: [process.execPath, agent, directory, agentMode],
    readyMs: 3000, agentMs: 3000, cleanupMs: 3000, stopMs: 3000,
    lock: join(root, 'active'), usagePath: join(directory, 'usage.json')
  };
}

test('session saves final accounting only after verified controller restoration', { timeout: 15000 }, async t => {
  const fixture = await sessionFixture(t);
  fixture.agentCwd = join(fixture.directory, 'empty-agent-cwd');
  await mkdir(fixture.agentCwd);
  fixture.sessionsRoot = join(fixture.directory, 'no-sessions');
  const result = await runTrialSession(fixture);
  assert.equal(readFileSync(join(fixture.directory, 'agent-cwd'), 'utf8'), fixture.agentCwd);
  assert.equal(result.lifecycleOutcome, 'completed');
  assert.equal(result.cleanup.status, 'verified');
  assert.match(result.review, /^pending/);
  assert.equal(result.taskOutcome, undefined);
  assert.equal(result.rolloutPath, null);
  assert.equal(result.status, 'final');
  assert.equal(result.usage.total_tokens, 110);
  assert.equal(result.threadId, 'fake-thread');
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
    runtimes: { node: runtimeRecord, codexLauncher: runtimeRecord, codexNative: runtimeRecord, te3: runtimeRecord, te: runtimeRecord }
  });
  await assert.rejects(run(fixture.directory, 'fake-A', false), /Fresh desktop handoff required/);
  await assert.rejects(run(fixture.directory, 'fake-A', true), /Unknown trial/);
  assert.equal(existsSync(join(fixture.directory, 'agent-started')), false);
});

test('rollout lookup finds the file named for the thread', async t => {
  const root = await mkdtemp(join(tmpdir(), 'four-arm-sessions-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const day = join(root, '2026', '09', '14');
  await mkdir(day, { recursive: true });
  const rollout = join(day, 'rollout-2026-09-14T06-38-58-thread-1.jsonl');
  await writeFile(rollout, '');
  await writeFile(join(day, 'rollout-2026-09-14T07-00-00-other-thread-2.jsonl'), '');
  assert.equal(findRollout('thread-1', root), rollout);
  assert.equal(findRollout('missing', root), null);
  assert.equal(findRollout(null, root), null);
  assert.equal(findRollout('thread-1', join(root, 'absent')), null);
});

test('summarize reports attempted trials and tolerates missing logs and reviews', async t => {
  const root = await mkdtemp(join(tmpdir(), 'four-arm-summary-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const trial = async (id, files) => {
    await mkdir(join(root, id));
    for (const [name, text] of Object.entries(files)) await writeFile(join(root, id, name), text);
  };
  await trial('luna-formatting-A-01', {
    'usage.json': JSON.stringify({
      id: 'luna-formatting-A-01', model: 'gpt-5.6-luna', effort: 'low', arm: 'A', task: 'formatting',
      lifecycleOutcome: 'completed', cleanup: { status: 'verified' }, timedOut: false, interrupted: false,
      status: 'final', usage: { input_tokens: 100, cached_input_tokens: 80, uncached_input_tokens: 20, output_tokens: 10, total_tokens: 110 }
    }),
    'calls.jsonl': [
      { type: 'tool-requested', call: 1, tool: 'windows_snapshot' },
      { type: 'tool-requested', call: 4, cost: 3, tool: 'windows_batch' },
      { type: 'batch-action-requested', call: 4, tool: 'windows_batch' },
      { type: 'tool-rejected', tool: 'windows_launch', reason: 'controller-owned lifecycle' },
      { type: 'result-error', call: 5, error: 'call budget exhausted (60/60 used; request needs 1)' }
    ].map(record => JSON.stringify(record)).join('\n'),
    'review.json': JSON.stringify({ taskOutcome: 'passed-observed-checks' })
  });
  await trial('sol-object-B-01', {
    'usage.json': JSON.stringify({ id: 'sol-object-B-01', status: 'unknown', usage: null })
  });
  await trial('terra-script-C-01', { 'prompt-template.txt': 'not attempted' });

  const rows = summarize(root);
  assert.deepEqual(rows.map(row => row.id), ['luna-formatting-A-01', 'sol-object-B-01']);
  const [scored, unscored] = rows;
  assert.equal(scored.cleanup, 'verified');
  assert.equal(scored.totalTokens, 110);
  assert.equal(scored.uncachedInputTokens, 20);
  assert.equal(scored.dispatchedCalls, 4);
  assert.equal(scored.toolRejections, 1);
  assert.equal(scored.budgetRejections, 1);
  assert.equal(scored.reviewOutcome, 'passed-observed-checks');
  assert.equal(scored.reviewed, true);
  assert.equal(unscored.model, null);
  assert.equal(unscored.inputTokens, null);
  assert.equal(unscored.dispatchedCalls, null);
  assert.equal(unscored.reviewOutcome, 'unreviewed');
  assert.equal(unscored.reviewed, false);
});

test('agent invocation uses selected model, fresh cwd and the trial gateway without hooks', () => {
  const args = agentArguments({ config: { codexEntry: 'codex.js' } },
    { id: 'terra-object-C-01', model: 'gpt-5.6-terra', effort: 'medium' }, 'study', 'empty-cwd');
  assert.equal(args[args.indexOf('-C') + 1], 'empty-cwd');
  assert.equal(args[args.indexOf('--model') + 1], 'gpt-5.6-terra');
  assert.ok(args.includes('model_reasoning_effort="medium"'));
  assert.ok(args.includes('features.hooks=false'));
  assert.ok(!args.some(arg => arg.includes('view_image')), 'every arm can review saved images');
  assert.ok(args.some(arg => arg.includes('terra-object-C-01/gateway.json')));
});
