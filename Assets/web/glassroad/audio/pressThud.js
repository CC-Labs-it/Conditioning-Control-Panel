// A short, low mechanical contact, kept under the recording on the shared SFX bus.
export function pressThudGain(payload = {}) {
  const gain = Number.isFinite(payload.gain) ? Math.max(0, Math.min(1, payload.gain)) : 0;
  return gain * (payload.gentle ? .55 : 1);
}

export function playPressThud(syn, payload) {
  const gain = pressThudGain(payload);
  if (!(gain > .01)) return;
  syn.tone(74, { type: 'sine', glide: 43, dur: .15, attack: .008, vol: .035 * gain });
  syn.noise({ dur: .09, vol: .012 * gain, freq: 260, sweep: 110, q: .7 });
}
