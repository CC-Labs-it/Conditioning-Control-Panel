// Mainframe: server hall, curved coolant canal, full reactor helix, high memory crossing and submerged return.
// Run node dev/gen-mainframe.mjs [--check]. Only runtime track data is written.
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { loadTrack } from '../track/load.js';
import { validate } from '../track/schema.js';
import { laneStones } from './gen-threshold.mjs';

const round = v => Math.round(v * 1000) / 1000;
const smooth = u => u * u * (3 - 2 * u);
export const PIECES = Object.freeze([
  { id: 'boot', group: 'boot-hall', len: 480, y: 0, act: 0 },
  { id: 'address-turn', group: 'boot-hall', turn: 90, r: 75, y: 6, act: 0 },
  { id: 'canal', group: 'coolant-canal', len: 240, y: 12, w: 36, surface: 'grip', act: 0 },
  { id: 'core-approach', group: 'core-approach', len: 110, y: 18, act: 1 },
  { id: 'core', group: 'spiral-core', turn: 360, r: 75, y: 66, linear: true, roll: 26, act: 1 },
  { id: 'cache-turn', group: 'memory-crossing', turn: 90, r: 75, y: 66, act: 1 },
  { id: 'memory', group: 'memory-crossing', len: 480, y: 66, act: 1 },
  { id: 'release-turn', group: 'coolant-chute', turn: 90, r: 75, y: 50, act: 2 },
  { id: 'chute', group: 'coolant-chute', len: 80, y: 8, w: 28, surface: 'stream', flow: [8, 0], act: 2 },
  { id: 'pool', group: 'coolant-chute', len: 40, y: 2, w: 32, surface: 'pool', act: 2 },
  { id: 'submerged', group: 'submerged', len: 200, y: 2, dip: 16, w: 28, act: 2 },
  { id: 'emerge', group: 'home', len: 30, y: 0, act: 2 },
  { id: 'home-turn', group: 'home', turn: 90, r: 75, y: 0, act: 2 },
]);

export function buildMainframe() {
  const nodes = [], spans = {};
  let x = 0, z = 0, y = 0, heading = 0;
  for (const p of PIECES) {
    const a = (p.turn || 0) * Math.PI / 180, length = p.len || a * p.r;
    const count = Math.round(length / 10), from = nodes.length;
    for (let k = 0; k < count; k++) {
      const u = k / count, d = length * u, h = heading + a * u;
      const px = p.turn ? x + p.r * (Math.sin(h) - Math.sin(heading)) : x + d * Math.cos(heading);
      const pz = p.turn ? z + p.r * (Math.cos(heading) - Math.cos(h)) : z + d * Math.sin(heading);
      const stream = p.stream && d >= p.stream[0] && d < p.stream[1];
      const ramp = p.stream ? Math.min(1, (d - p.stream[0] + 10) / 30, (p.stream[1] + 10 - d) / 30) : 1;
      nodes.push({ p: [round(px), round(y + (p.y - y) * (p.linear ? u : smooth(u)) - (p.dip || 0) * Math.sin(Math.PI * u) ** 2), round(pz)],
        roll: p.turn ? round((p.roll || 20) * Math.min(1, d / 25, (length - d) / 25)) : 0,
        w: p.w || 24, flags: ['rail'], speed: 1, surface: stream ? 'stream' : p.surface || 'glass',
        ...(stream ? { flow: [round(5 * ramp), round(-ramp)] } : p.flow ? { flow: p.flow } : {}) });
    }
    if (p.turn) { x += p.r * (Math.sin(heading + a) - Math.sin(heading)); z += p.r * (Math.cos(heading) - Math.cos(heading + a)); }
    else { x += length * Math.cos(heading); z += length * Math.sin(heading); }
    heading += a; y = p.y;
    spans[p.id] = { from, to: nodes.length, act: p.act, group: p.group };
  }
  if (Math.hypot(x, z, y) > 1e-6) throw new Error('Mainframe does not close');
  nodes[0].flags.unshift('start');
  const groups = [...new Set(PIECES.map(p => p.group))].map(group => Object.values(spans).filter(p => p.group === group));
  const track = { format: 'glassroad-track', version: 1, id: 'mainframe', name: { en: 'Mainframe' },
    author: 'ccl', created: '2026-10-24', loop: true, laps: 3, units: 'm', nodes,
    sections: groups.map(rows => ({ id: rows[0].group, act: ['induction', 'deepening', 'release'][rows[0].act],
      palette: ['dawn', 'deep', 'afterglow'][rows[0].act], sky: ['spiral-slow', 'spiral-deep', 'spiral-wide'][rows[0].act],
      bandSpeed: [0.6, 1, 0.8][rows[0].act], from: rows[0].from, to: rows.at(-1).to })),
    checkpoints: [0, spans.canal.from, spans.core.from, spans.memory.from, spans.chute.from, spans.submerged.from],
    spawn: { node: 0, grid: [[-4, -5], [4, -9], [-4, -13], [4, -17], [-4, -21], [4, -25], [-4, -29], [4, -33]] },
    items: [], boosts: [], jumps: [], routes: [], courts: [], fields: [], hazards: [], media: [],
    ai: { lines: [], pace: [1, 1.05, 1.1] }, beat: { bpm: null, offsetMs: 0, map: [] },
    limits: { fallY: -25, edgeGraceM: 1.2, edgeGraceS: 0.8 } };
  const loaded = loadTrack(track), nearest = s => loaded.nodeS.slice(0, nodes.length).reduce((best, n, i) =>
    Math.abs(n - s) < Math.abs(loaded.nodeS[best] - s) ? i : best, 0);
  for (const s of [95, 410, 655, 1410, 1880, 2280]) for (const lateral of [-6, -2, 2, 6]) track.items.push({ node: nearest(s), x: lateral, kind: 'box' });
  for (const s of [60, 710, 1250, 2050, 2460]) { const node = nearest(s); nodes[node].flags.push('boost'); track.boosts.push({ node, x: 0, w: 4, len: 6 }); }
  for (const s of [680, 690, 700, 710, 720]) track.items.push({ node: nearest(s), x: -3, kind: 'bubble' });
  const from = spans.memory.from + 5, to = from + 5, lanes = [-7, 0, 7], stones = [];
  const s0 = loaded.nodeS[from], s1 = loaded.nodeS[to];
  for (let i = from; i < to; i++) nodes[i].flags = ['open'];
  // The closing tile meets the deck: a short last tile plus another exit gap can swallow one full hop.
  lanes.forEach((lateral, lane) => laneStones(s1 - s0, lane * 3, 5, 4, 0).forEach(([a, b], k) => {
    let node = from;
    while (node + 1 < to && loaded.nodeS[node + 1] <= s0 + a + 1e-8) node++;
    stones.push({ id: `memory-${lane}-${k}`, node, ds: round(s0 + a - loaded.nodeS[node]), x: lateral, w: 5, len: round(b - a) });
  }));
  track.fields.push({ id: 'memory-tiles', from, to, lanes, stones });
  const scanS = [180, loaded.nodeS[spans.canal.from] + 85, loaded.nodeS[spans.canal.from] + 170,
    loaded.nodeS[spans.memory.from] + 190, loaded.nodeS[spans.memory.from] + 360, loaded.nodeS[spans.submerged.from] + 130];
  const scanHalls = scanS.map((s, i) => ({ id: 'scan-' + i, s, node: nearest(s), from: nearest(s - 20), to: nearest(s + 20) }));
  const errors = validate(track).errors;
  if (errors.length) throw new Error(errors.join('; '));
  return { track, spans, scanHalls, lengthM: loaded.lengthM };
}

// Flat rows keep geometry changes reviewable without hiding values inside minified documents.
export function formatTrack(track) {
  return '{\n' + Object.entries(track).map(([key, value]) => {
    const flatRows = Array.isArray(value) && value.length && typeof value[0] === 'object';
    const rows = flatRows ? value.map(row => JSON.stringify(row)) : [];
    const lines = key === 'nodes' ? Array.from({ length: Math.ceil(rows.length / 3) }, (_, i) => rows.slice(i * 3, i * 3 + 3).join(',')) : rows;
    return '  ' + JSON.stringify(key) + ': ' + (flatRows ? '[\n' + lines.map(row => '    ' + row).join(',\n') + '\n  ]' : JSON.stringify(value));
  }).join(',\n') + '\n}\n';
}
if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const built = buildMainframe(), loaded = loadTrack(built.track);
  console.log(JSON.stringify({ lengthM: built.lengthM, sections: loaded.sections, scanHalls: built.scanHalls.map(h => ({ ...h, fromS: loaded.nodeS[h.from], toS: loaded.nodeS[h.to] })),
    stones: loaded.fields.map(f => ({ fromS: f.fromS, toS: f.toS })) }));
  if (!process.argv.includes('--check')) writeFileSync(new URL('../tracks/mainframe.json', import.meta.url), formatTrack(built.track));
}
