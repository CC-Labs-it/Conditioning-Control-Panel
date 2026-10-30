// node dev/gen-boudoir.mjs [--check] regenerates the authored Boudoir course.
import { writeFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import { loadTrack } from '../track/load.js';
import { validate } from '../track/schema.js';
import { BOUDOIR_PIECES, boudoirPieceLength, boudoirSegmentCount, walkBoudoir, getBoudoirLayout } from '../track/boudoir-layout.js';
import { makeBoudoirForks } from '../track/boudoir-forks.js';
import { makeBoudoirBoosts } from '../track/boudoir-boosts.js';
const RAD = Math.PI / 180, smooth = u => u * u * (3 - 2 * u), round = x => +x.toFixed(3) || 0;
export function buildBoudoir() {
  const plan = walkBoudoir(BOUDOIR_PIECES), nodes = [], starts = [];
  if (Math.hypot(plan.x, plan.z) > 1e-7) throw Error('Boudoir course does not close');
  let y0 = 0;
  BOUDOIR_PIECES.forEach((p, i) => {
    starts.push(nodes.length);
    const start = plan.starts[i], count = boudoirSegmentCount(p), length = boudoirPieceLength(p);
    for (let k = 0; k < count; k++) {
      const u = k / count, d = u * length;
      let x = start.x + d * Math.cos(start.heading), z = start.z + d * Math.sin(start.heading);
      if (p.turn) {
        const h = start.heading + p.turn * RAD * u, radius = p.r * Math.sign(p.turn);
        x = start.x + radius * (Math.sin(h) - Math.sin(start.heading)); z = start.z + radius * (Math.cos(start.heading) - Math.cos(h));
      }
      // Gentle crests, a gallery S-bend and tapered widths vary the driving rhythm.
      // The envelope is flat at both ends, retaining the authored crossing and join positions.
      const profile = p.profile || {}, v = Math.max(0, (u - (profile.begin || 0)) / (1 - (profile.begin || 0)));
      const envelope = Math.sin(Math.PI * v) ** 2, weave = (profile.weave || 0) * envelope * Math.sin(2 * Math.PI * v);
      x -= Math.sin(start.heading) * weave; z += Math.cos(start.heading) * weave;
      const flags = p.jump ? ['jump', 'open'] : p.field ? ['open'] : p.edge && d >= 25 && d < length - 25 ? ['rail' + (p.edge === 'L' ? 'R' : 'L')] : ['rail'];
      if (!nodes.length) flags.unshift('start');
      nodes.push({ p: [round(x), round(y0 + (p.y - y0) * (p.kick ? u * u : p.linear ? u : smooth(u)) + (profile.crest || 0) * envelope), round(z)],
        roll: p.turn ? round(Math.sign(p.turn) * 10 * Math.min(1, d / 30, (length - d) / 30)) : round((profile.bank || 0) * envelope * Math.sin(2 * Math.PI * v)),
        w: p.stepWidth ? 32 + 7 * Math.min(2, Math.floor(u * 810 / 360)) : round(p.w - (profile.pinch || 0) * envelope), flags, speed: 1,
        surface: p.surface || 'glass', ...(p.flow ? { flow: p.flow } : {}) });
    }
    y0 = p.y;
  });
  const node = (id, u = 0) => {
    const i = BOUDOIR_PIECES.findIndex(p => p.id === id), count = boudoirSegmentCount(BOUDOIR_PIECES[i]);
    return starts[i] + Math.min(count - 1, Math.round(count * u));
  };
  const section = (p, i) => ({ id: p.id === 'bath-stream' ? 'coolant-canal' : p.id, from: starts[i], to: starts[i + 1] ?? nodes.length,
    act: i < 7 ? 'induction' : i < 26 ? 'deepening' : 'release', palette: i < 7 ? 'dawn' : i < 26 ? 'deep' : 'afterglow',
    sky: i < 7 ? 'spiral-slow' : i < 26 ? 'spiral-deep' : 'spiral-wide', bandSpeed: .7 });
  const routes = [35, 395].map((angle, i) => {
    const from = node('wardrobe-descent', angle / 810), to = from + 4;
    for (let j = from; j < to; j++) nodes[j].flags = ['railL'];
    return { id: 'wardrobe-cut-' + (i + 1), kind: 'drop', side: 'R', from, to,
      land: { from: node('wardrobe-descent', (angle + 330) / 810), to: node('wardrobe-descent', (angle + 450) / 810) },
      pad: { node: node('wardrobe-descent', (angle + 395) / 810), x: 3 } };
  });
  const fieldFrom = node('compacts'), fieldTo = node('compacts', 2 / 3);
  for (let i = fieldTo; i < node('compacts-exit'); i++) nodes[i].flags = ['rail'];
  const fields = [{ id: 'boudoir-compacts', from: fieldFrom, to: fieldTo, lanes: [-9, 0, 9],
    stones: Array.from({ length: 7 }, (_, i) => ({ id: 'compact-' + (i + 1), node: fieldFrom, ds: i * 29, x: 0, w: 32, len: 26 })) }];
  const track = { format: 'glassroad-track', version: 1, id: 'boudoir', name: { en: 'Boudoir' }, author: 'ccl', created: '2026-10-24',
    loop: true, laps: 2, units: 'm', nodes, sections: BOUDOIR_PIECES.map(section),
    checkpoints: [0, node('drawer-hall'), node('shelf-climb', .5), node('shelf-gallery'), node('wardrobe-bridge'), node('wardrobe-descent'), node('wardrobe-out'), node('bath-stream'), node('compacts-exit'), node('mirror-approach'), node('home')],
    spawn: { node: 0, grid: [[-5, -5], [5, -9], [-5, -13], [5, -17], [-5, -21], [5, -25], [-5, -29], [5, -33]] },
    items: [], boosts: [], jumps: [{ node: node('mirror-gap'), airLen: 24, height: 20 }], routes, fields, courts: [], hazards: [], media: [],
    ai: { lines: [], pace: [1, 1.05, 1.1] }, beat: { bpm: null, offsetMs: 0, map: [] }, limits: { fallY: -55, edgeGraceM: 1.2, edgeGraceS: .8 } };
  for (const [id, u] of [['vanity-grid', .55], ['drawer-hall', .2], ['shelf-climb', .6], ['shelf-gallery', .5], ['upper-gallery', .5], ['wardrobe-bridge', .2], ['wardrobe-descent', .7], ['bath-approach', .5], ['bath-stream', .25], ['compacts-exit', .5], ['home', .5]])
    for (const x of [-7, -2.5, 2.5, 7]) track.items.push({ node: node(id, u), x, kind: 'box' });
  for (const [id, x] of [['powder-return', 3], ['shelf-climb', -3], ['wardrobe-descent', 4], ['bath-stream', -4]])
    for (let i = 0; i < 5; i++) track.items.push({ node: node(id, .35), x, ds: i * 4, kind: 'bubble' });
  const loaded = loadTrack(track, { aerials: false, boostSafety: false, courseShape: false, ramps: false }), at = (id, d = 0) => loaded.nodeS[node(id)] + d;
  track.forks = makeBoudoirForks(loaded, node);
  track.boosts = makeBoudoirBoosts(loadTrack(track, { aerials: false, boostSafety: false, courseShape: false, ramps: false }));
  for (const pad of track.boosts) if (!nodes[pad.node].flags.includes('boost')) nodes[pad.node].flags.push('boost');
  track.waterSpills = [{ fromS: at('bath-stream', 100), toS: at('bath-stream', 150), side: -1, dropM: 36 },
    { fromS: at('bath-turn', 70), toS: at('bath-turn', 105), side: 1, dropM: 40 },
    { fromS: at('bath-chute', 20), toS: at('bath-chute', 55), side: 1, dropM: 34 }];
  const result = validate(track); if (!result.ok || result.warnings.length) throw Error(JSON.stringify(result));
  return track;
}
export function formatBoudoir(track) {
  return '{\n' + Object.entries(track).map(([key, value]) => {
    if (key !== 'nodes') return '  ' + JSON.stringify(key) + ': ' + JSON.stringify(value);
    return '  "nodes": [\n' + Array.from({ length: Math.ceil(value.length / 3) }, (_, i) => '    ' + value.slice(i * 3, i * 3 + 3).map(JSON.stringify).join(',')).join(',\n') + '\n  ]';
  }).join(',\n') + '\n}\n';
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const json = buildBoudoir(), track = loadTrack(json), layout = getBoudoirLayout(track);
  if (!process.argv.includes('--check')) writeFileSync(new URL('../tracks/boudoir.json', import.meta.url), formatBoudoir(json));
  console.log(JSON.stringify({ nodes: track.nodeCount, lengthM: track.lengthM, ...layout }, null, 2));
}
