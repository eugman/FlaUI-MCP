// Attribution and cumulative token accounting for a standalone Codex rollout.
// Output is deliberately limited to metadata; prompts and tool payloads stay out.
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

function validateExpected(expected) {
  for (const key of ['threadId', 'model', 'effort']) {
    if (typeof expected?.[key] !== 'string' || !expected[key].trim()) {
      throw new Error(`Expected ${key} is required`);
    }
  }
}

function readCounters(value) {
  const required = ['input_tokens', 'cached_input_tokens', 'output_tokens', 'total_tokens'];
  if (!value || !required.every(key => Number.isSafeInteger(value[key]) && value[key] >= 0)) {
    return null;
  }
  if (value.cached_input_tokens > value.input_tokens ||
      !Number.isSafeInteger(value.input_tokens + value.output_tokens) ||
      value.total_tokens !== value.input_tokens + value.output_tokens) return null;
  const counters = Object.fromEntries(required.map(key => [key, value[key]]));
  if (value.reasoning_output_tokens !== undefined) {
    if (!Number.isSafeInteger(value.reasoning_output_tokens) || value.reasoning_output_tokens < 0 ||
        value.reasoning_output_tokens > value.output_tokens) return null;
    counters.reasoning_output_tokens = value.reasoning_output_tokens;
  }
  counters.uncached_input_tokens = counters.input_tokens - counters.cached_input_tokens;
  return counters;
}

export function auditRollout(text, expected) {
  validateExpected(expected);
  const sessions = [];
  const contexts = [];
  const accountingErrors = [];
  let malformedLines = 0;
  let lastCounter = null;
  let counterEvents = 0;
  let startedEvents = 0;
  let lastStarted = -1;
  let lastComplete = -1;
  let lastInterrupted = -1;
  let lineNumber = 0;

  for (const line of text.split(/\r?\n/)) {
    lineNumber++;
    if (!line.trim()) continue;
    let record;
    try {
      record = JSON.parse(line);
      if (!record || typeof record.type !== 'string') throw new Error('missing event type');
    } catch {
      malformedLines++;
      continue;
    }
    if (record.type === 'session_meta') {
      sessions.push({ threadId: record.payload?.id, source: record.payload?.source });
    }
    if (record.type === 'turn_context') {
      contexts.push({ model: record.payload?.model, effort: record.payload?.effort });
    }
    if (record.type !== 'event_msg') continue;
    const event = record.payload;
    if (event?.type === 'task_started') {
      startedEvents++;
      lastStarted = lineNumber;
    }
    if (event?.type === 'task_complete') lastComplete = lineNumber;
    if (event?.type === 'turn_aborted' || event?.type === 'task_interrupted') lastInterrupted = lineNumber;
    if (event?.type !== 'token_count') continue;
    counterEvents++;
    const counters = readCounters(event.info?.total_token_usage);
    if (!counters) {
      accountingErrors.push(`Missing or invalid cumulative counters at line ${lineNumber}`);
      continue;
    }
    if (lastCounter && ['input_tokens', 'cached_input_tokens', 'output_tokens', 'total_tokens']
      .some(key => counters[key] < lastCounter.counters[key])) {
      accountingErrors.push(`Cumulative counters decreased at line ${lineNumber}`);
    }
    lastCounter = { counters, line: lineNumber, timestamp: record.timestamp ?? null };
  }

  const attributionReasons = [];
  let attributionStatus = 'verified';
  if (malformedLines) attributionReasons.push(`${malformedLines} malformed rollout line(s)`);
  if (sessions.length !== 1 || typeof sessions[0]?.threadId !== 'string') {
    attributionReasons.push('Expected exactly one session_meta with a thread id');
  } else if (sessions[0].threadId !== expected.threadId) {
    attributionStatus = 'mismatch';
    attributionReasons.push('session_meta thread id differs from expected thread');
  }
  // Standalone CLI uses a string source; reject an attributed subagent source.
  if (sessions[0]?.source && typeof sessions[0].source === 'object') {
    attributionStatus = 'mismatch';
    attributionReasons.push('Rollout source is not a standalone session');
  }
  if (!contexts.length) attributionReasons.push('No turn_context observations');
  if (startedEvents > 1) {
    attributionReasons.push('Multiple task_started events: cumulative thread usage is ambiguous for one trial');
  }
  for (const context of contexts) {
    if (typeof context.model !== 'string' || typeof context.effort !== 'string') {
      attributionReasons.push('Incomplete turn_context model/effort');
    } else if (context.model !== expected.model || context.effort !== expected.effort) {
      attributionStatus = 'mismatch';
      attributionReasons.push('Observed model/effort differs from expected');
    }
  }
  if (attributionReasons.length && attributionStatus === 'verified') attributionStatus = 'unknown';

  const reasons = [...new Set(accountingErrors)];
  if (attributionStatus !== 'verified') reasons.unshift('Rollout attribution is not verified');
  if (!lastCounter) reasons.push('No valid cumulative token counter');
  let status = 'unknown';
  let counters = null;
  if (!reasons.length) {
    counters = lastCounter.counters;
    // A completion marker after the last start and counter is evidence of a
    // finished turn, not independent proof that the UI task succeeded.
    const completed = lastStarted >= 0 && lastCounter.line > lastStarted && lastComplete > lastStarted &&
      lastComplete > lastCounter.line && lastComplete > lastInterrupted;
    status = completed ? 'final' : 'partial';
    if (!completed) reasons.push('No completed, uninterrupted turn with a counter after its start');
  }
  return {
    expected: { threadId: expected.threadId, model: expected.model, effort: expected.effort },
    attribution: {
      status: attributionStatus, reasons: [...new Set(attributionReasons)],
      observedThreadId: sessions.length === 1 ? sessions[0].threadId ?? null : null,
      observedContexts: [...new Map(contexts.map(context => [JSON.stringify(context), context])).values()],
      contextCount: contexts.length, startedEvents
    },
    usage: {
      status, reasons, counters, counterEvents,
      counterTimestamp: lastCounter?.timestamp ?? null,
      source: 'Last cumulative rollout token_count.info.total_token_usage; never a sum of cumulative counters.',
      scope: 'Cumulative standalone thread counters, not a per-turn delta. Multiple starts are rejected as ambiguous for one trial. Controller/evaluator overhead excluded; not an isolated image-token measurement.'
    },
    malformedLines
  };
}

export async function auditRolloutFile(file, expected) {
  return auditRollout(await readFile(file, 'utf8'), expected);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [file, threadId, model, effort, ...extra] = process.argv.slice(2);
    if (!file || extra.length) throw new Error('rollout-audit.mjs EXACT_ROLLOUT EXPECTED_THREAD MODEL EFFORT');
    const result = await auditRolloutFile(file, { threadId, model, effort });
    console.log(JSON.stringify(result, null, 2));
    if (result.attribution.status !== 'verified' || result.usage.status === 'unknown') process.exitCode = 1;
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
