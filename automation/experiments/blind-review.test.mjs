import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, mkdir, writeFile, readFile, readdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { pack, apply, rubrics } from './blind-review.mjs';

async function study(root, trials) {
  await mkdir(root);
  await writeFile(join(root, 'study.json'), JSON.stringify({ trials: trials.map(({ id, task }) => ({ id, task })) }));
  for (const { id, restored = true, image = true, attempted = true } of trials) {
    await mkdir(join(root, id));
    if (!attempted) continue;
    await writeFile(join(root, id, 'usage.json'), JSON.stringify({ cleanup: { status: restored ? 'verified' : 'unverified' } }));
    await writeFile(join(root, id, 'agent.jsonl'), `${JSON.stringify({ type: 'result', result: `saved ${join(root, id, 'result.png')}` })}\n{partial`);
    if (image) await writeFile(join(root, id, 'result.png'), 'png');
  }
}

test('pack hides trial identity and apply writes reviews that require every rubric item and restoration', async t => {
  const root = await mkdtemp(join(tmpdir(), 'blind-review-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const studyDir = join(root, 'study');
  await study(studyDir, [
    { id: '0a-sonnet-script-01', task: 'script' },
    { id: '0a-sonnet-script-02', task: 'script', restored: false },
    { id: '0a-sonnet-object-01', task: 'object', image: false },
    { id: '0a-sonnet-object-02', task: 'object', attempted: false }
  ]);
  const review = join(root, 'review');
  assert.deepEqual(pack([studyDir], review), { directory: review, items: 3 });

  const itemsText = await readFile(join(review, 'items.json'), 'utf8');
  assert.doesNotMatch(itemsText, /0a-sonnet/, 'items must not reveal rung, model or trial id');
  assert.equal((await readdir(join(review, 'items'))).length, 2);
  const items = JSON.parse(itemsText);
  const key = JSON.parse(await readFile(join(review, 'key.json'), 'utf8'));
  const codeFor = id => Object.keys(key).find(code => key[code].endsWith(id));
  assert.match(items.find(item => item.code === codeFor('script-01')).finalMessage, /^saved (<hidden>[\\/])+result\.png$/);

  const all = task => Object.fromEntries(rubrics[task].map(requirement => [requirement, true]));
  await writeFile(join(review, 'grades.json'), JSON.stringify({
    [codeFor('script-01')]: { rubric: all('script'), claimedSuccess: true },
    [codeFor('script-02')]: { rubric: all('script'), claimedSuccess: true },
    [codeFor('object-01')]: { rubric: all('object'), claimedSuccess: false, notes: 'no image' }
  }));
  assert.deepEqual(apply(review), { written: 3 });
  const reviewOf = async id => JSON.parse(await readFile(join(studyDir, id, 'review.json'), 'utf8'));
  assert.equal((await reviewOf('0a-sonnet-script-01')).passed, true);
  assert.equal((await reviewOf('0a-sonnet-script-02')).passed, false, 'unverified restoration fails the trial');
  assert.equal((await reviewOf('0a-sonnet-object-01')).passed, false, 'no image fails the trial');
});

test('apply rejects incomplete grades', async t => {
  const root = await mkdtemp(join(tmpdir(), 'blind-review-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const studyDir = join(root, 'study');
  await study(studyDir, [{ id: '0b-sonnet-script-01', task: 'script' }]);
  const review = join(root, 'review');
  pack([studyDir], review);
  const [code] = Object.keys(JSON.parse(await readFile(join(review, 'key.json'), 'utf8')));
  await writeFile(join(review, 'grades.json'), JSON.stringify({ [code]: { rubric: {}, claimedSuccess: true } }));
  assert.throws(() => apply(review), /Incomplete grade/);
});
