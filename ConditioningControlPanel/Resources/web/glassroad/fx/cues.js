/* fx/cues.js - the visual reaction registry: event name -> (fx, payload) => void.
 *
 * One entry per CONTRACT event, never a switch: a lane adds a reaction by adding a key
 * here, never by editing reactions.js. `fx` is the toolkit reactions.js hands in:
 *   { state, ch (envelopes by name), fire(ch, peak, inS, holdS, outS), hold(ch, peak, inS),
 *     release(ch, outS), ask(kind) (photosafe), shake, gentle, feel, pad(lifeS, riseS), palette(from, to, s, waitS) }
 * Every beat has an IN and an OUT (plan 6). fire/hold apply the Gentle rise floor and fade
 * length themselves; anything that brightens the screen asks photosafe first; motion kicks
 * (fov, push, shake, rumble, bloom pulses) go through gentle.kick so Gentle removes them.
 */

import { WATER_CUES } from '../render/waterMath.js';

/** Stepping stones (wave 2): the stone tick and bob, the void dip and darkening. Shorter than the drop cue. */
export const STONE_CUES = Object.freeze({ tickMul: 0.5, tickOutS: 0.18, bobM: 0.02, bobOutS: 0.22, dipDeg: -2, darken: 0.6, outS: 0.35 });

const j = (fx, k) => fx.feel.get('juice.' + k);
const rise = (fx) => j(fx, 'riseS');                                       // generic fast IN
const tail = (fx) => j(fx, 'tailTcs') * fx.feel.get('boost.tailTcS');     // an exponential tail is ~95% gone in 3 tc
const rumbleOf = (fx, tier) => j(fx, 'rumbleByTier')[Math.min(Math.max(tier, 1), 3) - 1];

/** Piecewise linear lookup of ys at x along ascending xs (clamped at both ends). */
function along(xs, ys, x) {
  if (x <= xs[0]) return ys[0];
  for (let i = 1; i < xs.length; i++) if (x <= xs[i]) return ys[i - 1] + (ys[i] - ys[i - 1]) * (x - xs[i - 1]) / Math.max(xs[i] - xs[i - 1], 1e-9);
  return ys[ys.length - 1];
}

/**
 * Polish 5 (J1), the boost camera ladder: a boost-start payload -> { step, fovDeg, lines, shakeM, shakeMul }.
 * Steps 0..3 = mini-turbo, drift tier 1, 2, 3 (fov.kickByTierDeg, juice.linesByTier). A drift uses its tier,
 * a launch boost.launchTier (or the tier it carries), a trick its grade, Surge tier 2. Pads, pickup bubbles,
 * slipstreams and the clean landing are sized by amount on the same ladder (boost.miniAmount, boost.tierAmount),
 * a bubble under the mini-turbo shrinking toward 0.3 of it. Shake only from step 2 and on launch: step 2 at
 * shake.tier2Mul of shake.boostAmpM, step 3 and launch the whole of it, every boost run dying at
 * shake.boostRepeatMul. Amount-sized kinds never shake. Gentle is applied by the caller (gentle.kick, shake).
 */
export function boostLadder(feel, { kind = 'drift', tier = 0, amount = 0 } = {}) {
  const fovs = feel.get('fov.kickByTierDeg'), lines = feel.get('juice.linesByTier');
  const steps = [0, 1, 2, 3];
  let step, scale = 1, shakes = false;
  if (kind === 'drift') { step = Math.min(Math.max(tier | 0, 0), 3); shakes = step >= 2; }
  else if (kind === 'launch') { step = Math.min(Math.max((tier | 0) || feel.get('boost.launchTier'), 1), 3); shakes = true; }
  else if (kind === 'trick') { step = Math.min(Math.max(tier | 0, 1), 3); shakes = step >= 2; }
  else if (kind === 'surge') { step = 2; shakes = true; }
  else {
    const amounts = [feel.get('boost.miniAmount'), ...feel.get('boost.tierAmount')];
    const a = amount > 0 ? amount : feel.get('pad.amount');
    step = along(amounts, steps, a);
    if (a < amounts[0]) scale = Math.max(0.3, a / amounts[0]);
  }
  const full = feel.get('shake.boostAmpM');
  const shakeM = !shakes ? 0 : kind === 'launch' || step >= 3 ? full : full * feel.get('shake.tier2Mul');
  return {
    step,
    fovDeg: along(steps, fovs, step) * scale,
    lines: along(steps, lines, step) * scale,
    shakeM,
    shakeMul: feel.get('shake.boostRepeatMul'),
  };
}

/** Polish 6: how a bump between the player and a kart is felt (numbers here, not in feel.json: BEAT owns it). */
export const BUMP_LOOK = Object.freeze({
  fullDvMps: 10,                         // closing speed of a full-size bump (the audio mix's bumpFullDvMps)
  shakeFromMul: 1.6,                     // no shake under this many cue-minimums: a real bump thuds, a hard one shakes
  sideAmpM: Object.freeze([0.07, 0.2]),  // side shake peak from the shake threshold to fullDvMps
});

/**
 * A bump's feel from its closing speed dv against the cue minimum: null for a graze (under the minimum), else
 * { level 0..1 (minimum..fullDvMps), sideM (0 under shakeFromMul), rumbleTier 1 | 2 }.
 */
export function bumpFeel(dv, minDv, L = BUMP_LOOK) {
  const d = Number(dv) || 0, min = Math.max(Number(minDv) || 0, 1e-3);
  if (d < min) return null;
  const level = Math.min(1, Math.max(0, (d - min) / Math.max(L.fullDvMps - min, 1e-3)));
  const from = min * L.shakeFromMul;
  const k = Math.min(1, Math.max(0, (d - from) / Math.max(L.fullDvMps - from, 1e-3)));
  const sideM = L.sideAmpM[1] * k;
  return { level, sideM, rumbleTier: level >= 0.35 ? 2 : 1 };
}

export const FX_CUES = {
  // 1. countdown: bands brighten and bloom steps up per pulse; go releases both
  countdown(fx, { n = 3 }) {
    const step = Math.max(0, j(fx, 'countdownPulses') + 1 - n);
    if (!fx.ask('bloom')) return;
    fx.hold('bloom', fx.gentle.kick(step * j(fx, 'countdownBloomStep')), rise(fx));
    fx.hold('band', fx.gentle.contrast(step / (j(fx, 'countdownPulses') + 1)), rise(fx));
  },
  go(fx) {
    fx.release('bloom', j(fx, 'goReleaseS'));
    fx.release('band', j(fx, 'goReleaseS'));
  },
  // 2. launch boost: forward push, settles over launchCamSettleS
  'launch-boost'(fx) {
    fx.fire('push', fx.gentle.kick(j(fx, 'launchPushM')), rise(fx), 0, j(fx, 'launchCamSettleS'));
  },
  // 3. drift spark ladder: tier colour + rumble ramp; release bursts and dies in sparkDieS
  'drift-start'(fx, { dir = 1 }) {
    fx.state.sparkSide = dir; fx.state.sparkTier = 0;
  },
  'drift-tier'(fx, { tier = 1, dir }) {
    fx.state.sparkTier = tier;
    if (dir) fx.state.sparkSide = dir;
    fx.hold('rumble', fx.gentle.kick(rumbleOf(fx, tier)), rise(fx));
  },
  'drift-release'(fx, { tier = 0 }) {
    fx.state.sparkTier = 0;
    if (tier > 0) fx.fire('burst', 1, 0, 0, j(fx, 'sparkDieS'));
    fx.release('rumble', j(fx, 'sparkDieS'));
  },
  'drift-cancel'(fx) {
    fx.state.sparkTier = 0; fx.state.sparkSide = 0;
    fx.release('rumble', rise(fx));
  },
  // 4. boost fire: fov kick, speed lines, bands x boostBandMul; all ease back over the tail
  // Polish 5: fov kick, speed lines and shake climb the boost ladder (boostLadder above); B1 grants
  // (pickup bubble, slipstream) keep their band speed in proportion to their size against a pad
  'boost-start'(fx, p) {
    const { kind, amount = 0 } = p, L = boostLadder(fx.feel, p);
    const small = kind === 'bubble' || kind === 'slip';
    const size = small ? Math.min(1, Math.max(0.3, amount / fx.feel.get('pad.amount'))) : 1;
    fx.hold('fov', fx.gentle.kick(L.fovDeg), fx.feel.get('fov.kickInS'));
    fx.hold('lines', L.lines, fx.feel.get('boost.riseS'));
    fx.hold('bandSpeed', fx.gentle.scroll((j(fx, 'boostBandMul') - 1) * size), fx.feel.get('boost.riseS'));
    if (L.shakeM > 0) fx.shake.kick('boost', L.shakeM, L.shakeMul);
  },
  'boost-end'(fx) {
    fx.release('fov', fx.feel.get('fov.kickOutS'));
    fx.release('lines', tail(fx));
    fx.release('bandSpeed', tail(fx));
  },
  // 5. hop and landing: squash on hop, stretch in air, landing tier picks squash, dust, shake
  hop(fx) {
    fx.fire('squash', fx.feel.get('air.squash')[0], rise(fx) / 2, 0, rise(fx));
    fx.hold('stretch', fx.feel.get('air.squash')[0], 2 * rise(fx));
  },
  airborne(fx) {
    fx.hold('stretch', fx.feel.get('air.squash')[0], 2 * rise(fx));
  },
  land(fx, { tier = 0, airWord = false }) {
    const gone = j(fx, 'landGoneS');
    fx.release('stretch', rise(fx));
    if (airWord) {
      fx.fire('squash', fx.gentle.scroll(.24), .04, .08, .16);
      return;
    }
    if (tier < 1) return;
    fx.fire('squash', fx.feel.get('air.squash')[Math.min(tier, 3) - 1], 0, 0, gone);
    fx.fire('dust', tier / 3, 0, 0, gone);
    fx.fire('thud', fx.gentle.kick(rumbleOf(fx, tier)), 0, 0, gone);   // its own channel: a drift's rumble survives
    if (tier >= 3) fx.shake.kick('hit');
  },
  // 6. edge grace: glow while past the edge; fades on return or hands over to the drop.
  // A wobbling line re-enters often: past the photosafe budget the glow only reaches edgeGlowRefused.
  'edge-enter'(fx, { side = 1 }) {
    fx.state.edgeSide = side;
    fx.hold('edge', fx.ask('edge') ? 1 : j(fx, 'edgeGlowRefused'), rise(fx));
  },
  'edge-exit'(fx) {
    fx.release('edge', j(fx, 'edgeGlowFadeS'));
  },
  // 7. drop and rescue: duck to one tone, tendril, sky brightens toward the kart, release pulse
  drop(fx) {
    fx.hold('duck', 1, rise(fx));
    fx.release('lines', rise(fx)); fx.release('fov', rise(fx)); fx.release('bandSpeed', rise(fx));
  },
  'rescue-pull'(fx) {
    const pullS = fx.feel.get('respawn.pullS');
    fx.hold('tendril', 1, pullS);
    if (fx.ask('flash')) {
      fx.hold('rescue', 1, pullS);
      fx.hold('white', fx.feel.get('respawn.pullWhite'), pullS);
    }
  },
  'rescue-hover'() {},
  'rescue-release'(fx) {
    const out = fx.feel.get('respawn.releaseS');
    fx.release('tendril', out); fx.release('rescue', out); fx.release('white', out);
    fx.release('duck', j(fx, 'duckLiftS'));
    if (fx.ask('bloom')) fx.fire('bloom', fx.gentle.kick(j(fx, 'releasePulse')), rise(fx), 0, out);
  },
  // 8. boost pad: ring under the saucer, pad glow and band flash as ONE photosafe transition,
  // all through envelopes (rise floor, fade length); refused, the pad shows nothing extra
  pad(fx) {
    if (!fx.ask('band')) return;
    fx.pad(fx.feel.get('pad.holdS') + tail(fx), rise(fx));
    fx.fire('band', fx.gentle.contrast(j(fx, 'padBandPeak')), rise(fx), 0, tail(fx));
  },
  // 9. section gate: the old palette lingers one beat, then crossfades (twice as slow when the
  // budget is spent); a gate mid-fade starts from the blend on screen, never a snap
  section(fx, { from = 0, to = 0 }) {
    fx.palette(from, to, j(fx, 'paletteFadeS') * (fx.ask('palette') ? 1 : 2), 60 / fx.feel.get('clock.bpm'));
  },
  // 10. finish: white-gold swell under the flash budget, camera orbit (juice.finishOrbitDeg, the
  // camera scales it; Gentle halves it); lands in finishSwellS
  finish(fx) {
    const swell = j(fx, 'finishSwellS');
    if (fx.ask('flash')) fx.fire('flash', 1, swell, 0, swell);
    fx.hold('orbit', fx.gentle.scroll(1), swell);
  },
  // B1 (CONTRACT-B1 events). Positional sparks, rings and wobbles are drawn by render/scene.js from
  // the same events; these are the player's camera and pad beats only.
  'item-hit'(fx, { victim = -1, verdict }) {
    if (victim !== 0) return;
    if (verdict === 'hit') { fx.shake.kick('hit'); fx.fire('thud', fx.gentle.kick(rumbleOf(fx, 3)), 0, 0, j(fx, 'landGoneS') * 2); }
    else if (verdict === 'glass' || verdict === 'golden') fx.fire('thud', fx.gentle.kick(rumbleOf(fx, 1)), 0, 0, j(fx, 'landGoneS'));
  },
  // polish 6: a bump is felt by how hard it was (bumpFeel): a graze nothing, a real bump a thud, a hard one a
  // short SIDE shake (fx.side, the camera's x; Gentle removes it) growing with the closing speed
  bump(fx, { a = -1, b = -1, dv = 0 }) {
    if (a !== 0 && b !== 0) return;
    const f = bumpFeel(dv, fx.feel.get('contact.cueMinDvMps'));
    if (!f) return;
    if (f.sideM > 0 && fx.side) fx.side(f.sideM, a === 0 ? 1 : -1);
    fx.fire('thud', fx.gentle.kick(rumbleOf(fx, f.rumbleTier)), 0, 0, j(fx, 'landGoneS') * (1 + f.level));
  },
  // slipstream: a thin wake of speed lines while the charge builds; the boost itself is a boost-start
  slipstream(fx, { racer = -1, state }) {
    if (racer !== 0) return;
    if (state === 'in') fx.hold('wake', 1, j(fx, 'riseS') * 3);
    else fx.release('wake', j(fx, 'edgeGlowFadeS'));
  },
  // a pickup bubble: the bands brighten a touch (one photosafe transition, quietly skipped when refused)
  'bubble-pop'(fx, { racer = -1 }) {
    if (racer !== 0 || !fx.ask('band')) return;
    fx.fire('band', fx.gentle.contrast(j(fx, 'padBandPeak') * 0.4), rise(fx), 0, j(fx, 'landGoneS') * 2);
  },
  // water (wave 1, CONTRACT splash / surface / chute; render/waterMath.js WATER_CUES): a splash is a short
  // camera dip (a negative fov beat through gentle.kick: Gentle removes it) and a thud; the white ring is
  // positional (render/scene.js -> waterfall.splash). Entering a stream lifts the fov softly; a chute leans it.
  splash(fx, { down = 0 }) {
    const W = WATER_CUES, k = Math.min(1, Math.max(0.3, (Number(down) || 0) / 10));
    fx.fire('fov', fx.gentle.kick(W.splashDipDeg * k), rise(fx), 0, W.splashOutS);
    fx.fire('thud', fx.gentle.kick(rumbleOf(fx, k >= 0.7 ? 2 : 1)), 0, 0, j(fx, 'landGoneS'));
  },
  surface(fx, { kind = 'glass', from = 'glass' }) {
    if (kind !== 'stream' || from === 'stream') return;
    const W = WATER_CUES;
    fx.fire('fov', fx.gentle.kick(W.streamLiftDeg), W.streamInS, 0, W.streamOutS);
  },
  chute(fx) {
    const W = WATER_CUES;
    fx.fire('fov', fx.gentle.kick(W.chuteLeanDeg), rise(fx), W.chuteHoldS, W.chuteOutS);
  },
  // stepping stones (wave 2, BRIEF-STONES; STONE_CUES): landing on a stone is a soft tick (the thud channel,
  // the reaction layer drops the rumble under Gentle) and a 2 cm bob (a camera push beat through gentle.kick:
  // Gentle keeps only the tick). Dropping below the deck over the void is a short fov dip and a short darkening
  // (the drop's duck tone, fired rather than held: shorter than the drop cue, which holds it to the rescue).
  stone(fx) {
    const C = STONE_CUES;
    fx.fire('thud', rumbleOf(fx, 1) * C.tickMul, 0, 0, C.tickOutS);
    fx.fire('push', fx.gentle.kick(C.bobM), rise(fx), 0, C.bobOutS);
  },
  void(fx) {
    const C = STONE_CUES;
    fx.fire('fov', fx.gentle.kick(C.dipDeg), rise(fx), 0, C.outS);
    fx.fire('duck', C.darken, rise(fx), 0, C.outS);
  },
  // the rest change nothing on screen; kept so every event has its one entry
  wall(fx, { dv = 0 }) {
    if (dv >= fx.feel.get('wall.shakeFrac') * fx.feel.get('wall.cueFullDvMps')) fx.shake.kick('hit');
  },
  checkpoint() {},
  lap() {},
  // The gate renderer already owns the crossing pulse and text; do not add a second camera beat.
  'phrase-gate'() {},
  'press-thud'() {},
  'air-word-hop'(fx) {
    fx.release('squash', .06);
    fx.fire('stretch', fx.gentle.scroll(.2), .06, .12, .28);
  },
  pause(fx, { on = false }) { fx.paused = !!on; },
};

export default FX_CUES;
