import assert from 'node:assert/strict';
import test from 'node:test';
import { auditRollout } from './rollout-audit.mjs';

const expected = { threadId: 'standalone-1', model: 'test-model', effort: 'low' };
const meta = { type: 'session_meta', payload: { id: expected.threadId, source: 'exec', prompt: 'PRIVATE' } };
const context = { type: 'turn_context', payload: { model: expected.model, effort: expected.effort } };
const event = type => ({ type: 'event_msg', payload: { type } });
const usage = (input = 100, cached = 80, output = 10) => ({
  type: 'event_msg', timestamp: '2026-09-13T15:48:36Z', payload: {
    type: 'token_count', info: { total_token_usage: {
      input_tokens: input, cached_input_tokens: cached, output_tokens: output,
      reasoning_output_tokens: 4, total_tokens: input + output, secret: 'PRIVATE'
    } }
  }
});
const serialize = records => records.map(record => JSON.stringify(record)).join('\n');
const complete = () => [meta, event('task_started'), context, usage(), event('task_complete')];

test('standalone metadata and every context establish attribution without subagent fields', () => {
  const records = complete();
  records.splice(3, 0, context);
  const result = auditRollout(serialize(records), expected);
  assert.equal(result.attribution.status, 'verified');
  assert.equal(result.attribution.contextCount, 2);
  assert.equal(result.usage.status, 'final');
  assert.equal(result.usage.counters.uncached_input_tokens, 20);
  assert.equal(result.usage.counters.total_tokens, 110);
  assert.doesNotMatch(JSON.stringify(result), /PRIVATE|prompt|secret/);
});

test('last cumulative counter is used, including repeated identical counters', () => {
  const records = complete();
  records.splice(4, 0, usage(200, 160, 20), usage(200, 160, 20));
  const result = auditRollout(serialize(records), expected);
  assert.equal(result.usage.counters.total_tokens, 220);
  assert.equal(result.usage.counterEvents, 3);
});

test('wrong thread, changed effort, or changed model cannot be attributed to the expected trial', () => {
  for (const changed of [
    { threadId: 'different', model: expected.model, effort: expected.effort },
    { ...expected, effort: 'medium' }, { ...expected, model: 'different' }
  ]) {
    const result = auditRollout(serialize(complete()), changed);
    assert.equal(result.attribution.status, 'mismatch');
    assert.equal(result.usage.status, 'unknown');
    assert.equal(result.usage.counters, null);
  }
  const records = complete();
  records.splice(3, 0, { type: 'turn_context', payload: { model: 'different', effort: 'low' } });
  assert.equal(auditRollout(serialize(records), expected).attribution.status, 'mismatch');
});

test('subagent rollout source is rejected even with a matching thread', () => {
  const records = complete();
  records[0] = { type: 'session_meta', payload: { id: expected.threadId, source: { subagent: {} } } };
  assert.equal(auditRollout(serialize(records), expected).attribution.status, 'mismatch');
});

test('missing or duplicate metadata and missing contexts remain unknown', () => {
  for (const records of [complete().slice(1), [...complete(), meta], complete().filter(record => record.type !== 'turn_context')]) {
    const result = auditRollout(serialize(records), expected);
    assert.equal(result.attribution.status, 'unknown');
    assert.equal(result.usage.counters, null);
  }
});

test('unfinished or interrupted turns retain partial cumulative thread totals', () => {
  for (const records of [complete().slice(0, -1), [...complete(), event('turn_aborted')]]) {
    const result = auditRollout(serialize(records), expected);
    assert.equal(result.usage.status, 'partial');
    assert.equal(result.usage.counters.total_tokens, 110);
  }
});

test('a counter preceding the latest start cannot establish final usage', () => {
  const records = [meta, context, usage(), event('task_started'), event('task_complete')];
  const result = auditRollout(serialize(records), expected);
  assert.equal(result.usage.status, 'partial');
  assert.equal(result.usage.counters.total_tokens, 110);
  assert.match(result.usage.scope, /Cumulative standalone thread counters, not a per-turn delta/);
});

test('resumed or multi-turn rollouts cannot attribute cumulative thread totals to one trial', () => {
  for (const continuation of [
    [event('task_started')],
    [event('task_started'), context, event('task_complete')],
    [event('task_started'), context, usage(200, 160, 20), event('task_complete')]
  ]) {
    const result = auditRollout(serialize([...complete(), ...continuation]), expected);
    assert.equal(result.attribution.status, 'unknown');
    assert.equal(result.attribution.startedEvents, 2);
    assert.match(result.attribution.reasons.join(' '), /ambiguous for one trial/);
    assert.equal(result.usage.status, 'unknown');
    assert.equal(result.usage.counters, null);
  }
});

test('malformed JSON or incomplete events are unknown, not skipped for attribution', () => {
  for (const bad of ['{broken', 'null', '{}']) {
    const result = auditRollout(serialize(complete()) + '\n' + bad, expected);
    assert.equal(result.malformedLines, 1);
    assert.equal(result.usage.status, 'unknown');
    assert.equal(result.usage.counters, null);
  }
});

test('missing, invalid, inconsistent, or decreasing counters do not fall back to earlier good usage', () => {
  const invalid = [event('token_count'), usage(100, 101), usage(-1), usage(50, 20)];
  const inconsistent = usage();
  inconsistent.payload.info.total_token_usage.total_tokens = 999;
  invalid.push(inconsistent);
  for (const counter of invalid) {
    const records = complete();
    records.splice(4, 0, counter);
    const result = auditRollout(serialize(records), expected);
    assert.equal(result.usage.status, 'unknown');
    assert.equal(result.usage.counters, null);
  }
  const noCounters = complete().filter(record => record.payload?.type !== 'token_count');
  assert.equal(auditRollout(serialize(noCounters), expected).usage.status, 'unknown');
});

test('expected identity and model fields must be supplied', () => {
  for (const field of ['threadId', 'model', 'effort']) {
    assert.throws(() => auditRollout('', { ...expected, [field]: '' }), new RegExp(field));
  }
});
