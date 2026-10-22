// dev/gen-threshold.mjs - builds tracks/threshold.json (greybox Threshold, Track JSON v1).
// Run: node dev/gen-threshold.mjs [--check]   (--check prints the report and writes nothing)
//
// A turtle walks pieces (straights and arcs) in the XZ plane; heading 0 = +X and a positive
// turn is a RIGHT turn (+X heading turns toward +Z, which is the right side with +Y up).
// Two straights marked `auto` get their lengths solved so the loop closes exactly.
// Then it checks spacing, plan overlap (allowed only with >= 10 m vertical separation),
// length, and that the jump gap is cleared at 60% to 124% of top speed.
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const HERE = dirname(fileURLToPath(import.meta.url));
const OUT = join(HERE, '..', 'tracks', 'threshold.json');
const DEG = Math.PI / 180;

// y = target height at the piece end; ease: smooth | lin | kick (grade grows to the end)
const PIECES = [
  { id: 'start', len: 90, y: 0, w: 16, sec: 0, pad: { at: 0.45, x: 0 } },
  { id: 'sweepA', turn: 90, r: 70, roll: 25, y: 2, sec: 0 },
  { id: 'L2', len: 30, y: 3, sec: 0 },
  { id: 'S1', turn: -40, r: 60, roll: 20, y: 4.5, sec: 0 },
  { id: 'S2', turn: 40, r: 60, roll: 20, y: 6, sec: 0 },
  { id: 'L2b', len: 40, y: 7, sec: 0, pad: { at: 0.5, x: -3 } },
  { id: 'sweepB', turn: 90, r: 70, roll: 30, y: 8, sec: 0 },
  { id: 'L3a', len: 90, y: 8, sec: 1 },
  { id: 'spiral', turn: 360, r: 48, roll: 20, y: -6, ease: 'lin', w: 13, speed: 0.9, sec: 1 },
  { id: 'L3b', len: 78, y: -6, sec: 1 },
  { id: 'openC', turn: 90, r: 70, roll: 22, y: -7, open: true, speed: 0.85, sec: 1 },
  { id: 'L4a', auto: true, y: -7, sec: 2, pad: { at: 0.85, x: 0 } },
  { id: 'kicker', len: 30, y: -4, ease: 'kick', sec: 2 },
  { id: 'gap', len: 12, y: -10, ease: 'lin', jump: true, sec: 2 },
  { id: 'landing', len: 40, y: -11, sec: 2 },
  { id: 'L4c', len: 30, y: -8, sec: 2, pad: { at: 0.5, x: 3 } },
  { id: 'sweepD', turn: 90, r: 70, roll: 25, y: -3, sec: 2 },
  { id: 'home', auto: true, y: 0, w: 16, sec: 2 },
];
const SECTIONS = [
  { id: 'induction', act: 'induction', palette: 'dawn', sky: 'spiral-slow', bandSpeed: 0.6 },
  { id: 'deepening', act: 'deepening', palette: 'deep', sky: 'spiral-deep', bandSpeed: 1.0 },
  { id: 'release', act: 'release', palette: 'afterglow', sky: 'spiral-wide', bandSpeed: 0.8 },
];
const W = 14, TOP = 30, G = 28;

// Item box rows: piece, place along it (0..1), box x positions (metres, + right). 3 to 4 across, away
// from the jump (kicker to landing) and the open corner; the checks below hold those margins.
const BOX_ROWS = [
  { piece: 'start', at: 0.8, xs: [-5, -1.7, 1.7, 5] },
  { piece: 'S2', at: 0.5, xs: [-3.5, 0, 3.5] },
  { piece: 'L3a', at: 0.5, xs: [-4.5, -1.5, 1.5, 4.5] },
  { piece: 'L3b', at: 0.3, xs: [-3.5, 0, 3.5] },
  { piece: 'sweepD', at: 0.5, xs: [-4.5, -1.5, 1.5, 4.5] },
];
// Pickup-bubble clusters: [ds metres past the node, x] per bubble, on the inside line of a bend.
const BUBBLE_CLUSTERS = [
  { piece: 'sweepA', at: 0.45, dots: [[0, 3], [3, 3.8], [6, 4.4], [9, 3.8], [12, 3]] },
  { piece: 'spiral', at: 0.55, dots: [[0, 2.5], [3, 3.2], [6, 3.6], [9, 3.2], [12, 2.5]] },
  { piece: 'L2', at: 0.1, dots: [[0, -2], [3, -1], [6, 0], [9, 1], [12, 2]] },
];
const ITEM_JUMP_CLEAR_M = 40, ITEM_OPEN_CLEAR_M = 30, ITEM_PAD_CLEAR_M = 8;

const pieceLen = (p) => (p.turn ? Math.abs(p.turn) * DEG * p.r : p.len);
const ease = (k, u) => (k === 'lin' ? u : k === 'kick' ? u * u : u * u * (3 - 2 * u));

// heading after each piece, and the plan displacement of a piece
function walkPlan(pieces) {
  let x = 0, z = 0, h = 0;
  const at = [];
  for (const p of pieces) {
    at.push({ x, z, h });
    if (p.turn) {
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

function build() {
  const closure = solveAuto(PIECES);
  const plan = walkPlan(PIECES);
  const nodes = [], meta = [];
  let y0 = 0, sAcc = 0;
  PIECES.forEach((p, pi) => {
    const L = pieceLen(p), n = segCount(L), st = plan.at[pi];
    const y1 = p.y ?? y0;
    for (let k = 0; k < n; k++) {
      const u = k / n, d = u * L;
      let x, z;
      if (p.turn) {
        const s = Math.sign(p.turn);
        const cx = st.x - s * Math.sin(st.h) * p.r, cz = st.z + s * Math.cos(st.h) * p.r;
        const h2 = st.h + p.turn * DEG * u;
        x = cx + s * Math.sin(h2) * p.r; z = cz - s * Math.cos(h2) * p.r;
      } else { x = st.x + Math.cos(st.h) * d; z = st.z + Math.sin(st.h) * d; }
      const y = y0 + (y1 - y0) * ease(p.ease || 'smooth', u);
      let roll = 0;
      if (p.turn) {
        const ramp = Math.min(20, L / 3);
        roll = Math.sign(p.turn) * p.roll * Math.min(1, d / ramp, (L - d) / ramp);
      }
      const flags = p.jump ? ['jump'] : p.open ? ['open'] : ['rail'];
      nodes.push({ p: [r2(x), r2(y), r2(z)], roll: r2(roll), w: p.w || W, flags, speed: p.speed || 1.0, surface: 'glass' });
      meta.push({ piece: pi, u, s: sAcc + d, sec: p.sec });
    }
    sAcc += L; y0 = y1;
  });
  nodes[0].flags.unshift('start');

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
  PIECES.forEach((p, pi) => {
    if (!p.pad) return;
    const i = nodeAt(pi, p.pad.at);
    nodes[i].flags.push('boost');
    boosts.push({ node: i, x: p.pad.x, w: 4, len: 6 });
  });
  const lipPiece = PIECES.findIndex((p) => p.jump);
  const lip = meta.findIndex((m) => m.piece === lipPiece);
  const jumps = [{ node: lip, airLen: PIECES[lipPiece].len, height: r2(nodes[lip].p[1] - nodes[lip + 1].p[1]) }];

  // B1 item boxes (rows across the road) and pickup-bubble clusters (CONTRACT-B1.md section 9)
  const items = [];
  const pieceIdx = (id) => PIECES.findIndex((p) => p.id === id);
  for (const row of BOX_ROWS) {
    const i = nodeAt(pieceIdx(row.piece), row.at);
    for (const x of row.xs) items.push({ node: i, x, kind: 'box' });
  }
  for (const c of BUBBLE_CLUSTERS) {
    const i = nodeAt(pieceIdx(c.piece), c.at);
    c.dots.forEach(([ds, x]) => items.push({ node: i, x, kind: 'bubble', ds }));
  }

  const checkpoints = [0, 0.25, 0.5, 0.75].map((f) => {
    let best = 0, bd = 1e9;
    meta.forEach((m, i) => {
      const d = Math.abs(m.s - f * sAcc);
      if (d < bd && !nodes[i].flags.includes('jump')) { bd = d; best = i; }
    });
    return best;
  });

  const minY = Math.min(...nodes.map((n) => n.p[1]));
  const track = {
    format: 'glassroad-track', version: 1,
    id: 'threshold', name: { en: 'Threshold' }, author: 'ccl', created: '2026-10-22',
    loop: true, laps: 3, units: 'm',
    nodes,
    sections: sectionRows,
    checkpoints,
    spawn: { node: 0, grid: [[-3, -4], [3, -6], [-3, -8], [3, -10], [-3, -12], [3, -14], [-3, -16], [3, -18]] },
    items, boosts, jumps, hazards: [], media: [],
    ai: { lines: [], pace: [1.0, 1.05, 1.1] },
    beat: { bpm: null, offsetMs: 0, map: [] },
    limits: { fallY: Math.floor(minY - 25), edgeGraceM: 1.2, edgeGraceS: 0.8 },
  };
  return { track, closure, lengthM: sAcc, meta, lipPiece };
}

const r2 = (v) => Math.round(v * 100) / 100;

function checks({ track, closure, lengthM, lipPiece, meta }) {
  const errs = [], N = track.nodes.length, P = track.nodes.map((n) => n.p);
  // items: inside the road, boxes clear of the jump and pads, everything clear of the open corner
  const L = lengthM, sd = (a, b) => { const d = Math.abs(((a - b) % L + L) % L); return Math.min(d, L - d); };
  const lipS = meta[track.jumps[0].node].s;
  let landI = track.jumps[0].node;
  while (track.nodes[landI].flags.includes('jump')) landI++;
  const landS = meta[landI].s;
  const openS = meta.filter((m, i) => track.nodes[i].flags.includes('open')).map((m) => m.s);
  for (const it of track.items) {
    const s = meta[it.node].s + (it.ds || 0), w = track.nodes[it.node].w;
    if (Math.abs(it.x) > w / 2 - 1) errs.push('item off the road at node ' + it.node);
    if (openS.some((o) => sd(s, o) < ITEM_OPEN_CLEAR_M)) errs.push(it.kind + ' near the open corner at node ' + it.node);
    if (it.kind !== 'box') continue;
    if (s > lipS - ITEM_JUMP_CLEAR_M && s < landS + ITEM_JUMP_CLEAR_M) errs.push('box near the jump at node ' + it.node);
    for (const b of track.boosts) { const bs = meta[b.node].s; if (s > bs - ITEM_PAD_CLEAR_M && s < bs + b.len + ITEM_PAD_CLEAR_M) errs.push('box on a pad at node ' + it.node); }
  }
  if (closure.closeErr > 0.01) errs.push('loop does not close: ' + closure.closeErr);
  if (Math.abs(closure.headErr) > 0.01 && Math.abs(closure.headErr - 360) > 0.01) errs.push('heading does not close: ' + closure.headErr);
  for (const p of PIECES) if (p.auto && p.len < 30) errs.push('auto straight ' + p.id + ' too short: ' + p.len.toFixed(1));
  let minGap = 1e9, maxGap = 0;
  for (let i = 0; i < N; i++) {
    const a = P[i], b = P[(i + 1) % N], d = Math.hypot(b[0] - a[0], b[1] - a[1], b[2] - a[2]);
    minGap = Math.min(minGap, d); maxGap = Math.max(maxGap, d);
  }
  if (minGap < 8 || maxGap > 15.5) errs.push('node spacing ' + minGap.toFixed(2) + '..' + maxGap.toFixed(2));
  if (lengthM < 1200 || lengthM > 1600) errs.push('length ' + lengthM.toFixed(0));
  // plan overlap: dense polyline, pairs far apart along the track
  const pts = [];
  for (let i = 0; i < N; i++) {
    const a = P[i], b = P[(i + 1) % N], w = track.nodes[i].w;
    for (let k = 0; k < 5; k++) { const t = k / 5; pts.push([a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t, w, i]); }
  }
  let minSep = 1e9, worst = null;
  for (let i = 0; i < pts.length; i++) for (let j = i + 1; j < pts.length; j++) {
    const di = Math.abs(pts[i][4] - pts[j][4]), cyc = Math.min(di, N - di);
    if (cyc < 6) continue;
    const dp = Math.hypot(pts[i][0] - pts[j][0], pts[i][2] - pts[j][2]);
    if (dp < (pts[i][3] + pts[j][3]) / 2 + 4) {
      const dy = Math.abs(pts[i][1] - pts[j][1]);
      if (dy < minSep) { minSep = dy; worst = [pts[i][4], pts[j][4]]; }
    }
  }
  if (minSep < 10) errs.push('plan overlap with only ' + minSep.toFixed(2) + ' m vertical at nodes ' + worst);
  // jump: kick ease gives lip grade 2 * rise / len
  const kick = PIECES[lipPiece - 1], gap = PIECES[lipPiece];
  const grade = 2 * (kick.y - PIECES[lipPiece - 2].y) / kick.len, pitch = Math.atan(grade);
  const drop = kick.y - gap.y, report = [];
  for (const f of [0.6, 0.8, 1.0, 1.24]) {
    const v = TOP * f, vy = v * Math.sin(pitch), vh = v * Math.cos(pitch);
    const t = (vy + Math.sqrt(vy * vy + 2 * G * drop)) / G, x = vh * t;
    report.push(f + ':' + x.toFixed(1) + 'm/' + t.toFixed(2) + 's');
    if (x < gap.len + 1) errs.push('jump not cleared at ' + f + ' of top: ' + x.toFixed(1) + ' m');
  }
  return { errs, info: { nodes: N, lengthM: Math.round(lengthM), spacing: [r2(minGap), r2(maxGap)], minOverlapSepM: r2(minSep),
    lipPitchDeg: r2(pitch / DEG), jumpLand: report.join(' '),
    items: track.items.filter((i) => i.kind === 'box').length + ' boxes, ' + track.items.filter((i) => i.kind === 'bubble').length + ' bubbles', autos: PIECES.filter((p) => p.auto).map((p) => p.id + '=' + p.len.toFixed(1)),
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

const built = build();
const { errs, info } = checks(built);
console.log(JSON.stringify(info));
if (errs.length) { console.error('FAIL\n' + errs.join('\n')); process.exit(1); }
if (!process.argv.includes('--check')) {
  writeFileSync(OUT, fmt(built.track, '') + '\n');
  console.log('wrote ' + OUT);
}
