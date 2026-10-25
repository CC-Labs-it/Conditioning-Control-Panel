/* Scripted word flights. Only a locally bound run owns these forgiving aerial crossings. */
import { S, RESPAWN, setPos, setVel, setQuat } from './state.js';
import { qlook } from '../core/math.js';
import { resetGlide } from './glide.js';
import { cancelDrift } from './drift.js';
import { boostFloor } from './boost.js';
const runs = new WeakMap(), held = new WeakMap();
const clamp = (x, lo, hi) => Math.max(lo, Math.min(hi, x));
const smooth = x => { x = clamp(x, 0, 1); return x * x * (3 - 2 * x); };
const frame = () => ({ p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0] });
const dot = (p, row, axis) => p.reduce((v, x, i) => v + (x - row.p[i]) * row[axis][i], 0);
const FALLBACK = ['FLOAT', 'BREATHE', 'DRIFT', 'RISE'];
const HOLD_S = .06, APPEAR_S = .42, FADE_S = .5;
const LYRIC_WINDOW_S = 1.25;
export function bindAirWords(st, run) { runs.set(st, run); held.delete(st); }
export function resetAirWords(value) { const run = runs.get(value) || value; if (run?.reset) run.reset(); held.delete(value); }
export function stepAirWords(st, action, feel, dt, events) {
  return runs.get(st)?.step(st, action, feel, dt, events) || false;
}

export function createAirWordRun(song, track) {
  let words = [];
  function setSong(nextSong) {
    words = (nextSong?.words || []).filter(w => typeof w.w === 'string' && w.w.trim() && w.w.length <= 32 && Number.isFinite(w.startS));
  }
  setSong(song);
  const pool = Array.from({ length: 12 }, (_, i) => ({ id: `air-word-${i}`, text: '', s: 0, ...frame(),
    widthM: 9, lengthM: 3.2, alpha: 0, solid: false, bornS: Infinity, endS: -1 }));
  const platforms = [], f = frame(), next = frame(), q = [0, 0, 0, 1];
  let clock = 0, songS = 0, rider = null, previous = null, route = null, paused = false, enabled = true;
  const run = { platforms, timeS: 0, activeSection: null, update, reset, step, setSong };
  function reset() {
    platforms.length = 0;
    if (rider) held.delete(rider);
    route = previous = null; run.activeSection = null; clock = run.timeS = 0;
    for (const p of pool) { p.bornS = Infinity; p.endS = -1; p.solid = false; p.alpha = 0; }
  }
  function update(t, st, { active = true, paused: stop = false } = {}) {
    paused = stop; enabled = active;
    if (!active || st[S.respawnPhase] && st[S.respawnPhase] !== RESPAWN.GRACE) { reset(); return run; }
    if (stop) return run;
    const p = [st[S.posX], st[S.posY], st[S.posZ]];
    if (previous && Math.hypot(...p.map((x, i) => x - previous[i])) > 20) reset();
    previous = p; rider = st;
    // A seek only selects future lettering. It cannot remove a word under an airborne racer.
    if (Number.isFinite(t)) songS = t;
    renderRows();
    return run;
  }
  function renderRows() {
    platforms.length = 0; run.timeS = clock;
    for (const row of pool) {
      row.alpha = smooth((clock - row.bornS) / .34) * smooth((row.endS - clock) / FADE_S);
      row.solid = row.alpha >= .98;
      if (row.alpha > 0) platforms.push(row);
    }
    // Only the departing and approaching word may share the view.
    while (platforms.length > 2) {
      const expired = platforms.shift(); expired.alpha = 0; expired.solid = false; expired.endS = clock;
    }
  }
  function lyric(at, i) {
    if (!words.length) return FALLBACK[i % FALLBACK.length];
    let best = words[0];
    for (const w of words) if (Math.abs(w.startS - at) < Math.abs(best.startS - at)) best = w;
    return Math.abs(best.startS - at) <= LYRIC_WINDOW_S ? best.w.trim() : FALLBACK[i % FALLBACK.length];
  }
  function lettering(row, at) {
    row.text = lyric(at, row.index);
    row.widthM = Math.min(row.maxWidthM, clamp(row.text.length * 1.12, 4.2, 10));
  }
  function begin(st, zone, events) {
    const start = track.frameAt(zone.gapS0, frame()), end = track.frameAt(zone.gapS1, frame());
    const count = Math.min(pool.length, Math.max(2, Math.ceil((zone.gapS1 - zone.gapS0) / 22) - 1));
    const points = [{ s: zone.gapS0, y: start.p[1], row: null }];
    for (let i = 0; i < count; i++) {
      const u = (i + 1) / (count + 1), s = zone.gapS0 + (zone.gapS1 - zone.gapS0) * u;
      track.frameAt(s, f);
      const row = pool[i], h = Math.hypot(f.t[0], f.t[2]) || 1;
      row.s = s; row.p = [f.p[0], start.p[1] + (end.p[1] - start.p[1]) * u - Math.sin(u * Math.PI) * 2, f.p[2]];
      const yaw = (i % 2 ? 1 : -1) * .085, c = Math.cos(yaw), sn = Math.sin(yaw);
      const tx = f.t[0] / h, tz = f.t[2] / h;
      row.t = [tx * c - tz * sn, 0, tz * c + tx * sn]; row.b = [-row.t[2], 0, row.t[0]]; row.n = [0, 1, 0];
      row.maxWidthM = Math.min(zone.widthM * .58, 10); row.lengthM = 3.2;
      row.bornS = Infinity; row.endS = Infinity; row.landedS = Infinity; row.catchS = Infinity;
      row.index = i; row.section = zone.id;
      lettering(row, songS + (s - st[S.s]) / 24);
      points.push({ s, y: row.p[1], row });
    }
    points.push({ s: zone.gapS1 + 2, y: track.frameAt(zone.gapS1 + 2, next).p[1], row: null });
    track.frameAt(st[S.s], f);
    const speed = Math.max(0, st[S.velX] * f.t[0] + st[S.velY] * f.t[1] + st[S.velZ] * f.t[2]);
    route = { zone, points, s: st[S.s], speed: clamp(speed, 4, 50), lateral: st[S.lateral],
      stage: 'ramp', leg: 0, elapsed: 0, hold: 0, age: 0,
      entry: [st[S.posX] - f.p[0] - f.b[0] * st[S.lateral], st[S.posY] - f.p[1] - f.b[1] * st[S.lateral], st[S.posZ] - f.p[2] - f.b[2] * st[S.lateral]] };
    run.activeSection = zone;
    resetGlide(st); cancelDrift(st, 'slip', events);
  }
  function launch(st, events) {
    const r = route, from = r.points[r.leg], to = r.points[r.leg + 1];
    r.stage = 'flight'; r.elapsed = 0;
    r.duration = clamp((to.s - from.s) / 22, 1.08, 1.3);
    track.frameAt(from.s, f); from.offset ||= f.b.map(v => v * r.lateral);
    track.frameAt(to.s, f); to.defaultOffset = f.b.map(v => v * r.lateral);
    to.offset = [...to.defaultOffset]; to.catchU = null;
    if (to.row) {
      lettering(to.row, songS + r.duration);
      to.row.catchPoints = null; to.row.catchText = null;
      to.row.bornS = clock + r.duration - APPEAR_S; to.row.endS = Infinity;
      to.row.catchS = clock + r.duration;
    }
    st[S.airborne] = 1; st[S.grounded] = 0; st[S.airTime] = 0; st[S.hopT] = 0;
    if (from.row) events.emit('air-word-hop', { id: from.row.id, section: r.zone.id, index: from.row.index, s: from.s, p: [...from.row.p] });
    else events.emit('airborne', { cause: 'lip' });
  }
  function step(st, action, feel, dt, events) {
    if (!enabled || !(dt > 0)) return false;
    if (paused) return !!route;
    if (st[S.respawnPhase] && st[S.respawnPhase] !== RESPAWN.GRACE) { reset(); return false; }
    if (!route) {
      if (track.forkAtSeg?.(st[S.seg])) return false;
      const zone = track.aerials?.find(z => st[S.s] >= z.rampS0 && st[S.s] < z.gapS0 + .5);
      if (!zone || Math.abs(st[S.lateral]) > track.frameAt(st[S.s], f).w / 2 + 1 || Math.abs(st[S.height]) > 8) return false;
      begin(st, zone, events);
    }
    const r = route;
    if (Math.abs(st[S.s] - r.s) > 20) { reset(); return false; }
    dt = Math.min(dt, .1); clock += dt; r.age += dt;
    const old = [st[S.posX], st[S.posY], st[S.posZ]], steering = clamp(Number(action.steer) || 0, -1, 1);
    const limit = Math.min(2.6, r.zone.widthM * .18), target = steering * limit;
    r.lateral += (target - r.lateral) * (1 - Math.exp(-dt / .5));
    let y = null, onGround = false, end = false, offset = null;
    if (r.stage === 'ramp' || r.stage === 'exit') {
      r.speed += clamp(24 - r.speed, -14 * dt, 14 * dt);
      r.s += r.speed * dt; onGround = true;
      if (r.stage === 'ramp' && r.s >= r.zone.gapS0) { r.s = r.zone.gapS0; launch(st, events); onGround = false; }
      if (r.stage === 'exit' && r.s >= Math.min(r.zone.landingS1 - 2, r.zone.gapS1 + 12)) end = true;
    } else if (r.stage === 'hold') {
      onGround = true; y = r.points[r.leg].y; r.hold -= dt;
      offset = r.points[r.leg].offset;
      if (r.hold <= 0) { r.points[r.leg].row.endS = clock + FADE_S; launch(st, events); onGround = false; }
    } else {
      r.elapsed = Math.min(r.duration, r.elapsed + dt);
      const u = r.elapsed / r.duration, from = r.points[r.leg], to = r.points[r.leg + 1];
      const candidates = to.row?.catchText === to.row?.text && to.row?.catchPoints;
      if (to.catchU === null && candidates?.length) {
        let best = null, score = Infinity;
        for (const point of candidates) {
          if (!Number.isFinite(point.x) || !Number.isFinite(point.z) || Math.abs(point.x) > .5 || Math.abs(point.z) > .5) continue;
          const distance = (point.x * to.row.widthM - r.lateral) ** 2 + (point.z * to.row.lengthM) ** 2;
          if (distance < score) { best = point; score = distance; }
        }
        if (best) {
          to.offset = to.row.b.map((v, i) => v * best.x * to.row.widthM + to.row.t[i] * best.z * to.row.lengthM);
          to.catchU = u;
        }
      }
      const catchBlend = to.catchU === null ? 0 : smooth((u - to.catchU) / Math.max(.001, 1 - to.catchU));
      track.frameAt(from.s + (to.s - from.s) * u, f);
      offset = from.offset.map((v, i) => v * (1 - u) + to.defaultOffset[i] * u +
        (to.offset[i] - to.defaultOffset[i]) * catchBlend + f.b[i] * steering * 1.2 * Math.sin(Math.PI * u));
      r.s = from.s + (to.s - from.s) * u;
      // Zero vertical velocity at either contact avoids the hard down/up reversal.
      y = from.y + (to.y - from.y) * smooth(u) + (r.leg === 0 ? 5.4 : 3.1) * Math.sin(Math.PI * u) ** 2;
      st[S.airTime] += dt;
      if (u >= 1) {
        st[S.airborne] = 0; st[S.grounded] = 1; onGround = true;
        events.emit('land', { tier: to.row ? 0 : 1, airTime: st[S.airTime], airWord: !!to.row });
        if (to.row) {
          to.row.bornS = Math.min(to.row.bornS, clock - .34);
          to.row.landedS = clock;
          events.emit('air-word-land', { id: to.row.id, text: to.row.text, s: to.s });
          r.leg++; r.stage = 'hold'; r.hold = HOLD_S; held.set(st, to.row);
        } else {
          r.stage = 'exit'; r.speed = 24; held.delete(st);
          r.lateral = to.offset.reduce((v, x, i) => v + x * f.b[i], 0);
        }
      }
    }
    track.frameAt(r.s, f);
    const entry = 1 - smooth(r.age / .3), p = f.p.map((v, i) => v + f.b[i] * r.lateral + r.entry[i] * entry);
    if (offset) for (let i = 0; i < 3; i++) p[i] = f.p[i] + offset[i] + r.entry[i] * entry;
    if (y !== null) p[1] = y + r.entry[1] * entry;
    const v = p.map((x, i) => (x - old[i]) / dt), h = Math.hypot(f.t[0], f.t[2]) || 1;
    const pitch = onGround ? f.t[1] : clamp(v[1] / Math.max(12, Math.hypot(v[0], v[2])), -.25, .35);
    const forward = [f.t[0] / h, pitch, f.t[2] / h], length = Math.hypot(...forward);
    setPos(st, p); setVel(st, v); setQuat(st, qlook(q, forward.map(x => x / length), [0, 1, 0]));
    st[S.s] = r.s; st[S.seg] = f.seg; st[S.segT] = f.segT || 0;
    st[S.lateral] = p.reduce((sum, value, i) => sum + (value - f.p[i]) * f.b[i], 0);
    st[S.height] = p[1] - f.p[1]; st[S.edgeT] = 0; st[S.yawRate] = 0;
    st[S.engineV] = r.stage === 'ramp' ? r.speed : 24; st[S.steerSlew] = steering;
    st[S.speedPct] = clamp(Math.hypot(v[0], v[2]) / feel.get('speed.topMps'), 0, feel.get('speed.maxPct'));
    st[S.hopT] += dt; st[S.stalled] = Math.max(0, st[S.stalled] - dt);
    boostFloor(st, feel, feel.get('speed.topMps'), dt, events);
    if (onGround) { st[S.grounded] = 1; st[S.airborne] = 0; st[S.latchSeg] = f.seg; }
    else { st[S.grounded] = 0; st[S.airborne] = 1; }
    renderRows();
    if (end) { run.activeSection = null; route = null; }
    return true;
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
