/* host/raceToys.js - sends the race's toy plan (fx/toyPlan.js) to a host that has toys (caps.toys, CONTRACT section 7).
 *
 * page -> host: toy { level, ms, tag }, toy-bed { level }, toy-stop. The host decides whether a toy takes any of it and
 * clamps everything: the page is untrusted input. The bed is re-sent while it runs because the host zeroes it on its own
 * when the sends stop, so a dead page cannot pin a toy. Pause, a hidden page and leaving stop at once; nothing here
 * goes over the network, so a rival never learns anything and the room judges nothing from it. */
import { toyPulse, bedLevel, chargeStep, TOY_BED, TOY_SEND } from '../fx/toyPlan.js';

export function createRaceToys({ bus, send, place = () => 0, bumpMinDv = 3, now = () => performance.now() }) {
  let mode = 'full', charge = 0, streak = 0, final = false, dirty = false, disposed = false;
  let silentUntil = 0, pulseUntil = 0, pulseLevel = 0, lastPulseAt = -Infinity, lastBedAt = -Infinity, lastBed = 0, lastHitAt = -Infinity;

  function stop() {
    pulseUntil = 0; pulseLevel = 0; lastBed = 0; lastBedAt = -Infinity;
    if (!dirty) return;
    dirty = false;
    send({ type: 'toy-stop' });
  }

  function onEvent(p, name) {
    if (disposed || mode === 'off') return;
    if (name === 'pause') { if (p?.on) stop(); return; }
    if (name === 'lap' && p?.final) final = true;
    const pulse = toyPulse(name, p, { bumpMinDv, place: name === 'finish' ? place() : 0 });
    if (!pulse) return;
    if (name === 'word-pop') { streak = Math.min(1, streak + TOY_BED.streakPerWord); charge = Math.min(1, charge + TOY_BED.wordCharge); }
    const t = now(), hit = pulse.silenceS > 0;
    if (hit && t - lastHitAt < TOY_SEND.sameBlowMs) return;           // item-hit and hitter-hit are one blow
    if (hit) lastHitAt = t;
    if (pulse.charge) charge = Math.max(0, charge * (1 - pulse.charge));
    if (!hit) {
      if (t < silentUntil) return;                                      // the silence after a hit is the whole point
      const stronger = t < pulseUntil && pulse.level > pulseLevel, busy = t < pulseUntil && !stronger;
      if (busy || (!stronger && t - lastPulseAt < TOY_SEND.pulseGapMs)) return;
    }
    lastPulseAt = t; pulseUntil = t + pulse.ms; pulseLevel = pulse.level;
    if (hit) silentUntil = pulseUntil + pulse.silenceS * 1000;
    dirty = true;
    send({ type: 'toy', level: pulse.level, ms: pulse.ms, tag: pulse.tag });
  }

  const off = bus.on('*', onEvent);

  return {
    /** Every frame. racing: the race clock runs for this player and the page is not paused. speedShare: 0..1. */
    update(dt, { racing = false, speedShare = 0, mode: wanted = 'full' } = {}) {
      if (disposed) return;
      if (wanted !== mode) { mode = wanted; if (mode === 'off') stop(); }
      if (mode === 'off') return;
      streak = Math.max(0, streak - TOY_BED.streakFadePerS * dt);
      if (racing) charge = chargeStep(charge, speedShare, streak, dt);
      const t = now(), level = racing && mode === 'full' && t >= silentUntil ? bedLevel(charge, final) : 0;
      if (level === lastBed && level === 0) return;
      const moved = Math.abs(level - lastBed) >= TOY_SEND.bedStep || level === 0;
      if (t - lastBedAt < TOY_SEND.bedGapMs && level !== 0) return;
      if (!moved && t - lastBedAt < TOY_SEND.bedRefreshMs) return;
      lastBed = level; lastBedAt = t; dirty = true;
      send({ type: 'toy-bed', level: Math.round(level * 1000) / 1000 });
    },
    /** A new run starts clean. */
    reset() { charge = 0; streak = 0; final = false; silentUntil = 0; lastHitAt = -Infinity; stop(); },
    stop,
    dispose() { if (disposed) return; stop(); disposed = true; off?.(); },
  };
}
