/* fx/reactions.js - the fx state the renderer and camera read, fed by the bus.
 *
 * createFx subscribes every entry of the FX_CUES registry (fx/cues.js) to its event and
 * runs the envelopes once per frame. It never switches on an event name: a new reaction
 * is a new registry key. An envelope rises to a peak (IN, Gentle rise floor), holds while
 * held or for holdS, then eases back to rest over outS (OUT, Gentle fade length). Pausing
 * freezes every envelope. update() also advances the photosafe window.
 * ITEM_CUES (below) is this lane's own small registry, run beside FX_CUES (item feel: the pad ticks when the player
 * uses an item, a throw flicks the speed lines, your hit landing rumbles). WAVE_CUES (fx/cues.js) are the feel
 * wave's events (rev, launch, hitter-hit, traps).
 * Feel wave 2026-10-26 (Lane A): the old camera shake / push / fov kick are RETIRED (the camera never read them).
 * One trauma value (fx/trauma.js) drives borderShakePx, rumble, duck, aberrationPx and the kart wobble; hits add
 * to it through fx.shock(). fx.hitstop(ms) and fx.finishHold() are render-side holds (state.holdS,
 * state.renderRate): the sim, the room clock and renderTime never stop.
 */
import { clamp, smoothstep } from '../core/math.js';
import { S } from '../sim/state.js';
import { FX_CUES, WAVE_CUES } from './cues.js';
import { createTrauma, noise } from './trauma.js';
import { speedShare, speedFeelMul, createSpeedSettle, SPEED_FEEL } from '../render/cameraMath.js';

const CHANNELS = ['bloom', 'band', 'lines', 'bandSpeed', 'burst', 'squash', 'stretch',
  'dust', 'edge', 'duck', 'tendril', 'rescue', 'white', 'flash', 'orbit', 'rumble', 'thud', 'pad',
  'wake'];   // B1: slipstream charge (speed lines at LOOK-free half strength)

/** Item cues beside the FX_CUES registry (render/items/look.js HIT_LOOK numbers). */
export const ITEM_CUES = {
  'item-hit'(fx, { victim = -1, by = -1, verdict }) {
    // item feel: your hit landed on someone: a firm pad rumble (Gentle: none); the camera lean is render/camera.js
    if (by === 0 && victim !== 0 && verdict === 'hit') {
      fx.fire('thud', fx.gentle.kick(fx.feel.get('itemFeel.confirmRumble')), 0, 0, fx.feel.get('itemFeel.confirmRumbleOutS'));
      return;
    }
  },
  // item feel: your own use ticks the pad (Gentle: none) and a throw flicks the speed lines forward
  'item-fire'(fx, { racer = -1, mode }) {
    if (racer !== 0) return;
    // Snap and Fog emit their item-hit before this item-fire: never let the tick replace a stronger confirm rumble
    const tick = fx.gentle.kick(fx.feel.get('itemFeel.fireRumble')), T = fx.ch.thud;
    if (!(T?.on && T.peak >= tick)) fx.fire('thud', tick, 0, 0, fx.feel.get('itemFeel.fireRumbleOutS'));
    const peak = fx.gentle.scroll(fx.feel.get('itemFeel.throwLinesPeak')), L = fx.ch.lines;
    // never cut a boost's held lines short, never pull a stronger beat down
    if ((mode === 'throw' || mode === 'bounce' || mode === 'tether' || mode === 'wide') && !L.held && !(L.on && L.v >= peak)) {
      fx.fire('lines', peak, fx.feel.get('juice.riseS'), 0, fx.feel.get('itemFeel.throwLinesOutS'));
    }
  },
};

export function restState() {
  return {
    // retired (feel wave): nothing reads these; shake[0] mirrors traumaOut for the camera boundary tests only
    shake: [0, 0, 0], fovKick: 0, camPush: 0,
    trauma: 0, traumaOut: 0, holdS: 0, renderRate: 1, borderShakePx: 0, aberrationPx: 0, wobble: 0,
    bloomBoost: 0, bandSpeedMul: 1, bandBright: 0,
    paletteMix: { from: 0, to: 0, t: 1 }, sparkTier: 0, sparkSide: 0, sparkBurst: 0, squash: 0,
    dust: 0, padRing: 0, padGlow: 0, speedLines: 0, edgeGlow: 0, edgeSide: 0, rescueGlow: 0, tendril: 0,
    whiteFade: 0, flashLevel: 0, orbit: 0, duck: 0,
    rumble: 0, // added: gamepad rumble 0..1 for boot (zero under Gentle)
    slipWake: 0, // B1: 0..1 while the player charges a slipstream (feeds speedLines at half)
    // speed wave: the settled speed share (0 at cruise, 1 at top, up to boostDeg/topDeg under boost, times the motion /
    // Gentle share) for the drawn-only camera widen, and the post edge blur strength 0..1 it feeds
    speedFeel: 0, speedBlur: 0,
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
  const trauma = createTrauma(feel), speedSettle = createSpeedSettle(SPEED_FEEL);
  let padT = -1, padLife = 1, clockS = 0, lastStopS = -1e9, finishT = -1, level = 'full';
  const T = (k, d) => { try { const v = feel.get(k); return Number.isFinite(v) ? v : d; } catch (e) { return d; } };
  // Gentle (host or preview) reads as 'less' at read time; storage keeps the player's pick
  const motion = () => (gentle.on && level === 'full' ? 'less' : level);

  const fx = {
    state, ch, gentle, feel, paused: false, trauma,
    /** Add trauma (0..1 total, clamped); Gentle and motion 'off' leave the channels still (traumaOut). */
    shock(amount) { trauma.add(amount); },
    /** Render-only hit-stop: the drawn pose holds for ms (boot.js); never on bumps, 400 ms apart, less halves. */
    hitstop(ms) {
      const m = motion(), gap = T('hitstop.minGapS', 0.4);
      if (m === 'off' || !(ms > 0) || clockS - lastStopS < gap) return;
      lastStopS = clockS;
      state.holdS = Math.max(state.holdS, (m === 'less' ? 0.5 : 1) * ms / 1000);
    },
    setMotion(l) { level = l === 'off' || l === 'less' ? l : 'full'; trauma.setMotion(motion()); },
    get motionMul() { trauma.setMotion(motion()); return trauma.motionMul; },
    get motion() { return motion(); },
    noise,
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
    /** The finish slow frame: the drawn pose runs at finishHold.rate for finishHold.s, then eases back. */
    finishHold() { if (motion() !== 'off') finishT = 0; },
  };

  const offs = [...Object.entries(FX_CUES), ...Object.entries(ITEM_CUES), ...Object.entries(WAVE_CUES)].map(([name, react]) => bus.on(name, (p) => react(fx, p || {})));

  function update(dt, kart) {
    if (fx.paused || !(dt > 0)) return;
    photosafe.update(dt);
    for (const n of CHANNELS) stepEnv(ch[n], dt);
    state.bloomBoost = clamp(ch.bloom.v, 0, 1);
    state.bandBright = clamp(ch.band.v, 0, 1);
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
    state.slipWake = clamp(ch.wake.v, 0, 1);
    state.speedLines = Math.max(ch.lines.v, state.slipWake * 0.5, clamp((pct - 1) / Math.max(maxPct - 1, 1e-3), 0, 1));
    // speed wave: the settled share behind the camera widen and the edge blur (Motion less / Gentle half, off none)
    state.speedFeel = speedSettle.update(speedShare(pct, maxPct, SPEED_FEEL) * speedFeelMul(motion(), gentle.on, SPEED_FEEL), dt);
    state.speedBlur = clamp(state.speedFeel, 0, 1) * SPEED_FEEL.blurMax;
    if (padT >= 0) { padT += dt; state.padRing = padT < padLife ? Math.max(padT / padLife, 1e-3) : 0; if (padT >= padLife) padT = -1; }
    const pm = state.paletteMix;
    if (pm.waitS > 0) pm.waitS = Math.max(0, pm.waitS - dt);   // the old palette lingers one beat
    else if (pm.t < 1) pm.t = Math.min(1, pm.t + dt / Math.max(fx.paletteS, 1e-3));
    if (pm.t >= 1 && pm.base) pm.base = null;
    traumaStep(dt);
  }

  // the one trauma value -> every screen-side channel (the chase camera never reads any of them)
  function traumaStep(dt) {
    clockS += dt;
    trauma.setMotion(motion());
    trauma.step(dt);
    const out = trauma.out, calm = gentle.on;
    state.trauma = trauma.value; state.traumaOut = out;
    state.shake[0] = out;
    const nt = clockS * T('trauma.noiseHz', 18);
    state.borderShakePx = calm ? 0 : out * T('trauma.borderPx', 6) * noise(0, nt);
    const safe = T("photosafe.on", 0) ? T("post.aberration.photosafeMul", 0.5) : 1;   // a strict photosafe host halves it
    state.aberrationPx = calm ? 0 : out * T('post.aberration.maxPx', 2.5) * safe;
    state.wobble = calm ? 0 : out * T('kart.wobble.mul', 1) * noise(1, nt * 0.5);
    state.duck = Math.max(state.duck, clamp(out * T('trauma.duck', 0.4), 0, 1));
    state.rumble = Math.max(state.rumble, gentle.kick(clamp(out * T('trauma.rumble', 0.8), 0, 1)));
    state.holdS = Math.max(0, state.holdS - dt);
    if (finishT >= 0) {
      finishT += dt;
      const hold = T('finishHold.s', 0.5), back = T('finishHold.backS', 0.3), rate = T('finishHold.rate', 0.4);
      const k = finishT <= hold ? 0 : Math.min(1, (finishT - hold) / Math.max(back, 1e-3));
      state.renderRate = rate + (1 - rate) * smoothstep(0, 1, k);
      if (k >= 1) { finishT = -1; state.renderRate = 1; }
    }
  }

  function reset() {
    for (const n of CHANNELS) Object.assign(ch[n], newEnv());
    Object.assign(state, restState());
    trauma.reset(); speedSettle.reset(); photosafe.reset?.();
    padT = -1; fx.paused = false; finishT = -1; lastStopS = -1e9;
  }

  return { state, update, reset, noise, setMotion: fx.setMotion, get motionMul() { return fx.motionMul; },
    hitstop: fx.hitstop, shock: fx.shock, dispose() { offs.forEach((off) => off()); } };
}
