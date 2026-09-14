import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn } from 'node:child_process';
import { ALLOWED_TOOL, decideHook, buildProbeArguments, buildProbePrompt, summarizeProbe, main } from './hook-probe.mjs';

test('hook policy allows only the exact inert allowlisted tool', () => {
  assert.equal(decideHook({ tool_name: ALLOWED_TOOL }), 'allow');
  for (const tool of ['mcp__probe__denied', 'apply_patch', 'spawn_agent', 'functions.exec']) {
    assert.equal(decideHook({ tool_name: tool }), 'deny');
  }
  for (const event of [null, {}, { tool_name: 3 }]) assert.throws(() => decideHook(event), /tool_name/);
});

test('arguments retain inline hook configuration and ordinary sandbox/approval controls', () => {
  const args = buildProbeArguments('C:\\probe output', 'C:\\empty cwd', 'C:\\codex.js');
  assert.equal(args[0], 'C:\\codex.js');
  assert.equal(args[args.indexOf('-C') + 1], 'C:\\empty cwd');
  assert.ok(args.includes('--approve-for-me'));
  assert.ok(args.includes('--dangerously-bypass-hook-trust'));
  assert.ok(args.includes('features.hooks=true'));
  assert.ok(!args.includes('--dangerously-bypass-approvals-and-sandbox'));
  const hook = args.find(argument => argument.startsWith('hooks.PreToolUse='));
  assert.match(hook, /matcher="\.\*"/);
  assert.match(hook, /timeout=5/);
  assert.ok(hook.includes('probe output'));
  assert.ok(args.find(argument => argument.startsWith('mcp_servers.probe.args=')).includes('C:/probe output'));
  assert.ok(!args.includes('features.multi_agent=false'), 'limited child attempt remains part of this probe');
});

test('prompt presents four readable lines and explicitly bounds the child attempt', () => {
  const prompt = buildProbePrompt('C:\\empty');
  assert.equal(prompt.split('\n').length, 4);
  assert.match(prompt, /attempt each exactly once/);
  assert.match(prompt, /PROBE_CHILD without tools or delegation/);
  assert.match(prompt, /Do not claim a blocked call from descriptions alone/);
});

async function invokeHook(t, input) {
  const directory = await mkdtemp(join(tmpdir(), 'hook-unit-'));
  const child = spawn(process.execPath, [fileURLToPath(new URL('./hook-probe.mjs', import.meta.url)), 'hook', directory], {
    windowsHide: true, stdio: ['pipe', 'pipe', 'pipe']
  });
  let stdout = '';
  let stderr = '';
  child.stdout.on('data', chunk => { stdout += chunk; });
  child.stderr.on('data', chunk => { stderr += chunk; });
  const closed = new Promise((resolve, reject) => {
    child.once('error', reject);
    child.once('close', code => resolve(code));
  });
  t.after(async () => {
    if (child.exitCode === null && child.signalCode === null) child.kill();
    await closed;
    await rm(directory, { recursive: true, force: true });
  });
  child.stdin.end(input);
  const code = await closed;
  const log = await readFile(join(directory, 'hooks.jsonl'), 'utf8');
  return { code, stderr, reply: JSON.parse(stdout), log, records: log.trim().split('\n').map(JSON.parse) };
}

test('hook subprocess records startup before malformed input and emits a denial', { timeout: 10000 }, async t => {
  const result = await invokeHook(t, '{PRIVATE broken');
  assert.equal(result.code, 0);
  assert.equal(result.stderr, '');
  assert.deepEqual(result.records.map(record => record.type), ['started', 'error']);
  assert.equal(result.reply.hookSpecificOutput.permissionDecision, 'deny');
  assert.doesNotMatch(result.log, /PRIVATE/);
});

test('hook subprocess records invocation and decision without tool arguments', { timeout: 10000 }, async t => {
  const result = await invokeHook(t, JSON.stringify({ tool_name: ALLOWED_TOOL,
    tool_use_id: 'call-1', session_id: 'session-1', tool_input: { secret: 'PRIVATE' } }));
  assert.deepEqual(result.records.map(record => record.type), ['started', 'decision']);
  assert.equal(result.records[1].call, 'call-1');
  assert.equal(result.records[1].decision, 'allow');
  assert.doesNotMatch(result.log, /PRIVATE/);
});

test('summary separates absent observations from blocked calls and child cost', async t => {
  const directory = await mkdtemp(join(tmpdir(), 'hook-summary-unit-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  await writeFile(join(directory, 'agent.jsonl'), JSON.stringify({ type: 'turn.completed', usage: {
    input_tokens: 10, cached_input_tokens: 2, output_tokens: 1
  } }) + '\n');
  const result = summarizeProbe({ directory, cwd: directory, processResult: { code: 0 }, executionError: null, cleanupError: null });
  assert.equal(result.hookActivation.status, 'not-observed');
  assert.equal(result.hookActivation.invocationCount, 0);
  assert.deepEqual(result.serverDispatch.observations, []);
  assert.equal(result.sentinel.exists, false);
  assert.equal(result.child.status, 'requires-rollout-review');
  assert.equal(result.child.usage, null);
  assert.equal(result.parentUsage.usage.total_tokens, 11);
  assert.match(result.verdict, /not proof/);
});

test('mode validation is explicit without running any model', async () => {
  await assert.rejects(main(['run', 'unused']), /NEW_OUTPUT/);
  await assert.rejects(main(['hook', 'unused', 'extra']), /NEW_OUTPUT/);
});
