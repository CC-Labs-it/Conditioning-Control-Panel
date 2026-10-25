/* Boudoir perfume: a visible timed volume slows the short line, with a permanent dry lane.
 * The caller supplies race time; the shared adapter maps it to heard song time in solo. */
import { S, RESPAWN } from './state.js';
import { getBoudoirLayout } from '../track/boudoir-layout.js';
import { machineryBeatAt, machineryRearming, beatCycle } from '../track/machineryBeat.js';

export const PERFUME = Object.freeze({ cycleS: 12, warnS: 3, sprayS: 5, activeS: 5.6,
  clearS: 9, fadeS: 10.2, rearmS: 2, heightM: 2.5, radiusM: .65, speedMps: 18, dragPerS: 3.2 });
const smooth = v => { v = Math.max(0, Math.min(1, v)); return v * v * (3 - 2 * v); };

/** Shared phase authority for simulation and drawing. All fades last at least 600 ms. */
export function perfumePhase(zone, timeS, out = {}, beat = null) {
  if (beat) {
    const step = beat.beatS, offset = Math.round((zone.offsetS || 0) / step);
    const warn = Math.round(PERFUME.warnS / step), spray = warn + Math.ceil((PERFUME.sprayS - PERFUME.warnS) / step);
    const active = spray + Math.ceil((PERFUME.activeS - PERFUME.sprayS) / step);
    const clear = Math.max(active + 1, Math.round(PERFUME.clearS / step));
    const fade = clear + Math.ceil((PERFUME.fadeS - PERFUME.clearS) / step);
    const period = Math.max(Math.ceil(PERFUME.cycleS / step / 4) * 4, Math.ceil((fade + 1) / 4) * 4);
    const t = beatCycle(beat.beat + offset, period);
    // Initial pickup and every seek wait for a full clear/warn/spray sequence.
    const safe = machineryRearming(beat, offset, period) || beat.beat < beatCycle(-offset, period);
    const phase = safe ? 'warn' : t < warn || t >= fade ? 'clear' : t < spray ? 'warn' : t < active ? 'spray' : t < clear ? 'active' : 'fade';
    const mist = safe || t < spray || t >= fade ? 0 : t < active ? smooth((t - spray) / (active - spray))
      : t < clear ? 1 : 1 - smooth((t - clear) / (fade - clear));
    const warning = safe ? 1 : phase === 'warn' ? smooth((t - warn) * step / .6)
      : phase === 'spray' || phase === 'active' ? 1 : phase === 'fade' ? mist : 0;
    return Object.assign(out, { phase, mist, warning, active: phase === 'active', cycleS: t * step });
  }
  const K = PERFUME, t = ((Math.max(0, Number.isFinite(timeS) ? timeS : 0) + (zone.offsetS || 0)) % K.cycleS + K.cycleS) % K.cycleS;
  const phase = t < K.warnS || t >= K.fadeS ? 'clear' : t < K.sprayS ? 'warn' : t < K.activeS ? 'spray' : t < K.clearS ? 'active' : 'fade';
  const mist = t < K.sprayS || t >= K.fadeS ? 0 : t < K.activeS ? smooth((t - K.sprayS) / (K.activeS - K.sprayS))
    : t < K.clearS ? 1 : 1 - smooth((t - K.clearS) / (K.fadeS - K.clearS));
  const warning = phase === 'warn' ? smooth((t - K.warnS) / .6) : phase === 'spray' || phase === 'active' ? 1
    : phase === 'fade' ? mist : 0;
  return Object.assign(out, { phase, mist, warning, active: phase === 'active', cycleS: t });
}

export function perfumeZones(track, zones = getBoudoirLayout(track)?.perfume || []) {
  return zones.map((z, i) => {
    const row = { id: z.id || 'perfume-' + i, s0: z.s0, s1: z.s1, x: z.x ?? 0, w: z.w, offsetS: z.offsetS ?? 0 };
    if (![row.s0, row.s1, row.x, row.w, row.offsetS].every(Number.isFinite) || row.s0 < 0 ||
        row.s1 <= row.s0 || row.s1 > track.lengthM || row.w <= 0) throw new Error('Invalid perfume zone ' + row.id);
    let width = Infinity;
    for (let s = row.s0; s <= row.s1 + 4; s += 4) width = Math.min(width, track.frameAt(Math.min(s, row.s1)).w);
    const left = width / 2 + row.x - row.w / 2, right = width / 2 - row.x - row.w / 2;
    if (Math.min(left, right) < 0 || Math.max(left, right) < 6) throw new Error('Perfume zone needs a 6 m dry bypass: ' + row.id);
    return { ...row, dryX: right >= left ? row.x + row.w / 2 + right / 2 : row.x - row.w / 2 - left / 2 };
  });
}

function interval(a, b, lo, hi, bounds) {
  const d = b - a;
  if (Math.abs(d) < 1e-8) return a >= lo && a <= hi;
  const x = (lo - a) / d, y = (hi - a) / d;
  bounds[0] = Math.max(bounds[0], Math.min(x, y)); bounds[1] = Math.min(bounds[1], Math.max(x, y));
  return bounds[0] <= bounds[1];
}

export function createBoudoirPerfume(track, events, { zones } = {}) {
  const rows = perfumeZones(track, zones), K = PERFUME, pose = {}, bounds = [0, 1];
  let previous = new WeakMap(), lastT = null, safeUntil = -Infinity;
  const api = { rows, ready: true, hits: 0, update, reset,
    inspect(timeS = lastT ?? 0) { return { ready: api.ready, hits: api.hits, timeS, zones: rows.map(z => ({ ...z, ...perfumePhase(z, timeS, {}, machineryBeatAt(track, timeS)) })) }; } };
  function reset() { previous = new WeakMap(); lastT = null; safeUntil = -Infinity; api.ready = true; api.hits = 0; }
  function update(racers, timeS, dt = 1 / 60, { paused = false } = {}) {
    if (paused || !rows.length || !Number.isFinite(timeS) || !Number.isFinite(dt) || dt <= 0) return;
    // Seeks/loops clear swept paths and give two full seconds of warning before contact returns.
    if (lastT !== null && (timeS < lastT - .02 || timeS - lastT > .5)) {
      previous = new WeakMap(); safeUntil = timeS + K.rearmS;
    }
    api.ready = timeS >= safeUntil;
    for (const racer of racers) {
      if (track.forkAtSeg?.(racer.st[S.seg])) { previous.delete(racer); continue; }
      const st = racer.st, now = { s: st[S.s], x: st[S.lateral], h: st[S.height], t: timeS, inside: null };
      const prev = previous.get(racer) || now;
      previous.set(racer, now);
      if (!api.ready || racer.finished || racer.kind === 'remote' || st[S.respawnPhase] !== RESPAWN.NONE || Math.abs(now.s - prev.s) > 25) continue;
      let exposure = 0, hit = null;
      for (const zone of rows) {
        bounds[0] = 0; bounds[1] = 1;
        if (!interval(prev.s, now.s, zone.s0, zone.s1, bounds) ||
            !interval(prev.x, now.x, zone.x - zone.w / 2 - K.radiusM, zone.x + zone.w / 2 + K.radiusM, bounds) ||
            !interval(prev.h, now.h, -.3, K.heightM, bounds)) continue;
        // Midpoint probes integrate the swept interval instead of treating a whole tick as contact.
        let active = 0;
        for (let n = 0; n < 4; n++) {
          const a = bounds[0] + (bounds[1] - bounds[0]) * (n + .5) / 4;
          const t = prev.t + (timeS - prev.t) * a;
          if (t >= safeUntil && perfumePhase(zone, t, pose, machineryBeatAt(track, t)).active) active++;
        }
        const share = (bounds[1] - bounds[0]) * active / 4;
        if (share > exposure) { exposure = share; hit = zone; }
      }
      if (!hit) continue;
      const wasInside = prev.inside; now.inside = hit.id;
      const drag = 1 - Math.exp(-K.dragPerS * Math.min(dt, .1) * exposure);
      // Preserve vertical jump/fall velocity, steering and item impulses. Only planar excess is dragged.
      const n = track.frameAt(now.s).n;
      const normalV = st[S.velX] * n[0] + st[S.velY] * n[1] + st[S.velZ] * n[2];
      const planar = [0, 1, 2].map(j => st[S.velX + j] - normalV * n[j]);
      const speed = Math.hypot(...planar), next = speed - Math.max(0, speed - K.speedMps) * drag;
      if (speed > K.speedMps) for (let j = 0; j < 3; j++) st[S.velX + j] = planar[j] * next / speed + normalV * n[j];
      const engine = st[S.engineV];
      st[S.engineV] = Math.sign(engine) * (Math.abs(engine) - Math.max(0, Math.abs(engine) - K.speedMps) * drag);
      if (wasInside !== hit.id) {
        api.hits++;
        events?.emit('perfume-contact', { racer: racer.i, zone: hit.id, speedMps: K.speedMps });
      }
    }
    lastT = timeS;
  }
  return api;
}
