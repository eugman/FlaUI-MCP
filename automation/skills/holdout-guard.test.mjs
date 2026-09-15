import test from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

// Hold-out tasks must never get guidance; these phrases name their UI.
const holdoutPhrases = ['calculation group', 'model menu', 'root node', 'best practice analyzer', 'dax query',
  // Round-1 hold-outs, still kept out of guidance.
  'save-to-folder', 'dax editor > general'];

test('skill layers never mention hold-out UI', () => {
  const layers = join(dirname(fileURLToPath(import.meta.url)), 'layers');
  for (const file of readdirSync(layers).filter(name => name.endsWith('.md'))) {
    const text = readFileSync(join(layers, file), 'utf8').toLowerCase();
    for (const phrase of holdoutPhrases) assert.ok(!text.includes(phrase), `${file} mentions hold-out UI: ${phrase}`);
  }
});
