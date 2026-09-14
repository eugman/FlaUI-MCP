import test from 'node:test';
import assert from 'node:assert/strict';
import { auditCompliance } from './compliance-audit.mjs';

const call = name => JSON.stringify({ type: 'response_item', payload: { type: 'function_call', name, call_id: name } });
test('generic MCP is permitted but not declared fully compliant without review', () => {
  const result = auditCompliance(call('mcp__study__windows_find'), 'A');
  assert.equal(result.violations.length, 0);
  assert.equal(result.status, 'review-required');
});
test('map and companion access follow arm assignment', () => {
  assert.equal(auditCompliance(call('mcp__study__te3_catalog'), 'B').status, 'contaminated');
  assert.equal(auditCompliance(call('mcp__study__te3_catalog'), 'C').violations.length, 0);
  assert.equal(auditCompliance(call('mcp__study__te3_navigate'), 'C').status, 'contaminated');
  assert.equal(auditCompliance(call('mcp__study__te3_navigate'), 'D').violations.length, 0);
});
test('prohibited attempts count and nested source requires review', () => {
  const result = auditCompliance([call('apply_patch'), call('multi_agent_v1.spawn_agent'), call('functions.exec')].join('\n'), 'A');
  assert.equal(result.violations.length, 2);
  assert.equal(result.review.length, 1);
});
test('missing or malformed evidence is not a clean result', () => {
  assert.equal(auditCompliance('', 'A').status, 'review-required');
  assert.equal(auditCompliance('broken', 'D').malformedLines, 1);
  assert.throws(() => auditCompliance('', 'E'));
});
