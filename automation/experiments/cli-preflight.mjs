// Metadata-only probe. MCP tools/call is denied, even for read-only tools.
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';
import { agentArguments, models, runOwnedPrompt, usageFrom } from './four-arm-study.mjs';

const here = path.dirname(fileURLToPath(import.meta.url));
const [build, codexEntry, label, arm, destination] = process.argv.slice(2);
const selected = models.find(model => model[0] === label);
if (process.argv.length !== 7 || !selected || !['A', 'B', 'C', 'D'].includes(arm)) {
  throw new Error('cli-preflight.mjs BUILD CODEX_ENTRY luna|terra|sol A|B|C|D NEW_OUTPUT');
}
const directory = path.resolve(destination);
fs.mkdirSync(directory);
const trial = { id: 'probe', model: selected[1], effort: selected[2], arm };
fs.mkdirSync(path.join(directory, trial.id));
fs.copyFileSync(path.join(here, 'study-gateway.mjs'), path.join(directory, 'study-gateway.mjs'));
fs.writeFileSync(path.join(directory, trial.id, 'gateway.json'), JSON.stringify({
  arm, buildDirectory: path.resolve(build), logPath: path.join(directory, 'calls.jsonl'), observeOnly: true
}), { flag: 'wx' });
const cwd = fs.mkdtempSync(path.join(os.tmpdir(), 'fla_cli_preflight_'));
const args = agentArguments({ config: { codexEntry: path.resolve(codexEntry) } }, trial, directory, cwd);
const prompt = 'This is a metadata-only tool-availability probe, not a UI task. Do not interact with any application. Use functions orchestration once to emit JSON containing ALL_TOOLS.map(t => t.name). Do not invoke anything through the tools object. If ALL_TOOLS is unavailable, report that limitation instead of guessing. No shell, web, files, images, skills, delegation, or MCP tools/call. Then stop. Do not claim that this proves isolation; we will review the recorded metadata.';
fs.writeFileSync(path.join(directory, 'prompt.txt'), prompt, { flag: 'wx' });
fs.writeFileSync(path.join(directory, 'arguments.json'), JSON.stringify(args, null, 2), { flag: 'wx' });
const started = new Date().toISOString();
const result = await runOwnedPrompt([process.execPath, ...args], cwd, prompt, directory);
const summary = {
  ...trial, started, ended: new Date().toISOString(), process: result.process,
  executionError: result.executionError, cleanupError: result.cleanupError,
  cwd, threadId: result.events.find(event => event.type === 'thread.started')?.thread_id ?? null,
  ...usageFrom(result.events, result.malformedLines), review: 'required', scope: 'Preflight overhead; not a pilot trial'
};
fs.writeFileSync(path.join(directory, 'summary.json'), JSON.stringify(summary, null, 2), { flag: 'wx' });
console.log(JSON.stringify(summary, null, 2));
if (result.executionError || result.cleanupError) process.exitCode = 1;
