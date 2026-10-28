// node dev/gen-goonworks.mjs [--check] [--out file] builds the complete compact funhouse course.
import { writeFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { validate } from '../track/schema.js';
import { loadTrack } from '../track/load.js';
import { GOONWORKS_PIECES, goonworksPieceLength, goonworksSegmentCount, walkGoonworks, getGoonworksLayout } from '../track/goonworks-layout.js';
const RAD = Math.PI / 180, smooth = u => u * u * (3 - 2 * u), round = x => +x.toFixed(3) || 0;
export function buildGoonworks() {
  const plan = walkGoonworks(GOONWORKS_PIECES), nodes = [], spans = {}; let y0 = 0;
  if (Math.hypot(plan.x, plan.z) > 1e-6) throw Error('GOONWORKS is not closed');
  GOONWORKS_PIECES.forEach((p, i) => {
    const st = plan.starts[i], len = goonworksPieceLength(p), count = goonworksSegmentCount(p), from = nodes.length;
    for (let k = 0; k < count; k++) {
      const u = k / count, d = u * len, angle = (p.turn || 0) * RAD, sign = Math.sign(angle), h = st.heading + angle * u;
      const grow = sign * ((p.rEnd || p.r) - p.r) * smooth(u);
      const x = p.turn ? st.x + sign * p.r * (Math.sin(h) - Math.sin(st.heading)) + grow * Math.sin(h) : st.x + Math.cos(st.heading) * d;
      const z = p.turn ? st.z + sign * p.r * (Math.cos(st.heading) - Math.cos(h)) - grow * Math.cos(h) : st.z + Math.sin(st.heading) * d;
      const flags = p.jump ? ['jump', 'open'] : p.field ? ['open'] : ['rail'];
      if (!nodes.length) flags.unshift('start');
      if (p.id === 'pinball-ramp' || p.id === 'pinball-gap') flags.push('glide');
      nodes.push({ p: [round(x), round(y0 + (p.y - y0) * (p.kick ? u * u : p.linear ? u : smooth(u))), round(z)],
        roll: p.turn ? round(sign * (p.roll ?? 10) * Math.min(1, d / 30, (len - d) / 30)) : 0,
        w: round(p.wFrom ? p.wFrom + (p.w - p.wFrom) * smooth(u) : p.w), flags, speed: 1, surface: p.surface || 'glass' });
    }
    spans[p.id] = { from, to: nodes.length, group: p.group }; y0 = p.y;
  });
  const node = (id, u = 0) => spans[id].from + Math.min(spans[id].to - spans[id].from - 1, Math.round((spans[id].to - spans[id].from) * u));
  const groups = [...new Set(GOONWORKS_PIECES.map(p => p.group))];
  const track = { format: 'glassroad-track', version: 1, id: 'goonworks', name: { en: 'GOONWORKS: The Rabbit Hole' },
    author: 'ccl', created: '2026-10-24', loop: true, laps: 2, units: 'm', nodes,
    sections: groups.map((id, i) => { const rows = Object.values(spans).filter(s => s.group === id); return { id,
      from: rows[0].from, to: rows.at(-1).to, act: i < 4 ? 'induction' : i < 8 ? 'deepening' : 'release',
      palette: 'deep', sky: 'spiral-deep', bandSpeed: .45 }; }),
    checkpoints: [0, node('grotto-gallery'), node('mill-hall'), node('quiz-ballroom'), node('ribbon-braid', .5),
      node('leapfrog-approach'), node('pinball-table'), node('fold-overlook'), node('finish-underpass'), node('finish-crossing')],
    spawn: { node: 0, grid: [[-4,-5],[4,-9],[-4,-13],[4,-17],[-4,-21],[4,-25],[-4,-29],[4,-33]] },
    items: [], boosts: [], jumps: [{ node: node('pinball-gap'), airLen: 20, height: 26 }], routes: [], fields: [], courts: [], hazards: [], media: [],
    ai: { lines: [], pace: [1, 1.05, 1.1] }, beat: { bpm: null, offsetMs: 0, map: [] },
    limits: { fallY: -45, edgeGraceM: 1.2, edgeGraceS: .8 } };
  const base = loadTrack(track, { aerials: false, boostSafety: false, courseShape: false }), nearest = s => base.nodeS.slice(0, nodes.length).reduce((best, v, i) => Math.abs(v-s)<Math.abs(base.nodeS[best]-s)?i:best,0);
  for (const id of ['start-terrace','grotto-gallery','mill-hall','quiz-ballroom','ribbon-braid','leapfrog-exit','pinball-table','fold-overlook','finish-crossing'])
    for (const x of [-6,-2,2,6]) track.items.push({ node: node(id, .5), x, kind: 'box' });
  // Side-lane chains reward holding a line while leaving the centre free to coast.
  // The last pad clears each timed toy or shortcut lip by at least 120 m: boost holds a speed floor even under braking.
  const padRun = (id, offsetM, count, x, spacingM = 30) => {
    const s0 = base.nodeS[spans[id].from];
    for (let i = 0; i < count; i++) {
      const n = nearest(s0 + offsetM + i * spacingM);
      const len = 10, s = base.nodeS[n];
      // Broad pads keep both the central coast lane and six metres of outer recovery room.
      const half = Math.min(...[0,2.5,5,7.5,len].map(d => base.frameAt(s+d).w/2));
      const w = round(Math.min(9, half-2.6-6.1)), lane = round(Math.sign(x)*(w/2+2.6));
      nodes[n].flags.push('boost'); track.boosts.push({ node: n, x: lane, w, len });
    }
  };
  padRun('rabbit-exit', 5, 3, -6);
  padRun('mill-approach', 0, 2, 6, 20);
  padRun('quiz-approach', 0, 2, -6, 20);
  padRun('ribbon-braid', 140, 3, -6);
  padRun('ribbon-braid', 650, 3, -6);
  padRun('pinball-approach', 10, 3, -6);
  padRun('fold-overlook', 10, 2, 6);
  padRun('fold-runout', 40, 2, -6);
  padRun('home-straight', 40, 4, 6);
  const from = node('leapfrog-field'), to = node('leapfrog-field', 2 / 3), s0 = base.nodeS[from], s1 = base.nodeS[to], stones = [];
  for (let i = to; i < node('leapfrog-exit'); i++) nodes[i].flags = ['rail'];
  const count = 5, gap = 10, pitch = (s1 - s0 + gap) / count;
  for (let i = 0; i < count; i++) {
    const s = s0 + i * pitch, n = nearest(s), anchor = base.nodeS[n] > s ? n - 1 : n;
    stones.push({ id: 'leapfrog-' + i, node: anchor, ds: round(s-base.nodeS[anchor]), x: 0, w: 30,
      len: round(i === count-1 ? s1-s : pitch-gap) });
  }
  track.fields.push({ id: 'goonworks-leapfrog', from, to, lanes: [-8,0,8], platforming: true, stones });
  const descent = spans['fold-descent'], turns = descent.to - descent.from;
  for (const [i, phase] of [.06,.2].entries()) {
    const from = descent.from + Math.round(turns * phase), to = from + 4, target = from + Math.round(turns * 2 / 3);
    for (let n = from; n < to; n++) nodes[n].flags = ['railR'];
    track.routes.push({ id: 'grand-fold-cut-' + (i+1), kind: 'drop', side: 'L', from, to,
      land: { from: target-3, to: Math.min(descent.to-1,target+13) }, pad: { node: target+6, x: -4 } });
  }
  const result = validate(track); if (!result.ok || result.warnings.length) throw Error(JSON.stringify(result));
  return track;
}
export function formatGoonworks(track) {
  const lines = ['{'];
  for (const [key, value] of Object.entries(track)) {
    if (key === 'nodes') lines.push('  "nodes": [', ...Array.from({ length: Math.ceil(value.length/3) }, (_,i) => '    '+value.slice(i*3,i*3+3).map(JSON.stringify).join(',')+(i*3+3<value.length?',':'')), '  ],');
    else lines.push('  '+JSON.stringify(key)+': '+JSON.stringify(value)+',');
  }
  lines[lines.length-1] = lines.at(-1).slice(0,-1); return lines.join('\n')+'\n}\n';
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const json = buildGoonworks(), track = loadTrack(json), layout = getGoonworksLayout(track);
  const arg = process.argv.indexOf('--out'), out = arg >= 0 ? process.argv[arg+1] : fileURLToPath(new URL('../tracks/goonworks.json', import.meta.url));
  if (!process.argv.includes('--check')) writeFileSync(out, formatGoonworks(json));
  console.log(JSON.stringify({ nodes: track.nodeCount, lengthM: track.lengthM, bounds: layout.bounds,
    sections: layout.sections.map(s => ({ id:s.id,s0:Math.round(s.s0),s1:Math.round(s.s1) })) },null,2));
}
