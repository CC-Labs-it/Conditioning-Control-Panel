// dev/gen-threshold.mjs - builds tracks/threshold.json (Threshold, Track JSON v1).
// Run: node dev/gen-threshold.mjs [--check] [--out <path>]   (--check prints the report and writes nothing)
//
// Six sections: the original stacked course opens into tidal galleries, a high observatory spiral,
// a flowing descent and a lower basin. The start straight runs under the
// induction bridge, the road spirals up round a tall column onto the top deck, drives a classic wheel
// (a vertical 360 loop), drops back down a coil whose open edge is the drop shortcut, takes the jump and a
// chain of two hoops at ground level and comes home.
//
// A turtle walks pieces in the XZ plane; heading 0 = +X and a positive turn is a RIGHT turn (+X heading
// turns toward +Z, which is the right side with +Y up). A `loop` piece is a vertical 360 of radius r whose
// exit sits `off` metres to the right (negative: left) of its entry, heading unchanged, so the road never
// meets itself. Two straights marked `auto` get their lengths solved so the course closes exactly.
// Then it checks spacing, plan overlap (allowed only with >= 10 m vertical separation; a loop's own body
// is checked by its offset instead), length, the jump at 60% to 124% of top speed, the drop routes, and
// that every open edge over a road AHEAD in the lap belongs to a registered route.
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { validate } from '../track/schema.js';
import { createScalar } from '../track/spline.js';
import { densifyBoosts } from '../track/boostPlan.js';

const HERE = dirname(fileURLToPath(import.meta.url));
const argOut = process.argv.indexOf('--out');
const OUT = argOut > 0 ? process.argv[argOut + 1] : join(HERE, '..', 'tracks', 'threshold.json');
const DEG = Math.PI / 180;

// y = target height at the piece end; ease: smooth | lin | kick (grade grows to the end)
export const PIECES = [
  // induction: ground level, the climb onto deck 1, the bridge over the start straight
  { id: 'start', len: 90, y: 0, w: 36, sec: 0, pad: { at: 0.45, x: 0 } },
  // water wave 1: the first sweep from u 0.15 and all of L2 are a STREAM; the current runs +5 m/s along and
  // -2.5 m/s to the left (the outside of the right turn), eased in over the first two stream nodes and out
  // over the last two (STREAM_RAMP). The hairpin after it stays glass.
  { id: 'sweepA', turn: 90, r: 45, roll: 25, y: 1, sec: 0, stream: { from: 0.15, flow: [5, -2.5] } },
  { id: 'L2', len: 24, y: 3, sec: 0, stream: { from: 0, flow: [5, -2.5] } },
  { id: 'hairpin', turn: 180, r: 36, roll: 28, y: 14, sec: 0 },
  // Give the enlarged stepping islands their own bridge district. The higher approach clears
  // the returning ground road by ten metres while the open field sits beyond that crossing.
  { id: 'bridge', len: 324, y: 18, sec: 0, pad: { at: 0.35, x: -3 } },
  // A long excursion leaves the compact induction stack before returning above it.
  { id: 'prism-approach', len: 160, y: 48, w: 40, sec: 3 },
  { id: 'tidal-in', turn: 90, r: 90, roll: 16, y: 34, w: 34, sec: 3 },
  { id: 'tidal-gallery', len: 450, y: 34, w: 48, sec: 3, surface: 'stream', flow: [4, 0] },
  { id: 'tidal-out', turn: 90, r: 90, roll: 18, y: 38, w: 34, sec: 3 },
  { id: 'prism-run', len: 300, y: 48, w: 40, sec: 4 },
  { id: 'observatory-spiral', turn: -360, r: 110, roll: 22, y: 84, ease: 'lin', w: 34, sec: 4 },
  { id: 'lens-gallery', len: 400, y: 84, w: 48, sec: 4 },
  { id: 'lens-out', turn: 90, r: 90, roll: 16, y: 84, w: 34, sec: 5 },
  { id: 'cascade-descent', len: 350, y: -16, w: 34, surface: 'stream', flow: [5, 0], sec: 5 },
  { id: 'cascade-return', turn: 90, r: 90, roll: 18, y: -16, w: 34, sec: 5 },
  { id: 'crescent', len: 500, y: -16, w: 34, surface: 'pool', sec: 5 },
  { id: 'rejoin', len: 100, y: 14, w: 20, sec: 5 },
  // deepening: up round the column onto the top deck, the wheel and its hoop, the dare, down the keyhole
  { id: 'column', turn: -360, r: 28, roll: 18, y: 28, ease: 'lin', w: 16.25, speed: 0.92, sec: 1 },
  { id: 'deck', len: 40, y: 28, w: 17.5, sec: 1, pad: { at: 0.55, x: 0 } },
  // The nested loops and dare retain their technical widths: expanding them closes real air gaps.
  { id: 'wheel', loop: true, r: 16, off: -20, y: 28, w: 14, sec: 1 },
  { id: 'link', len: 50, y: 28, w: 14, sec: 1 },
  { id: 'hoop', loop: true, r: 14, off: 20, y: 28, w: 14, sec: 1 },
  { id: 'deck2', len: 30, y: 28, w: 14, sec: 1, alongside: true },
  // the coil (the dare): one and a half turns whose radius widens 20 -> 28.75 -> 37.5,
  // c1 and c3 about one centre. c2 dives under deck2 (10 m clear) and c3 climbs back up OUTSIDE c1, the
  // same way round, its edge 2.5 m off c1's outer edge and about 5 m lower: carry your speed straight out of
  // c1 (a boost or a hop) and you clear the gap onto c3
  { id: 'c1', turn: 180, r: 20, w: 14, roll: 22, y: 26.5, ease: 'lin', speed: 0.9, sec: 1, alongside: true },
  { id: 'c2', turn: 180, r: 28.75, w: 14, roll: 20, y: 13, ease: 'dive', sec: 1, alongside: true },
  { id: 'c3', turn: 180, r: 37.5, w: 16, roll: 18, y: 23, sec: 1, alongside: true },
  // release: down the return lane, round under the deck and the column, the jump, home along the ground
  // water wave 1: the old auto L4a (y 23 -> 6) is now the approach (the lip in view), the CHUTE (a stream falling
  // 14 m over 24 m, max grade about 41 deg, flow 8 m/s along), the POOL (still water, 20 m wide, catches a fall
  // from the coil) and L4b, the closure straight. L4a and the chute still run alongside the coil.
  { id: 'L4a', len: 30, y: 21, w: 14, ease: 'lin', sec: 2, alongside: true },
  { id: 'chute', len: 24, y: 7, w: 14, ease: 'smooth', surface: 'stream', flow: [8, 0], speed: 1.3, sec: 2, alongside: true },
  { id: 'pool', len: 40, y: 6, w: 20, surface: 'pool', sec: 2 },
  { id: 'L4b', auto: true, y: 6, w: 14, sec: 2 },
  { id: 'sweepD', turn: 90, r: 40, roll: 25, y: 2, ease: 'lin', sec: 2 },
  { id: 'L4d', auto: true, y: 2, sec: 2, pad: { at: 0.86, x: 0 } },
  { id: 'kicker', len: 30, y: 5, ease: 'kick', sec: 2 },
  { id: 'gap', len: 12, y: -1, ease: 'lin', jump: true, sec: 2 },
  { id: 'landing', len: 24, y: -1, sec: 2 },
  { id: 'sweepE', turn: -90, r: 24, roll: 24, y: 0, speed: 0.9, sec: 2 },
  { id: 'sweepF', turn: -90, r: 24, roll: 24, y: 0, speed: 0.85, sec: 2 },
  { id: 'home', len: 60, y: 0, w: 36, sec: 2 },
];
const SECTIONS = [
  { id: 'induction', act: 'induction', palette: 'dawn', sky: 'spiral-slow', bandSpeed: 0.6 },
  { id: 'deepening', act: 'deepening', palette: 'deep', sky: 'spiral-deep', bandSpeed: 1.0 },
  { id: 'release', act: 'release', palette: 'afterglow', sky: 'spiral-wide', bandSpeed: 0.8 },
  { id: 'tidal-gallery', act: 'induction', palette: 'dawn', sky: 'spiral-wide', bandSpeed: 0.65 },
  { id: 'prism-observatory', act: 'deepening', palette: 'deep', sky: 'spiral-deep', bandSpeed: 0.85 },
  { id: 'cascade-return', act: 'release', palette: 'afterglow', sky: 'spiral-wide', bandSpeed: 0.7 },
];
// Ordinary lanes have room to correct a slide; nested loops and open shortcuts keep their real gaps.
const W = 32, TOP = 30, G = 28;
/** Loop clearance: the exit lane's edge keeps this much plan room from the entry lane's edge. */
const LOOP_SIDE_GAP_M = 4;
/** Lanes marked alongside keep at least this much plan gap between edges and this much height between them. */
const ALONGSIDE_GAP_M = 1.5, ALONGSIDE_DY_M = 2.5;
/** Six-section lap length, in metres (tests/gen-threshold.test.mjs holds the same window). */
const LENGTH_M = [5000, 6500];
/** Water: a stream run's flow ramps over its first and last STREAM_RAMP nodes (1/3, 2/3, 1 ... 1, 2/3, 1/3). */
const STREAM_RAMP = 3;
/** Water checks: the chute may not exceed this grade, the pool must be at least this long. */
const CHUTE_GRADE_DEG = 45, POOL_MIN_M = 35;

// Item box rows: piece, place along it (0..1), box x positions (metres, + right). 3 to 4 across, away
// from the jump (kicker to landing), the open corner and the loops; the checks below hold those margins.
const BOX_ROWS = [
  { piece: 'start', at: 0.8, xs: [-5, -1.7, 1.7, 5] },
  { piece: 'L2', at: 0.6, xs: [-3.5, 0, 3.5] },
  { piece: 'bridge', at: 0.2, xs: [-4.5, -1.5, 1.5, 4.5] },   // water wave 1: 0.12 sat where every kart still hugs the hairpin's outer rail
  { piece: 'tidal-gallery', at: .45, xs: [-8, -3, 3, 8] },
  { piece: 'prism-run', at: .7, xs: [-7, 0, 7] },
  { piece: 'lens-gallery', at: .5, xs: [-8, -3, 3, 8] },
  { piece: 'crescent', at: .5, xs: [-7, 0, 7] },
  { piece: 'L4a', at: 0.2, xs: [-3.5, 0, 3.5] },
  { piece: 'home', at: 0.6, xs: [-4.5, -1.5, 1.5, 4.5] },
];
// Pickup-bubble clusters: [ds metres past the node, x] per bubble, on the inside line of a bend.
const BUBBLE_CLUSTERS = [
  { piece: 'sweepA', at: 0.45, dots: [[0, 3], [3, 3.8], [6, 4.4], [9, 3.8], [12, 3]] },
  { piece: 'column', at: 0.55, dots: [[0, -2.5], [3, -3.2], [6, -3.6], [9, -3.2], [12, -2.5]] },
  { piece: 'L2', at: 0.1, dots: [[0, -2], [3, -1], [6, 0], [9, 1], [12, 2]] },
];
const ITEM_JUMP_CLEAR_M = 40, ITEM_OPEN_CLEAR_M = 30, ITEM_PAD_CLEAR_M = 8, ITEM_LOOP_CLEAR_M = 12;
// Drop shortcuts (polish 4): the rail on `side` of `piece` is open from `from` to `to` (0..1 of the piece);
// a kart that leaves that edge can land on `land` (landFrom..landTo of that piece) one deck lower. The pad
// is the landing marker (`at` of the land piece, x metres + right). The checks below hold the drop height
// and that the open edge sits over the landing road.
// Polish 6, the dare (owner 2026-10-23): the top deck's right side is open where the return lane runs
// alongside it about 8 m lower and 20 m across; a hop over the gap lands on the return lane and skips the
// keyhole. land may span pieces (land .. landEnd); drop is the allowed height range over the landing road.
const ROUTES = [
  { id: 'dare', kind: 'gap', side: 'L', piece: 'c1', from: 0.6, to: 0.99, land: 'c3', landFrom: 0.5, landTo: 0.98,
    pad: { at: 0.85, ds: 0, x: 0 }, drop: [3, 8], reachM: 6 },
];
// Falls (contract P6.2): open stretches over a road EARLIER in the lap; a kart that falls lands there and
// loses ground. side L | R | both.
// sweepF: the open corner keeps only its inside open (its outside railed: a boosted kart off the jump cannot
// slide off it, SIM 16:23)
const FALLS = [
  { piece: 'sweepF', side: 'L', from: 0, to: 1 },
];
// Stepping-stone fields (stones wave, BRIEF-STONES.md): from `from` to `to` of `piece` the deck is gone and the kart
// drives on distinct islands over the void, beyond the lower-road crossing.
// Three staggered lanes of stoneM-long stones with gapM between them (period stoneM + gapM), lane k's regular
// stones starting k * staggerM past the field start, so every s of the field has a stone in some lane. Every lane
// starts with a LEAD stone from the field start to its first regular stone's end (the deck hands over to a stone
// on every lane, root 2026-10-23: an entry void on a lane the kick cannot reach was a fall) and ends with a
// CLOSING stone that reaches FIELD_END_GAP_M before the deck (an exit void longer than a gap was a fall too), so
// stone lengths run FIELD_LEN_M (the renderer scales one island to each authored platform). The road eases to widthM
// over FIELD_RAMP_M before the field and back after it. Nodes inside carry `open` (no rails: the lips are the edges).
export const FIELDS = [
  // Start beyond the lower-road crossing so missed jumps never skip ahead in the lap.
  { id: 'bridge-stones', piece: 'bridge', from: 0.50, to: 0.76, lanes: [-18, 0, 18], stoneM: 10, gapM: 15, staggerM: 8, widthM: 50 },
];
const FIELD_RAMP_M = 20, FIELD_END_GAP_M = 2, FIELD_SPARE_M = 0.5, FIELD_GAP_M = [14.5, 15.5], FIELD_PAD_LEAD_M = 6;
/** Lead and closing islands have generous landing room around the regular ten-metre islands. */
const FIELD_LEN_M = [6, 34];
/** One lane of stones as [start, end] pairs from 0 (the field start) to lenM - FIELD_END_GAP_M (the closing edge). */
export function laneStones(lenM, phaseM, stoneM, gapM, endGapM = FIELD_END_GAP_M, lenRange = FIELD_LEN_M) {
  const end = lenM - endGapM, out = [[0, phaseM + stoneM]];
  for (let s = phaseM + stoneM + gapM; s + stoneM <= end + 1e-9; s += stoneM + gapM) out.push([s, s + stoneM]);
  const last = out[out.length - 1];
  if (end - last[1] >= gapM + lenRange[0]) out.push([last[1] + gapM, end]);      // a closing stone fits after one more gap
  else last[1] = end;                                                             // else the last stone reaches the closing edge
  return out.map(([a, b]) => [+a.toFixed(2), +b.toFixed(2)]);
}
const ROUTE_DROP_M = [10, 16];
/** A road counts as beneath an open edge when it lies this far below (metres). */
const BENEATH_M = [3, 40];
/** How far past an open lip (metres beyond the road edge) a fall or a hop can carry a kart. */
const HOP_REACH_M = [2, 6, 10];

const ease = (k, u) => (k === 'lin' ? u : k === 'kick' ? u * u : k === 'dive' ? 1 - (1 - u) * (1 - u) : u * u * (3 - 2 * u));
const smooth = (u) => u * u * (3 - 2 * u);

/** Loop body in the entry frame: forward, side (+ right) and up, at u in 0..1. */
export function loopLocal(p, u) {
  const th = 2 * Math.PI * u;
  return { f: p.r * Math.sin(th), side: p.off * smooth(u), up: p.r * (1 - Math.cos(th)) };
}
function loopLen(p) {
  let L = 0, a = loopLocal(p, 0);
  for (let k = 1; k <= 720; k++) {
    const b = loopLocal(p, k / 720);
    L += Math.hypot(b.f - a.f, b.side - a.side, b.up - a.up); a = b;
  }
  return L;
}
const pieceLen = (p) => (p.loop ? loopLen(p) : p.turn ? Math.abs(p.turn) * DEG * p.r : p.len);

// heading after each piece, and the plan displacement of a piece
function walkPlan(pieces) {
  let x = 0, z = 0, h = 0;
  const at = [];
  for (const p of pieces) {
    at.push({ x, z, h });
    if (p.loop) { x += -Math.sin(h) * p.off; z += Math.cos(h) * p.off; }
    else if (p.turn) {
      const s = Math.sign(p.turn), a = p.turn * DEG;
      // centre lies on the turn side: right of heading for s > 0
      const cx = x - s * Math.sin(h) * p.r, cz = z + s * Math.cos(h) * p.r;
      const h2 = h + a;
      x = cx + s * Math.sin(h2) * p.r; z = cz - s * Math.cos(h2) * p.r; h = h2;
    } else { x += Math.cos(h) * p.len; z += Math.sin(h) * p.len; }
  }
  return { x, z, h, at };
}

function solveAuto(pieces) {
  const autos = pieces.filter((p) => p.auto);
  if (autos.length !== 2) throw new Error('need exactly two auto straights');
  autos.forEach((p) => { p.len = 0; });
  const base = walkPlan(pieces);
  const i0 = pieces.indexOf(autos[0]), i1 = pieces.indexOf(autos[1]);
  const d0 = [Math.cos(base.at[i0].h), Math.sin(base.at[i0].h)], d1 = [Math.cos(base.at[i1].h), Math.sin(base.at[i1].h)];
  // translation is linear in auto lengths: base + a*d0 + c*d1 = 0
  const det = d0[0] * d1[1] - d0[1] * d1[0];
  if (Math.abs(det) < 1e-6) throw new Error('auto straights are parallel');
  const bx = -base.x, bz = -base.z;
  autos[0].len = (bx * d1[1] - bz * d1[0]) / det;
  autos[1].len = (d0[0] * bz - d0[1] * bx) / det;
  const end = walkPlan(pieces);
  return { closeErr: Math.hypot(end.x, end.z), headErr: ((end.h / DEG) % 360 + 360) % 360 };
}

function segCount(len) {
  let n = Math.max(1, Math.round(len / 11));
  if (len / n > 15) n++;
  if (len / n < 8 && n > 1) n--;
  return n;
}

export function build(pieces = PIECES) {
  const closure = solveAuto(pieces);
  const plan = walkPlan(pieces);
  const nodes = [], meta = [];
  let y0 = 0, sAcc = 0;
  pieces.forEach((p, pi) => {
    const L = pieceLen(p), n = p.loop ? Math.max(8, Math.round(L / 10.5)) : segCount(L), st = plan.at[pi];
    const y1 = p.y ?? y0;
    const fx = Math.cos(st.h), fz = Math.sin(st.h), rx = -Math.sin(st.h), rz = Math.cos(st.h);
    for (let k = 0; k < n; k++) {
      const u = k / n, d = u * L;
      let x, z, y;
      if (p.loop) {
        const q = loopLocal(p, u);
        x = st.x + fx * q.f + rx * q.side; z = st.z + fz * q.f + rz * q.side; y = y0 + q.up;
      } else {
        if (p.turn) {
          const s = Math.sign(p.turn);
          const cx = st.x - s * Math.sin(st.h) * p.r, cz = st.z + s * Math.cos(st.h) * p.r;
          const h2 = st.h + p.turn * DEG * u;
          x = cx + s * Math.sin(h2) * p.r; z = cz - s * Math.cos(h2) * p.r;
        } else { x = st.x + fx * d; z = st.z + fz * d; }
        y = y0 + (y1 - y0) * ease(p.ease || 'smooth', u);
      }
      let roll = 0;
      if (p.turn) {
        const ramp = Math.min(20, L / 3);
        roll = Math.sign(p.turn) * p.roll * Math.min(1, d / ramp, (L - d) / ramp);
      }
      const flags = p.jump ? ['jump'] : p.open ? ['open'] : p.loop ? ['rail', 'magnet'] : ['rail'];
      // Aperture Court opens out on the flat return, then gathers before the launch ramp.
      const courtWidth = p.id === 'L4d' ? W + (40 - W) * Math.min(1, d / 17, (L - d) / 20) : 0;
      // stones: the road widens to the field's width over FIELD_RAMP_M before it and narrows back after it
      const field = FIELDS.find((f) => f.piece === p.id);
      const fieldWidth = field ? W + (field.widthM - W) * Math.max(0, Math.min(1, (d - (field.from * L - FIELD_RAMP_M)) / FIELD_RAMP_M, (field.to * L + field.gapM + FIELD_RAMP_M - d) / FIELD_RAMP_M)) : 0;
      // water: a piece-wide surface (`surface`, optional `flow`) or a stream from `stream.from` with a ramped flow
      const onStream = p.stream && u >= p.stream.from - 1e-9;
      const surface = onStream ? 'stream' : p.surface || 'glass';
      const flow = onStream ? p.stream.flow.slice() : surface === 'stream' && p.flow ? p.flow.slice() : null;
      nodes.push({ p: [r2(x), r2(y), r2(z)], roll: r2(roll), w: r2(courtWidth || fieldWidth || p.w || W), flags, speed: p.speed || 1.0, surface, ...(flow ? { flow } : {}) });
      meta.push({ piece: pi, u, s: sAcc + d, sec: p.sec, ramp: !!onStream });
    }
    sAcc += L; y0 = y1;
  });
  nodes[0].flags.unshift('start');
  // stream ramps: each run of ramped stream nodes eases its flow in and out over STREAM_RAMP nodes
  for (let i = 0; i < nodes.length; i++) {
    if (!meta[i].ramp || (i > 0 && meta[i - 1].ramp)) continue;
    let M = 0;
    while (i + M < nodes.length && meta[i + M].ramp) M++;
    for (let j = 0; j < M; j++) {
      const f = Math.min((j + 1) / STREAM_RAMP, (M - j) / STREAM_RAMP, 1);
      nodes[i + j].flow = nodes[i + j].flow.map((v) => r2(v * f));
    }
    i += M - 1;
  }

  const N = nodes.length;
  const sections = [];
  for (let i = 0; i < N; i++) {
    const sec = meta[i].sec;
    if (!sections.length || sections[sections.length - 1].sec !== sec) sections.push({ sec, from: i });
  }
  const sectionRows = sections.map((s, k) => ({ ...SECTIONS[s.sec], from: s.from, to: k + 1 < sections.length ? sections[k + 1].from : N }));

  const nodeAt = (pi, u) => {
    let best = -1, bd = 1e9;
    meta.forEach((m, i) => { if (m.piece === pi && Math.abs(m.u - u) < bd) { bd = Math.abs(m.u - u); best = i; } });
    return best;
  };
  const boosts = [];
  pieces.forEach((p, pi) => {
    if (!p.pad) return;
    const i = nodeAt(pi, p.pad.at);
    nodes[i].flags.push('boost');
    boosts.push({ node: i, x: p.pad.x, w: 6, len: 7.5 });
  });
  const lipPiece = pieces.findIndex((p) => p.jump);
  const lip = meta.findIndex((m) => m.piece === lipPiece);
  const jumps = [{ node: lip, airLen: pieces[lipPiece].len, height: r2(nodes[lip].p[1] - nodes[lip + 1].p[1]) }];

  // B1 item boxes (rows across the road) and pickup-bubble clusters (CONTRACT-B1.md section 9)
  const items = [];
  const pieceIdx = (id) => pieces.findIndex((p) => p.id === id);
  for (const row of BOX_ROWS) {
    const i = nodeAt(pieceIdx(row.piece), row.at);
    for (const x of row.xs) items.push({ node: i, x, kind: 'box' });
  }
  for (const c of BUBBLE_CLUSTERS) {
    const i = nodeAt(pieceIdx(c.piece), c.at);
    c.dots.forEach(([ds, x]) => items.push({ node: i, x, kind: 'bubble', ds }));
  }

  // drop shortcuts: open the rail on one side over the gap stretch, register the landing stretch
  const routes = ROUTES.map((rt) => {
    const pi = pieceIdx(rt.piece), li = pieceIdx(rt.land), le = pieceIdx(rt.landEnd || rt.land);
    const idxs = meta.map((m, i) => (m.piece === pi && m.u >= rt.from - 1e-9 && m.u < rt.to ? i : -1)).filter((i) => i >= 0);
    const keep = rt.side === 'L' ? 'railR' : 'railL';
    for (const i of idxs) nodes[i].flags = nodes[i].flags.map((f) => (f === 'rail' ? keep : f));
    const lands = meta.map((m, i) => (m.piece >= li && m.piece <= le && (m.piece > li || m.u >= rt.landFrom - 1e-9) && (m.piece < le || m.u <= rt.landTo + 1e-9) ? i : -1)).filter((i) => i >= 0);
    return { id: rt.id, kind: rt.kind, side: rt.side, from: idxs[0], to: idxs[idxs.length - 1] + 1, ...(rt.open ? { open: rt.open } : {}),
      land: { from: lands[0], to: lands[lands.length - 1] }, pad: { node: nodeAt(rt.pad.piece ? pieceIdx(rt.pad.piece) : li, rt.pad.at), ds: rt.pad.ds || 0, x: rt.pad.x } };
  });
  for (const fl of FALLS) {
    const pi = pieceIdx(fl.piece);
    meta.forEach((m, i) => {
      if (m.piece !== pi || m.u < fl.from - 1e-9 || m.u >= fl.to) return;
      nodes[i].flags = nodes[i].flags.map((f) => (f !== 'rail' ? f : fl.side === 'both' ? 'open' : fl.side === 'L' ? 'railR' : 'railL'));
    });
  }
  // stepping-stone fields: the field's nodes go open, stones anchored to the last field node at or before them
  const fields = FIELDS.map((fd) => {
    const pi = pieceIdx(fd.piece);
    const idxs = meta.map((m, i) => (m.piece === pi && m.u >= fd.from - 1e-9 && m.u < fd.to ? i : -1)).filter((i) => i >= 0);
    for (const i of idxs) nodes[i].flags = nodes[i].flags.map((f) => (f === 'rail' ? 'open' : f));
    const from = idxs[0], to = idxs[idxs.length - 1] + 1, fromS = meta[from].s, toS = meta[to].s;
    const stones = [];
    fd.lanes.forEach((x, li) => {
      laneStones(toS - fromS, li * fd.staggerM, fd.stoneM, fd.gapM).forEach(([a, b], k) => {
        const s = fromS + a;
        let node = from;
        while (node + 1 < to && meta[node + 1].s <= s + 1e-9) node++;
        stones.push({ id: fd.id + '-' + li + '-' + k, node, ds: s - meta[node].s, x, w: fd.stoneM, len: Math.min(b - a, toS - FIELD_END_GAP_M - s) });
      });
    });
    return { id: fd.id, from, to, lanes: fd.lanes.slice(), platforming: true, stones };
  });

  // checkpoints near the quarter marks, never on a jump or inside a loop
  const checkpoints = [0, 0.25, 0.5, 0.75].map((f) => {
    let best = 0, bd = 1e9;
    meta.forEach((m, i) => {
      const d = Math.abs(m.s - f * sAcc), fl = nodes[i].flags;
      if (d < bd && !fl.includes('jump') && !fl.includes('magnet')) { bd = d; best = i; }
    });
    return best;
  });

  const minY = Math.min(...nodes.map((n) => n.p[1]));
  const courtNodes = meta.map((m, i) => pieces[m.piece].id === 'L4d' ? i : -1).filter((i) => i >= 0);
  const courts = [{
    id: 'aperture-court', from: courtNodes[0], to: courtNodes[courtNodes.length - 1] + 1,
    holes: [
      { id: 'aperture-left', node: courtNodes[0], ds: 27, x: -7.25, w: 6, len: 8 },
      { id: 'aperture-right', node: courtNodes[0], ds: 46, x: 7.25, w: 6, len: 8 },
    ],
  }];
  const track = {
    format: 'glassroad-track', version: 1,
    id: 'threshold', name: { en: 'Threshold' }, author: 'ccl', created: '2026-10-22',
    loop: true, laps: 3, units: 'm',
    nodes,
    sections: sectionRows,
    checkpoints,
    spawn: { node: 0, grid: [[-4, -5], [4, -9], [-4, -13], [4, -17], [-4, -21], [4, -25], [-4, -29], [4, -33]] },
    items, boosts, jumps, routes, courts, fields, hazards: [], media: [],
    ai: { lines: [], pace: [1.0, 1.05, 1.1] },
    beat: { bpm: null, offsetMs: 0, map: [] },
    limits: { fallY: Math.floor(minY - 25), edgeGraceM: 1.2, edgeGraceS: 0.8 },
  };
  track.boosts = densifyBoosts(track).boosts;
  return { track, closure, lengthM: sAcc, meta, lipPiece, pieces };
}

const r2 = (v) => Math.round(v * 100) / 100;

/** Dense centreline samples [x, y, z, w, node, s] (5 per segment). */
function densePts(track, meta, L) {
  const P = track.nodes.map((n) => n.p), N = P.length, pts = [];
  const widthAt = createScalar(track.nodes.map((n) => n.w));
  for (let i = 0; i < N; i++) {
    const a = P[i], b = P[(i + 1) % N], s0 = meta[i].s, s1 = i + 1 < N ? meta[i + 1].s : L;
    for (let k = 0; k < 5; k++) { const t = k / 5; pts.push([a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t, widthAt(i, t), i, s0 + (s1 - s0) * t]); }
  }
  return pts;
}

/** Sides of node i with no rail: [] | ['L'] | ['R'] | ['L', 'R']. Jump nodes are not edges. */
export function openSides(flags) {
  if (flags.includes('jump')) return [];
  if (flags.includes('rail')) return [];
  if (flags.includes('railL')) return ['R'];
  if (flags.includes('railR')) return ['L'];
  return ['L', 'R'];
}

/**
 * Every open edge of the course: what lies beneath it. Returns rows { node, side, under: node | -1,
 * dy, ahead (metres forward in the lap to the road beneath, or -1 when it is behind), route (id | null) }.
 */
export function openEdgeReport(track, meta, L) {
  const P = track.nodes.map((n) => n.p), N = P.length, pts = densePts(track, meta, L), out = [];
  for (let i = 0; i < N; i++) {
    for (const side of openSides(track.nodes[i].flags)) {
      const a = P[i], b = P[(i + 1) % N], sg = side === 'L' ? -1 : 1;
      const tx = b[0] - a[0], tz = b[2] - a[2], tl = Math.hypot(tx, tz) || 1;
      let under = -1, dyBest = 0, sUnder = 0;
      // straight down past the lip, and as far out as a hop carries a kart
      for (const out of HOP_REACH_M) {
        const reach = track.nodes[i].w / 2 + out;
        const ex = a[0] + sg * (-tz / tl) * reach, ez = a[2] + sg * (tx / tl) * reach;
        for (const q of pts) {
          const dy = a[1] - q[1];
          if (dy < BENEATH_M[0] || dy > BENEATH_M[1]) continue;
          if (Math.hypot(q[0] - ex, q[2] - ez) > q[3] / 2 + 1) continue;
          if (under < 0 || q[5] > sUnder) { under = q[4]; dyBest = dy; sUnder = q[5]; }
        }
      }
      // ahead: later in this lap (the lap line is never crossed by a fall)
      const ahead = under >= 0 && sUnder > meta[i].s ? sUnder - meta[i].s : -1;
      const rt = track.routes.find((r) => r.side === side && i >= r.from && i < r.to);
      out.push({ node: i, side, under, dy: r2(dyBest), ahead: r2(ahead), route: rt ? rt.id : null });
    }
  }
  return out;
}

export function checks({ track, closure, lengthM, lipPiece, meta, pieces }) {
  const errs = [], N = track.nodes.length, P = track.nodes.map((n) => n.p);
  // items: inside the road, boxes clear of the jump and pads, everything clear of the open corner and the loops
  const L = lengthM, sd = (a, b) => { const d = Math.abs(((a - b) % L + L) % L); return Math.min(d, L - d); };
  const lipS = meta[track.jumps[0].node].s;
  let landI = track.jumps[0].node;
  while (track.nodes[landI].flags.includes('jump')) landI++;
  const landS = meta[landI].s;
  const openS = meta.filter((m, i) => track.nodes[i].flags.includes('open')).map((m) => m.s);
  const loopS = meta.filter((m, i) => track.nodes[i].flags.includes('magnet')).map((m) => m.s);
  for (const it of track.items) {
    const s = meta[it.node].s + (it.ds || 0), w = track.nodes[it.node].w;
    if (Math.abs(it.x) > w / 2 - 1) errs.push('item off the road at node ' + it.node);
    if (openS.some((o) => sd(s, o) < ITEM_OPEN_CLEAR_M)) errs.push(it.kind + ' near the open corner at node ' + it.node);
    if (loopS.some((o) => sd(s, o) < ITEM_LOOP_CLEAR_M)) errs.push(it.kind + ' inside a loop at node ' + it.node);
    if (it.kind !== 'box') continue;
    if (s > lipS - ITEM_JUMP_CLEAR_M && s < landS + ITEM_JUMP_CLEAR_M) errs.push('box near the jump at node ' + it.node);
    for (const b of track.boosts) { const bs = meta[b.node].s; if (s > bs - ITEM_PAD_CLEAR_M && s < bs + b.len + ITEM_PAD_CLEAR_M) errs.push('box on a pad at node ' + it.node); }
  }
  // water: the chute's steepest segment, the pool's length, every stream node carries a flow, pools carry none
  const pieceNodes = (id) => meta.map((m, i) => (pieces[m.piece].id === id ? i : -1)).filter((i) => i >= 0);
  let chuteGrade = 0;
  for (const i of pieceNodes('chute')) {
    const a = P[i], b = P[(i + 1) % N], run = Math.hypot(b[0] - a[0], b[2] - a[2]);
    chuteGrade = Math.max(chuteGrade, Math.atan2(Math.abs(b[1] - a[1]), run) / DEG);
  }
  const chuteP = pieces.find((p) => p.id === 'chute');
  if (chuteP) chuteGrade = Math.max(chuteGrade, Math.atan(1.5 * Math.abs(chuteP.y - pieces[pieces.indexOf(chuteP) - 1].y) / chuteP.len) / DEG);
  if (chuteGrade > CHUTE_GRADE_DEG) errs.push('chute grade ' + chuteGrade.toFixed(1) + ' deg over ' + CHUTE_GRADE_DEG);
  const poolP = pieces.find((p) => p.id === 'pool');
  if (poolP && pieceLen(poolP) < POOL_MIN_M) errs.push('pool ' + pieceLen(poolP) + ' m is under ' + POOL_MIN_M);
  track.nodes.forEach((n, i) => {
    if (n.surface === 'stream' && !(n.flow && (n.flow[0] || n.flow[1]))) errs.push('stream node ' + i + ' has no flow');
    if (n.surface !== 'stream' && n.flow) errs.push('flow on a ' + n.surface + ' node ' + i);
  });
  const waterNodes = track.nodes.filter((n) => n.surface === 'stream' || n.surface === 'pool').length;
  if (closure.closeErr > 0.01) errs.push('loop does not close: ' + closure.closeErr);
  if (Math.abs(closure.headErr) > 0.01 && Math.abs(closure.headErr - 360) > 0.01) errs.push('heading does not close: ' + closure.headErr);
  for (const p of pieces) if (p.auto && p.len < 30) errs.push('auto straight ' + p.id + ' too short: ' + p.len.toFixed(1));
  let minGap = 1e9, maxGap = 0;
  for (let i = 0; i < N; i++) {
    const a = P[i], b = P[(i + 1) % N], d = Math.hypot(b[0] - a[0], b[1] - a[1], b[2] - a[2]);
    minGap = Math.min(minGap, d); maxGap = Math.max(maxGap, d);
  }
  if (minGap < 8 || maxGap > 15.5) errs.push('node spacing ' + minGap.toFixed(2) + '..' + maxGap.toFixed(2));
  if (lengthM < LENGTH_M[0] || lengthM > LENGTH_M[1]) errs.push('length ' + lengthM.toFixed(0));
  // plan overlap: dense polyline, pairs far apart along the track. A loop's own body (and the straights
  // either side of it) stand on their own rule: the side offset keeps the exit lane clear of the entry.
  const loopOf = meta.map((m) => (pieces[m.piece].loop ? m.piece : -1));
  const nearLoop = (i) => {
    for (const d of [0, -1, 1, -2, 2]) { const k = ((i + d) % N + N) % N; if (loopOf[k] >= 0) return loopOf[k]; }
    return -1;
  };
  const pts = densePts(track, meta, L);
  let minSep = 1e9, worst = null;
  for (let i = 0; i < pts.length; i++) for (let j = i + 1; j < pts.length; j++) {
    const di = Math.abs(pts[i][4] - pts[j][4]), cyc = Math.min(di, N - di);
    if (cyc < 6) continue;
    const li = nearLoop(pts[i][4]);
    if (li >= 0 && li === nearLoop(pts[j][4])) continue;
    const dp = Math.hypot(pts[i][0] - pts[j][0], pts[i][2] - pts[j][2]);
    // lanes marked alongside (the dare) may run side by side, footprints apart, a few metres lower
    if (pieces[meta[pts[i][4]].piece].alongside && pieces[meta[pts[j][4]].piece].alongside &&
      dp >= (pts[i][3] + pts[j][3]) / 2 + ALONGSIDE_GAP_M && Math.abs(pts[i][1] - pts[j][1]) >= ALONGSIDE_DY_M) continue;
    if (dp < (pts[i][3] + pts[j][3]) / 2 + 4) {
      const dy = Math.abs(pts[i][1] - pts[j][1]);
      if (dy < minSep) { minSep = dy; worst = [pts[i][4], pts[j][4]]; }
    }
  }
  if (minSep < 10) errs.push('plan overlap with only ' + minSep.toFixed(2) + ' m vertical at nodes ' + worst);
  for (const p of pieces) if (p.loop) {
    const pi = pieces.indexOf(p), exit = pieces[(pi + 1) % pieces.length];
    const entryExitWidth = ((p.w || W) + (exit.w || W)) / 2;
    if (Math.abs(p.off) < entryExitWidth + LOOP_SIDE_GAP_M) errs.push('loop ' + p.id + ' exit offset ' + p.off + ' m is under the entry/exit half widths plus ' + LOOP_SIDE_GAP_M);
    if (p.r < 12 || p.r > 18) errs.push('loop ' + p.id + ' radius ' + p.r + ' outside 12..18 m');
  }
  // the decks: at least three levels, and the course crosses over itself at least twice (distinct places)
  const ys = P.map((p) => p[1]);
  const levels = [0, 14, 28].filter((lv) => ys.some((y) => Math.abs(y - lv) < 1.5));
  if (levels.length < 3) errs.push('decks at ' + levels.join(', ') + ' only');
  const crossings = [];
  for (let i = 0; i < pts.length; i += 2) for (let j = i + 1; j < pts.length; j += 2) {
    const di = Math.abs(pts[i][4] - pts[j][4]), cyc = Math.min(di, N - di);
    if (cyc < 12 || nearLoop(pts[i][4]) >= 0 || nearLoop(pts[j][4]) >= 0) continue;
    if (Math.hypot(pts[i][0] - pts[j][0], pts[i][2] - pts[j][2]) > 3 || Math.abs(pts[i][1] - pts[j][1]) < 10) continue;
    const c = [(pts[i][0] + pts[j][0]) / 2, (pts[i][2] + pts[j][2]) / 2];
    if (!crossings.some((o) => Math.hypot(o[0] - c[0], o[1] - c[1]) < 40)) crossings.push(c);
  }
  if (crossings.length < 2) errs.push('the course crosses over itself ' + crossings.length + ' times');
  // jump: kick ease gives lip grade 2 * rise / len
  const kick = pieces[lipPiece - 1], gap = pieces[lipPiece];
  const grade = 2 * (kick.y - pieces[lipPiece - 2].y) / kick.len, pitch = Math.atan(grade);
  const drop = kick.y - gap.y, report = [];
  for (const f of [0.6, 0.8, 1.0, 1.24]) {
    const v = TOP * f, vy = v * Math.sin(pitch), vh = v * Math.cos(pitch);
    const t = (vy + Math.sqrt(vy * vy + 2 * G * drop)) / G, x = vh * t;
    report.push(f + ':' + x.toFixed(1) + 'm/' + t.toFixed(2) + 's');
    if (x < gap.len + 1) errs.push('jump not cleared at ' + f + ' of top: ' + x.toFixed(1) + ' m');
  }
  // drop shortcuts: every open-edge node sits 10 to 16 m over the landing road, and its open edge is over it
  const routeInfo = [];
  for (const rt of track.routes) {
    const land = [];
    for (let i = rt.land.from; i <= rt.land.to; i++) land.push(i);
    let over = 0;
    for (let i = rt.from; i < rt.to; i++) {
      const a = P[i], b = P[i + 1], side = rt.side === 'L' ? -1 : 1;
      const tx = b[0] - a[0], tz = b[2] - a[2], tl = Math.hypot(tx, tz);
      const ex = a[0] + side * (-tz / tl) * track.nodes[i].w / 2, ez = a[2] + side * (tx / tl) * track.nodes[i].w / 2;
      let best = 1e9, by = 0;
      for (const j of land) { const d = Math.hypot(P[j][0] - ex, P[j][2] - ez); if (d < best) { best = d; by = P[j][1]; } }
      const dy = a[1] - by;
      const cfg = ROUTES.find((c) => c.id === rt.id) || {}, dr = cfg.drop || ROUTE_DROP_M;
      if (dy < dr[0] || dy > dr[1]) errs.push('route ' + rt.id + ' drop ' + dy.toFixed(1) + ' m at node ' + i);
      if (best < track.nodes[land[0]].w / 2 + (cfg.reachM || 6)) over++;
    }
    if (!over) errs.push('route ' + rt.id + ' open edge is not over its landing road');
    const skip = (meta[rt.pad.node].s + rt.pad.ds) - meta[rt.from].s;
    routeInfo.push(rt.id + ': nodes ' + rt.from + '..' + (rt.to - 1) + ' side ' + rt.side + ', s ' + meta[rt.from].s.toFixed(0) + '..' + meta[rt.to].s.toFixed(0) +
      ' -> land s ' + meta[rt.land.from].s.toFixed(0) + '..' + meta[rt.land.to].s.toFixed(0) + ', pad s ' + (meta[rt.pad.node].s + rt.pad.ds).toFixed(0) + ' (skip ' + skip.toFixed(0) + ' m, ' + (100 * skip / lengthM).toFixed(0) + '% of a lap)');
  }
  // stepping-stone fields: stones inside the road with spare, lane gaps in range, never a wall, stones from the
  // field start, the last stone short of the deck, no item or pad inside, the pad before it clear, open nodes,
  // and what lies beneath the field (a fall through the void lands there)
  const fieldInfo = [];
  for (const fd of track.fields || []) {
    const cfg = FIELDS.find((c) => c.id === fd.id) || { gapM: 4 }, fromS = meta[fd.from].s, toS = meta[fd.to].s;
    const stones = fd.stones.map((h) => ({ ...h, s: meta[h.node].s + (h.ds || 0) }));
    for (let i = fd.from; i < fd.to; i++) if (!track.nodes[i].flags.includes('open')) errs.push('field ' + fd.id + ' node ' + i + ' is not open');
    for (const h of stones) {
      const half = Math.min(track.nodes[h.node].w, track.nodes[Math.min(h.node + 1, N - 1)].w) / 2;
      if (Math.abs(h.x) + h.w / 2 + FIELD_SPARE_M > half) errs.push('field ' + fd.id + ' stone ' + h.id + ' leaves the road (half width ' + half + ')');
      if (h.s < fromS - 1e-6 || h.s + h.len > toS - FIELD_END_GAP_M + 1e-6) errs.push('field ' + fd.id + ' stone ' + h.id + ' outside the field or short of the end gap');
      if (h.len < FIELD_LEN_M[0] || h.len > FIELD_LEN_M[1]) errs.push('field ' + fd.id + ' stone ' + h.id + ' len ' + h.len + ' outside ' + FIELD_LEN_M.join('..'));
    }
    const counts = [];
    fd.lanes.forEach((x) => {
      const lane = stones.filter((h) => h.x === x).sort((a, b) => a.s - b.s);
      counts.push(lane.length);
      // every lane starts at the deck's edge and ends at the closing edge (no entry void, an exit void of one short coast)
      if (lane.length && Math.abs(lane[0].s - fromS) > 0.1) errs.push('field ' + fd.id + ' lane ' + x + ' first stone ' + (lane[0].s - fromS).toFixed(2) + ' m past the field start');
      if (lane.length && Math.abs(lane[lane.length - 1].s + lane[lane.length - 1].len - (toS - FIELD_END_GAP_M)) > 0.1)
        errs.push('field ' + fd.id + ' lane ' + x + ' last stone ends ' + (toS - lane[lane.length - 1].s - lane[lane.length - 1].len).toFixed(2) + ' m before the deck');
      for (let k = 1; k < lane.length; k++) {
        const gap = lane[k].s - (lane[k - 1].s + lane[k - 1].len);
        if (gap < FIELD_GAP_M[0] || gap > FIELD_GAP_M[1]) errs.push('field ' + fd.id + ' lane ' + x + ' gap ' + gap.toFixed(2) + ' m before stone ' + lane[k].id);
      }
    });
    if (!stones.length) errs.push('field ' + fd.id + ' has no stones');
    else {
      const first = Math.min(...stones.map((h) => h.s)), last = Math.max(...stones.map((h) => h.s + h.len));
      if (first - fromS > 1) errs.push('field ' + fd.id + ' first stone ' + (first - fromS).toFixed(1) + ' m past the field start');
      for (let s = fromS; s < toS; s += 0.25) {
        if (stones.some((h) => s >= h.s && s <= h.s + h.len)) continue;
        if (toS - s > cfg.gapM + 0.5) { errs.push('field ' + fd.id + ' is a wall at s ' + s.toFixed(1)); break; }
      }
      fieldInfo.push(fd.id + ': nodes ' + fd.from + '..' + (fd.to - 1) + ', s ' + fromS.toFixed(0) + '..' + toS.toFixed(0) + ' (' + (toS - fromS).toFixed(0) + ' m), w ' +
        track.nodes[fd.from].w + ', lanes ' + fd.lanes.join('/') + ' stones ' + counts.join('/') + ', first s ' + first.toFixed(1) + ', last ends s ' + last.toFixed(1) +
        ' (' + (toS - last).toFixed(1) + ' m before the deck)');
    }
    for (const it of track.items) { const s = meta[it.node].s + (it.ds || 0); if (s >= fromS && s < toS) errs.push('field ' + fd.id + ' has an item inside at node ' + it.node); }
    let lead = null;
    for (const b of track.boosts) {
      const bs = meta[b.node].s;
      if (bs + b.len > fromS && bs < toS) errs.push('field ' + fd.id + ' has a pad inside at node ' + b.node);
      if (bs < fromS && (lead === null || bs > lead)) lead = bs;
    }
    if (lead === null || fromS - lead < FIELD_PAD_LEAD_M) errs.push('field ' + fd.id + ' pad before it is under ' + FIELD_PAD_LEAD_M + ' m ahead');
    // beneath: the road straight under each field node (the fall through the void lands there), and the open-edge rows
    const beneath = new Map();
    for (let i = fd.from; i < fd.to; i++) {
      const a = P[i];
      for (const q of densePts(track, meta, L)) {
        const dy = a[1] - q[1];
        if (dy < BENEATH_M[0] || dy > BENEATH_M[1] || Math.hypot(q[0] - a[0], q[2] - a[2]) > q[3] / 2) continue;
        const id = pieces[meta[q[4]].piece].id, prev = beneath.get(id);
        beneath.set(id, prev ? [Math.min(prev[0], dy), Math.max(prev[1], dy), prev[2] || q[5] > meta[i].s] : [dy, dy, q[5] > meta[i].s]);
      }
    }
    const under = [...beneath].map(([id, [d0, d1, ahead]]) => id + ' dy ' + d0.toFixed(1) + '..' + d1.toFixed(1) + (ahead ? ' (ahead)' : ' (behind)'));
    fieldInfo.push(fd.id + ' beneath: ' + (under.length ? under.join(', ') : 'nothing (edge drop and rescue)'));
  }
  // the schema the loader enforces (track/schema.js), errors and warnings both
  const sv = validate(JSON.parse(JSON.stringify(track)));
  for (const m of [...sv.errors, ...sv.warnings]) errs.push('schema: ' + m);
  // CONTRACT P6.2: an open edge over a road AHEAD in the lap must be a registered route
  const edges = openEdgeReport(track, meta, L);
  for (const e of edges) if (e.ahead >= 0 && !e.route) errs.push('open edge at node ' + e.node + ' side ' + e.side + ' over road ' + e.ahead.toFixed(0) + ' m ahead has no route');
  const over = edges.filter((e) => e.under >= 0);
  return { errs, info: { nodes: N, lengthM: Math.round(lengthM), routes: routeInfo, fields: fieldInfo, spacing: [r2(minGap), r2(maxGap)], minOverlapSepM: r2(minSep),
    levels, crossings: crossings.map((c) => c.map(Math.round)),
    openOver: over.map((e) => e.node + e.side + (e.ahead >= 0 ? '>' + e.route : '<behind') + ' dy ' + e.dy).join(' '),
    lipPitchDeg: r2(pitch / DEG), jumpLand: report.join(' '),
    items: track.items.filter((i) => i.kind === 'box').length + ' boxes, ' + track.items.filter((i) => i.kind === 'bubble').length + ' bubbles', autos: pieces.filter((p) => p.auto).map((p) => p.id + '=' + p.len.toFixed(1)),
    water: { nodes: waterNodes, chuteGradeDeg: r2(chuteGrade), ranges: ['sweepA', 'L2', 'L4a', 'chute', 'pool', 'L4b'].map((id) => {
      const ix = pieceNodes(id), from = ix.length ? meta[ix[0]].s : 0, to = ix.length ? (ix[ix.length - 1] + 1 < N ? meta[ix[ix.length - 1] + 1].s : L) : 0;
      return id + ' s ' + from.toFixed(0) + '..' + to.toFixed(0);
    }) },
    yRange: [Math.min(...P.map((p) => p[1])), Math.max(...P.map((p) => p[1]))] } };
}

// short arrays and objects on one line, so a node is one line of the file
function fmt(v, ind) {
  const one = JSON.stringify(v);
  if (v === null || typeof v !== 'object' || one.length <= 110) return one;
  const pad = ind + '  ';
  if (Array.isArray(v)) return '[\n' + v.map((x) => pad + fmt(x, pad)).join(',\n') + '\n' + ind + ']';
  return '{\n' + Object.entries(v).map(([k, x]) => pad + JSON.stringify(k) + ': ' + fmt(x, pad)).join(',\n') + '\n' + ind + '}';
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const built = build();
  const { errs, info } = checks(built);
  console.log(JSON.stringify(info));
  if (errs.length) { console.error('FAIL\n' + errs.join('\n')); if (!process.argv.includes('--force')) process.exit(1); }
  if (!process.argv.includes('--check')) {
    writeFileSync(OUT, fmt(built.track, '') + '\n');
    console.log('wrote ' + OUT);
  }
}
