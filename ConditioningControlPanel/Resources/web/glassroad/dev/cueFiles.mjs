import { readFile } from 'node:fs/promises';
import { join } from 'node:path';

// Only the private preview reads these files. Song words, anchors and times are never bundled.
export function cueEvents(chart) {
  const rows = [
    ...(chart.events || []).map(e => ({ event: e.event || e.kind || (e.bus && `${e.bus}:${e.note}`), startS: e.startS ?? e.t })),
    ...(chart.triggers || []).map(e => ({ event: `trigger:${e.kind}`, startS: e.startS })),
    ...(chart.sfx || []).map(e => ({ event: `sfx:${e.id}`, startS: e.startS })),
  ];
  return rows.filter(e => typeof e.event === 'string' && e.event && Number.isFinite(e.startS) && e.startS >= 0)
    .sort((a, b) => a.startS - b.startS);
}

export async function withPrivateCues(root, course, chart) {
  if (!['threshold', 'mainframe', 'boudoir', 'goonworks'].includes(course)) throw new Error('Unsupported cue course');
  let file;
  try { file = JSON.parse(await readFile(join(root, 'setpieces', `${course}.cues.json`), 'utf8')); }
  catch (error) { if (error.code !== 'ENOENT') throw error; file = { version: 1, cues: [] }; }
  if (file.version !== 1 || !Array.isArray(file.cues)) throw new Error('Invalid private cue file');
  // Runtime resolves these anchors against this exact chart. Missing anchors never become guessed times.
  return { ...chart, events: cueEvents(chart), cues: file.cues };
}
