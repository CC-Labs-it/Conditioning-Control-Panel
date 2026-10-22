/* tests/sim-fake.mjs - a fake Track (CONTRACT.md section 5 shape) and drivers for the sim tests.
 * The road is a closed circle turning right, optionally banked by rollDeg, with per-segment flags
 * and surfaces; query() is analytic. A big node count makes it near straight.
 */
import { readFileSync } from 'node:fs';
import { v3cross, v3dot, v3addScaled, v3norm, wrap, qlook, qaxis, qrotate } from '../core/math.js';
import { createFeel } from '../core/feel.js';
import { createActions } from '../core/actions.js';
import { S, createState, setPos, setQuat, setVel } from '../sim/state.js';
import { step } from '../sim/kart.js';
import { updateRespawn } from '../sim/respawn.js';

export const DT = 1 / 60;
const FEEL = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
export const makeFeel = (over = {}) => {
  const f = createFeel(FEEL);
  for (const [k, v] of Object.entries(over)) f.set(k, v);
  return f;
};

export function fakeTrack({ nodes = 4000, spacing = 10, w = 14, rollDeg = 0, flags = () => ['rail'], surface = () => 'glass',
  boosts = [], jumps = [], checkpoints = [0, 1000, 2000, 3000], laps = 3, fallY = -30 } = {}) {
  const L = nodes * spacing, R = L / (2 * Math.PI), roll = rollDeg * Math.PI / 180;
  const nodeS = Float64Array.from({ length: nodes + 1 }, (_, i) => i * spacing);
  function frameAt(s, out = {}) {
    s = wrap(s, L);
    const th = s / R, c = Math.cos(th), sn = Math.sin(th);
    const t = [c, 0, sn];
    const n = [-sn * Math.sin(roll), Math.cos(roll), c * Math.sin(roll)];
    const seg = Math.min(nodes - 1, Math.floor(s / spacing));
    return Object.assign(out, { p: [R * sn, 0, R - R * c], t, n, b: v3cross([0, 0, 0], t, n), roll, w,
      flags: flags(seg), surface: surface(seg), seg, segT: s / spacing - seg });
  }
  function query(px, py, pz, hint = -1, out = {}) {
    const s = wrap(Math.atan2(px, R - pz), 2 * Math.PI) * R;
    const f = frameAt(s, {});
    const rel = [px - f.p[0], py - f.p[1], pz - f.p[2]];
    const lateral = v3dot(rel, f.b), height = v3dot(rel, f.n), edgeDist = w / 2 - Math.abs(lateral);
    const gapSeg = f.flags.includes('jump');
    const railed = !gapSeg && (f.flags.includes('rail') || f.flags.includes(lateral >= 0 ? 'railR' : 'railL'));
    return Object.assign(out, { seg: f.seg, segT: f.segT, s, lateral, height, normal: f.n, onRoad: !gapSeg && edgeDist >= 0,
      edgeDist, surface: f.surface, flags: f.flags, railed, p: v3addScaled([0, 0, 0], f.p, f.b, lateral) });
  }
  const sections = [{ id: 'a', fromS: 0, toS: L / 2 }, { id: 'b', fromS: L / 2, toS: L }];
  return {
    id: 'fake', laps, loop: true, lengthM: L, nodeCount: nodes, nodeS, sections,
    sectionAt: (s) => (wrap(s, L) < L / 2 ? 0 : 1),
    checklines: checkpoints.map((node, idx) => ({ idx, node, s: nodeS[node] })),
    boosts: boosts.map((b) => ({ ...b, s: nodeS[b.node] })),
    jumps: jumps.map((j) => ({ ...j, s: nodeS[j.node], landS: nodeS[j.land] })),
    limits: { fallY, edgeGraceM: 1.2, edgeGraceS: 0.8 },
    spawnPose(slot) { const s = wrap(-4 - 2 * slot, L), f = frameAt(s); return { p: f.p, q: qlook([0, 0, 0, 1], f.t, f.n), seg: f.seg, s, lateral: 0 }; },
    nodeFlags: (i) => flags(i), frameAt, query,
  };
}

/** A kart on the road at s, lateral, heading (deg, + right of travel), forward speed and engine. */
export function place(track, { s = 100, lateral = 0, height = 0, headingDeg = 0, speed = 0, engine = speed, airborne = false } = {}) {
  const st = createState(), f = track.frameAt(s, { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] });   // a real track writes into these arrays
  const F = qrotate([0, 0, 0], qaxis([0, 0, 0, 1], f.n, -headingDeg * Math.PI / 180), f.t);
  const p = v3addScaled([0, 0, 0], f.p, f.b, lateral);
  setPos(st, v3addScaled(p, p, f.n, height)); setQuat(st, qlook([0, 0, 0, 1], v3norm(F, F), f.n));
  setVel(st, [F[0] * speed, F[1] * speed, F[2] * speed]);
  st[S.engineV] = engine; st[S.s] = s; st[S.seg] = f.seg; st[S.latchSeg] = f.seg; st[S.lateral] = lateral;
  st[S.hopT] = 1e3; st[S.grounded] = airborne ? 0 : 1; st[S.airborne] = airborne ? 1 : 0; st[S.height] = height;
  return st;
}

/** Event recorder with a sim clock. */
export function recorder() {
  const r = { t: 0, list: [], emit(name, payload = {}) { r.list.push({ name, payload, t: r.t }); },
    names: () => r.list.map((e) => e.name), all: (n) => r.list.filter((e) => e.name === n), first: (n) => r.list.find((e) => e.name === n) };
  return r;
}

export const acts = (o = {}) => Object.assign(createActions(), o);

/** Step kart + respawn for `seconds` (or until `until(st)` is true); returns the sim time spent. */
export function run(st, track, feel, a, seconds, rec, until = null) {
  const n = Math.round(seconds / DT);
  for (let i = 0; i < n; i++) {
    rec.t += DT;
    step(st, a, track, feel, DT, rec);
    updateRespawn(st, track, feel, DT, rec);
    a.hopPressed = false;
    if (until && until(st)) return (i + 1) * DT;
  }
  return n * DT;
}
