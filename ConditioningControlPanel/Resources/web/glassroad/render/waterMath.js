/* render/waterMath.js - the water lane's numbers and pure shapes (no three; tests/render-water.test.mjs).
 *
 * Stream and pool vertices (road attribute `surface` 3 / 4) render as water in render/materials/water.js;
 * the chute (stream nodes on a steep grade) wears a waterfall sheet (render/waterfall.js). The shapes here
 * are the same ones the shader draws, as numbers: the ripple scroll from the flow attribute, the foam
 * weights, sheet placement, water volume bounds, wakes and splashes. Existing art masks replace
 * procedural shading fields without changing these physical placement rules.
 */

export const WATER_LOOK = Object.freeze({
  rippleM: [1.7, 0.9],          // the two ripple fields' wavelengths (metres)
  rippleTurnRad: [0, 0.62],     // field B is turned off the flow so the two never lock into one grid
  rippleSpeedMul: [1, 0.55],    // scroll speed = baseScrollMps + |flow| * mul (metres per second)
  baseScrollMps: 0.35,          // a still pool still breathes
  foamRailM: [0.7, 2.1],        // rail foam: full inside foamRailM[0] of the true edge, gone past [1]
  poolFoamMul: 1.5,             // a pool's edges foam more (its water meets the road, not a current)
  foamLatMul: 0.05,             // lateral flow pushes a little foam to the side it runs into (per m/s)
  bump: 0.42,                   // ripple slope gain on the normal
  specPow: 42, specGain: 0.5,   // the soft specular
  underglow: 0.16,              // the faint light from below (section colour)
  alpha: 0.9, foamGain: 0.55,
  gentleScrollMul: 0.5,         // Gentle: the scroll clock at half speed, nothing pulses
  chuteGradeDeg: 25,            // a stream node steeper than this is a chute (SIM's water.chuteGradeDeg twin)
  sheetLiftM: 0.12, sheetOverM: 0.35, sheetRunM: 2.5,   // shallow water stays close to the physical contact plane
  streakM: 2.6, streakW: 0.45, sheetSpeedMps: 9, sheetAlpha: 0.4,
  sprayEveryS: 0.24, sprayNearM: 70, sprayCount: 3, spraySpeedMps: 2.4,
  wakeWidthMul: 1.8, wakeWhite: 0.65,
  splashRingM: [2.2, 5.5], splashDownMps: [2, 12], splashCount: 14,
});

export const WATER_VOLUME = Object.freeze({ liftM: 0.1, waveM: 0.025, depthM: 1.4, stepM: 2, spillDropM: 16 });

/** Wet stretches with physical support. The reservoir renderer owns submerged waterlines. */
export function waterRuns(track) {
  const points = track.nodeS ? [...track.nodeS, track.lengthM] : Array.from({ length: Math.ceil(track.lengthM / 2) + 1 }, (_, i) => Math.min(track.lengthM, i * 2));
  const runs = [];
  for (let i = 0; i < points.length - 1; i++) {
    const a = points[i], b = points[i + 1], f = track.frameAt((a + b) / 2);
    if (b <= a) continue;
    const section = track.sections?.find(v => a >= v.fromS && a < v.toS)?.id || '';
    if (!isWaterSurface(f.surface) || /submerged|emerge/.test(section) || f.n[1] < 0.45 || f.flags?.includes('jump')) continue;
    const last = runs.at(-1);
    if (last && Math.abs(last.toS - a) < 0.001) last.toS = b;
    else runs.push({ fromS: a, toS: b });
  }
  return runs;
}

/** A shallow canal waterline meets its banks instead of climbing their walls. */
export function waterHalfWidth(track, s, frame = track.frameAt(s)) {
  const half = Math.max(0.1, frame.w / 2 - 0.04);
  if (!track.canalProfile || track.canalProfile(s, half, {}).height <= WATER_VOLUME.liftM) return half;
  let lo = 0, hi = half;
  for (let i = 0; i < 14; i++) { const x = (lo + hi) / 2; if (track.canalProfile(s, x, {}).height > WATER_VOLUME.liftM) hi = x; else lo = x; }
  return lo;
}

/** Authored spills are clipped to wet runs; ordinary courses get short alternating edge spills. */
export function waterSpills(track, runs = waterRuns(track)) {
  const requested = Array.isArray(track.waterSpills) ? track.waterSpills : runs.filter(r => r.toS - r.fromS > 18).map((r, i) => {
    const length = Math.min(24, (r.toS - r.fromS) * 0.35), fromS = r.fromS + (r.toS - r.fromS - length) * 0.55;
    return { fromS, toS: fromS + length, side: i % 2 ? 1 : -1, dropM: WATER_VOLUME.spillDropM };
  });
  return requested.flatMap(spill => {
    if (![spill.fromS, spill.toS].every(Number.isFinite) || ![-1, 1].includes(spill.side)) return [];
    return runs.flatMap(run => {
      const fromS = Math.max(run.fromS, spill.fromS), toS = Math.min(run.toS, spill.toS);
      if (toS - fromS < 1) return [];
      const mid = (fromS + toS) / 2;
      if (waterHalfWidth(track, mid) < track.frameAt(mid).w / 2 - 0.1) return [];
      return [{ fromS, toS, side: spill.side, dropM: Math.max(3, Math.min(40, Number(spill.dropM) || WATER_VOLUME.spillDropM)) }];
    });
  });
}

/** Road attribute codes (track/mesh.js `surface`). */
export const SURFACE_CODE = Object.freeze({ glass: 0, grip: 1, haze: 2, stream: 3, pool: 4 });
export const isWaterCode = (c) => c >= 2.5 && c < 4.5;
export const isWaterSurface = (s) => s === 'stream' || s === 'pool';

/** Flow [along, lat] (m/s) -> unit scroll direction in uv space ([du, dv]: u lateral, v along) and the speed. */
export function flowDir(flow) {
  const a = (flow && +flow[0]) || 0, l = (flow && +flow[1]) || 0, mag = Math.hypot(a, l);
  return mag > 1e-6 ? { dir: [l / mag, a / mag], mag } : { dir: [0, 1], mag: 0 };
}

/**
 * Where the two ripple fields have moved to at time t (metres, [du, dv] each): the pattern is sampled at
 * p minus this, so the ripples travel WITH the current. Still water still drifts at baseScrollMps.
 */
export function rippleScroll(flow, t, { gentle = false } = {}, L = WATER_LOOK) {
  const { dir, mag } = flowDir(flow), g = gentle ? L.gentleScrollMul : 1;
  return L.rippleM.map((_, i) => {
    const c = Math.cos(L.rippleTurnRad[i]), s = Math.sin(L.rippleTurnRad[i]);
    const d = [dir[0] * c - dir[1] * s, dir[0] * s + dir[1] * c];
    const v = (L.baseScrollMps + mag * L.rippleSpeedMul[i]) * g;
    return [d[0] * v * t, d[1] * v * t];
  });
}

const smooth = (a, b, x) => { const k = Math.min(1, Math.max(0, (x - a) / Math.max(b - a, 1e-9))); return k * k * (3 - 2 * k); };

/**
 * Foam weight 0..1 at trueEdge metres in from the road edge, lateral u (+ right) on a surface code with
 * flow [along, lat]: foam toward the rails, more on a pool's edges, a little more on the side a lateral
 * current runs into. Zero off water.
 */
export function foamWeight(trueEdge, code, { u = 0, flow = null } = {}, L = WATER_LOOK) {
  if (!isWaterCode(code)) return 0;
  const rail = 1 - smooth(L.foamRailM[0], L.foamRailM[1], Math.max(0, trueEdge));
  const pool = code >= 3.5 ? L.poolFoamMul : 1;
  const lat = flow ? (+flow[1] || 0) : 0;
  const side = lat !== 0 && Math.sign(u) === Math.sign(lat) ? Math.abs(lat) * L.foamLatMul : 0;
  return Math.min(1, rail * pool + side * rail);
}

/** Grade in degrees between two road samples (vertical rise over along distance). */
export function gradeDeg(dy, ds) { return ds > 1e-9 ? Math.atan2(Math.abs(dy), ds) * 180 / Math.PI : 0; }

/**
 * Chute runs from road samples [{ s, y, surface }] in ascending s: every run of stream samples whose
 * grade (between neighbours) is at least chuteGradeDeg, as { s0, s1 } (metres; s0 the lip, s1 the foot).
 * A pool, glass or flat stream sample ends a run. The sheet strip is laid over sheetRunM past each end.
 */
export function sheetRuns(samples, L = WATER_LOOK) {
  const out = [];
  let start = -1;
  for (let i = 0; i < samples.length; i++) {
    const a = samples[i], b = samples[i + 1];
    const steep = !!b && a.surface === 'stream' && b.surface === 'stream' && gradeDeg(b.y - a.y, b.s - a.s) >= L.chuteGradeDeg;
    if (steep && start < 0) start = i;
    if (!steep && start >= 0) { out.push({ s0: samples[start].s, s1: a.s }); start = -1; }
  }
  if (start >= 0) out.push({ s0: samples[start].s, s1: samples[samples.length - 1].s });
  return out;
}

/** Per-metre surface codes for O(1) water lookups by s (the wakes read it; no per-frame track query). */
export function waterTable(surfaceAt, lengthM) {
  const n = Math.max(1, Math.ceil(lengthM)), t = new Uint8Array(n);
  for (let i = 0; i < n; i++) t[i] = SURFACE_CODE[surfaceAt((i + 0.5) * lengthM / n)] || 0;
  return t;
}
export function tableCode(table, s, lengthM) {
  if (!table || !table.length || !(lengthM > 0)) return 0;
  const i = Math.floor(((s % lengthM) + lengthM) % lengthM / lengthM * table.length);
  return table[Math.min(table.length - 1, Math.max(0, i))];
}

/** A boost wake on water: wider and whiter (widthMul, and the share of white mixed into its colour). */
export function wakeOnWater(code, L = WATER_LOOK) {
  return isWaterCode(code) ? { widthMul: L.wakeWidthMul, white: L.wakeWhite } : { widthMul: 1, white: 0 };
}

/** The splash ring's size (metres) from the landing's downward speed; Gentle keeps the same size. */
export function splashRingM(down, L = WATER_LOOK) {
  const k = smooth(L.splashDownMps[0], L.splashDownMps[1], Math.max(0, +down || 0));
  return L.splashRingM[0] + (L.splashRingM[1] - L.splashRingM[0]) * k;
}

/** The cue sizes: splash dip (fov degrees, negative), stream lift, chute lean; a Gentle kick is the caller's. */
export const WATER_CUES = Object.freeze({ splashDipDeg: -2.5, splashOutS: 0.5, streamLiftDeg: 1.5, streamInS: 0.3, streamOutS: 0.8, chuteLeanDeg: 4, chuteHoldS: 0.3, chuteOutS: 0.6 });
