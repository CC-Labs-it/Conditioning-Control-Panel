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
  const out = { steps: 0, alpha: 0, frameDt: 0, beats: 0, renderTime: 0 };
  // renderTime = simTime + alpha * dt: smooth on any display rate. Animate the world from it, not from
  // simTime, which moves in whole sim steps (0, 1 or 2 per frame on a 144 Hz display = 60 Hz judder).
  const clock = { dt, tick: 0, simTime: 0, renderTime: 0, beat, frame, setBpm, pause, reset };

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
    out.renderTime = clock.renderTime = clock.simTime + out.alpha * dt;
    return out;
  }
  function setBpm(v) { if (v > 0) beat.bpm = v; }
  /** Paused: no steps and the beat holds. Resuming starts a fresh frame, so nothing is caught up. */
  function pause(on) { paused = !!on; if (!paused) last = null; }
  function reset(nowMs) {
    clock.tick = 0; clock.simTime = 0; clock.renderTime = 0; acc = 0; beatPos = 0;
    beat.index = 0; beat.phase = 0;
    last = nowMs == null ? null : nowMs;
  }
  return clock;
}

/**
 * The frame cap (options frameCap: 'display' | 60 | 30). Called on every requestAnimationFrame;
 * due(now) says whether this one renders. It learns the display interval from the rAF gaps (the
 * 10th percentile of each 60 gaps, and the lowest seen, relaxing 1% per read so a slower monitor is
 * learnt in time). A cap at or above the display rate renders every frame; a whole ratio (120 Hz to
 * 60, 60 Hz to 30) renders every nth frame, so a drifting display clock never drops one; any other
 * ratio (144 Hz to 60) keeps a deadline that advances by exactly the cap interval, so the long run
 * average is the cap. A stall resyncs instead of rendering a burst.
 */
export function createFrameGate({ cap = 'display', windowN = 60, gapMs = 250 } = {}) {
  const ring = new Float64Array(windowN), sorted = new Float64Array(windowN);
  let n = 0, filled = 0, last = null, best = 0, next = 0, count = 0, capMs = 0;
  const gate = { due, setCap, get displayMs() { return best; }, get capMs() { return capMs; }, get targetMs() { return target(); } };
  function setCap(c) {
    const hz = c === 'display' || c == null ? 0 : Number(c);
    capMs = hz > 0 ? 1000 / hz : 0;
    count = 0; next = 0;
  }
  function learn(gap) {
    ring[n] = gap; n = (n + 1) % windowN; if (filled < windowN) filled++;
    if (n !== 0) return;
    sorted.set(ring); sorted.sort();
    const p10 = sorted[Math.floor(windowN * 0.1)];
    best = !best || p10 < best ? p10 : Math.min(p10, best * 1.01);
  }
  /** The frame interval to aim for: the cap, or the display when it is slower than the cap. */
  function target() { return capMs && best ? Math.max(capMs, best) : best || capMs || 1000 / 60; }
  function due(now) {
    if (last !== null) { const g = now - last; if (g > 0 && g < gapMs) learn(g); }
    last = now;
    if (!capMs || !best || capMs < best * 1.1) { next = now; count = 0; return true; }
    const ratio = capMs / best, whole = Math.round(ratio);
    if (Math.abs(ratio - whole) < 0.2) {
      count = (count + 1) % whole;
      if (count !== 1 && whole !== 1) return false;
      next = now + capMs;                      // kept, so a switch to the deadline mode never bursts
      return true;
    }
    if (now - next > capMs) next = now;
    if (now >= next - best / 2) { next += capMs; return true; }
    return false;
  }
  setCap(cap);
  return gate;
}
