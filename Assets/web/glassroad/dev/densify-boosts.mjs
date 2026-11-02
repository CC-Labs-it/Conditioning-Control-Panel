// Regenerate only the committed boost plans. Existing authored layouts and media are retained.
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { build } from './gen-threshold.mjs';
import { buildKeep } from './gen-keep.mjs';
import { buildMainframe } from './gen-mainframe.mjs';

export function regenerateBoosts(id) {
  if (id === 'threshold') return build().track.boosts;
  if (id === 'keep') return buildKeep().boosts;
  if (id === 'mainframe') return buildMainframe().track.boosts;
  throw Error('Boost plan for ' + id + ' is owned by its active course builder');
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  for (const id of ['threshold', 'keep', 'mainframe']) {
    const path = fileURLToPath(new URL('../tracks/' + id + '.json', import.meta.url));
    const text = readFileSync(path, 'utf8'), json = JSON.parse(text), boosts = regenerateBoosts(id);
    if (process.argv.includes('--write')) {
      const key = text.indexOf('"boosts":'), start = text.indexOf('[', key);
      let end = start, depth = 0;
      do { if (text[end] === '[') depth++; if (text[end] === ']') depth--; end++; } while (depth && end < text.length);
      if (key < 0 || start < 0 || depth) throw Error('Expected boosts array in ' + path);
      writeFileSync(path, text.slice(0, start) + JSON.stringify(boosts) + text.slice(end));
    }
    console.log(JSON.stringify({ id, before: json.boosts.length, after: boosts.length, write: process.argv.includes('--write') }));
  }
}
