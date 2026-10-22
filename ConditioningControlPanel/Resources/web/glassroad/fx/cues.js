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

const j = (fx, k) => fx.feel.get('juice.' + k);
const rise = (fx) => j(fx, 'riseS');                                       // generic fast IN
const tail = (fx) => j(fx, 'tailTcs') * fx.feel.get('boost.tailTcS');     // an exponential tail is ~95% gone in 3 tc
const rumbleOf = (fx, tier) => j(fx, 'rumbleByTier')[Math.min(Math.max(tier, 1), 3) - 1];

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
  'boost-start'(fx, { kind }) {
    fx.hold('fov', fx.gentle.kick(fx.feel.get('fov.boostKickDeg')), fx.feel.get('fov.kickInS'));
    fx.hold('lines', 1, fx.feel.get('boost.riseS'));
    fx.hold('bandSpeed', fx.gentle.scroll(j(fx, 'boostBandMul') - 1), fx.feel.get('boost.riseS'));
    if (kind !== 'pad') fx.shake.kick('boost');
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
  land(fx, { tier = 0 }) {
    const gone = j(fx, 'landGoneS');
    fx.release('stretch', rise(fx));
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
  // the rest change nothing on screen; kept so every event has its one entry
  wall(fx, { dv = 0 }) {
    if (dv >= fx.feel.get('wall.shakeFrac') * fx.feel.get('wall.cueFullDvMps')) fx.shake.kick('hit');
  },
  checkpoint() {},
  lap() {},
  pause(fx, { on = false }) { fx.paused = !!on; },
};

export default FX_CUES;
