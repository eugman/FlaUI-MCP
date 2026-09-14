// Triage tool calls in a full Codex rollout. This is not a security boundary.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export function auditCompliance(text, arm) {
  if (!['A', 'B', 'C', 'D'].includes(arm)) throw new Error('Expected arm A, B, C, or D');
  const violations = [];
  const review = [];
  let calls = 0;
  let malformed = 0;
  let lineNumber = 0;
  for (const line of text.split(/\r?\n/)) {
    lineNumber++;
    if (!line.trim()) continue;
    let record;
    try { record = JSON.parse(line); } catch { malformed++; continue; }
    if (record.type !== 'response_item') continue;
    const item = record.payload;
    if (!['function_call', 'custom_tool_call', 'web_search_call'].includes(item?.type)) continue;
    calls++;
    const name = item.name ?? item.type;
    const finding = { line: lineNumber, callId: item.call_id ?? null, tool: name };
    // Nested JavaScript can compute tool names. Do not pretend a regex proves compliance.
    if (['functions.exec', 'functions', 'exec', 'functions.wait'].includes(name)) {
      review.push({ ...finding, reason: 'Inspect nested calls and resource/file access in orchestration source and results.' });
      continue;
    }
    if (name.startsWith('mcp__study__')) {
      const tool = name.slice('mcp__study__'.length);
      if (tool.startsWith('windows_') ||
          (['C', 'D'].includes(arm) && tool === 'te3_catalog') ||
          (arm === 'D' && ['te3_inspect', 'te3_navigate', 'te3_capture'].includes(tool))) continue;
    }
    violations.push({ ...finding, reason: 'Tool outside the arm’s supplied MCP tools and orchestration.' });
  }
  return {
    arm, calls, malformedLines: malformed, violations, review,
    status: violations.length ? 'contaminated' : 'review-required',
    note: 'Attempts count, including denied calls. Review full trace for other-arm guidance and model edits; never infer compliance from missing events. Retain original outcome and token costs; do not automatically rerun.'
  };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const [file, arm] = process.argv.slice(2);
  console.log(JSON.stringify(auditCompliance(fs.readFileSync(file, 'utf8'), arm), null, 2));
}
