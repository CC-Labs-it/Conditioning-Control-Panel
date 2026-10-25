/* Authored main-road boost runs. Each painted pad is also a real physics trigger. */
import { getBoudoirLayout } from './boudoir-layout.js';
import { getBoudoirSetpieces } from './boudoir-setpieces.js';

// Starts are measured from the named landmark, then snapped to real spline nodes.
// Runs after traps reward a clean exit. Side runs leave the ordinary line unboosted.
// Parallel runs offer a real line choice. The tight shelf/wardrobe helices stay unboosted:
// actual steering checks at the 36 m/s pad floor missed pads or reached the rail there.
const RUNS = [
  ['vanity-left', 'vanity-grid', [60, 80, 100], -11, 'dry conveyor bypass'],
  ['vanity-right', 'vanity-grid', [60, 80, 100], 11, 'alternate dry conveyor bypass'],
  ['perfume-dry', 'perfume-court', [20, 40, 60], 13, 'permanent dry perfume bypass'],
  ['gallery-runout', 'upper-runout', [15, 35, 55], 0, 'straight gallery recovery'],
  ['wardrobe-dry', 'wardrobe-bridge', [35, 55, 75], -12, 'dry conveyor and perfume bypass'],
  ['bath-entry', 'bath-stream', [40, 60, 80], -4, 'grounded channel acceleration'],
  ['bath-entry-right', 'bath-stream', [40, 60, 80], 4, 'alternate grounded channel line'],
  ['bath-current', 'bath-stream', [200, 220, 240], 4, 'alternate channel line before the broad turn'],
  ['bath-current-left', 'bath-stream', [200, 220, 240], -4, 'left channel line before the broad turn'],
  ['compact-exit-left', 'compacts-exit', [60, 80, 100], -8, 'recovery after the compact field and its press'],
  ['mirror-runup', 'mirror-approach', [10, 30, 50], 0, 'press recovery with an unboosted jump approach'],
  ['mirror-landing-exit', 'mirror-landing', [80, 100, 120], 0, 'recovery after the landing settles'],
  ['home-straight', 'home', [20, 40, 60], 0, 'finish straight'],
  ['home-right', 'home', [20, 40, 60], 8, 'alternate finish line'],
];

/** Shared authoring metadata lets tests drive every intended sequence and its real exclusions. */
export function getBoudoirBoostPlan(track) {
  const layout = getBoudoirLayout(track);
  if (!layout) return { pads: [], runs: [], exclusions: [] };
  const stations = getBoudoirSetpieces(track, layout), exclusions = [];
  const exclude = (id, kind, s0, s1) => exclusions.push({ id, kind, s0, s1 });
  // Braking below 22 m/s takes 107 m after a dry pad, or 126 m in the bath current.
  // Reserve 125 m on dry road and another 10 m for wet pads below.
  for (const h of [...stations.presses, ...stations.sweepers, ...stations.mirrorGates])
    exclude(h.id, h.kind, h.s - 125 - h.depthM / 2, h.s + h.depthM / 2 + 18);
  for (const f of track.forks || []) {
    exclude(f.id + '-entry', 'fork', f.fromS - 125, f.fromS + 80);
    exclude(f.id + '-merge', 'fork', f.toS - 80, f.toS + 45);
  }
  for (const r of track.routes || []) {
    exclude(r.id + '-opening', 'drop', r.openS0 - 125, r.openS1 + 35);
    exclude(r.id + '-catch', 'landing', r.land.fromS - 20, r.land.toS + 25);
  }
  for (const f of track.fields || []) exclude(f.id, 'field', f.fromS - 125, f.toS + 18);
  for (const j of track.jumps || []) exclude('mirror-jump', 'jump', j.s - 125, j.landS + 60);
  const nearestNode = s => {
    let best = 0;
    for (let n = 1; n < track.nodeCount; n++) if (Math.abs(track.nodeS[n] - s) < Math.abs(track.nodeS[best] - s)) best = n;
    return best;
  };
  const runs = RUNS.map(([id, landmark, offsets, x, purpose]) => {
    const span = layout.landmarks.find(p => p.id === landmark);
    const pads = offsets.map(offset => {
      const node = nearestNode(span.s0 + offset);
      return { node, s: track.nodeS[node], x, w: 7.5, len: 10, runId: id };
    });
    return { id, landmark, purpose, pads };
  });
  const pads = runs.flatMap(run => run.pads).sort((a, b) => a.s - b.s);
  for (const p of pads) {
    const wetRunoff = track.frameAt(p.s).surface === 'stream' ? 10 : 0;
    const hit = exclusions.find(e => p.s + p.len > e.s0 - wetRunoff && p.s < e.s1);
    if (hit) throw Error(`Boudoir boost ${p.runId} at ${p.s.toFixed(1)} enters ${hit.id} braking space`);
    for (let ds = 0; ds <= p.len; ds += 2) {
      const f = track.frameAt(p.s + ds);
      if (Math.abs(p.x) + p.w / 2 + 2.5 > f.w / 2 || f.flags.includes('jump'))
        throw Error(`Boudoir boost ${p.runId} exceeds supported road at ${(p.s + ds).toFixed(1)}`);
    }
    for (const c of stations.conveyors) if (p.s + p.len > c.s0 && p.s < c.s1 && Math.abs(p.x - c.x) < (p.w + c.w) / 2 + .7)
      throw Error(`Boudoir boost ${p.runId} crosses the active conveyor`);
    for (const cloud of layout.perfume) if (p.s + p.len > cloud.s0 && p.s < cloud.s1 && Math.abs(p.x - cloud.x) < (p.w + cloud.w) / 2 + .7)
      throw Error(`Boudoir boost ${p.runId} crosses the perfume cloud`);
  }
  return { pads, runs, exclusions };
}

export function makeBoudoirBoosts(track) {
  return getBoudoirBoostPlan(track).pads.map(({ node, x, w, len }) => ({ node, x, w, len }));
}
