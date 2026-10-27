/* fx/cues.js - the visual reaction registry: event name -> (fx, payload) => void.
 *
 * One entry per CONTRACT event, never a switch: a lane adds a reaction by adding a key
 * here, never by editing reactions.js. `fx` is the toolkit reactions.js hands in:
 *   { state, ch (envelopes by name), fire(ch, peak, inS, holdS, outS), hold(ch, peak, inS),
 *     release(ch, outS), ask(kind) (photosafe), shake, gentle, feel, pad(lifeS, riseS), palette(from, to, s, waitS) }
 * Every beat has an IN and an OUT (plan 6). fire/hold apply the Gentle rise floor and fade
 * length themselves; anything that brightens the screen asks photosafe first; motion kicks
 * (rumble, bloom pulses) go through gentle.kick so Gentle removes them.
 * Feel wave 2026-10-26 (Lane A): no cue moves the camera any more (fov kick, push and shake are retired).
 * Hits add TRAUMA (fx.shock, fx/trauma.js; TRAUMA below) and the big ones a render-only hit-stop (fx.hitstop).
 */


/** Stepping stones (wave 2): the stone tick and bob, the void dip and darkening. Shorter than the drop cue. */
export const STONE_CUES = Object.freeze({ tickMul: 0.5, tickOutS: 0.18, bobM: 0.02, bobOutS: 0.22, dipDeg: -2, darken: 0.6, outS: 0.35 });

/** Trauma each hit adds (owner brief): hits, bumps, landings by tier, a hit received, a tier 3 drift release. */
export const TRAUMA = Object.freeze({ hit: 0.5, bump: 0.2, land: Object.freeze([0.1, 0.2, 0.3]), received: 0.35,
  drift3: 0.15, finish: 0.3, lap: 0.08, launchEarly: 0.1, launchPerfect: 0.15 });
/** Physical trap events that hit like a wall (fx/payoff.js MALUS_EVENTS minus the non-physical ones). */
export const TRAP_EVENTS = Object.freeze(['keep-gate-hit', 'keep-chain-hit', 'boudoir-press-hit', 'boudoir-sweeper-hit',
  'boudoir-mirror-hit', 'goonworks-hit', 'moving-trap-hit']);
const stopMs = (fx, big) => { try { return fx.feel.get(big ? "hitstop.bigMs" : "hitstop.ms"); } catch (e) { return big ? 60 : 40; } };

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
 * Polish 5 (J1), the boost ladder: a boost-start payload -> { step, lines }. Steps 0..3 = mini-turbo, drift tier
 * 1, 2, 3 (juice.linesByTier). A drift uses its tier, a launch boost.launchTier (or the tier it carries), a trick
 * its grade, Surge tier 2. Pads, pickup bubbles, slipstreams and the clean landing are sized by amount on the same
 * ladder (boost.miniAmount, boost.tierAmount), a bubble under the mini-turbo shrinking toward 0.3 of it.
 * Feel wave: the fov kick and boost shake of this ladder are retired (the camera never read them).
 */
export function boostLadder(feel, { kind = 'drift', tier = 0, amount = 0 } = {}) {
  const lines = feel.get('juice.linesByTier');
  const steps = [0, 1, 2, 3];
  let step, scale = 1;
  if (kind === 'drift') step = Math.min(Math.max(tier | 0, 0), 3);
  else if (kind === 'launch') step = Math.min(Math.max((tier | 0) || feel.get('boost.launchTier'), 1), 3);
  else if (kind === 'trick') step = Math.min(Math.max(tier | 0, 1), 3);
  else if (kind === 'surge') step = 2;
  else {
    const amounts = [feel.get('boost.miniAmount'), ...feel.get('boost.tierAmount')];
    const a = amount > 0 ? amount : feel.get('pad.amount');
    step = along(amounts, steps, a);
    if (a < amounts[0]) scale = Math.max(0.3, a / amounts[0]);
  }
  return { step, lines: along(steps, lines, step) * scale };
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
  // 2. launch boost: the camera push is retired; the launch grade's feel is WAVE_CUES.launch
  'launch-boost'() {},
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
    if (tier >= 3) fx.shock?.(TRAUMA.drift3);
    if (tier > 0) fx.fire('burst', 1, 0, 0, j(fx, 'sparkDieS'));
    fx.release('rumble', j(fx, 'sparkDieS'));
  },
  'drift-cancel'(fx) {
    fx.state.sparkTier = 0; fx.state.sparkSide = 0;
    fx.release('rumble', rise(fx));
  },
  // 4. boost fire: speed lines, bands x boostBandMul; all ease back over the tail
  // Polish 5: speed lines climb the boost ladder (boostLadder above); B1 grants
  // (pickup bubble, slipstream) keep their band speed in proportion to their size against a pad
  'boost-start'(fx, p) {
    const { kind, amount = 0 } = p, L = boostLadder(fx.feel, p);
    const small = kind === 'bubble' || kind === 'slip';
    const size = small ? Math.min(1, Math.max(0.3, amount / fx.feel.get('pad.amount'))) : 1;
    fx.hold('lines', L.lines, fx.feel.get('boost.riseS'));
    fx.hold('bandSpeed', fx.gentle.scroll((j(fx, 'boostBandMul') - 1) * size), fx.feel.get('boost.riseS'));
  },
  'boost-end'(fx) {
    fx.release('lines', tail(fx));
    fx.release('bandSpeed', tail(fx));
  },
  // 5. hop and landing: squash on hop, stretch in air, landing tier picks squash, dust, trauma
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
    fx.shock?.(TRAUMA.land[Math.min(tier, 3) - 1]);
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
    fx.release('lines', rise(fx)); fx.release('bandSpeed', rise(fx));
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
  // camera scales it; Gentle halves it); lands in finishSwellS. Feel wave: trauma and the finish slow frame.
  finish(fx) {
    const swell = j(fx, 'finishSwellS');
    fx.shock?.(TRAUMA.finish); fx.finishHold?.();
    if (fx.ask('flash')) fx.fire('flash', 1, swell, 0, swell);
    fx.hold('orbit', fx.gentle.scroll(1), swell);
  },
  // B1 (CONTRACT-B1 events). Positional sparks, rings and wobbles are drawn by render/scene.js from
  // the same events; these are the player's camera and pad beats only.
  'item-hit'(fx, { victim = -1, verdict, big = false }) {
    if (victim !== 0) return;
    if (verdict === 'hit') { fx.shock?.(TRAUMA.hit); fx.hitstop?.(stopMs(fx, !!big)); fx.fire('thud', fx.gentle.kick(rumbleOf(fx, 3)), 0, 0, j(fx, 'landGoneS') * 2); }
    else if (verdict === 'glass' || verdict === 'golden') fx.fire('thud', fx.gentle.kick(rumbleOf(fx, 1)), 0, 0, j(fx, 'landGoneS'));
  },
  // polish 6: a bump is felt by how hard it was (bumpFeel): a graze nothing, a real bump a thud, a hard one
  // trauma growing with the closing speed (never a hit-stop)
  bump(fx, { a = -1, b = -1, dv = 0 }) {
    if (a !== 0 && b !== 0) return;
    const f = bumpFeel(dv, fx.feel.get('contact.cueMinDvMps'));
    if (!f) return;
    if (f.sideM > 0) fx.shock?.(TRAUMA.bump * (0.4 + 0.6 * f.sideM / BUMP_LOOK.sideAmpM[1]));
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
  // water (wave 1, CONTRACT splash / surface / chute): a splash is a thud and a little trauma; the white ring is
  // positional (render/scene.js -> waterfall.splash). Feel wave: the old fov dips and leans are retired.
  splash(fx, { down = 0 }) {
    const k = Math.min(1, Math.max(0.3, (Number(down) || 0) / 10));
    fx.shock?.(TRAUMA.land[0] * k);
    fx.fire('thud', fx.gentle.kick(rumbleOf(fx, k >= 0.7 ? 2 : 1)), 0, 0, j(fx, 'landGoneS'));
  },
  surface(fx) {},   // feel wave: the stream fov lift is retired
  chute(fx) {},     // feel wave: the chute fov lean is retired
  // stepping stones (wave 2, BRIEF-STONES; STONE_CUES): landing on a stone is a soft tick (the thud channel,
  // the reaction layer drops the rumble under Gentle). Dropping below the deck over the void is a short
  // darkening (the drop's duck tone, fired rather than held: shorter than the drop cue, which holds it to the
  // rescue). Feel wave: the camera bob and fov dip are retired.
  stone(fx) {
    const C = STONE_CUES;
    fx.fire('thud', rumbleOf(fx, 1) * C.tickMul, 0, 0, C.tickOutS);
  },
  void(fx) {
    const C = STONE_CUES;
    fx.fire('duck', C.darken, rise(fx), 0, C.outS);
  },
  // the rest change nothing on screen; kept so every event has its one entry
  wall(fx, { dv = 0 }) {
    const full = fx.feel.get('wall.cueFullDvMps');
    if (dv < fx.feel.get('wall.shakeFrac') * full) return;
    const amount = Math.min(1, Math.max(0, dv / full));
    fx.shock?.(TRAUMA.hit * amount);
    if (amount >= 0.6) fx.hitstop?.(stopMs(fx, false));
    fx.fire('thud', fx.gentle.kick(rumbleOf(fx, 2) * amount), 0, 0, j(fx, 'landGoneS'));
  },
  checkpoint() {},
  // a lap line: a small trauma (the screen border pulses with it), never a hit-stop
  lap(fx) { fx.shock?.(TRAUMA.lap); },
  // The gate renderer already owns the crossing pulse and text; do not add a second camera beat.
  'phrase-gate'() {},
  'press-thud'() {},
  'trap-tick'() {},
  // The initial lip already owns launch motion; this notification only adds its spring sound.
  'air-word-launch'() {},
  'air-word-hop'(fx) {
    // The landing squash finishes while the kart is already moving into its rebound.
    fx.fire('stretch', fx.gentle.scroll(.2), .14, .12, .28);
  },
  pause(fx, { on = false }) { fx.paused = !!on; },
};

/**
 * Feel wave events (fixed contracts in the feel brief): other lanes only emit, the reactions live here. Kept beside
 * FX_CUES (like ITEM_CUES) so the CONTRACT event registry test stays exact.
 */
export const WAVE_CUES = {
  // rocket start, kart half: the kart crouches as the rev builds (Gentle halves it)
  rev(fx, { level = 0 }) {
    const l = Math.min(1, Math.max(0, Number(level) || 0));
    fx.hold('squash', fx.gentle.scroll(l * fx.feel.get('air.squash')[0]), rise(fx));
  },
  // GO: perfect stretches forward with a kick of trauma, good stretches less, early slumps
  launch(fx, { grade = 'none' }) {
    fx.release('squash', rise(fx));
    const sq = fx.feel.get('air.squash');
    if (grade === 'perfect') { fx.fire('stretch', fx.gentle.scroll(sq[2]), rise(fx) / 2, 0.08, 0.3); fx.shock?.(TRAUMA.launchPerfect); }
    else if (grade === 'good') fx.fire('stretch', fx.gentle.scroll(sq[1]), rise(fx) / 2, 0.05, 0.25);
    else if (grade === 'early') { fx.fire('squash', fx.gentle.scroll(sq[1]), rise(fx) / 2, 0.15, 0.4); fx.shock?.(TRAUMA.launchEarly); }
  },
  // a hit received: trauma at least TRAUMA.received (item-hit already added its own: never stacks past it)
  'hitter-hit'(fx, { racer = -1 }) {
    if (racer !== 0 || !fx.trauma) return;
    const v = fx.trauma.value;
    if (v < TRAUMA.received) fx.shock(TRAUMA.received - v);
  },
};
// traps hit like a wall: trauma and the hit-stop on the player only (no racer field = the player's own trap)
for (const name of TRAP_EVENTS) {
  WAVE_CUES[name] = (fx, { racer = 0 }) => {
    if (racer !== 0) return;
    fx.shock?.(TRAUMA.hit); fx.hitstop?.(stopMs(fx, false));
  };
}

export default FX_CUES;
