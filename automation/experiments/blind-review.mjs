// Blind grading: pack trials under random codes (no rung, model or trial name),
// let a reviewer write grades.json, then apply grades back as each trial's review.json.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const preferences = section => [
  `${section} is selected in the Preferences tree`,
  'The search box is empty',
  `All ${section} controls are visible`,
  'The Preferences title and OK/Cancel buttons are readable'
];
const outputDialog = ['The output dialog title is visible', 'The Hello World text is visible', 'The Close button is visible'];
const columnIdentity = [
  'The Comparison table context is visible',
  'The Amount row under Comparison is selected',
  'Properties are readable with Name Amount',
  'Object Type shows a column type',
  "DAX identifier reads 'Comparison'[Amount]"
];

export const rubrics = {
  formatting: [
    'Auto Formatting is selected in the Preferences tree',
    'The search box is empty',
    'All Auto Formatting controls are visible, including Use default formatting settings',
    'The Preferences title and OK/Cancel buttons are readable'
  ],
  'code-actions': preferences('Code Actions'),
  column: [...columnIdentity, 'The TOM Explorer search box is empty'],
  measure: [
    'The Total Amount measure under Sales is selected',
    "The expression editor shows SUM('Sales'[Amount])",
    'Properties are readable with Name Total Amount',
    'The TOM Explorer search box is empty'
  ],
  table: ['The Sales table is selected', 'Properties are readable with Name Sales', 'Object Type shows Table', 'The TOM Explorer search box is empty'],
  'tom-tree': ['Sales is expanded showing Amount and Total Amount', 'Comparison is expanded showing Amount', 'The TOM Explorer search box is empty'],
  'script-run': outputDialog,
  'dax-general': preferences('DAX Editor General'),
  'save-to-folder': [...preferences('Save-to-folder').slice(0, 2), 'All Save-to-folder controls are visible, including Serialization mode', preferences('Save-to-folder')[3]],
  'calc-group-menu': ['The Model menu is open', 'The Calculation Group item is readable in the menu', 'No dialog or new model object is shown'],
  'model-properties': ['The model root node is selected in TOM Explorer', 'Properties are readable for the model', 'The TOM Explorer search box is empty'],
  'script-edit': [
    'The C# script editor shows exactly two lines: foreach (var m in Selected.Measures) and m.FormatString = "#,0.00";',
    'Nothing was added or changed by autocomplete, such as a using directive or a replaced word',
    'No script output dialog is open'
  ],
  'bpa-view': ['The Best Practice Analyzer view is open and selected', 'Its rule list or results are readable', 'No dialog is open'],
  'dax-query': ['A DAX Query document tab is active', 'Its query editor is visible and empty', 'No query result or error is shown'],
  // Round-1 tasks, kept so their trials can still be packed.
  'script-source': ['The C# script editor shows "Hello World".Output();', 'No script output dialog is open'],
  // Replaced by model-properties: the fixture has no relationships. Kept so those trials can still be packed.
  relationship: ['A relationship is selected in TOM Explorer', 'Properties show its from and to columns', 'The TOM Explorer search box is empty'],
  // Task names from the four-task study, kept so its trials can still be packed.
  object: columnIdentity,
  script: outputDialog
};

const readJson = file => JSON.parse(fs.readFileSync(file, 'utf8'));

export function finalMessage(transcriptPath) {
  if (!fs.existsSync(transcriptPath)) return null;
  let message = null;
  for (const line of fs.readFileSync(transcriptPath, 'utf8').split(/\r?\n/)) {
    try {
      const event = JSON.parse(line);
      if (event.type === 'result' && typeof event.result === 'string') message = event.result;
    } catch { /* partial lines from killed trials */ }
  }
  return message;
}

// Agents often echo their save path, which names the study and trial.
const redact = (message, study, id) => message && [study, path.basename(study), id]
  .reduce((text, secret) => text.split(secret).join('<hidden>'), message);

// Attempted trials only; restoration and edits are checked from the trial's own records, not the image.
export function pack(studies, output) {
  const out = path.resolve(output);
  if (fs.existsSync(out)) throw new Error('Use a fresh review directory');
  fs.mkdirSync(path.join(out, 'items'), { recursive: true });
  const key = {};
  const items = [];
  for (const study of studies.map(directory => path.resolve(directory))) {
    for (const trial of readJson(path.join(study, 'study.json')).trials) {
      const dir = path.join(study, trial.id);
      if (!fs.existsSync(path.join(dir, 'usage.json'))) continue;
      const code = crypto.randomBytes(4).toString('hex');
      key[code] = dir;
      const hasImage = fs.existsSync(path.join(dir, 'result.png'));
      if (hasImage) fs.copyFileSync(path.join(dir, 'result.png'), path.join(out, 'items', `${code}.png`));
      items.push({ code, task: trial.task, image: hasImage ? `items/${code}.png` : null,
        rubric: rubrics[trial.task], finalMessage: redact(finalMessage(path.join(dir, 'agent.jsonl')), study, trial.id) });
    }
  }
  items.sort((a, b) => a.code.localeCompare(b.code));
  fs.writeFileSync(path.join(out, 'items.json'), JSON.stringify(items, null, 2) + '\n');
  fs.writeFileSync(path.join(out, 'key.json'), JSON.stringify(key, null, 2) + '\n');
  return { directory: out, items: items.length };
}

// grades.json: { CODE: { rubric: { requirement: bool }, claimedSuccess: bool, notes } }.
// A trial passes only if every rubric item holds and its settings restoration was verified.
export function apply(output) {
  const out = path.resolve(output);
  const key = readJson(path.join(out, 'key.json'));
  const items = new Map(readJson(path.join(out, 'items.json')).map(item => [item.code, item]));
  const grades = readJson(path.join(out, 'grades.json'));
  let written = 0;
  for (const [code, grade] of Object.entries(grades)) {
    const dir = key[code];
    const item = items.get(code);
    if (!dir || !item) throw new Error(`Unknown code: ${code}`);
    const missing = item.rubric.filter(requirement => typeof grade.rubric?.[requirement] !== 'boolean');
    if (missing.length || typeof grade.claimedSuccess !== 'boolean') throw new Error(`Incomplete grade for ${code}`);
    const restored = readJson(path.join(dir, 'usage.json')).cleanup?.status === 'verified';
    const passed = restored && item.image !== null && item.rubric.every(requirement => grade.rubric[requirement]);
    const review = { passed, claimedSuccess: grade.claimedSuccess, rubric: { ...grade.rubric, 'settings restored': restored }, notes: grade.notes ?? '' };
    fs.writeFileSync(path.join(dir, 'review.json'), JSON.stringify(review, null, 2) + '\n', { flag: 'wx' });
    written++;
  }
  return { written };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [command, ...args] = process.argv.slice(2);
    if (command === 'pack') console.log(JSON.stringify(pack(args.slice(0, -1), args.at(-1))));
    else if (command === 'apply') console.log(JSON.stringify(apply(args[0])));
    else throw new Error('pack STUDY... REVIEW_DIRECTORY | apply REVIEW_DIRECTORY');
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
