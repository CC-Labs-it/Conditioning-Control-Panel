// Mainframe: server hall, curved coolant canal, full reactor helix, high memory crossing and submerged return.
// Run node dev/gen-mainframe.mjs [--check]. Only runtime track data is written.
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { loadTrack } from '../track/load.js';
import { validate } from '../track/schema.js';

const round = v => Math.round(v * 1000) / 1000 || 0;
const smooth = u => u * u * (3 - 2 * u);
export const PIECES = Object.freeze([
  { id: 'boot', group: 'boot-hall', len: 160, y: 0, act: 0 },
  { id: 'address-left', group: 'address-chicane', turn: -60, r: 90, y: 3, act: 0 },
  { id: 'address-right', group: 'address-chicane', turn: 120, r: 90, y: 6, act: 0 },
  { id: 'address-return', group: 'address-chicane', turn: -60, r: 90, y: 3, act: 0 },
  { id: 'boot-exit', group: 'address-chicane', len: 120, y: 3, act: 0 },
  { id: 'address-turn', group: 'address-chicane', turn: 90, r: 90, y: 6, act: 0 },
  { id: 'canal', group: 'coolant-canal', len: 300, y: 12, w: 36, surface: 'grip', act: 0 },
  { id: 'core-approach', group: 'core-approach', len: 520 - 200 * Math.sqrt(3), y: 16, act: 1 },
  { id: 'core-weave-a', group: 'core-approach', turn: 60, r: 100, y: 19, act: 1 },
  { id: 'core-weave-b', group: 'core-approach', turn: -120, r: 100, y: 22, act: 1 },
  { id: 'core-weave-c', group: 'core-approach', turn: 60, r: 100, y: 24, act: 1 },
  { id: 'core', group: 'spiral-core', turn: 360, r: 85, y: 76, linear: true, roll: 26, act: 1 },
  { id: 'cache-turn', group: 'memory-crossing', turn: 90, r: 90, y: 76, act: 1 },
  { id: 'memory-flare', group: 'memory-crossing', len: 80, y: 76, wFrom: 28, w: 104, act: 1 },
  { id: 'memory', group: 'memory-crossing', len: 440, y: 76, w: 104, act: 1 },
  { id: 'memory-exit', group: 'memory-crossing', len: 180 * Math.sqrt(3) - 240, y: 76, wFrom: 104, w: 28, act: 1 },
  { id: 'bus-climb', group: 'upper-bus', len: 180, y: 120, act: 1 },
  { id: 'bus-turn', group: 'upper-bus', turn: 90, r: 90, y: 120, act: 1 },
  { id: 'bus-weave-a', group: 'upper-bus', turn: -45, r: 75, y: 126, act: 1 },
  { id: 'bus-weave-b', group: 'upper-bus', turn: 90, r: 75, y: 126, act: 1 },
  { id: 'bus-weave-c', group: 'upper-bus', turn: -45, r: 75, y: 120, act: 1 },
  { id: 'bus-descent', group: 'bus-descent', turn: 720, r: 65, rEnd: 105, y: 60, linear: true, roll: 10, wFrom: 28, w: 72, act: 1 },
  { id: 'bus-runout', group: 'lower-bus', len: 100, y: 48, wFrom: 72, w: 28, act: 1 },
  { id: 'bus-return-turn', group: 'lower-bus', turn: 90, r: 90, y: 44, act: 1 },
  { id: 'bus-underpass', group: 'lower-bus', len: 220, y: 44, act: 1 },
  { id: 'bus-south-turn', group: 'lower-bus', turn: 90, r: 90, y: 16, act: 1 },
  { id: 'bus-return-a', group: 'return-weave', turn: 45, r: 75, y: 16, act: 1 },
  { id: 'bus-return-b', group: 'return-weave', turn: -90, r: 75, y: 16, act: 1 },
  { id: 'bus-return-c', group: 'return-weave', turn: 45, r: 75, y: 16, act: 1 },
  { id: 'bus-home-run', group: 'return-weave', len: 100, y: 28, act: 1 },
  { id: 'bus-home-turn', group: 'return-weave', turn: 90, r: 90, y: 40, act: 1 },
  { id: 'gantry-turn', group: 'gantry-turn', turn: 180, r: 70, y: 86, act: 1 },
  { id: 'skybridge', group: 'skybridge', len: 300, y: 86, act: 1 },
  { id: 'spillway-turn', group: 'spillway-turn', turn: -180, r: 70, y: 60, w: 34, surface: 'stream', flow: [5, 0.5], act: 2 },
  { id: 'spillway', group: 'spillway', len: 300, y: 48, w: 34, surface: 'stream', flow: [6, -0.6], act: 2 },
  { id: 'release-turn', group: 'coolant-chute', turn: 90, r: 90, y: 40, w: 34, surface: 'stream', flow: [5, 0], act: 2 },
  { id: 'chute', group: 'coolant-chute', len: 100, y: 4, w: 34, surface: 'stream', flow: [8, 0], act: 2 },
  { id: 'pool', group: 'coolant-chute', len: 40, y: 0, w: 34, surface: 'pool', act: 2 },
  { id: 'submerged', group: 'submerged', len: 80, y: -16, w: 32, act: 2 },
  { id: 'submerged-floor', group: 'submerged', len: 200, y: -16, w: 32, act: 2 },
  { id: 'emerge', group: 'emerge', len: 120, y: 6, w: 32, act: 2 },
  { id: 'home-turn', group: 'home', turn: 90, r: 90, y: 0, act: 2 },
]);

export function buildMainframe() {
  const nodes = [], spans = {};
  let x = 0, z = 0, y = 0, heading = 0;
  for (const p of PIECES) {
    const a = (p.turn || 0) * Math.PI / 180, radius = (p.r || 0) * Math.sign(a), length = p.len || Math.abs(a) * (p.r + (p.rEnd || p.r)) / 2;
    const count = Math.round(length / (p.rEnd ? 10.8 : 10)), from = nodes.length;
    for (let k = 0; k < count; k++) {
      const u = k / count, d = length * u, h = heading + a * u;
      const grow = Math.sign(a) * ((p.rEnd || p.r) - p.r) * smooth(u);
      const px = p.turn ? x + radius * (Math.sin(h) - Math.sin(heading)) + grow * Math.sin(h) : x + d * Math.cos(heading);
      const pz = p.turn ? z + radius * (Math.cos(heading) - Math.cos(h)) - grow * Math.cos(h) : z + d * Math.sin(heading);
      const stream = p.stream && d >= p.stream[0] && d < p.stream[1];
      const ramp = p.stream ? Math.min(1, (d - p.stream[0] + 10) / 30, (p.stream[1] + 10 - d) / 30) : 1;
      nodes.push({ p: [round(px), round(y + (p.y - y) * (p.linear ? u : smooth(u)) - (p.dip || 0) * Math.sin(Math.PI * u) ** 2), round(pz)],
        roll: p.turn ? round(Math.sign(a) * (p.roll || 20) * Math.min(1, d / 25, (length - d) / 25)) : 0,
        w: round(p.wFrom ? p.wFrom + (p.w - p.wFrom) * smooth(u) : p.w || 28), flags: ['rail'], speed: 1, surface: stream ? 'stream' : p.surface || 'glass',
        ...(stream ? { flow: [round(5 * ramp), round(-ramp)] } : p.flow ? { flow: p.flow } : {}) });
    }
    if (p.turn) { const grow = Math.sign(a) * ((p.rEnd || p.r) - p.r); x += radius * (Math.sin(heading + a) - Math.sin(heading)) + grow * Math.sin(heading + a); z += radius * (Math.cos(heading) - Math.cos(heading + a)) - grow * Math.cos(heading + a); }
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
    checkpoints: [0, spans.canal.from, spans.core.from, spans.memory.from, spans['bus-climb'].from, spans['bus-descent'].from, spans['bus-underpass'].from, spans.skybridge.from, spans.spillway.from, spans.chute.from, spans.submerged.from],
    spawn: { node: 0, grid: [[-4, -5], [4, -9], [-4, -13], [4, -17], [-4, -21], [4, -25], [-4, -29], [4, -33]] },
    items: [], boosts: [], jumps: [], routes: [], courts: [], fields: [], hazards: [], media: [],
    ai: { lines: [], pace: [1, 1.05, 1.1] }, beat: { bpm: null, offsetMs: 0, map: [] },
    reservoir: { waterY: 4 },
    limits: { fallY: -40, edgeGraceM: 1.2, edgeGraceS: 0.8 } };
  const loaded = loadTrack(track), nearest = s => loaded.nodeS.slice(0, nodes.length).reduce((best, n, i) =>
    Math.abs(n - s) < Math.abs(loaded.nodeS[best] - s) ? i : best, 0);
  const at = (id, ds = 0) => loaded.nodeS[spans[id].from] + ds;
  // Two optional cuts across the descending bus. The lower turn expands outward to catch the kart.
  const descent = spans['bus-descent'], turns = descent.to - descent.from;
  for (const [i, phase] of [0.09, 0.3].entries()) {
    const from = descent.from + Math.round(turns * phase), to = from + 4;
    const target = from + Math.round(turns / 2), land = { from: target - 3, to: Math.min(descent.to - 1, target + 13) };
    for (let n = from; n < to; n++) nodes[n].flags = ['railR'];
    track.routes.push({ id: 'bus-cut-' + (i + 1), kind: 'drop', side: 'L', from, to,
      open: { ds: 3, len: 25 }, land, pad: { node: target + 6, x: -4 } });
  }
  track.waterSpills = [
    { fromS: at('spillway-turn', 65), toS: at('spillway-turn', 105), side: 1, dropM: 28 },
    { fromS: at('spillway', 70), toS: at('spillway', 125), side: -1, dropM: 36 },
    { fromS: at('chute', 20), toS: at('chute', 55), side: 1, dropM: 40 },
  ];
  for (const s of [95, at('boot-exit', 40), at('canal', 50), at('core', 380), at('memory-flare', 25), at('bus-turn', 70), at('bus-descent', 650), at('bus-underpass', 100), at('skybridge', 100), at('spillway', 180), at('submerged-floor', 75)])
    for (const lateral of [-6, -2, 2, 6]) track.items.push({ node: nearest(s), x: lateral, kind: 'box' });
  for (const s of [60, at('canal', 210), at('core', 270), at('bus-climb', 80), at('bus-underpass', 85), at('skybridge', 70), at('spillway', 70), at('emerge', 85)]) {
    const node = nearest(s); nodes[node].flags.push('boost'); track.boosts.push({ node, x: 0, w: 4, len: 6 });
  }
  for (const d of [80, 90, 100, 110, 120]) track.items.push({ node: nearest(at('canal', d)), x: -3, kind: 'bubble' });
  const from = spans.memory.from + 4, to = spans.memory.to - 4, lanes = [-40, -20, 0, 20, 40], stones = [];
  const s0 = loaded.nodeS[from], s1 = loaded.nodeS[to];
  for (let i = from; i < to; i++) nodes[i].flags = ['open'];
  const put = (id, a, b, lateral, width) => {
    let node = from;
    while (node + 1 < to && loaded.nodeS[node + 1] <= s0 + a + 1e-8) node++;
    stones.push({ id, node, ds: round(s0 + a - loaded.nodeS[node]), x: lateral, w: width, len: round(b - a), shape: 'hex' });
  };
  // Separate islands: broad landing faces, visible void and occasional lane-changing links.
  const count = Math.round((s1 - s0) / 26), pitch = (s1 - s0 + 4) / count;
  for (let row = 0; row < count; row++) {
    const a = row * pitch, b = row === count - 1 ? s1 - s0 : a + pitch - 4;
    lanes.forEach((lateral, lane) => put(`memory-${lane}-${row}`, a, b, lateral, 18));
    if (row < count - 1) for (const [lane, lateral] of (row % 2 ? [-10, 30] : [-30, 10]).entries()) {
      put(`memory-link-${lane}-${row}`, b - 4, b + 8, lateral, 16);
      if ((row + lane) % 3 === 0) stones.at(-1).spin = row % 2 ? -0.085 : 0.075;
    }
  }
  track.fields.push({ id: 'memory-tiles', from, to, lanes, stones });
  const scanS = [180, loaded.nodeS[spans.canal.from] + 85, loaded.nodeS[spans.canal.from] + 170,
    at('skybridge', 160), loaded.nodeS[spans['memory-exit'].to] - 35, loaded.nodeS[spans.submerged.from] + 130];
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
