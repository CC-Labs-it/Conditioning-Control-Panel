/* fx/reactions.js - the fx state the renderer and camera read, fed by the bus.
 *
 * createFx subscribes every entry of the FX_CUES registry (fx/cues.js) to its event and
 * runs the envelopes once per frame. It never switches on an event name: a new reaction
 * is a new registry key. An envelope rises to a peak (IN, Gentle rise floor), holds while
 * held or for holdS, then eases back to rest over outS (OUT, Gentle fade length). Pausing
 * freezes every envelope. update() also advances the photosafe window.
 */
import { clamp, smoothstep } from '../core/math.js';
import { S } from '../sim/state.js';
import { createShake } from './shake.js';
import { FX_CUES } from './cues.js';

const CHANNELS = ['bloom', 'band', 'push', 'fov', 'lines', 'bandSpeed', 'burst', 'squash', 'stretch',
  'dust', 'edge', 'duck', 'tendril', 'rescue', 'white', 'flash', 'orbit', 'rumble', 'thud', 'pad'];

export function restState() {
  return {
    shake: [0, 0, 0], fovKick: 0, camPush: 0, bloomBoost: 0, bandSpeedMul: 1, bandBright: 0,
    paletteMix: { from: 0, to: 0, t: 1 }, sparkTier: 0, sparkSide: 0, sparkBurst: 0, squash: 0,
    dust: 0, padRing: 0, padGlow: 0, speedLines: 0, edgeGlow: 0, edgeSide: 0, rescueGlow: 0, tendril: 0,
    whiteFade: 0, flashLevel: 0, orbit: 0, duck: 0,
    rumble: 0, // added: gamepad rumble 0..1 for boot (zero under Gentle)
  };
}

/** Section weights of the palette on screen now: {sectionIdx: weight}, tiny weights dropped. */
export function shownWeights(pm) {
  const w = {}, t = pm.t, add = (k, v) => { w[k] = (w[k] || 0) + v; };
  if (pm.base) for (const [k, v] of Object.entries(pm.base)) add(k, v * (1 - t));
  else add(pm.from, 1 - t);
  add(pm.to, t);
  let sum = 0;
  for (const k of Object.keys(w)) { if (w[k] < 0.01) delete w[k]; else sum += w[k]; }
  for (const k of Object.keys(w)) w[k] /= sum;
  return w;
}

const newEnv = () => ({ v: 0, from: 0, peak: 0, inS: 0, holdS: 0, outS: 0, t: 0, held: false, on: false });

function stepEnv(e, dt) {
  if (!e.on) return 0;
  e.t += dt;
  if (e.t < e.inS) e.v = e.from + (e.peak - e.from) * smoothstep(0, 1, e.t / e.inS);
  else if (e.held || e.t < e.inS + e.holdS) e.v = e.peak;
  else {
    const k = e.outS > 0 ? (e.t - e.inS - e.holdS) / e.outS : 1;
    if (k >= 1) { e.v = 0; e.on = false; } else e.v = e.peak * (1 - smoothstep(0, 1, k));
  }
  return e.v;
}

/** @returns {{state: object, update(dt: number, kart?: Float32Array, race?: object): void, reset(): void, dispose(): void}} */
export function createFx(bus, feel, { gentle, photosafe }) {
  const state = restState();
  const ch = Object.fromEntries(CHANNELS.map((n) => [n, newEnv()]));
  const shake = createShake(feel);
  let padT = -1, padLife = 1;

  const fx = {
    state, ch, shake, gentle, feel, paused: false,
    fire(name, peak, inS, holdS, outS) {
      const e = ch[name];
      e.from = e.v; e.peak = peak; e.inS = inS > 0 || gentle.on ? gentle.rise(inS) : 0;
      e.holdS = holdS; e.outS = gentle.fade(outS); e.t = 0; e.held = false; e.on = true;
      if (e.inS === 0) e.v = peak;
    },
    hold(name, peak, inS) { fx.fire(name, peak, inS, 0, 0); ch[name].held = true; },
    release(name, outS) {
      const e = ch[name];
      if (!e.on) return;
      e.peak = e.v; e.inS = 0; e.holdS = 0; e.t = 0; e.held = false; e.outS = gentle.fade(outS);
    },
    ask: (kind) => photosafe.request(kind),
    /** Pad ring: padRing runs 0..1 over lifeS (scale); its glow is an envelope, so the rise floor applies. */
    pad(lifeS, inS) {
      padT = 0; padLife = Math.max(lifeS, 1e-3);
      const r = inS > 0 || gentle.on ? gentle.rise(inS) : 0;
      fx.fire('pad', 1, inS, 0, Math.max(padLife - r, 1e-3));
    },
    /** Crossfade to palette `to` after waitS; a fade still running becomes the start blend (base). */
    palette(from, to, s, waitS = 0) {
      const pm = state.paletteMix;
      const base = pm.t < 1 || pm.base ? shownWeights(pm) : null;
      state.paletteMix = { from, to, t: 0, base, waitS };
      fx.paletteS = gentle.fade(s);
    },
    paletteS: 1,
  };

  const offs = Object.entries(FX_CUES).map(([name, react]) => bus.on(name, (p) => react(fx, p || {})));

  function update(dt, kart) {
    if (fx.paused || !(dt > 0)) return;
    photosafe.update(dt);
    for (const n of CHANNELS) stepEnv(ch[n], dt);
    state.bloomBoost = clamp(ch.bloom.v, 0, 1);
    state.bandBright = clamp(ch.band.v, 0, 1);
    state.camPush = ch.push.v;
    state.fovKick = ch.fov.v;
    state.bandSpeedMul = 1 + ch.bandSpeed.v;
    state.sparkBurst = ch.burst.v;
    state.squash = ch.squash.v - ch.stretch.v;
    state.dust = ch.dust.v;
    state.edgeGlow = ch.edge.v;
    state.duck = clamp(ch.duck.v, 0, 1);
    state.tendril = ch.tendril.v;
    state.rescueGlow = ch.rescue.v;
    state.whiteFade = ch.white.v;
    state.flashLevel = clamp(ch.flash.v, 0, 1);
    state.orbit = ch.orbit.v;
    state.rumble = gentle.kick(clamp(Math.max(ch.rumble.v, ch.thud.v), 0, 1));
    state.padGlow = clamp(ch.pad.v, 0, 1);
    // the one variable: speed past top speed draws the speed lines
    const pct = kart ? kart[S.speedPct] : 0, maxPct = feel.get('speed.maxPct');
    state.speedLines = Math.max(ch.lines.v, clamp((pct - 1) / Math.max(maxPct - 1, 1e-3), 0, 1));
    if (padT >= 0) { padT += dt; state.padRing = padT < padLife ? Math.max(padT / padLife, 1e-3) : 0; if (padT >= padLife) padT = -1; }
    const pm = state.paletteMix;
    if (pm.waitS > 0) pm.waitS = Math.max(0, pm.waitS - dt);   // the old palette lingers one beat
    else if (pm.t < 1) pm.t = Math.min(1, pm.t + dt / Math.max(fx.paletteS, 1e-3));
    if (pm.t >= 1 && pm.base) pm.base = null;
    const sh = shake.update(dt);
    state.shake[0] = sh[0]; state.shake[1] = sh[1]; state.shake[2] = sh[2];
  }

  function reset() {
    for (const n of CHANNELS) Object.assign(ch[n], newEnv());
    Object.assign(state, restState());
    shake.reset(); photosafe.reset?.();
    padT = -1; fx.paused = false;
  }

  return { state, update, reset, dispose() { offs.forEach((off) => off()); } };
}
