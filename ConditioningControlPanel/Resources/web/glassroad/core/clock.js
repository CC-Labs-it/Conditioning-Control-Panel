/* core/clock.js - the ONE clock: fixed-step sim, render blend, beat clock (CONTRACT.md section 4).
 *
 * An accumulator on real time pays one sim step per 1/hz s. A long frame may run up to
 * maxSubsteps steps; time past that is dropped, never caught up. The beat clock advances with sim
 * time, so it holds while paused and the countdown, bands and HUD pulse all share it.
 */

/** @returns {Clock} */
export function createClock({ hz = 60, maxSubsteps = 2, bpm = 96 } = {}) {
  const dt = 1 / hz;
  let last = null, acc = 0, paused = false, beatPos = 0;
  const beat = { bpm, phase: 0, index: 0 };
  const out = { steps: 0, alpha: 0, frameDt: 0, beats: 0 };
  const clock = { dt, tick: 0, simTime: 0, beat, frame, setBpm, pause, reset };

  function frame(nowMs) {
    const real = last == null ? 0 : Math.max(0, (nowMs - last) / 1000);
    last = nowMs;
    out.frameDt = Math.min(real, 0.1);
    out.steps = 0; out.beats = 0;
    if (!paused) {
      acc += out.frameDt;
      // a hair of tolerance so a 16.67 ms frame is one step, not zero then two
      let n = Math.floor((acc + 1e-6) / dt);
      if (n > maxSubsteps) n = maxSubsteps;
      acc = Math.max(0, acc - n * dt);
      if (n === maxSubsteps && acc >= dt) acc -= Math.floor(acc / dt) * dt;   // dropped, never caught up
      out.steps = n;
      clock.tick += n;
      clock.simTime += n * dt;
      const before = Math.floor(beatPos);
      beatPos += n * dt * beat.bpm / 60;
      beat.index = Math.floor(beatPos);
      beat.phase = beatPos - beat.index;
      out.beats = beat.index - before;
    }
    out.alpha = Math.min(1, acc / dt);
    return out;
  }
  function setBpm(v) { if (v > 0) beat.bpm = v; }
  /** Paused: no steps and the beat holds. Resuming starts a fresh frame, so nothing is caught up. */
  function pause(on) { paused = !!on; if (!paused) last = null; }
  function reset(nowMs) {
    clock.tick = 0; clock.simTime = 0; acc = 0; beatPos = 0;
    beat.index = 0; beat.phase = 0;
    last = nowMs == null ? null : nowMs;
  }
  return clock;
}
