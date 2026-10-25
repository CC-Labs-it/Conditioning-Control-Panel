// A rounded landing pluck opens into a little rising spring. Always below the vocal.
import { ladderSemis } from './mix.js';

const previous = new WeakMap();
const gentleOn = syn => {
  try { return !!syn.feel?.get('gentle.on'); } catch { return false; }
};

export function playAirWordLand(syn, payload = {}) {
  const gain = gentleOn(syn) || payload.gentle ? .65 : 1;
  // A soft wooden tap marks contact before the separate spring launches.
  syn.tone(syn.semis(-12), { type: 'sine', dur: .12, vol: .075 * gain, attack: .004, glide: syn.semis(-19) });
  syn.noise({ dur: .06, vol: .018 * gain, freq: 700, q: .7 });
}

export function playAirWordHop(syn, payload = {}) {
  const { id, section, index } = payload;
  if (id == null || section == null || !Number.isInteger(index) || index < 0) return false;
  const time = syn.ctx.currentTime, last = previous.get(syn);
  if (last && time >= last.time && time - last.time < .3 &&
      last.id === id && last.section === section && last.index === index) return false;
  previous.set(syn, { id, section, index, time });
  // The route's index resets the melody each crossing and caps long flights below a shrill register.
  const hz = syn.semis(ladderSemis(Math.min(7, index))), soft = gentleOn(syn) || payload.gentle;
  const gain = soft ? .65 : 1;
  syn.tone(hz, { type: 'triangle', dur: .3, vol: .046 * gain, attack: .008, glide: hz * .6 });
  syn.tone(hz * .5, { type: 'sine', dur: .12, vol: .019 * gain, attack: .006, glide: hz * .4 });
  syn.tone(hz * .65, { type: 'sine', dur: .24, vol: .028 * gain, attack: .014, glide: hz * 1.5, at: .025 });
  syn.tone(hz * 2, { type: 'sine', dur: .3, vol: .011 * gain, attack: .014, at: .055 });
  syn.noise({ dur: .13, vol: .009 * gain, freq: 850, sweep: soft ? 1900 : 2800, q: .65 });
  return true;
}
