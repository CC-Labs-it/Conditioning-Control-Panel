/* Temporary, physical song words. The song supplies every word and the only clock. */
import { S } from './state.js';
const runs = new WeakMap(), held = new WeakMap();
const smooth = x => { x = Math.max(0, Math.min(1, x)); return x * x * (3 - 2 * x); };
const frame = () => ({ p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] });
const dot = (p, row, axis) => p.reduce((v, x, i) => v + (x - row.p[i]) * row[axis][i], 0);
export function bindAirWords(st, run) { runs.set(st, run); held.delete(st); }
export function resetAirWords(value) { const run = runs.get(value) || value; if (run?.reset) run.reset(); held.delete(value); }

export function createAirWordRun(song, track) {
  const words = (song?.words || []).filter(w => typeof w.w === 'string' && w.w.trim() && w.w.length <= 32 && Number.isFinite(w.startS));
  const pool = Array.from({ length: 12 }, (_, i) => ({ id: `air-word-${i}`, text: '', s: 0, ...frame(), widthM: 12, lengthM: 14, alpha: 0, solid: false, endS: -1 }));
  const platforms = [], seen = new Set(), f = frame();
  let last = null, previous = null, rider = null, section = null;
  const run = { platforms, timeS: 0, activeSection: null, update, reset };
  function reset() { platforms.length = 0; seen.clear(); if (rider) held.delete(rider); last = previous = section = null; run.activeSection = null; for (const p of pool) { p.endS = -1; p.solid = false; p.alpha = 0; } }
  function update(t, st, { active = true, paused = false } = {}) {
    if (!active || !Number.isFinite(t) || st[S.respawnPhase]) { reset(); return run; }
    if (paused || t === last) return run;
    const p = [st[S.posX], st[S.posY], st[S.posZ]];
    if (last !== null && (t < last || t - last > .5 || previous && Math.hypot(...p.map((x, i) => x - previous[i])) > 20)) reset();
    run.timeS = t; last = t; previous = p; rider = st;
    section = !track.forkAtSeg?.(st[S.seg]) && (track.aerials || []).find(a => st[S.s] >= a.rampS0 - 45 && st[S.s] < a.landingS1);
    run.activeSection = section || null;
    track.frameAt(st[S.s], f);
    const speed = Math.max(0, st[S.velX] * f.t[0] + st[S.velY] * f.t[1] + st[S.velZ] * f.t[2]);
    if (section && speed >= 7) for (let i = 0; i < words.length; i++) {
      const w = words[i], remain = w.startS - t;
      if (seen.has(i) || remain < 0 || remain > .6) continue;
      const s = st[S.s] + speed * (remain + .35);
      if (s < section.gapS0 + 12 || s > section.gapS1 - 5) continue;
      if (pool.some(row => row.endS >= t && Math.abs(row.s - s) < Math.max(12, speed * .85))) continue;
      const row = pool.find(row => row.endS < t && held.get(st) !== row);
      if (!row) break;
      seen.add(i); track.frameAt(s, f);
      row.text = w.w.trim(); row.s = s; row.p = [f.p[0], f.p[1] + 2, f.p[2]];
      const l = Math.hypot(...f.t) || 1, h = Math.hypot(f.t[0], f.t[2]) || 1;
      row.t = f.t.map(v => v / l); row.b = [-f.t[2] / h, 0, f.t[0] / h];
      row.n = [-row.b[2] * row.t[1], row.b[2] * row.t[0] - row.b[0] * row.t[2], row.b[0] * row.t[1]];
      row.widthM = Math.min(section.widthM, Math.max(12, section.widthM * .85));
      row.lengthM = Math.max(12, Math.min(18, speed * .75));
      row.bornS = t; row.cueS = w.startS; row.endS = w.startS + 3.5; row.solid = false;
    }
    platforms.length = 0;
    for (const row of pool) if (row.endS >= t) {
      if (held.get(rider) === row && rider[S.grounded]) row.endS = Math.max(row.endS, t + 1.2);
      row.alpha = smooth((t - row.bornS) / .45) * smooth((row.endS - t) / .7);
      row.solid = row.alpha >= .98;
      if (row.alpha > 0) platforms.push(row);
    }
    return run;
  }
  return run;
}

/** Decorate the ordinary road query only on the visible footprint and an actual downward crossing. */
export function airWordQuery(st, p, query, previous = null) {
  query.airWord = null;
  const run = runs.get(st);
  if (!run || st[S.respawnPhase] || previous && Math.hypot(...p.map((v, i) => v - previous[i])) > 20) { held.delete(st); return query; }
  const old = held.get(st);
  for (const row of run.platforms) {
    if (!row.solid) continue;
    const height = dot(p, row, 'n'), x = dot(p, row, 'b'), z = dot(p, row, 't');
    const h0 = previous ? dot(previous, row, 'n') : height;
    const standing = old === row && st[S.grounded] && Math.abs(height) < .6;
    if (!(standing || previous && st[S.airborne] && h0 >= -.03 && height <= 0 && height < h0)) continue;
    const u = standing ? 1 : h0 / (h0 - height), at = previous ? previous.map((v, i) => v + (p[i] - v) * u) : p;
    if (Math.abs(x) > row.widthM / 2 || Math.abs(z) > row.lengthM / 2 ||
      Math.abs(dot(at, row, 'b')) > row.widthM / 2 || Math.abs(dot(at, row, 't')) > row.lengthM / 2) continue;
    query.airWord = row; query.onRoad = true; query.height = height;
    for (let i = 0; i < 3; i++) query.normal[i] = row.n[i];
    query.p ||= [0, 0, 0]; for (let i = 0; i < 3; i++) query.p[i] = p[i] - row.n[i] * height;
    query.railed = false;
    query.hole = null; query.stone = null;
    query.flags = []; query.edgeDist = row.widthM / 2 - Math.abs(x);
    held.set(st, row); return query;
  }
  held.delete(st); return query;
}
