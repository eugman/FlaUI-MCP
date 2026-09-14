import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { checkArmInventories, compareMapResources } from './tool-preflight.mjs';

test('embedded map resources must match the frozen topic files exactly', async t => {
  const directory = await mkdtemp(join(tmpdir(), 'map-compare-'));
  t.after(() => rm(directory, { recursive: true, force: true }));
  await writeFile(join(directory, 'start.md'), '﻿# Start\r\nbody');
  assert.equal(compareMapResources({ 'te3://map/start': '# Start\r\nbody' }, directory), 1);
  assert.throws(() => compareMapResources({ 'te3://map/start': '# Start\nbody' }, directory), /differs from frozen file: start.md/);
  assert.throws(() => compareMapResources({ 'te3://map/start': '# Start\r\nbody', 'te3://map/extra': '' }, directory), /topics differ/);
});

function observations() {
  const tools = names => names.map(name => ({ name }));
  const generic = ['windows_find', 'windows_screenshot'];
  const resources = [{ uri: 'te3://map/start' }];
  return {
    A: { tools: tools(generic), resources: [] },
    B: { tools: tools(generic), resources: [] },
    C: { tools: tools([...generic, 'te3_catalog']), resources },
    D: { tools: tools([...generic, 'te3_catalog', 'te3_inspect', 'te3_navigate', 'te3_capture']), resources }
  };
}

test('planned arm tool differences and identical C/D resources pass', () => {
  assert.deepEqual(checkArmInventories(observations()), { genericTools: 2, mapResources: 1 });
});

test('unexpected companion access in unguided arms fails', () => {
  const arms = observations();
  arms.A.tools.push({ name: 'te3_catalog' });
  assert.throws(() => checkArmInventories(arms), /only generic/);
});

test('missing D actions and differing map resources fail', () => {
  const arms = observations();
  arms.D.tools.pop();
  assert.throws(() => checkArmInventories(arms), /companion tool inventory/);
  const different = observations();
  different.D.resources = [{ uri: 'te3://map/other' }];
  assert.throws(() => checkArmInventories(different), /same nonempty/);
});

test('missing observations and duplicate names do not pass', () => {
  assert.throws(() => checkArmInventories({}), /Missing A/);
  const arms = observations();
  arms.B.tools.push(arms.B.tools[0]);
  assert.throws(() => checkArmInventories(arms), /duplicate/);
});

test('malformed and duplicate resource URIs fail even when C/D match', () => {
  for (const resources of [[{}], [{ uri: '' }], [{ uri: 'te3://map/start' }, { uri: 'te3://map/start' }]]) {
    const arms = observations();
    arms.C.resources = resources;
    arms.D.resources = resources;
    assert.throws(() => checkArmInventories(arms), /map resource URIs/);
  }
});
