#!/usr/bin/env node
// A/B expose generic tools; C adds guidance; D adds the full companion.
// The study prompt, not this gateway, supplies the skill for B/C/D.
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { appendFile, readFile } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import readline from 'node:readline';

function send(message) {
  process.stdout.write(`${JSON.stringify(message)}\n`);
}

function errorReply(id, code, message) {
  return { jsonrpc: '2.0', id, error: { code, message } };
}

class Backend {
  constructor(name, command) {
    this.name = name;
    this.nextId = 0;
    this.pending = new Map();
    this.failure = null;
    this.child = spawn(command[0], command.slice(1), {
      stdio: ['pipe', 'pipe', 'inherit'], windowsHide: true,
      // Restrict the study irrespective of the parent's environment.
      env: { ...process.env, FLAUI_MCP_ALLOWED_APPS: 'TabularEditor3' }
    });
    this.lines = readline.createInterface({ input: this.child.stdout });
    this.lines.on('line', line => this.receive(line));
    this.child.on('error', error => this.fail(error));
    this.child.stdin.on('error', error => this.fail(error));
    this.closed = new Promise(resolveClosed => {
      this.child.once('close', (code, signal) => {
        this.fail(new Error(`exited (code ${code}, signal ${signal})`));
        this.lines.close();
        resolveClosed();
      });
    });
  }

  fail(error) {
    this.failure ??= new Error(`${this.name} backend unavailable: ${error.message}`);
    for (const { reject } of this.pending.values()) reject(this.failure);
    this.pending.clear();
  }

  receive(line) {
    let message;
    try {
      message = JSON.parse(line);
    } catch {
      this.fail(new Error('invalid JSON response'));
      return;
    }
    const pending = this.pending.get(message.id);
    if (!pending) return; // Notifications are not request responses.
    this.pending.delete(message.id);
    if (message.error) pending.reject(new Error(message.error.message));
    else pending.resolve(message.result);
  }

  request(method, params) {
    if (this.failure) return Promise.reject(this.failure);
    const id = ++this.nextId;
    return new Promise((resolveRequest, reject) => {
      this.pending.set(id, { resolve: resolveRequest, reject });
      this.write({ jsonrpc: '2.0', id, method, params });
    });
  }

  write(message) {
    try {
      this.child.stdin.write(`${JSON.stringify(message)}\n`, error => {
        if (error) this.fail(error);
      });
    } catch (error) {
      this.fail(error);
    }
  }

  notify(method, params) {
    if (!this.failure) this.write({ jsonrpc: '2.0', method, params });
  }

  async stop() {
    this.fail(new Error('stopped'));
    if (this.child.exitCode === null && this.child.signalCode === null) this.child.kill();
    let timer;
    try {
      await Promise.race([
        this.closed,
        new Promise((_, reject) => {
          timer = setTimeout(() => reject(new Error(`${this.name} backend shutdown was not confirmed within five seconds`)), 5000);
        })
      ]);
    } finally {
      clearTimeout(timer);
      // Descendants can keep inherited pipes open after the backend exits.
      this.lines.close();
      this.child.stdout.destroy();
      this.child.stdin.destroy();
    }
  }
}

// The controller owns TE3 lifecycle; agents may not start or close windows.
const forbiddenTools = new Set(['windows_launch', 'windows_close']);

export class StudyGateway {
  constructor(config, options = {}) {
    this.config = validate(config);
    // Persisted so a restarted gateway cannot grant a fresh allowance.
    this.budgetPath = `${this.config.logPath}.budget.json`;
    this.calls = existsSync(this.budgetPath) ? JSON.parse(readFileSync(this.budgetPath, 'utf8')).calls : 0;
    if (!Number.isInteger(this.calls) || this.calls < 0) throw new Error(`invalid budget state: ${this.budgetPath}`);
    this.owners = new Map();
    this.backends = {};
    const commands = options.backendCommands || defaultCommands(this.config);
    this.backends.generic = new Backend('generic', commands.generic);
    if (this.config.arm === 'C' || this.config.arm === 'D') {
      this.backends.companion = new Backend('companion', commands.companion);
    }
  }

  async log(record) {
    await appendFile(this.config.logPath, `${JSON.stringify(record)}\n`);
  }

  async request(method, params, preferred = 'generic') {
    const backend = this.backends[preferred];
    if (!backend) throw new Error(`${preferred} backend unavailable`);
    return backend.request(method, params);
  }

  async initialize(params) {
    const replies = await Promise.all(Object.entries(this.backends).map(async ([name, backend]) => {
      return [name, await backend.request('initialize', params)];
    }));
    const initialized = Object.fromEntries(replies);
    // Only advertise routed methods. Subscriptions and change notifications
    // are not forwarded, even if an individual backend supports them.
    const capabilities = { tools: {} };
    if (initialized.companion?.capabilities?.resources) capabilities.resources = {};
    return { ...initialized.generic, capabilities };
  }

  async listTools(params) {
    const replies = await Promise.all(Object.entries(this.backends).map(async ([name, backend]) => {
      return [name, await backend.request('tools/list', params)];
    }));
    const owners = new Map();
    const tools = [];
    for (const [owner, reply] of replies) {
      for (const tool of reply.tools || []) {
        if (owners.has(tool.name)) throw new Error(`duplicate tool ownership: ${tool.name}`);
        owners.set(tool.name, owner);
        tools.push(tool);
      }
    }
    // Replace the inventory only after the whole response validates.
    this.owners = owners;
    return { tools };
  }

  // Reserve synchronously before any await, so concurrent requests cannot overspend.
  // Failed attempts keep their slots; no UI action is retried here.
  reserve(cost) {
    if (this.calls + cost > this.config.maxCalls) {
      throw new Error(`call budget exhausted (${this.calls}/${this.config.maxCalls} used; request needs ${cost})`);
    }
    this.calls += cost;
    writeFileSync(this.budgetPath, `${JSON.stringify({ calls: this.calls })}\n`);
    return this.calls;
  }

  // Rejections are logged so summaries can count them. The reservation itself stays synchronous.
  async reserveOrLog(cost, record) {
    try {
      return this.reserve(cost);
    } catch (error) {
      await this.log({ type: 'budget-rejected', ...record, cost, error: error.message, time: new Date().toISOString() });
      throw error;
    }
  }

  // Results are summarized, not copied, so logs never hold screenshots or UI text.
  async forward(method, params, owner, record) {
    try {
      const result = await this.request(method, params, owner);
      await this.log({
        type: 'result', ...record, isError: result?.isError === true,
        dispatch: result?.structuredContent?.dispatch ?? null, provider: result?.structuredContent?.provider ?? null,
        content: (result?.content ?? result?.contents ?? []).map(item => ({
          type: item?.type ?? null, chars: typeof item?.text === 'string' ? item.text.length : 0
        })),
        time: new Date().toISOString()
      });
      return result;
    } catch (error) {
      await this.log({ type: 'result-error', ...record, error: error.message, time: new Date().toISOString() });
      throw error;
    }
  }

  async readResource(params) {
    if (this.config.observeOnly) return this.request('resources/read', params, 'companion');
    const uri = typeof params?.uri === 'string' ? params.uri : null;
    const call = await this.reserveOrLog(1, { uri });
    await this.log({ type: 'resource-requested', call, cost: 1, uri, time: new Date().toISOString() });
    return this.forward('resources/read', params, 'companion', { call, uri });
  }

  async callTool(params) {
    const name = params?.name;
    const owner = this.owners.get(name);
    if (!owner) throw new Error(`unknown tool: ${name}`);
    if (this.config.observeOnly) throw new Error('observeOnly preflight forbids tools/call');
    if (forbiddenTools.has(name)) {
      await this.log({ type: 'tool-rejected', tool: name, reason: 'controller-owned lifecycle', time: new Date().toISOString() });
      throw new Error(`${name} is not permitted in this study; the controller owns TE3 lifecycle.`);
    }
    const actions = params?.arguments?.actions;
    // Each batch step is a dispatched action, so it costs the same as a separate call.
    const cost = Array.isArray(actions) ? Math.max(1, actions.length) : 1;
    const call = await this.reserveOrLog(cost, { tool: name });
    await this.log({ type: 'tool-requested', call, cost, tool: name, time: new Date().toISOString() });
    if (Array.isArray(actions)) {
      for (const action of actions) {
        await this.log({
          type: 'batch-action-requested', call, tool: name,
          action: typeof action?.action === 'string' ? action.action : 'unknown',
          time: new Date().toISOString()
        });
      }
    }
    // These records describe requests, not proof of executed batch steps.
    return this.forward('tools/call', params, owner, { call, tool: name });
  }

  async dispatch(method, params) {
    switch (method) {
      case 'initialize': return this.initialize(params);
      case 'tools/list': return this.listTools(params);
      case 'tools/call': return this.callTool(params);
      case 'resources/list':
        if (this.backends.companion) return this.request(method, params, 'companion');
        break;
      case 'resources/read':
        if (this.backends.companion) return this.readResource(params);
        break;
    }
    const error = new Error(`unsupported method: ${method}`);
    error.code = -32601;
    throw error;
  }

  async handle(message) {
    if (!message || typeof message !== 'object' || typeof message.method !== 'string') {
      send(errorReply(message?.id ?? null, -32600, 'invalid request'));
      return;
    }
    if (message.method === 'notifications/initialized') {
      for (const backend of Object.values(this.backends)) backend.notify(message.method, message.params);
      return;
    }
    if (message.id === undefined) return;
    try {
      const result = await this.dispatch(message.method, message.params);
      send({ jsonrpc: '2.0', id: message.id, result });
    } catch (error) {
      send(errorReply(message.id, error.code ?? -32603, error.message));
    }
  }

  async stop() {
    await Promise.all(Object.values(this.backends).map(backend => backend.stop()));
  }
}

function validate(config) {
  if (!config || !['A', 'B', 'C', 'D'].includes(config.arm) ||
      typeof config.buildDirectory !== 'string' || typeof config.logPath !== 'string') {
    throw new Error('config requires arm A-D, buildDirectory, and logPath');
  }
  const maxCalls = config.maxCalls ?? 60;
  if (!Number.isInteger(maxCalls) || maxCalls < 1 || maxCalls > 60) {
    throw new Error('maxCalls must be 1..60');
  }
  if (config.observeOnly !== undefined && typeof config.observeOnly !== 'boolean') {
    throw new Error('observeOnly must be boolean');
  }
  return {
    arm: config.arm, buildDirectory: config.buildDirectory, logPath: config.logPath,
    maxCalls, observeOnly: config.observeOnly === true
  };
}

function defaultCommands(config) {
  return {
    generic: [join(config.buildDirectory, 'FlaUI.Mcp.exe'), 'mcp'],
    companion: [join(config.buildDirectory, 'FlaUI.Automation.exe'), 'mcp',
      ...(config.arm === 'C' ? ['--guidance-only'] : [])]
  };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const config = JSON.parse(await readFile(process.argv[2], 'utf8'));
  const gateway = new StudyGateway(config);
  const input = readline.createInterface({ input: process.stdin });
  input.on('line', line => {
    let message;
    try {
      message = JSON.parse(line);
    } catch {
      send(errorReply(null, -32700, 'parse error'));
      return;
    }
    void gateway.handle(message);
  });
  input.once('close', () => { void gateway.stop(); });
  for (const [signal, exitCode] of [['SIGINT', 130], ['SIGTERM', 143]]) {
    process.once(signal, () => {
      input.close();
      process.exitCode = exitCode;
      void gateway.stop();
    });
  }
}
