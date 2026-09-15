// Composes the skill text injected for a ladder rung from cumulative layer files.
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';

const defaultLayers = path.join(path.dirname(fileURLToPath(import.meta.url)), 'layers');

// 1 original MCP, 2 new MCP, 3 + generic skill, 4 + map, 5 + TE3 tools and the line that points to them.
export const rungLayers = {
  '1': [],
  '2': [],
  '3': ['1-mcp-basics'],
  '4': ['1-mcp-basics', 'map'],
  '5': ['1-mcp-basics', 'map', 'te3-tools']
};

// Layer files may hold authoring notes in HTML comments; agents never see them.
export function composeSkill(rung, layersDirectory = defaultLayers) {
  const layers = rungLayers[rung];
  if (!layers) throw new Error(`Unknown rung: ${rung}`);
  const text = layers
    .map(name => fs.readFileSync(path.join(layersDirectory, `${name}.md`), 'utf8').replace(/<!--[\s\S]*?-->/g, '').replace(/\n{3,}/g, '\n\n').trim())
    .filter(Boolean)
    .join('\n\n');
  return { rung, layers, text, sha256: crypto.createHash('sha256').update(text).digest('hex') };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [rung, output] = process.argv.slice(2);
    if (!rung) throw new Error('node build-skill.mjs RUNG [OUTPUT_FILE]');
    const skill = composeSkill(rung);
    if (output) fs.writeFileSync(output, skill.text, { flag: 'wx' });
    console.log(JSON.stringify({ rung: skill.rung, layers: skill.layers, sha256: skill.sha256, characters: skill.text.length }));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
