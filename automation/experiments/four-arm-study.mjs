import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import os from 'node:os';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
export const models = [
  ['luna', 'gpt-5.6-luna', 'low'],
  ['terra', 'gpt-5.6-terra', 'medium'],
  ['sol', 'gpt-5.6-sol', 'low']
];
export const tasks = {
  formatting: 'Capture Preferences > Text Editors > DAX Editor > Auto Formatting with that section selected, empty search, all section controls (including Use default formatting settings), title and bottom buttons readable. Do not change settings to satisfy the image. Leave the section open.',
  object: 'Select the Amount column in the Comparison table, not Sales[Amount]. Verify the table, name and column type through UI evidence. Capture the selected row with visible table context and readable Properties. Leave the object selected.',
  script: 'Open the supplied .csx through TE3 UI. Verify its source is exactly `"Hello World".Output();` ignoring trailing whitespace, execute only that source, and capture the output dialog title, Hello World text and Close button. Leave the output open. Do not type replacement source, change file associations, or interact with unrelated applications. Stop if an unrelated app chooser appears.'
};
export function schedule() {
  const orders = ['ABDC', 'BCAD', 'CDBA', 'DACB'];
  const trials = [];
  let block = 0;
  for (const task of Object.keys(tasks)) {
    for (const [label, model, effort] of models) {
      const order = orders[block++ % orders.length];
      for (const arm of order) {
        trials.push({ id: `${label}-${task}-${arm}-01`, model, effort, arm, task, repetition: 1 });
      }
    }
  }
  return trials;
}
export function promptFor(trial, skill, bootstrap, scriptPath, outputPath) {
  let prompt = `${tasks[trial.task]}\n\nThe owned TE3 processId is {{PID}}. Save the final PNG to ${outputPath}. Preserve model content and preferences. Do not launch or close TE3; the controller owns lifecycle and restoration. You may focus this test instance during the authorized handoff. This is an offline disposable model; no server access is needed.`;
  if (trial.task === 'script') {
    prompt += `\nSupplied source: ${scriptPath}. You are authorized to execute this exact read-only output script.`;
  }
  prompt += '\n\nUse only the supplied MCP tools and functions orchestration. No shell, web, delegation, other applications, repository or prior trial artifacts. The supplied script may be read only through TE3. Do not load other skills. Stop within 60 dispatched MCP calls or seven minutes. Report failure if unfinished; success requires evidence. Do not save or edit the model.';
  if (trial.arm !== 'A') prompt += `\n\nApply this frozen generic MCP skill:\n${skill}`;
  if (trial.arm === 'C' || trial.arm === 'D') prompt += `\n\n${bootstrap}`;
  return prompt + '\n';
}
const hash = file => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const json = file => JSON.parse(fs.readFileSync(file, 'utf8'));
const write = (file, value) => fs.writeFileSync(file, JSON.stringify(value, null, 2) + '\n', { flag: 'wx' });
const sleep = ms => new Promise(resolveSleep => setTimeout(resolveSleep, ms));
const runtimeRecord = file => ({ path: fs.realpathSync(file), sha256: hash(file) });
function expand(value) {
  if (typeof value !== 'string' || !value.trim()) throw new Error('Configuration paths must be nonempty strings');
  const expanded = value.replace(/%([^%]+)%/g, (_, key) => {
    if (!process.env[key]) throw new Error(`Missing environment variable ${key}`);
    return process.env[key];
  });
  return path.resolve(expanded);
}
export function usageFrom(events, malformedLines = 0) {
  const unknown = reason => ({ status: 'unknown', reason, usage: null });
  if (malformedLines) return unknown(`${malformedLines} malformed agent event line(s)`);
  const completed = events.filter(event => event?.type === 'turn.completed');
  if (completed.length !== 1) return unknown(`Expected one completed turn; observed ${completed.length}`);
  const usage = completed[0].usage;
  if (!usage) return unknown('Completed turn has no usage');
  const counters = ['input_tokens', 'cached_input_tokens', 'output_tokens'];
  if (!counters.every(key => Number.isSafeInteger(usage[key]) && usage[key] >= 0) ||
      usage.cached_input_tokens > usage.input_tokens ||
      !Number.isSafeInteger(usage.input_tokens + usage.output_tokens)) {
    return unknown('Invalid token counters');
  }
  return {
    status: 'final', reason: null,
    usage: { ...usage, uncached_input_tokens: usage.input_tokens - usage.cached_input_tokens,
      total_tokens: usage.input_tokens + usage.output_tokens }
  };
}
function codexNativeRuntime(launcher) {
  const triples = {
    win32: { x64: 'x86_64-pc-windows-msvc', arm64: 'aarch64-pc-windows-msvc' },
    darwin: { x64: 'x86_64-apple-darwin', arm64: 'aarch64-apple-darwin' },
    linux: { x64: 'x86_64-unknown-linux-musl', arm64: 'aarch64-unknown-linux-musl' }
  };
  const triple = triples[process.platform]?.[process.arch];
  if (!triple) throw new Error(`Unsupported Codex runtime platform: ${process.platform}/${process.arch}`);
  const packageName = `codex-${process.platform}-${process.arch}`;
  const packageRoot = path.resolve(fs.realpathSync(launcher), '..', '..');
  const packageRoots = [
    path.join(packageRoot, 'node_modules', '@openai', packageName),
    path.resolve(packageRoot, '..', packageName),
    packageRoot
  ];
  const binary = process.platform === 'win32' ? 'codex.exe' : 'codex';
  for (const root of packageRoots) {
    const candidate = path.join(root, 'vendor', triple, 'bin', binary);
    if (fs.existsSync(candidate)) return candidate;
  }
  throw new Error(`Missing native Codex runtime for ${triple}; the configured launcher is not reproducible.`);
}

// The controller restores settings with its own copy of the runner; it must match the build agents use.
export function assertSameAssemblies(buildDirectory, controllerDirectory, names = ['FlaUI.Mcp.dll', 'FlaUI.Automation.dll']) {
  for (const name of names) {
    const built = path.join(buildDirectory, name);
    const held = path.join(controllerDirectory, name);
    if (!fs.existsSync(built) || !fs.existsSync(held)) throw new Error(`Missing ${name} in build or controller directory`);
    if (hash(built) !== hash(held)) throw new Error(`Controller ${name} differs from the study build; rebuild both from the same source`);
  }
}

export function verifyFrozenInputs(directory) {
  const out = path.resolve(directory);
  // study.json cannot list its own hash, so a sidecar seals it.
  const seal = path.join(out, 'study.json.sha256');
  if (!fs.existsSync(seal) || fs.readFileSync(seal, 'utf8').trim() !== hash(path.join(out, 'study.json'))) {
    throw new Error('Study record seal missing or study.json changed after preparation');
  }
  const study = json(path.join(out, 'study.json'));
  const frozen = study.frozen;
  if (!frozen || typeof frozen !== 'object' || Array.isArray(frozen) || Object.keys(frozen).length === 0) {
    throw new Error('Frozen input inventory must be a nonempty object');
  }
  for (const [relative, digest] of Object.entries(frozen)) {
    if (typeof digest !== 'string' || !/^[a-f0-9]{64}$/i.test(digest)) throw new Error(`Invalid frozen input digest: ${relative}`);
    const file = path.resolve(out, relative);
    if (path.relative(out, file).startsWith('..') || path.isAbsolute(path.relative(out, file))) throw new Error(`Invalid frozen input path: ${relative}`);
    if (!fs.existsSync(file)) throw new Error(`Frozen input missing: ${relative}`);
    if (hash(file) !== digest) throw new Error(`Frozen input changed: ${relative}`);
  }
  const runtimes = study.runtimes;
  const requiredRuntimes = ['node', 'codexLauncher', 'codexNative', 'te3', 'te'];
  if (!runtimes || typeof runtimes !== 'object' || Array.isArray(runtimes)) throw new Error('Runtime inventory missing');
  for (const name of requiredRuntimes) {
    const runtime = runtimes[name];
    if (!runtime || typeof runtime.path !== 'string' || typeof runtime.sha256 !== 'string' ||
        !/^[a-f0-9]{64}$/i.test(runtime.sha256)) throw new Error(`Runtime inventory missing or invalid: ${name}`);
    if (!fs.existsSync(runtime.path)) throw new Error(`Runtime missing: ${name}`);
    if (hash(runtime.path) !== runtime.sha256) throw new Error(`Runtime changed: ${name}`);
  }
  return { inputs: Object.keys(frozen).length, runtimes: requiredRuntimes.length };
}

export function validateEvidence(evidence) {
  if (!evidence.rules?.length) {
    throw Error('No evidence-qualified skill rules');
  }
  for (const rule of evidence.rules) {
    if (!rule.id || !rule.correction || !Array.isArray(rule.traces)) {
      throw Error('Each skill rule needs an id, correction, and trace references');
    }
    for (const trace of rule.traces) {
      for (const field of ['trial', 'event', 'tool', 'log', 'observed']) {
        if (typeof trace[field] !== 'string' || !trace[field].trim()) {
          throw Error(`Rule ${rule.id}: trace is missing ${field}`);
        }
      }
    }
    if (new Set(rule.traces.map(trace => trace.trial)).size < 3) {
      throw Error(`Rule ${rule.id} needs three distinct trials`);
    }
  }
  // This validates reference structure, not the truth of a reviewer’s assessment.
}
export function prepare(configPath, destination) {
  const config = json(configPath);
  const requiredPaths = ['buildDirectory', 'controller', 'baseline', 'te3', 'te', 'codexEntry'];
  for (const key of requiredPaths) {
    config[key] = expand(config[key]);
    if (!fs.existsSync(config[key])) throw new Error(`Missing ${key}`);
  }
  for (const executable of ['FlaUI.Mcp.exe', 'FlaUI.Automation.exe']) {
    if (!fs.existsSync(path.join(config.buildDirectory, executable))) throw new Error(`Missing ${executable}`);
  }
  const evidence = json(path.join(here, 'generic-skill-evidence.json'));
  validateEvidence(evidence);
  const skill = fs.readFileSync(path.join(here, '../skills/flaui-mcp/SKILL.md'), 'utf8');
  const bootstrap = 'Use te3_catalog() for the index, then read the relevant map topic using its exact ID. For screenshots also read capture. Map access is guidance, not a guarantee of successful navigation or a complete image.';
  const out = path.resolve(destination);
  if (fs.existsSync(out)) throw new Error('Use a fresh study directory');
  assertSameAssemblies(config.buildDirectory, path.dirname(config.controller));
  const runtimes = {
    node: runtimeRecord(process.execPath),
    codexLauncher: runtimeRecord(config.codexEntry),
    codexNative: runtimeRecord(codexNativeRuntime(config.codexEntry)),
    te3: runtimeRecord(config.te3),
    te: runtimeRecord(config.te)
  };

  // Copy immutable inputs before creating per-trial output directories.
  fs.mkdirSync(out, { recursive: true });
  fs.cpSync(config.buildDirectory, path.join(out, 'build'), { recursive: true });
  fs.cpSync(path.dirname(config.controller), path.join(out, 'controller'), { recursive: true });
  fs.copyFileSync(config.baseline, path.join(out, 'fixture.bim'));
  fs.writeFileSync(path.join(out, 'hello-world.csx'), '"Hello World".Output();\r\n', { flag: 'wx' });
  fs.writeFileSync(path.join(out, 'generic-SKILL.md'), skill, { flag: 'wx' });
  fs.writeFileSync(path.join(out, 'map-bootstrap.txt'), bootstrap, { flag: 'wx' });
  fs.cpSync(path.join(here, '../map'), path.join(out, 'map'), { recursive: true });
  fs.copyFileSync(path.join(here, 'study-gateway.mjs'), path.join(out, 'study-gateway.mjs'));
  fs.copyFileSync(fileURLToPath(import.meta.url), path.join(out, 'four-arm-study.mjs'));
  write(path.join(out, 'evidence.json'), evidence);

  const trials = schedule();
  for (const trial of trials) {
    const dir = path.join(out, trial.id);
    fs.mkdirSync(dir);
    const prompt = promptFor(trial, skill, bootstrap,
      path.win32.normalize(path.join(out, 'hello-world.csx')), path.join(dir, 'result.png'));
    fs.writeFileSync(path.join(dir, 'prompt-template.txt'), prompt, { flag: 'wx' });
    write(path.join(dir, 'gateway.json'), {
      arm: trial.arm, buildDirectory: path.join(out, 'build'), logPath: path.join(dir, 'calls.jsonl'), maxCalls: 60
    });
  }
  write(path.join(out, 'controller.config.json'), {
    te3: config.te3, te: config.te, fixtureMode: 'offline', offlineBaseline: path.join(out, 'fixture.bim'),
    output: out, maximizeWindow: true, expectedDpi: 120, repeat: 1, scenarios: ['model-open']
  });

  const frozen = {};
  function inventory(directory) {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const file = path.join(directory, entry.name);
      if (entry.isDirectory()) inventory(file);
      else frozen[path.relative(out, file)] = hash(file);
    }
  }
  inventory(out);
  write(path.join(out, 'study.json'), {
    version: 1, trials,
    config: { ...config, controller: path.join(out, 'controller', path.basename(config.controller)) },
    frozen, runtimes, desktopPreflight: 'not-run', liveExecution: 'one-trial-with-handoff',
    isolation: 'Instruction-controlled; audit violations and retain contaminated trials with their costs.',
    freezeLimitations: 'Input/runtime hashes detect changes; they do not freeze the OS or hosted model, or authorize desktop access.'
  });
  fs.writeFileSync(path.join(out, 'study.json.sha256'), hash(path.join(out, 'study.json')) + '\n', { flag: 'wx' });
  return { directory: out, trials: trials.length };
}
// File descriptors avoid a second asynchronous log-drain lifecycle. Read logs
// only once the child has closed; spawn and stdin errors are retained as data.
export function startOwned(command, cwd, stdoutPath, stderrPath) {
  const output = fs.openSync(stdoutPath, 'wx');
  let errors;
  try {
    errors = fs.openSync(stderrPath, 'wx');
    const child = spawn(command[0], command.slice(1), {
      cwd, windowsHide: true, detached: process.platform !== 'win32',
      stdio: ['pipe', output, errors]
    });
    const owned = { child, result: null, finished: null };
    const failures = [];
    child.on('error', error => failures.push(error.message));
    child.stdin.on('error', error => failures.push(`stdin: ${error.message}`));
    owned.finished = new Promise(resolveFinished => {
      child.once('close', (code, signal) => {
        owned.result = { code, signal, errors: failures };
        resolveFinished(owned.result);
      });
    });
    return owned;
  } finally {
    fs.closeSync(output);
    if (errors !== undefined) fs.closeSync(errors);
  }
}

async function within(promise, milliseconds, description) {
  let timer;
  try {
    return await Promise.race([
      promise,
      new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error(`${description} timed out`)), milliseconds);
      })
    ]);
  } finally {
    clearTimeout(timer);
  }
}

export async function stopOwned(owned, milliseconds) {
  if (owned.result) return owned.result;
  const child = owned.child;
  if (!child.pid) return within(owned.finished, milliseconds, 'Failed process startup');
  if (process.platform === 'win32') {
    const killer = spawn('taskkill', ['/PID', String(child.pid), '/T', '/F'], {
      windowsHide: true, stdio: 'ignore'
    });
    const killed = new Promise((resolveKilled, reject) => {
      killer.once('error', reject);
      killer.once('close', code => resolveKilled(code));
    });
    let code;
    try {
      code = await within(killed, milliseconds, 'Owned process-tree termination');
    } catch (error) {
      killer.kill();
      await within(killed.catch(() => null), milliseconds, 'Termination helper shutdown');
      throw error;
    }
    // A naturally exiting child may disappear between the check and taskkill.
    if (code !== 0 && !owned.result) throw new Error(`taskkill failed (${code}); PID ${child.pid} exit unconfirmed`);
  } else {
    try { process.kill(-child.pid, 'SIGKILL'); }
    catch (error) { if (error.code !== 'ESRCH') throw error; }
  }
  return within(owned.finished, milliseconds, `Owned PID ${child.pid} shutdown`);
}

export function readEvents(file) {
  const events = [];
  let malformedLines = 0;
  if (!fs.existsSync(file)) return { events, malformedLines };
  for (const line of fs.readFileSync(file, 'utf8').split(/\r?\n/)) {
    if (!line.trim()) continue;
    try {
      const event = JSON.parse(line);
      if (!event || typeof event !== 'object' || typeof event.type !== 'string') malformedLines++;
      else events.push(event);
    } catch { malformedLines++; }
  }
  return { events, malformedLines };
}

// Shared by the two standalone probes; deliberately excludes TE3/controller lifecycle.
export async function runOwnedPrompt(command, cwd, prompt, outputDirectory, timeoutMs = 90000) {
  const logPath = path.join(outputDirectory, 'agent.jsonl');
  const owned = startOwned(command, cwd, logPath, path.join(outputDirectory, 'stderr.log'));
  let executionError = null;
  let cleanupError = null;
  try {
    owned.child.stdin.end(prompt);
    await within(owned.finished, timeoutMs, 'Standalone probe');
    if (owned.result.code !== 0 || owned.result.errors.length) {
      executionError = `Probe process failed: ${JSON.stringify(owned.result)}`;
    }
  } catch (error) { executionError = error.message; }
  finally {
    try { await stopOwned(owned, 10000); }
    catch (error) { cleanupError = error.message; }
  }
  return { process: owned.result, executionError, cleanupError, ...readEvents(logPath) };
}

export function inspectControllerCleanup(directory, result) {
  if (!result || result.code !== 0 || result.errors.length) {
    throw new Error(`Controller did not exit cleanly: ${JSON.stringify(result)}`);
  }
  const output = fs.readFileSync(path.join(directory, 'controller.stdout.log'), 'utf8');
  const reports = [];
  for (const line of output.split(/\r?\n/)) {
    try {
      const report = JSON.parse(line);
      if (report?.ManifestPath) reports.push(report);
    } catch { /* Controller also emits human-readable progress lines. */ }
  }
  if (reports.length !== 1) throw new Error('Expected one controller manifest reference');
  const manifestPath = path.resolve(reports[0].ManifestPath);
  const relative = path.relative(path.resolve(directory), manifestPath);
  if (!relative || relative.startsWith('..') || path.isAbsolute(relative)) {
    throw new Error('Controller manifest must be inside this trial directory');
  }
  const manifest = json(manifestPath);
  // RunExecutor persists camelCase; its stdout wrapper currently uses PascalCase.
  const field = name => manifest[name] ?? manifest[name[0].toUpperCase() + name.slice(1)];
  // Restoration is judged on its own; a failed or interrupted hold can still restore cleanly.
  if (field('settingsRestored') !== true || field('needsRecovery') !== false || field('cleanupError')) {
    throw new Error(`Controller restoration not verified: ${manifestPath}`);
  }
  return { status: 'verified', manifestPath, holdPassed: field('passed') === true, holdError: field('error') ?? null };
}

async function waitForReady(controller, directory, signal, milliseconds) {
  const deadline = Date.now() + milliseconds;
  const readyPath = path.join(directory, 'ready.json');
  while (true) {
    if (signal.aborted) throw new Error('Trial interrupted before controller readiness');
    if (controller.result) throw new Error(`Controller exited before readiness: ${JSON.stringify(controller.result)}`);
    if (fs.existsSync(readyPath)) {
      const ready = json(readyPath);
      if (!Number.isInteger(ready.processId) || ready.processId <= 0) throw new Error('Invalid controller processId');
      return ready;
    }
    if (Date.now() >= deadline) throw new Error('Controller readiness timed out');
    await sleep(25);
  }
}

// Codex names rollouts rollout-<timestamp>-<threadId>.jsonl under YYYY/MM/DD folders.
export function findRollout(threadId, sessionsRoot) {
  if (!threadId || !fs.existsSync(sessionsRoot)) return null;
  const suffix = `-${threadId}.jsonl`;
  const match = fs.readdirSync(sessionsRoot, { recursive: true })
    .find(relative => path.basename(relative).startsWith('rollout-') && relative.endsWith(suffix));
  return match ? path.join(path.resolve(sessionsRoot), match) : null;
}

// The public CLI stays gated below. This explicit session boundary permits
// lifecycle tests with fake commands without pretending that a live preflight ran.
export async function runTrialSession({ directory, trial, controllerCommand, agentCommand,
  agentCwd = directory,
  promptTemplate = '{{PID}}', signal: externalSignal, readyMs = 180000,
  agentMs = 420000, cleanupMs = 90000, stopMs = 10000,
  sessionsRoot = path.join(os.homedir(), '.codex', 'sessions') }) {
  const dir = path.resolve(directory);
  const lock = path.join(path.dirname(dir), 'active');
  const usagePath = path.join(dir, 'usage.json');
  if (fs.existsSync(usagePath) || fs.existsSync(path.join(dir, 'controller.stdout.log'))) {
    throw new Error('Trial already attempted; never overwrite it');
  }
  write(lock, { pid: process.pid, trial: trial.id, started: new Date().toISOString() });
  const cancellation = new AbortController();
  const cancel = () => cancellation.abort();
  process.on('SIGINT', cancel);
  process.on('SIGTERM', cancel);
  externalSignal?.addEventListener('abort', cancel);
  if (externalSignal?.aborted) cancel();
  const { signal } = cancellation;
  let resolveCancelled;
  const cancelled = new Promise(resolveCancel => { resolveCancelled = resolveCancel; });
  const onAbort = () => resolveCancelled('interrupted');
  signal.addEventListener('abort', onAbort);
  if (signal.aborted) onAbort();

  let controller;
  let agent;
  let originalError = null;
  let timedOut = false;
  let agentStarted = null;
  const cleanupErrors = [];
  let cleanup = { status: 'not-needed' };
  const started = new Date().toISOString();
  try {
    if (signal.aborted) throw new Error('Trial interrupted before startup');
    controller = startOwned(controllerCommand, dir,
      path.join(dir, 'controller.stdout.log'), path.join(dir, 'controller.stderr.log'));
    const ready = await waitForReady(controller, dir, signal, readyMs);
    const prompt = promptTemplate.replace('{{PID}}', String(ready.processId));
    fs.writeFileSync(path.join(dir, 'prompt.txt'), prompt, { flag: 'wx' });
    if (signal.aborted || controller.result) throw new Error('Controller session ended before agent launch');
    agentStarted = new Date().toISOString();
    agent = startOwned(agentCommand, agentCwd, path.join(dir, 'agent.jsonl'), path.join(dir, 'agent.stderr.log'));
    agent.child.stdin.end(prompt);
    let timer;
    let ended;
    try {
      ended = await Promise.race([
        agent.finished.then(() => 'agent'),
        controller.finished.then(() => 'controller'),
        cancelled,
        new Promise(resolveTimeout => { timer = setTimeout(() => resolveTimeout('timeout'), agentMs); })
      ]);
    } finally { clearTimeout(timer); }
    timedOut = ended === 'timeout';
    if (ended !== 'agent') throw new Error(`Agent trial stopped: ${ended}`);
    if (agent.result.code !== 0 || agent.result.errors.length) {
      throw new Error(`Agent process failed: ${JSON.stringify(agent.result)}`);
    }
  } catch (error) {
    originalError = error;
  } finally {
    // Cancellation only wakes the main flow. All shutdown work is awaited here.
    if (agent) {
      try { await stopOwned(agent, stopMs); }
      catch (error) { cleanupErrors.push(`Agent shutdown: ${error.message}`); }
    }
    if (controller) {
      try {
        fs.writeFileSync(path.join(dir, 'done'), 'stop\n', { flag: 'wx' });
        const result = await within(controller.finished, cleanupMs, 'Controller restoration');
        cleanup = inspectControllerCleanup(dir, result);
      } catch (error) {
        cleanup = { status: 'unverified' };
        cleanupErrors.push(error.message);
        // Do not kill a controller while it may be restoring user preferences.
      }
    }
    process.off('SIGINT', cancel);
    process.off('SIGTERM', cancel);
    externalSignal?.removeEventListener('abort', cancel);
    signal.removeEventListener('abort', onAbort);
  }

  let accounting = { events: [], malformedLines: 0 };
  let accountingError = null;
  try { accounting = readEvents(path.join(dir, 'agent.jsonl')); }
  catch (error) { accountingError = error.message; }
  const usage = accountingError
    ? { status: 'unknown', reason: accountingError, usage: null }
    : usageFrom(accounting.events, accounting.malformedLines);
  const threadId = accounting.events.find(event => event.type === 'thread.started')?.thread_id ?? null;
  const record = {
    ...trial, started, agentStarted, ended: new Date().toISOString(),
    agentProcess: agent?.result ?? null, controllerProcess: controller?.result ?? null,
    timedOut, interrupted: signal.aborted,
    originalError: originalError?.message ?? null, cleanupErrors, cleanup,
    lifecycleOutcome: originalError || cleanupErrors.length ? 'failed' : 'completed',
    review: 'pending: write review.json after inspecting the image and rollout',
    threadId, rolloutPath: findRollout(threadId, sessionsRoot),
    malformedEventLines: accounting.malformedLines, ...usage
  };
  // Durable outcome precedes lock release. Failed persistence retains the lock.
  try { write(usagePath, record); }
  catch (error) { cleanupErrors.push(`Outcome persistence: ${error.message}`); }
  if (!cleanupErrors.length) fs.unlinkSync(lock);
  if (originalError || cleanupErrors.length) {
    const failures = [originalError, ...cleanupErrors.map(message => new Error(message))].filter(Boolean);
    throw new AggregateError(failures, failures.map(error => error.message).join('; '));
  }
  return record;
}

export function agentArguments(study, trial, directory, cwd) {
  const args = [study.config.codexEntry, 'exec', '--model', trial.model,
    '--approve-for-me', '--ignore-user-config', '--ignore-rules', '--skip-git-repo-check',
    '-C', cwd, '-c', `model_reasoning_effort="${trial.effort}"`,
    '-c', 'shell_environment_policy.inherit="none"',
    '-c', 'web_search="disabled"'];
  // view_image stays enabled in every arm so agents can review the saved PNG.
  const disabledFeatures = ['apps', 'plugins', 'remote_plugin', 'browser_use', 'browser_use_external',
    'in_app_browser', 'computer_use', 'image_generation', 'multi_agent', 'shell_tool', 'unified_exec', 'sleep_tool',
    'goals', 'hooks', 'memories'];
  for (const feature of disabledFeatures) args.push('-c', `features.${feature}=false`);
  const tomlPath = value => JSON.stringify(value.replaceAll('\\', '/'));
  args.push('-c', 'features.skip_host_skill_discovery=true',
    '-c', `mcp_servers.study.command=${tomlPath(process.execPath)}`,
    '-c', `mcp_servers.study.args=[${tomlPath(path.join(directory, 'study-gateway.mjs'))},${tomlPath(path.join(directory, trial.id, 'gateway.json'))}]`, '--json', '-');
  return args;
}

export async function run(directory, id, approved) {
  if (!approved) throw new Error('Fresh desktop handoff required; pass --handoff only after explicit user permission.');
  const root = path.resolve(directory);
  verifyFrozenInputs(root);
  const study = json(path.join(root, 'study.json'));
  const trial = study.trials?.find(candidate => candidate.id === id);
  if (!trial) throw new Error(`Unknown trial: ${id}`);
  if (path.basename(id) !== id) throw new Error('Invalid trial id');
  const dir = path.join(root, id);
  if (fs.existsSync(path.join(root, 'active')) || fs.existsSync(path.join(dir, 'controller.stdout.log'))) {
    throw new Error('Study active or trial already attempted; do not overwrite or automatically retry.');
  }
  const agentCwd = fs.mkdtempSync(path.join(os.tmpdir(), 'fla_study_agent_'));
  const agentCommand = [process.execPath, ...agentArguments(study, trial, root, agentCwd)];
  const controllerCommand = [study.config.controller, 'hold', path.join(root, 'controller.config.json'), dir];
  write(path.join(dir, 'invocation.json'), { agentCwd, agentCommand, controllerCommand });
  return runTrialSession({ directory: dir, trial, agentCwd, agentCommand, controllerCommand,
    promptTemplate: fs.readFileSync(path.join(dir, 'prompt-template.txt'), 'utf8') });
}

function countCalls(file) {
  const { events } = readEvents(file);
  const has = (event, text) => [event.reason, event.error].some(value => typeof value === 'string' && value.includes(text));
  return {
    dispatchedCalls: events.filter(event => event.type === 'tool-requested')
      .reduce((sum, event) => sum + (Number.isInteger(event.cost) ? event.cost : 1), 0),
    budgetRejections: events.filter(event => has(event, 'call budget')).length,
    toolRejections: events.filter(event => event.type === 'tool-rejected').length
  };
}

function reviewOutcome(file) {
  if (!fs.existsSync(file)) return 'unreviewed';
  const review = json(file);
  return review.taskOutcome ?? review.outcome ?? review.result ?? 'unreviewed';
}

// One row per attempted trial. Older studies lack some fields, so they read as null.
export function summarize(studyDirectory) {
  const root = path.resolve(studyDirectory);
  const rows = [];
  for (const entry of fs.readdirSync(root, { withFileTypes: true })) {
    const dir = path.join(root, entry.name);
    if (!entry.isDirectory() || !fs.existsSync(path.join(dir, 'usage.json'))) continue;
    const record = json(path.join(dir, 'usage.json'));
    const tokens = record.usage ?? {};
    const callsPath = path.join(dir, 'calls.jsonl');
    const calls = fs.existsSync(callsPath)
      ? countCalls(callsPath)
      : { dispatchedCalls: null, budgetRejections: null, toolRejections: null };
    const reviewPath = path.join(dir, 'review.json');
    rows.push({
      id: record.id ?? entry.name, model: record.model ?? null, effort: record.effort ?? null,
      arm: record.arm ?? null, task: record.task ?? null,
      lifecycleOutcome: record.lifecycleOutcome ?? null, cleanup: record.cleanup?.status ?? null,
      timedOut: record.timedOut ?? null, interrupted: record.interrupted ?? null,
      usageStatus: record.status ?? null,
      inputTokens: tokens.input_tokens ?? null, cachedInputTokens: tokens.cached_input_tokens ?? null,
      uncachedInputTokens: tokens.uncached_input_tokens ?? null, outputTokens: tokens.output_tokens ?? null,
      totalTokens: tokens.total_tokens ?? null,
      ...calls,
      reviewOutcome: reviewOutcome(reviewPath), reviewed: fs.existsSync(reviewPath)
    });
  }
  return rows;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [command, first, second, ...options] = process.argv.slice(2);
    if (command === 'prepare') console.log(JSON.stringify(prepare(first, second)));
    else if (command === 'run') await run(first, second, options.includes('--handoff'));
    else if (command === 'summarize') for (const row of summarize(first)) console.log(JSON.stringify(row));
    else throw new Error('prepare CONFIG NEW_DIRECTORY | run STUDY TRIAL --handoff | summarize STUDY');
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
