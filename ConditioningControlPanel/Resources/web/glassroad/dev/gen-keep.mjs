// node dev/gen-keep.mjs [--check] [--out path] regenerates the original Keep course.
import { writeFileSync } from 'node:fs';
import { pathToFileURL, fileURLToPath } from 'node:url';
import { validate } from '../track/schema.js';
import { loadTrack } from '../track/load.js';
import { KEEP_PIECES, keepPieceLength, keepSegmentCount, walkKeep, getKeepLayout } from '../track/keep-layout.js';
const smooth = (u) => u * u * (3 - 2 * u), round = (x) => +x.toFixed(3), RAD = Math.PI / 180;
export function buildKeep() {
  const plan = walkKeep(KEEP_PIECES), nodes = [], starts = [];
  if (Math.hypot(plan.x, plan.z) > 1e-7) throw Error('Keep does not close');
  let y0 = 0;
  KEEP_PIECES.forEach((p, i) => {
    starts.push(nodes.length);
    const st = plan.starts[i], count = keepSegmentCount(p), len = keepPieceLength(p);
    for (let k = 0; k < count; k++) {
      const u = k / count, d = u * len;
      let x = st.x + Math.cos(st.heading) * d, z = st.z + Math.sin(st.heading) * d;
      if (p.turn) {
        const sign = Math.sign(p.turn), h = st.heading + p.turn * RAD * u;
        x = st.x + sign * p.r * (Math.sin(h) - Math.sin(st.heading));
        z = st.z + sign * p.r * (Math.cos(st.heading) - Math.cos(h));
      }
      const bank = p.turn ? Math.sign(p.turn) * 12 * Math.min(1, d / 25, (len - d) / 25) : 0;
      nodes.push({ p: [round(x), round(y0 + (p.y - y0) * smooth(u)), round(z)],
        roll: round(bank), w: p.w, flags: nodes.length ? ['rail'] : ['start', 'rail'], speed: 1, surface: 'glass' });
    }
    y0 = p.y;
  });
  const node = (id, u = 0) => {
    const i = KEEP_PIECES.findIndex((p) => p.id === id);
    return starts[i] + Math.min(keepSegmentCount(KEEP_PIECES[i]) - 1, Math.round(keepSegmentCount(KEEP_PIECES[i]) * u));
  };
  const section = (id, from, to, act, bandSpeed) => ({ id, from, to, act, palette: 'deep', sky: 'spiral-deep', bandSpeed });
  const items = [], boosts = [];
  for (const [id, u] of [['forecourt', .55], ['crown-walk', .45], ['west-approach', .45], ['home', .35]])
    for (const x of [-6, -2, 2, 6]) items.push({ node: node(id, u), x, kind: 'box' });
  for (const [id, x] of [['east-ascent', 3], ['north-tower', 3], ['west-tower', 3]])
    for (let k = 0; k < 5; k++) items.push({ node: node(id, .35), x, kind: 'bubble', ds: k * 3 });
  for (const id of ['outer-exit', 'crown-walk', 'home']) {
    const n = node(id, .5); nodes[n].flags.push('boost'); boosts.push({ node: n, x: 0, w: 4, len: 6 });
  }
  const track = {
    format: 'glassroad-track', version: 1, id: 'keep', name: { en: 'The Keep' }, author: 'ccl', created: '2026-10-24',
    loop: true, laps: 3, units: 'm', nodes,
    sections: [
      section('outer-bailey', 0, node('high-gate'), 'induction', .55),
      section('crown-ramparts', node('high-gate'), node('dogleg-in'), 'deepening', .75),
      section('moat-descent', node('dogleg-in'), nodes.length, 'release', .6),
    ],
    checkpoints: [0, node('east-ascent'), node('north-tower'), node('west-descent'), node('west-tower')],
    spawn: { node: 0, grid: [[-4, -5], [4, -9], [-4, -13], [4, -17], [-4, -21], [4, -25], [-4, -29], [4, -33]] },
    items, boosts, jumps: [], routes: [], courts: [], fields: [], hazards: [], media: [],
    ai: { lines: [], pace: [1, 1.05, 1.1] }, beat: { bpm: null, offsetMs: 0, map: [] },
    limits: { fallY: -30, edgeGraceM: 1.2, edgeGraceS: .8 },
  };
  const result = validate(track);
  if (!result.ok || result.warnings.length) throw Error(JSON.stringify(result));
  return track;
}
export function formatKeep(track) {
  const lines = ['{'];
  for (const [key, value] of Object.entries(track)) {
    if (key === 'nodes') lines.push('  "nodes": [', ...value.map((n, i) => '    ' + JSON.stringify(n) + (i + 1 < value.length ? ',' : '')), '  ],');
    else lines.push('  ' + JSON.stringify(key) + ': ' + JSON.stringify(value) + ',');
  }
  lines[lines.length - 1] = lines.at(-1).slice(0, -1);
  return lines.join('\n') + '\n}\n';
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const json = buildKeep(), track = loadTrack(json), layout = getKeepLayout(track);
  const arg = process.argv.indexOf('--out'), out = arg >= 0 ? process.argv[arg + 1] : fileURLToPath(new URL('../tracks/keep.json', import.meta.url));
  if (!process.argv.includes('--check')) writeFileSync(out, formatKeep(json));
  console.log(JSON.stringify({ nodes: track.nodeCount, lengthM: track.lengthM, ...layout }, null, 2));
}

