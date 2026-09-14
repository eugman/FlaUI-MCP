import assert from 'node:assert/strict';
import test from 'node:test';
import {readFileSync} from 'node:fs';
import {mkdtemp, mkdir, rm, writeFile} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {readEvents, runOwnedPrompt} from './four-arm-study.mjs';

const fakeProbe = String.raw`
import fs from 'node:fs';
const [mode, pidFile] = process.argv.slice(2);
process.stdin.resume();
if (mode === 'success') {
  console.log(JSON.stringify({type: 'turn.completed', usage: {input_tokens: 3, output_tokens: 2}}));
} else if (mode === 'nonzero') {
  console.log(JSON.stringify({type: 'turn.failed'}));
  process.exitCode = 7;
} else if (mode === 'malformed') {
  console.log('{not json');
} else if (mode === 'wait') {
  fs.writeFileSync(pidFile, String(process.pid));
  setInterval(() => {}, 1000);
}
`;

async function fixture(t) {
  const root = await mkdtemp(join(tmpdir(), 'four-arm-probe-'));
  const cwd = join(root, 'cwd');
  const output = join(root, 'owned-output');
  const child = join(root, 'fake-probe.mjs');
  await Promise.all([mkdir(cwd), mkdir(output), writeFile(child, fakeProbe)]);
  t.after(() => rm(root, {recursive: true, force: true}));
  return {cwd, output, child, pidFile: join(root, 'child.pid')};
}

function command(child, mode, pidFile) {
  return [process.execPath, child, mode, pidFile];
}

test('owned prompt reads a successful probe event stream', async t => {
  const files = await fixture(t);
  const result = await runOwnedPrompt(command(files.child, 'success', files.pidFile), files.cwd, 'prompt', files.output, 1000);

  assert.equal(result.executionError, null);
  assert.equal(result.cleanupError, null);
  assert.equal(result.process.code, 0);
  assert.deepEqual(result.events.map(event => event.type), ['turn.completed']);
});

test('owned prompt reports a nonzero probe exit', async t => {
  const files = await fixture(t);
  const result = await runOwnedPrompt(command(files.child, 'nonzero', files.pidFile), files.cwd, 'prompt', files.output, 1000);

  assert.equal(result.process.code, 7);
  assert.match(result.executionError, /Probe process failed/);
  assert.equal(result.cleanupError, null);
});

test('readEvents counts malformed JSON without admitting it as evidence', async t => {
  const files = await fixture(t);
  const log = join(files.output, 'agent.jsonl');
  await writeFile(log, '{bad json\n{"type":"turn.completed"}\n{"noType":true}\n');

  assert.deepEqual(readEvents(log), {
    events: [{type: 'turn.completed'}],
    malformedLines: 2
  });
});

test('owned prompt deadline terminates the child before returning', async t => {
  const files = await fixture(t);
  const result = await runOwnedPrompt(command(files.child, 'wait', files.pidFile), files.cwd, 'prompt', files.output, 50);
  const pid = Number(readFileSync(files.pidFile, 'utf8'));

  assert.match(result.executionError, /timed out/);
  assert.equal(result.cleanupError, null);
  assert.throws(() => process.kill(pid, 0), {code: 'ESRCH'});
});
