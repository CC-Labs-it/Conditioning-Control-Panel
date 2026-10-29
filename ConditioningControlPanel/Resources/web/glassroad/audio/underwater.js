// One output treatment for every voice. AudioContext time freezes its glide during pause.
export function createUnderwater(ctx, output) {
  const filter = typeof ctx.createBiquadFilter === 'function' ? ctx.createBiquadFilter() : null;
  const attenuation = ctx.createGain();
  const dryHz = Math.min(18000, (ctx.sampleRate || 48000) * .45), wetHz = Math.min(650, dryHz);
  let submerged = 0, targetCutoffHz = dryHz, targetGain = 1, disposed = false;
  if (filter) {
    filter.type = 'lowpass'; filter.frequency.value = dryHz;
    if (filter.Q) filter.Q.value = Math.SQRT1_2;
    filter.connect(attenuation);
  }
  attenuation.gain.value = 1; attenuation.connect(output);
  function set(param, value, tc = 0) {
    if (!param) return;
    const now = ctx.currentTime;
    try {
      if (param.cancelAndHoldAtTime) param.cancelAndHoldAtTime(now);
      else param.cancelScheduledValues?.(now);
      if (tc && param.setTargetAtTime) param.setTargetAtTime(value, now, tc);
      else if (param.setValueAtTime) param.setValueAtTime(value, now);
      else param.value = value;
    } catch { param.value = value; }
  }
  function update(value = 0) {
    if (disposed) return;
    const amount = Number.isFinite(value) ? Math.max(0, Math.min(1, value)) : 0;
    if (Math.abs(amount - submerged) < .0001) return;
    const tc = amount > submerged ? .18 : .30;
    submerged = amount;
    targetCutoffHz = dryHz * (wetHz / dryHz) ** amount;
    targetGain = 1 - .18 * amount;
    set(filter?.frequency, targetCutoffHz, tc); set(attenuation.gain, targetGain, tc);
  }
  function reset() {
    if (disposed) return;
    submerged = 0; targetCutoffHz = dryHz; targetGain = 1;
    set(filter?.frequency, dryHz); set(attenuation.gain, 1);
  }
  return {
    input: filter || attenuation, update, reset,
    get inspect() { return Object.freeze({ available: !!filter && !disposed, submerged,
      cutoffHz: filter?.frequency.value ?? dryHz, gain: attenuation.gain.value, targetCutoffHz, targetGain }); },
    dispose() { if (disposed) return; reset(); disposed = true; filter?.disconnect?.(); attenuation.disconnect?.(); },
  };
}
