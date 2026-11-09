/* Tempo uses active game milliseconds, independent of cosmetic route speed. */
export const BEAT_MS = 1000;
export const BEAT_WINDOW_MS = 130;
export const MIN_BPM = 40, MAX_BPM = 208;
export const WEIGHT_TOP = 60, WEIGHT_BOTTOM = 148;
export function clampBpm(value) {
  return Math.max(MIN_BPM, Math.min(MAX_BPM, Math.round(typeof value === 'number' && Number.isFinite(value) ? value : 60)));
}
export const beatPeriod = bpm => 60000 / clampBpm(bpm);
export const weightY = bpm => WEIGHT_TOP + (clampBpm(bpm) - MIN_BPM) / (MAX_BPM - MIN_BPM) * (WEIGHT_BOTTOM - WEIGHT_TOP);
export const bpmAtWeight = y => clampBpm(MIN_BPM + (y - WEIGHT_TOP) / (WEIGHT_BOTTOM - WEIGHT_TOP) * (MAX_BPM - MIN_BPM));
export function createPulse(bpm, now = 0) {
  return { bpm: clampBpm(bpm), lastTick: now, side: 1, phase: 0, held: false };
}
export function advancePulse(pulse, now) {
  if (pulse.held) return false;
  const period = beatPeriod(pulse.bpm), elapsed = Math.max(0, now - pulse.lastTick);
  const crossed = Math.floor((elapsed + 1e-7) / period);
  if (crossed) { pulse.lastTick += crossed * period; pulse.side = (pulse.side + crossed) % 2; }
  pulse.phase = Math.max(0, Math.min(1, (now - pulse.lastTick) / period));
  return crossed > 0;
}
export function retimePulse(pulse, bpm, now) {
  pulse.bpm = clampBpm(bpm);
  // Keep the frozen swing position, then continue from there at the chosen speed.
  pulse.lastTick = now - pulse.phase * beatPeriod(pulse.bpm);
}

/* A lever press and a main-button press share one reward window per beat. */
export function createRhythm() { return { streak: 0, lastPaid: null, lastBeat: null }; }
export function takeBeat(state, now, lastTick, period = BEAT_MS) {
  if (!Number.isFinite(now) || !Number.isFinite(lastTick) || !Number.isFinite(period) || period <= 0) return 0;
  const window = Math.min(BEAT_WINDOW_MS, period * .2);
  const nearest = lastTick + (now - lastTick > period / 2 ? period : 0);
  if (Math.abs(now - nearest) > window) { state.streak = 0; return 0; }
  if (state.lastBeat != null && Math.abs(nearest - state.lastBeat) < 1) return 0;
  if (state.lastPaid != null && now - state.lastPaid < period - 2 * window - .001) return 0;
  state.lastPaid = now; state.lastBeat = nearest;
  state.streak += 1;
  return Math.min(4, state.streak);
}
