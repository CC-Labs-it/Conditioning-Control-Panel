// Shared phrase checkpoints. Placement leaves the following 100 m free of driving decisions.
import { getKeepLayout } from './keep-layout.js';
import { getBoudoirSetpieces } from './boudoir-setpieces.js';
import { goonworksSetpieces } from './goonworks-setpieces.js';
import { scanZones } from '../sim/mainframeScan.js';
import { industryZones } from '../sim/mainframeIndustry.js';

export const PHRASE_THEMES = Object.freeze({
  threshold: { color: '#ffacd9', words: ['LET GO', 'FOLLOW THE FLOW', 'DRIFT DEEPER'] },
  mainframe: { color: '#80ffd2', words: ['SYNC IN', 'FOLLOW THE SIGNAL', 'STAY IN THE FLOW'] },
  keep: { color: '#ffd99a', words: ['SINK IN', 'FOLLOW THE PATH', 'DRIFT DEEPER'] },
  boudoir: { color: '#ffb7e4', words: ['SOFTEN', 'LET GO', 'JUST FLOAT'] },
  goonworks: { color: '#e3b8ff', words: ['FOLLOW THE FLOW', 'KEEP DRIFTING', 'LET GO'] },
});
const dot = (a, b) => a.reduce((v, x, i) => v + x * b[i], 0);
const distance = (a, b, L) => Math.abs(((a - b + L * 1.5) % L) - L / 2);

export function phraseExclusions(track) {
  const spans = [];
  const add = (a, b = a) => { if (Number.isFinite(a) && Number.isFinite(b)) spans.push([a, b]); };
  for (const row of track.sections) if (['coolant-canal', 'rabbit-hole', 'submerged', 'emerge', 'perfume-court'].includes(row.id)) add(row.fromS, row.toS);
  for (const row of [...(track.fields || []), ...(track.courts || [])]) add(row.s0 ?? row.fromS, row.s1 ?? row.toS);
  for (const row of track.jumps || []) add(row.s, row.landS);
  for (const row of track.routes || []) { add(row.fromS, row.toS); add(row.land.fromS, row.land.toS); }
  for (const row of track.forks || []) add(row.fromS, row.toS);
  for (const row of [...scanZones(track), ...industryZones(track), ...goonworksSetpieces(track)]) add(row.s - 12, row.s + 12);
  const keep = getKeepLayout(track);
  for (const row of [...(keep?.gates || []), ...(keep?.chains || [])]) add(row.s - 12, row.s + 12);
  const boudoir = getBoudoirSetpieces(track);
  for (const row of [...boudoir.presses, ...boudoir.sweepers, ...boudoir.mirrorGates]) add(row.s - 12, row.s + 12);
  for (const row of boudoir.conveyors) add(row.s0, row.s1);
  return spans;
}

export function phraseSiteClear(track, s, spans = phraseExclusions(track)) {
  if (s < 110 || s + 110 > track.lengthM) return false;
  if (spans.some(([a, b]) => s + 100 >= a && s - 25 <= b)) return false;
  const origin = track.frameAt(s);
  for (let d = -20; d <= 100; d += 10) {
    const f = track.frameAt(s + d);
    const protectedTurn = track.id === 'threshold' && f.flags.includes('rail');
    if (f.w < 14 || f.n[1] < .88 || Math.abs(f.t[1]) > .2 || (!protectedTurn && dot(origin.t, f.t) < .8)
      || ['stream', 'pool', 'ice'].includes(f.surface) || f.flags.some(v => ['jump', 'gap', 'magnet'].includes(v))) return false;
  }
  return true;
}

export function phraseGateLayout(track) {
  const theme = PHRASE_THEMES[track.id];
  if (!theme) return [];
  const spans = phraseExclusions(track), candidates = [], chosen = [];
  for (let s = 120; s < track.lengthM - 110; s += 10) if (phraseSiteClear(track, s, spans)) candidates.push(s);
  const count = Math.min(6, Math.max(3, Math.round(track.lengthM / 1400)));
  for (let i = 0; i < count; i++) {
    const target = track.lengthM * (i + .45) / count;
    const available = candidates.filter(s => chosen.every(v => distance(s, v, track.lengthM) >= 400));
    available.sort((a, b) => Math.abs(a - target) - Math.abs(b - target));
    if (available.length) chosen.push(available[0]);
  }
  return chosen.sort((a, b) => a - b).map((s, i) => ({ id: track.id + '-phrase-' + i, s,
    text: theme.words[i % theme.words.length], color: theme.color, frame: track.frameAt(s),
    section: track.sections[track.sectionAt(s)].id }));
}
