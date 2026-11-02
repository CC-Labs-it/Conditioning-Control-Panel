// dev/decision-gaps.mjs - how long a solo player drives between decisions, per course. MEASURE ONLY.
// Run (from glassroad/): node dev/decision-gaps.mjs [--track threshold] [--json out.json]
//   [--chart threshold=C:/path/chart.json ...] [--target 10] [--top 6] [--bend-deg 45] [--bend-m 150]
// Charts are private: save each course's chart from a running dev/serve.mjs (the URL track/selection.js
// courseSongs names, e.g. /private-audio/pink-road/chart.json) to a scratch folder, never into this tree.
//
// The walk: the real sim (createRacers + kart.js) drives the player with the real AI (createAi) for two laps,
// solo clean pace on (no song drive, so the quiet ceiling), no items, no traps applied. Lap 2 is the flying lap;
// every decision's time is the first lap-2 frame at or past its s. A decision on a branch the AI skipped
// (a drop cut) is timed at its main-road s.
//
// Decision points (s on the main road):
//   box    item box row (boxes within ROW_M merge)        bubble  track pickup bubble row
//   word   song word bubble (only with --chart; song-timed, the song assumed to start at the race start).
//          The live picker (songGates on): gate and trigger words leave the bubbles for full-width song gates.
//   gate   song gate (with --chart): COUNTED but not a decision (a full-width plane every kart drives through)
//   cut    drop shortcut opening / fork mouth              air     aerial ramp or jump
//   trap   moving trap, course hazard station, court hole, stepping stone, Mainframe industry zone (within 25 m: one)
//   bend   drift-worthy bend: same-hand turn of BEND_DEG or more within BEND_M of road (one per bend)
// Moments closer than MERGE_S merge into one. A gap is the time between consecutive moments (wrapping the lap).
import { readFileSync, writeFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { createFeel } from '../core/feel.js';
import { createActions } from '../core/actions.js';
import { createRacers } from '../sim/racers.js';
import { createAi } from '../sim/ai.js';
import { S } from '../sim/state.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { getBoudoirLayout } from '../track/boudoir-layout.js';
import { getGoonworksLayout } from '../track/goonworks-layout.js';
import { createKeepHazards } from '../sim/keepHazards.js';
import { createBoudoirHazards } from '../sim/boudoirHazards.js';
import { createGoonworksHazards } from '../sim/goonworksHazards.js';
import { createMovingHazards } from '../sim/movingHazards.js';
import { industryZones } from '../sim/mainframeIndustry.js';
import { scriptBubbles, WORD_LOOK } from '../sim/words.js';
import { pickWords, ROAD, GATE } from '../sim/wordPick.js';
import { songGateCues } from '../sim/songGates.js';
import { COURSES } from '../track/catalog.js';

const DT = 1 / 60, ROW_M = 12, MERGE_S = 1, STEP_M = 2;
const argv = process.argv.slice(2);
const arg = (k, d) => { const i = argv.indexOf('--' + k); return i >= 0 ? argv[i + 1] : d; };
const TARGET = Number(arg('target', 10)), TOP = Number(arg('top', 6));
// ~4 s of road at the flying pace; #3052 left no bend tighter than ~47 deg in 100 m on any course
const BEND_DEG = Number(arg('bend-deg', 45)), BEND_M = Number(arg('bend-m', 150));
const charts = Object.fromEntries(argv.flatMap((a, i) => a === '--chart' ? [argv[i + 1].split('=')] : []));
const only = arg('track', null);
const feel = createFeel(JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8')));
const wrapM = (s, L) => ((s % L) + L) % L;

/** Every row with a finite s (or s0) anywhere inside an inspect() result. */
function rowsWithS(o, out = [], depth = 0) {
  if (!o || typeof o !== 'object' || depth > 3) return out;
  if (Array.isArray(o)) { for (const v of o) rowsWithS(v, out, depth + 1); return out; }
  const s = Number.isFinite(o.s) ? o.s : Number.isFinite(o.s0) ? o.s0 : null;
  if (s !== null && typeof o.id === 'string') { out.push({ id: o.id, s }); return out; }
  for (const v of Object.values(o)) if (v && typeof v === 'object') rowsWithS(v, out, depth + 1);
  return out;
}

/** Rows within m metres of a cluster's FIRST row join it (no chaining: a long stone field stays many decisions). */
function cluster(rows, m) {
  const out = [];
  for (const r of [...rows].sort((a, b) => a.s - b.s)) {
    const last = out[out.length - 1];
    if (last && r.s - last.s <= m) { last.lastS = r.s; last.n++; } else out.push({ ...r, lastS: r.s, n: 1 });
  }
  return out;
}

/** Signed in-plane bend per STEP_M (deg, + right), the way sim/ai.js measures it. */
function bendProfile(track) {
  const N = Math.ceil(track.lengthM / STEP_M), bend = new Float64Array(N), W = 8;
  const A = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] }, B = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] };
  for (let k = 0; k < N; k++) {
    const s = k * STEP_M;
    track.frameAt(s - W / 2, A); track.frameAt(s + W / 2, B);
    const d = A.t[0] * B.t[0] + A.t[1] * B.t[1] + A.t[2] * B.t[2];
    const side = B.t[0] * A.b[0] + B.t[1] * A.b[1] + B.t[2] * A.b[2];
    const gap = (track.frameAt(s).flags || []).includes('jump');
    bend[k] = gap ? 0 : Math.atan2(side, Math.abs(d)) * 180 / Math.PI / W * STEP_M;
  }
  return bend;
}

/** One decision per bend: where the same-hand turn over the next BEND_M first reaches BEND_DEG. */
function bends(track) {
  const bend = bendProfile(track), N = bend.length, win = Math.round(BEND_M / STEP_M), out = [];
  let until = -1;
  for (let k = 0; k < N; k++) {
    let sum = 0;
    for (let j = 0; j < win; j++) sum += bend[(k + j) % N];
    if (Math.abs(sum) >= BEND_DEG && k > until) {
      // the bend's extent: keep going while the window still turns that much the same way
      let e = k, peak = Math.abs(sum);
      for (let q = k + 1; q < k + N; q++) {
        let s2 = 0;
        for (let j = 0; j < win; j++) s2 += bend[(q + j) % N];
        if (Math.sign(s2) !== Math.sign(sum) || Math.abs(s2) < BEND_DEG) break;
        e = q; peak = Math.max(peak, Math.abs(s2));
      }
      out.push({ id: (sum > 0 ? 'R' : 'L') + Math.round(peak) + 'deg', s: k * STEP_M });
      until = e + win;
    }
  }
  return out;
}

function decisions(track, song) {
  const L = track.lengthM, D = [];
  const add = (kind, rows) => { for (const r of rows) D.push({ kind, id: r.id, s: wrapM(r.s, L), n: r.n || 1 }); };
  add('box', cluster(track.items.filter(i => i.kind === 'box').map(i => ({ id: 'box', s: i.s })), ROW_M));
  add('bubble', cluster(track.items.filter(i => i.kind === 'bubble').map(i => ({ id: 'bubble', s: i.s })), ROW_M));
  add('cut', track.routes.map(r => ({ id: r.id, s: r.openS0 })));
  add('cut', (track.forks || []).map(f => ({ id: f.id, s: f.fromS })));
  add('air', track.aerials.map(a => ({ id: a.id, s: a.rampS0 })));
  add('air', track.jumps.map(j => ({ id: 'jump-' + j.node, s: j.s })));
  const kl = getKeepLayout(track), bl = getBoudoirLayout(track), gl = getGoonworksLayout(track);
  const hz = gl ? createGoonworksHazards(track, gl) : kl ? createKeepHazards(track, kl) : bl ? createBoudoirHazards(track, bl) : null;
  const traps = [...rowsWithS(createMovingHazards(track).inspect(0).traps), ...(hz ? rowsWithS(hz.inspect(0)) : []),
    ...industryZones(track).map(z => ({ id: z.id, s: z.s })),
    ...track.courts.flatMap(c => c.holes.map(h => ({ id: h.id, s: h.s }))),
    ...track.fields.flatMap(f => f.stones.map(x => ({ id: f.id, s: x.s })))];
  add('trap', cluster(traps, 25));
  add('bend', bends(track));
  return D;
}

/** Drive two laps with the AI as the player; return lap-2 samples [{t (s since lap 2 start), s (unwrapped)}]. */
function walk(track) {
  const queue = { emit() {} }, racers = createRacers(track, feel, queue, { count: 1 }), P = racers.player;
  const ai = createAi(track, feel, { seed: 7 }), acts = createActions();
  const race = { phase: 'racing', timeS: 0, lapStartS: 0, finish() { race.phase = 'finished'; } };
  const ctx = { race, racers: [P], items: null, standings: null };
  let laps = 0, lap2T0 = 0, lastS = null, unwrapped = 0, falls = 0, wasResc = false, lap1S = 0;
  const samples = [];
  const emit = P.events.emit;
  P.events.emit = (name, p = {}) => {
    if (name === 'lap') { laps++; race.lapStartS = race.timeS; if (laps === 1) { lap2T0 = race.timeS; lap1S = p.lapTimeS; } }
    emit(name, p);
  };
  for (let i = 0; i < 60 * 60 * 12 && laps < 2; i++) {
    ai.drive(P, ctx, DT, acts);
    racers.step(DT, acts, race, { cleanPace: true, songDrive: 0 });
    race.timeS += DT;
    const resc = P.st[S.respawnPhase] !== 0;
    if (resc && !wasResc && laps === 1) falls++;
    wasResc = resc;
    if (laps === 1) {
      const s = P.st[S.s];
      if (lastS === null) unwrapped = s;
      else { let ds = s - lastS; if (ds < -track.lengthM / 2) ds += track.lengthM; if (ds > track.lengthM / 2) ds -= track.lengthM; unwrapped += ds; }
      lastS = s;
      samples.push({ t: race.timeS - lap2T0, s: unwrapped, v: P.st[S.engineV] });
    }
  }
  if (laps < 2) throw new Error(track.id + ': the AI did not finish two laps');
  return { samples, lapS: samples[samples.length - 1].t, lap1S, falls, startS: samples[0].s };
}

const fmtS = s => Math.round(s) + ' m';
const median = a => { const b = [...a].sort((x, y) => x - y), m = b.length >> 1; return b.length % 2 ? b[m] : (b[m - 1] + b[m]) / 2; };

function measure(id) {
  const json = JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8'));
  const track = loadTrack(json, { moving: true }), L = track.lengthM;
  const { samples, lapS, lap1S, falls, startS } = walk(track);
  // time at a lap s: first lap-2 sample at or past it (s measured from where lap 2 began)
  const timeAt = (s) => {
    const want = startS + wrapM(s - wrapM(startS, L), L);
    let lo = 0, hi = samples.length - 1;
    while (lo < hi) { const mid = (lo + hi) >> 1; if (samples[mid].s < want) lo = mid + 1; else hi = mid; }
    return samples[lo].t;
  };
  const sAt = (t) => { const k = samples.findIndex(x => x.t >= t); return wrapM(samples[k < 0 ? samples.length - 1 : k].s, L); };
  const song = charts[id] ? JSON.parse(readFileSync(charts[id], 'utf8')) : null;
  const D = decisions(track).map(d => ({ ...d, t: timeAt(d.s) }));
  if (song) {
    // song-timed: lap 2 starts lap1S into the race; the song loops at durationS
    const dur = song.durationS || 0, words = scriptBubbles(song, WORD_LOOK, s => pickWords(s).map(k => k >= GATE ? ROAD : k));
    const timed = [...words.map(w => ['word', w]), ...songGateCues(song).map(c => ['gate', c])];
    for (let rep = 0; dur > 0 && rep * dur < lap1S + lapS; rep++) for (const [kind, w] of timed) {
      const t = w.startS + rep * dur - lap1S;
      if (t >= 0 && t < lapS) D.push({ kind, id: w.w || w.text, t, s: sAt(t), n: 1 });
    }
  }
  D.sort((a, b) => a.t - b.t);
  const moments = [];
  for (const d of D) {
    if (d.kind === 'gate') continue;
    const last = moments[moments.length - 1];
    if (last && d.t - last.t <= MERGE_S) { last.kinds.add(d.kind); continue; }
    moments.push({ t: d.t, s: d.s, kinds: new Set([d.kind]) });
  }
  const gaps = moments.map((m, i) => {
    const next = moments[(i + 1) % moments.length], dt = i + 1 < moments.length ? next.t - m.t : next.t + lapS - m.t;
    const sec = track.sections[track.sectionAt(m.s + 1)].id, sec2 = track.sections[track.sectionAt(next.s - 1)].id;
    return { fromT: m.t, toT: m.t + dt, dt, fromS: m.s, toS: next.s, place: sec === sec2 ? sec : sec + ' > ' + sec2,
      after: [...m.kinds].join('+'), before: [...next.kinds].join('+') };
  });
  const counts = {};
  for (const d of D) counts[d.kind] = (counts[d.kind] || 0) + 1;
  const long = [...gaps].sort((a, b) => b.dt - a.dt);
  const over = gaps.filter(g => g.dt > TARGET);
  return { id, lengthM: Math.round(L), lapS: +lapS.toFixed(1), meanMps: +(L / lapS).toFixed(1), falls, words: !!song, counts,
    moments: moments.length, medianGapS: +median(gaps.map(g => g.dt)).toFixed(1),
    overTarget: over.length, overTargetS: +over.reduce((a, g) => a + g.dt, 0).toFixed(1),
    longest: long.slice(0, TOP).map(g => ({ dt: +g.dt.toFixed(1), fromS: Math.round(g.fromS), toS: Math.round(g.toS),
      fromT: +g.fromT.toFixed(1), place: g.place, after: g.after, before: g.before })) };
}

const ids = only ? [only] : COURSES.map(c => c.id);
const results = [];
for (const id of ids) {
  const r = measure(id); results.push(r);
  console.log(`\n${r.id}: ${fmtS(r.lengthM)}, flying lap ${r.lapS} s (${r.meanMps} m/s), falls ${r.falls}, words ${r.words ? 'on' : 'off'}`);
  console.log(`  decisions ${JSON.stringify(r.counts)} -> ${r.moments} moments, median gap ${r.medianGapS} s, ` +
    `${r.overTarget} gaps over ${TARGET} s (${r.overTargetS} s of the lap)`);
  for (const g of r.longest) console.log(`  ${String(g.dt).padStart(5)} s  s ${g.fromS}-${g.toS}  lap t ${g.fromT}  ${g.place}  (${g.after} -> ${g.before})`);
}
const out = arg('json', null);
if (out) writeFileSync(out, JSON.stringify(results, null, 2));
