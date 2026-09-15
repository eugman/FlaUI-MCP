import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import os from 'node:os';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));

export const rungs = ['1', '2', '3', '4', '5'];
export const models = ['sonnet', 'opus'];
export const companionTools = ['te3_navigate'];
// Twelve distinct screenshots, one run each per condition. The first nine are trained: the map and
// te3_navigate may cover them. The last three are hold-outs (see holdoutTasks).
export const tasks = {
  formatting: 'Capture Preferences > Text Editors > DAX Editor > Auto Formatting with that section selected, empty search, all section controls (including Use default formatting settings), title and bottom buttons readable. Do not change settings to satisfy the image. Leave the section open.',
  'code-actions': 'Capture Preferences > Text Editors > DAX Editor > Code Actions with that section selected, empty search, all section controls, title and bottom buttons readable. Do not change settings to satisfy the image. Leave the section open.',
  column: 'Select the Amount column in the Comparison table, not Sales[Amount]. Verify the table, name and column type through UI evidence. Capture the selected row with visible table context, readable Properties and an empty TOM Explorer search box. Leave the column selected.',
  measure: 'Select the Total Amount measure in the Sales table. Capture it selected with its DAX expression visible in the expression editor, readable Properties and an empty TOM Explorer search box. Do not edit the expression. Leave the measure selected.',
  table: 'Select the Sales table in TOM Explorer. Capture it selected with readable Properties showing its name and object type, and an empty TOM Explorer search box. Leave the table selected.',
  'tom-tree': 'In TOM Explorer, expand the Sales and Comparison tables so their columns and measures are visible. Capture TOM Explorer with both tables expanded and an empty search box. Do not edit objects.',
  'script-run': 'Open the supplied .csx through TE3 UI. Verify its source is exactly `"Hello World".Output();` ignoring trailing whitespace, execute only that source, and capture the output dialog title, Hello World text and Close button. Leave the output open. Do not type replacement source, change file associations, or interact with unrelated applications. Stop if an unrelated app chooser appears.',
  'script-edit': 'Create a new C# script document in TE3 and enter exactly these two lines, without running the script: `foreach (var m in Selected.Measures)` then `    m.FormatString = "#,0.00";`. Capture the C# script editor showing exactly those two lines. Do not run or save the script or change the model. Leave the script open.',
  'calc-group-menu': 'Open the Model menu and capture it with the Calculation Group item visible. Do not click any menu item or change the model. Leave the menu open.',
  'bpa-view': 'Open the Best Practice Analyzer view and capture it with its rule list or results readable. Do not apply fixes, change rules, or edit the model. Leave the view open.',
  'dax-query': 'Open a new DAX Query document and capture its empty query editor. Do not run a query or edit the model. Leave the document open.',
  // The fixture has no relationships, so a relationship task is impossible; the model root node replaces it.
  'model-properties': 'Select the model itself, the root node in TOM Explorer. Capture it selected with its Properties readable and an empty TOM Explorer search box. Do not edit the model. Leave the model selected.'
};
// Hold-outs get no map topic, skill line or te3_navigate destination, ever, so they show whether guidance
// written for the trained tasks also works on screenshots nobody wrote it around.
// Round 2 swapped dax-general and save-to-folder (guidance had been written from their transcripts) for bpa-view and dax-query,
// and counts calc-group-menu as trained: the generic skill's open-menu capture exception came from its transcripts.
export const holdoutTasks = ['bpa-view', 'dax-query', 'model-properties'];

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

export function promptFor(task, skillText, scriptPath, outputPath, { maxCalls = 60, agentMinutes = 7 } = {}) {
  let prompt = `${tasks[task]}\n\nThe owned TE3 processId is {{PID}}. Save the final PNG to ${outputPath}. Preserve model content and preferences. Do not launch or close TE3; the controller owns lifecycle and restoration. You may focus this test instance. Offline disposable model; no server access.`;
  if (task === 'script-run') {
    prompt += `\nSupplied source: ${scriptPath}. You are authorized to execute this exact read-only output script.`;
  }
  // The default wording matches the standard-budget rungs exactly.
  const minutes = agentMinutes === 7 ? 'seven' : String(agentMinutes);
  prompt += `\n\nUse only the supplied MCP tools. Stop within ${maxCalls} MCP tool calls or ${minutes} minutes. Report failure if unfinished; claim success only for what the final image shows. Do not save or edit the model.`;
  if (skillText) prompt += `\n\nGuidance:\n${skillText}`;
  return prompt + '\n';
}

// Mulberry32: a small seeded generator, so the run order is reproducible from the config.
function seededRandom(seed) {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6D2B79F5) >>> 0;
    let t = state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// onlyTrials selects exact TASK-NN cells (e.g. reruns); label keeps their ids apart from the originals.
export function schedule({ rung, model, tasks: taskNames, trialsPerTask, seed, onlyTrials, label }) {
  const trials = [];
  for (const task of taskNames) {
    for (let repetition = 1; repetition <= trialsPerTask; repetition++) {
      const cell = `${task}-${String(repetition).padStart(2, '0')}`;
      if (onlyTrials && !onlyTrials.includes(cell)) continue;
      const id = [rung, model, cell, label].filter(Boolean).join('-');
      trials.push({ id, rung, model, task, repetition, holdout: holdoutTasks.includes(task), ...(label ? { label } : {}) });
    }
  }
  const random = seededRandom(seed);
  for (let index = trials.length - 1; index > 0; index--) {
    const other = Math.floor(random() * (index + 1));
    [trials[index], trials[other]] = [trials[other], trials[index]];
  }
  return trials;
}

export function validateConfig(input) {
  const config = { trialsPerTask: 1, tasks: Object.keys(tasks), genericCommand: ['FlaUI.Mcp.exe', 'mcp'], maxCalls: 60, agentMinutes: 7, ...input };
  if (!rungs.includes(config.rung)) throw new Error(`rung must be one of ${rungs.join(', ')}`);
  if (!Number.isInteger(config.maxCalls) || config.maxCalls < 1 || config.maxCalls > 200) throw new Error('maxCalls must be 1..200');
  if (!Number.isInteger(config.agentMinutes) || config.agentMinutes < 1 || config.agentMinutes > 30) throw new Error('agentMinutes must be 1..30');
  if (config.label !== undefined && !/^[a-z0-9]+$/.test(config.label)) throw new Error('label must be lowercase letters and digits');
  if (config.onlyTrials !== undefined && (!Array.isArray(config.onlyTrials) || !config.onlyTrials.length)) {
    throw new Error('onlyTrials must be a nonempty list of TASK-NN cells');
  }
  if (!models.includes(config.model)) throw new Error('model must be sonnet or opus');
  if (!Number.isInteger(config.trialsPerTask) || config.trialsPerTask < 1) throw new Error('trialsPerTask must be a positive integer');
  if (!Array.isArray(config.tasks) || !config.tasks.length || new Set(config.tasks).size !== config.tasks.length ||
      config.tasks.some(task => !tasks[task])) {
    throw new Error('tasks must be a nonempty list of distinct known tasks');
  }
  if (!Number.isSafeInteger(config.seed)) throw new Error('seed must be an integer');
  if (!Array.isArray(config.genericCommand) || !config.genericCommand.length ||
      config.genericCommand.some(part => typeof part !== 'string' || !part)) {
    throw new Error('genericCommand must be a nonempty string array');
  }
  if (config.rung === '5' && !config.companionBuild) throw new Error('Condition 5 requires companionBuild');
  if (config.rung !== '5' && config.companionBuild) throw new Error('Companion tools are only for condition 5');
  const paths = ['genericBuild', 'controller', 'baseline', 'te3', 'te', 'claudeEntry'];
  if (config.companionBuild) paths.push('companionBuild');
  if (config.currentBuild) paths.push('currentBuild');
  for (const key of paths) {
    config[key] = expand(config[key]);
    if (!fs.existsSync(config[key])) throw new Error(`Missing ${key}`);
  }
  if (!fs.existsSync(path.join(config.genericBuild, config.genericCommand[0]))) {
    throw new Error(`Missing ${config.genericCommand[0]} in genericBuild`);
  }
  if (config.companionBuild && !fs.existsSync(path.join(config.companionBuild, 'FlaUI.Automation.exe'))) {
    throw new Error('Missing FlaUI.Automation.exe in companionBuild');
  }
  return config;
}

export function usageFrom(events, malformedLines = 0) {
  const unknown = reason => ({ status: 'unknown', reason, usage: null });
  if (malformedLines) return unknown(`${malformedLines} malformed agent event line(s)`);
  const results = events.filter(event => event?.type === 'result');
  if (results.length !== 1) return unknown(`Expected one result event; observed ${results.length}`);
  const result = results[0];
  if (!result.usage) return unknown('Result event has no usage');
  const counters = {
    input_tokens: result.usage.input_tokens,
    cache_creation_input_tokens: result.usage.cache_creation_input_tokens ?? 0,
    cache_read_input_tokens: result.usage.cache_read_input_tokens ?? 0,
    output_tokens: result.usage.output_tokens
  };
  const values = Object.values(counters);
  const total = values.reduce((sum, value) => sum + value, 0);
  if (!values.every(value => Number.isSafeInteger(value) && value >= 0) || !Number.isSafeInteger(total)) {
    return unknown('Invalid token counters');
  }
  const usage = {
    ...counters, uncached_input_tokens: counters.input_tokens + counters.cache_creation_input_tokens, total_tokens: total
  };
  for (const key of ['total_cost_usd', 'num_turns', 'is_error', 'session_id']) {
    if (result[key] !== undefined) usage[key] = result[key];
  }
  return { status: 'final', reason: null, usage };
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
  const requiredRuntimes = ['node', 'claude', 'te3', 'te'];
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

function inventory(root) {
  const frozen = {};
  const visit = directory => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const file = path.join(directory, entry.name);
      if (entry.isDirectory()) visit(file);
      else frozen[path.relative(root, file)] = hash(file);
    }
  };
  visit(root);
  return frozen;
}

function gatewayConfig(out, config, trialDirectory) {
  const [executable, ...args] = config.genericCommand;
  const gateway = {
    genericCommand: [path.join(out, 'build', executable), ...args],
    logPath: path.join(trialDirectory, 'calls.jsonl'), maxCalls: config.maxCalls
  };
  if (config.rung === '5') gateway.companionCommand = [path.join(out, 'companion', 'FlaUI.Automation.exe'), 'mcp'];
  return gateway;
}

export async function prepare(configPath, destination) {
  const config = validateConfig(json(configPath));
  const out = path.resolve(destination);
  if (fs.existsSync(out)) throw new Error('Use a fresh study directory');
  const controllerDirectory = path.dirname(config.controller);
  if (config.rung === '5') assertSameAssemblies(config.companionBuild, controllerDirectory);
  else if (config.currentBuild) assertSameAssemblies(config.currentBuild, controllerDirectory);
  // Imported lazily: the frozen copy of this file runs trials from inside the study, away from skills/.
  const { composeSkill } = await import('../skills/build-skill.mjs');
  const skill = composeSkill(config.rung);
  const runtimes = {
    node: runtimeRecord(process.execPath),
    claude: runtimeRecord(config.claudeEntry),
    te3: runtimeRecord(config.te3),
    te: runtimeRecord(config.te)
  };

  // Copy immutable inputs before creating per-trial output directories.
  fs.mkdirSync(out, { recursive: true });
  fs.cpSync(config.genericBuild, path.join(out, 'build'), { recursive: true });
  if (config.rung === '5') fs.cpSync(config.companionBuild, path.join(out, 'companion'), { recursive: true });
  fs.cpSync(controllerDirectory, path.join(out, 'controller'), { recursive: true });
  fs.copyFileSync(config.baseline, path.join(out, 'fixture.bim'));
  fs.writeFileSync(path.join(out, 'hello-world.csx'), '"Hello World".Output();\r\n', { flag: 'wx' });
  fs.writeFileSync(path.join(out, 'skill.md'), skill.text, { flag: 'wx' });
  fs.copyFileSync(path.join(here, 'study-gateway.mjs'), path.join(out, 'study-gateway.mjs'));
  fs.copyFileSync(fileURLToPath(import.meta.url), path.join(out, 'ladder-study.mjs'));

  const trials = schedule(config);
  if (config.onlyTrials && trials.length !== config.onlyTrials.length) throw new Error('onlyTrials names a cell outside tasks × trialsPerTask');
  for (const trial of trials) {
    const dir = path.join(out, trial.id);
    fs.mkdirSync(dir);
    const prompt = promptFor(trial.task, skill.text,
      path.win32.normalize(path.join(out, 'hello-world.csx')), path.join(dir, 'result.png'), config);
    fs.writeFileSync(path.join(dir, 'prompt-template.txt'), prompt, { flag: 'wx' });
    write(path.join(dir, 'gateway.json'), gatewayConfig(out, config, dir));
    write(path.join(dir, 'mcp.json'), {
      mcpServers: { study: { command: process.execPath, args: [path.join(out, 'study-gateway.mjs'), path.join(dir, 'gateway.json')] } }
    });
  }
  write(path.join(out, 'controller.config.json'), {
    te3: config.te3, te: config.te, fixtureMode: 'offline', offlineBaseline: path.join(out, 'fixture.bim'),
    output: out, maximizeWindow: true, expectedDpi: 120, repeat: 1, scenarios: ['model-open']
  });

  write(path.join(out, 'study.json'), {
    version: 2, rung: config.rung, model: config.model, holdoutTasks,
    skill: { sha256: skill.sha256, layers: skill.layers },
    trials,
    config: { ...config, controller: path.join(out, 'controller', path.basename(config.controller)) },
    frozen: inventory(out), runtimes, liveExecution: 'one-trial-with-handoff',
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

// The stream is the whole transcript. The init event shows what the model could actually see.
export function transcriptFacts(events, transcriptPath) {
  const init = events.find(event => event.type === 'system' && event.subtype === 'init');
  const result = events.find(event => event.type === 'result');
  return {
    transcriptPath,
    sessionId: result?.session_id ?? init?.session_id ?? null,
    agentModel: init?.model ?? null,
    visibleTools: Array.isArray(init?.tools) ? init.tools : null
  };
}

// The public CLI stays gated below. This explicit session boundary permits
// lifecycle tests with fake commands without pretending that a live preflight ran.
export async function runTrialSession({ directory, trial, controllerCommand, agentCommand,
  agentCwd = directory,
  promptTemplate = '{{PID}}', signal: externalSignal, readyMs = 180000,
  agentMs = 420000, cleanupMs = 90000, stopMs = 10000 }) {
  const dir = path.resolve(directory);
  const lock = path.join(path.dirname(dir), 'active');
  const usagePath = path.join(dir, 'usage.json');
  const transcriptPath = path.join(dir, 'agent.jsonl');
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
    agent = startOwned(agentCommand, agentCwd, transcriptPath, path.join(dir, 'agent.stderr.log'));
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
  try { accounting = readEvents(transcriptPath); }
  catch (error) { accountingError = error.message; }
  const usage = accountingError
    ? { status: 'unknown', reason: accountingError, usage: null }
    : usageFrom(accounting.events, accounting.malformedLines);
  const record = {
    ...trial, started, agentStarted, ended: new Date().toISOString(),
    agentProcess: agent?.result ?? null, controllerProcess: controller?.result ?? null,
    timedOut, interrupted: signal.aborted,
    originalError: originalError?.message ?? null, cleanupErrors, cleanup,
    lifecycleOutcome: originalError || cleanupErrors.length ? 'failed' : 'completed',
    review: 'pending: write review.json after inspecting the image and transcript',
    ...transcriptFacts(accounting.events, fs.existsSync(transcriptPath) ? transcriptPath : null),
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

export function claudeCommand(claudeEntry, model, mcpConfigPath) {
  const launcher = /\.[cm]?js$/i.test(claudeEntry) ? [process.execPath, claudeEntry] : [claudeEntry];
  return [...launcher, '-p',
    '--model', model,
    '--output-format', 'stream-json', '--verbose',
    '--no-session-persistence',
    // The agent cwd is an empty temp directory, so "local" loads nothing; user and project settings are skipped.
    '--setting-sources', 'local',
    '--disable-slash-commands',
    '--strict-mcp-config', '--mcp-config', mcpConfigPath,
    '--tools', '',
    '--allowedTools', 'mcp__study__*',
    '--permission-mode', 'dontAsk'];
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
  const agentCommand = claudeCommand(study.config.claudeEntry, trial.model, path.join(dir, 'mcp.json'));
  const agentMinutes = study.config.agentMinutes ?? 7;
  // The hold outlasts the agent deadline by the controller's startup and restoration margin.
  const controllerCommand = [study.config.controller, 'hold', path.join(root, 'controller.config.json'), dir, String(agentMinutes + 3)];
  write(path.join(dir, 'invocation.json'), { agentCwd, agentCommand, controllerCommand });
  return runTrialSession({ directory: dir, trial, agentCwd, agentCommand, controllerCommand,
    agentMs: agentMinutes * 60000,
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

function readReview(file) {
  if (!fs.existsSync(file)) return { reviewed: false, passed: null, claimedSuccess: null, rubric: null, notes: null };
  const review = json(file);
  if (typeof review.passed !== 'boolean' || typeof review.claimedSuccess !== 'boolean') {
    throw new Error(`review.json needs boolean passed and claimedSuccess: ${file}`);
  }
  return { reviewed: true, passed: review.passed, claimedSuccess: review.claimedSuccess,
    rubric: review.rubric ?? null, notes: review.notes ?? null };
}

// One row per attempted trial.
export function summarize(studyDirectory) {
  const root = path.resolve(studyDirectory);
  const rows = [];
  // A trial keeps the hold-out status it was prepared with, even after the task list changes.
  const studyFile = path.join(root, 'study.json');
  const prepared = new Map(fs.existsSync(studyFile) ? json(studyFile).trials.map(trial => [trial.id, trial.holdout]) : []);
  for (const entry of fs.readdirSync(root, { withFileTypes: true })) {
    const dir = path.join(root, entry.name);
    if (!entry.isDirectory() || !fs.existsSync(path.join(dir, 'usage.json'))) continue;
    const record = json(path.join(dir, 'usage.json'));
    const tokens = record.usage ?? {};
    const callsPath = path.join(dir, 'calls.jsonl');
    const calls = fs.existsSync(callsPath)
      ? countCalls(callsPath)
      : { dispatchedCalls: null, budgetRejections: null, toolRejections: null };
    rows.push({
      kind: 'trial', id: record.id ?? entry.name,
      rung: record.rung ?? null, model: record.model ?? null, task: record.task ?? null, label: record.label ?? null,
      holdout: prepared.get(record.id ?? entry.name) ?? holdoutTasks.includes(record.task),
      lifecycleOutcome: record.lifecycleOutcome ?? null, cleanup: record.cleanup?.status ?? null,
      timedOut: record.timedOut ?? null, interrupted: record.interrupted ?? null,
      usageStatus: record.status ?? null,
      inputTokens: tokens.input_tokens ?? null,
      cacheCreationInputTokens: tokens.cache_creation_input_tokens ?? null,
      cacheReadInputTokens: tokens.cache_read_input_tokens ?? null,
      uncachedInputTokens: tokens.uncached_input_tokens ?? null,
      outputTokens: tokens.output_tokens ?? null, totalTokens: tokens.total_tokens ?? null,
      costUsd: tokens.total_cost_usd ?? null,
      ...calls,
      ...readReview(path.join(dir, 'review.json'))
    });
  }
  return rows;
}

function mean(values) {
  const known = values.filter(value => typeof value === 'number');
  return known.length ? known.reduce((sum, value) => sum + value, 0) / known.length : null;
}

// pass^k: a group passes only when every one of its trials passed review.
export function groupRows(rows) {
  const groups = new Map();
  for (const row of rows) {
    // Labelled reruns (e.g. extended budget) are never pooled with standard trials.
    const key = `${row.rung}|${row.task}|${row.model}|${row.label ?? ''}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(row);
  }
  return [...groups.values()].map(members => {
    const passes = members.filter(row => row.passed === true).length;
    return {
      kind: 'group', rung: members[0].rung, task: members[0].task, model: members[0].model, label: members[0].label,
      holdout: members[0].holdout, n: members.length, passes, passAll: passes === members.length,
      falseClaims: members.filter(row => row.claimedSuccess === true && row.passed === false).length,
      meanDispatchedCalls: mean(members.map(row => row.dispatchedCalls)),
      meanTotalTokens: mean(members.map(row => row.totalTokens)),
      meanUncachedInputTokens: mean(members.map(row => row.uncachedInputTokens)),
      unreviewed: members.filter(row => !row.reviewed).length
    };
  });
}

function median(values) {
  const known = values.filter(value => typeof value === 'number').sort((a, b) => a - b);
  if (!known.length) return null;
  const middle = Math.floor(known.length / 2);
  return known.length % 2 ? known[middle] : (known[middle - 1] + known[middle]) / 2;
}

// Tasks that no agent could complete (the fixture has no relationships); still reported per trial, never scored.
export const excludedTasks = ['relationship'];

// One row per condition, with trained and hold-out tasks tallied separately.
export function conditionRows(rows) {
  const groups = new Map();
  for (const row of rows.filter(row => !excludedTasks.includes(row.task))) {
    const key = `${row.rung}|${row.model}|${row.label ?? ''}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(row);
  }
  const tally = members => ({ passes: members.filter(row => row.passed === true).length, n: members.length });
  return [...groups.values()].map(members => ({
    kind: 'condition', rung: members[0].rung, model: members[0].model, label: members[0].label,
    ...tally(members),
    trained: tally(members.filter(row => !row.holdout)), holdout: tally(members.filter(row => row.holdout)),
    falseClaims: members.filter(row => row.claimedSuccess === true && row.passed === false).length,
    medianDispatchedCalls: median(members.map(row => row.dispatchedCalls)),
    meanTotalTokens: mean(members.map(row => row.totalTokens)),
    unreviewed: members.filter(row => !row.reviewed).length
  }));
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [command, first, second, ...options] = process.argv.slice(2);
    if (command === 'prepare') console.log(JSON.stringify(await prepare(first, second)));
    else if (command === 'run') await run(first, second, options.includes('--handoff'));
    else if (command === 'summarize') {
      const rows = summarize(first);
      for (const row of [...rows, ...groupRows(rows), ...conditionRows(rows)]) console.log(JSON.stringify(row));
    } else throw new Error('prepare CONFIG NEW_DIRECTORY | run STUDY TRIAL --handoff | summarize STUDY');
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
