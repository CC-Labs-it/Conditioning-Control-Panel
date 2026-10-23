// Mainframe: boot corridor, rising data stream, memory bridge, descending return bus.
// Run node dev/gen-mainframe.mjs [--check]. Only runtime track data is written.
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { loadTrack } from '../track/load.js';
import { validate } from '../track/schema.js';
import { laneStones } from './gen-threshold.mjs';

const round = v => Math.round(v * 1000) / 1000;
const smooth = u => u * u * (3 - 2 * u);
export const PIECES = Object.freeze([
  { id: 'boot', len: 280, y: 0, act: 0 },
  { id: 'address-turn', turn: 90, r: 60, y: 6, act: 0 },
  { id: 'data-rise', len: 170, y: 22, act: 1, stream: true },
  { id: 'cache-turn', turn: 90, r: 60, y: 22, act: 1 },
  { id: 'memory', len: 280, y: 22, act: 1 },
  { id: 'release-turn', turn: 90, r: 60, y: 15, act: 2 },
  { id: 'return-bus', len: 170, y: 0, act: 2 },
  { id: 'home-turn', turn: 90, r: 60, y: 0, act: 2 },
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
      const stream = p.stream && d >= 30 && d < 130;
      const ramp = Math.min(1, (d - 20) / 30, (140 - d) / 30);
      nodes.push({ p: [round(px), round(y + (p.y - y) * smooth(u)), round(pz)],
        roll: p.turn ? round(20 * Math.min(1, d / 25, (length - d) / 25)) : 0,
        w: 24, flags: ['rail'], speed: 1, surface: stream ? 'stream' : 'glass',
        ...(stream ? { flow: [round(5 * ramp), round(-ramp)] } : {}) });
    }
    if (p.turn) { x += p.r * (Math.sin(heading + a) - Math.sin(heading)); z += p.r * (Math.cos(heading) - Math.cos(heading + a)); }
    else { x += length * Math.cos(heading); z += length * Math.sin(heading); }
    heading += a; y = p.y;
    spans[p.id] = { from, to: nodes.length, act: p.act };
  }
  if (Math.hypot(x, z, y) > 1e-6) throw new Error('Mainframe does not close');
  nodes[0].flags.unshift('start');
  const acts = [0, 1, 2].map(act => Object.values(spans).filter(p => p.act === act));
  const track = { format: 'glassroad-track', version: 1, id: 'mainframe', name: { en: 'Mainframe' },
    author: 'ccl', created: '2026-10-24', loop: true, laps: 3, units: 'm', nodes,
    sections: acts.map((rows, i) => ({ id: ['boot', 'memory', 'surge'][i], act: ['induction', 'deepening', 'release'][i],
      palette: ['dawn', 'deep', 'afterglow'][i], sky: ['spiral-slow', 'spiral-deep', 'spiral-wide'][i],
      bandSpeed: [0.6, 1, 0.8][i], from: rows[0].from, to: rows.at(-1).to })),
    checkpoints: [0, spans['data-rise'].from, spans.memory.from, spans['return-bus'].from],
    spawn: { node: 0, grid: [[-4, -5], [4, -9], [-4, -13], [4, -17], [-4, -21], [4, -25], [-4, -29], [4, -33]] },
    items: [], boosts: [], jumps: [], routes: [], courts: [], fields: [], hazards: [], media: [],
    ai: { lines: [], pace: [1, 1.05, 1.1] }, beat: { bpm: null, offsetMs: 0, map: [] },
    limits: { fallY: -25, edgeGraceM: 1.2, edgeGraceS: 0.8 } };
  const loaded = loadTrack(track), nearest = s => loaded.nodeS.slice(0, nodes.length).reduce((best, n, i) =>
    Math.abs(n - s) < Math.abs(loaded.nodeS[best] - s) ? i : best, 0);
  for (const s of [95, 405, 900, 1100]) for (const lateral of [-6, -2, 2, 6]) track.items.push({ node: nearest(s), x: lateral, kind: 'box' });
  for (const s of [60, 540, 1050]) { const node = nearest(s); nodes[node].flags.push('boost'); track.boosts.push({ node, x: 0, w: 4, len: 6 }); }
  for (const s of [450, 460, 470, 480, 490]) track.items.push({ node: nearest(s), x: -3, kind: 'bubble' });
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
  const scanHalls = [{ id: 'boot-scan', from: 18, to: 24 }, { id: 'memory-scan', from: spans.memory.from + 17, to: spans.memory.from + 23 }];
  const errors = validate(track).errors;
  if (errors.length) throw new Error(errors.join('; '));
  return { track, spans, scanHalls, lengthM: loaded.lengthM };
}

// Flat rows keep geometry changes reviewable without hiding values inside minified documents.
export function formatTrack(track) {
  return '{\n' + Object.entries(track).map(([key, value]) => {
    const flatRows = Array.isArray(value) && value.length && typeof value[0] === 'object';
    return '  ' + JSON.stringify(key) + ': ' + (flatRows ? '[\n' + value.map(row => '    ' + JSON.stringify(row)).join(',\n') + '\n  ]' : JSON.stringify(value));
  }).join(',\n') + '\n}\n';
}
if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const built = buildMainframe(), loaded = loadTrack(built.track);
  console.log(JSON.stringify({ lengthM: built.lengthM, scanHalls: built.scanHalls.map(h => ({ ...h, fromS: loaded.nodeS[h.from], toS: loaded.nodeS[h.to] })),
    stones: loaded.fields.map(f => ({ fromS: f.fromS, toS: f.toS })) }));
  if (!process.argv.includes('--check')) writeFileSync(new URL('../tracks/mainframe.json', import.meta.url), formatTrack(built.track));
}
