import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { composeSkill } from './build-skill.mjs';

async function layers(t, files) {
  const directory = await mkdtemp(join(tmpdir(), 'skill-layers-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  for (const [name, text] of Object.entries(files)) await writeFile(join(directory, `${name}.md`), text);
  return directory;
}

test('baseline rungs inject nothing', async t => {
  const directory = await layers(t, {});
  assert.equal(composeSkill('0a', directory).text, '');
  assert.equal(composeSkill('0b', directory).text, '');
});

test('rungs are cumulative, strip authoring comments and skip empty layers', async t => {
  const directory = await layers(t, {
    '1-mcp-basics': '<!-- note -->\n- basics\n',
    '2-te3': '<!-- only a note -->\n',
    '3-companion': '- companion'
  });
  assert.equal(composeSkill('1', directory).text, '- basics');
  assert.equal(composeSkill('2', directory).text, '- basics');
  assert.equal(composeSkill('3', directory).text, '- basics\n\n- companion');
});

test('identical layers give identical hashes', async t => {
  const first = await layers(t, { '1-mcp-basics': '- basics' });
  const second = await layers(t, { '1-mcp-basics': '<!-- different note -->- basics' });
  assert.equal(composeSkill('1', first).sha256, composeSkill('1', second).sha256);
  assert.throws(() => composeSkill('9', first), /Unknown rung/);
});
