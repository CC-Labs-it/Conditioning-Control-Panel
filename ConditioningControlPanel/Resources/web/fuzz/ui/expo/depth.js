// The room uses real active seconds. Pausing its owner stops this one clock.
export const PHASE_SECONDS = Object.freeze([0, 20, 45, 90, 150, 210]);
export const PHASE_DEPTHS = Object.freeze([0, 0.1, 0.25, 0.5, 0.75, 1]);
export const DOME_CUES = Object.freeze({
  dome_0: 'There is nothing to see here. Please move along.',
  dome_1: 'There is still nothing to see here.',
  dome_2: 'Visitors are reminded that the ceiling has always done that.',
  dome_3: 'The fog is not part of the exhibit. Neither are you. Please remain calm. You are.',
  dome_4: 'There is nothing to see here. You are doing it very well.',
  dome_5: 'The tour will continue without you. That is fine. Everything is fine. Stay.',
  dome_leave: 'Welcome back. You were gone for some time. We did not mind.',
});
const validSeconds = n => Number.isFinite(n) ? Math.max(0, n) : 0;
export function phaseAt(seconds) {
  const s = validSeconds(seconds);
  for (let i = PHASE_SECONDS.length - 1; i >= 0; i--) if (s >= PHASE_SECONDS[i]) return i;
  return 0;
}
export function depthAt(seconds) {
  const s = Math.min(210, validSeconds(seconds)), i = phaseAt(s);
  if (i === 5) return 1;
  const blend = (s - PHASE_SECONDS[i]) / (PHASE_SECONDS[i + 1] - PHASE_SECONDS[i]);
  return PHASE_DEPTHS[i] + blend * (PHASE_DEPTHS[i + 1] - PHASE_DEPTHS[i]);
}
export function createDepth() {
  let seconds = 0, visitSeconds = 0, lastVisitSeconds = 0, inside = false;
  return {
    step(dt, nextInside) {
      const elapsed = validSeconds(dt), entered = !!nextInside && !inside, left = !nextInside && inside;
      const previousPhase = phaseAt(seconds);
      if (left) { lastVisitSeconds = visitSeconds; visitSeconds = 0; }
      if (entered) visitSeconds = 0;
      inside = !!nextInside;
      if (inside) { seconds = Math.min(210, seconds + elapsed); visitSeconds += elapsed; }
      else seconds = Math.max(0, seconds - elapsed * 0.25);
      return { entered, left, previousPhase };
    },
    get seconds() { return seconds; },
    get depth() { return depthAt(seconds); },
    get phase() { return phaseAt(seconds); },
    get inside() { return inside; },
    get visitSeconds() { return visitSeconds; },
    get lastVisitSeconds() { return lastVisitSeconds; },
  };
}
