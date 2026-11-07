// Three deliberate performances, each returning to rest before the next begins.
const clamp = x => Math.max(0, Math.min(1, x));
const ease = x => { x = clamp(x); return x * x * x * (x * (x * 6 - 15) + 10); };
const pulse = (t, a, b, c, d) => ease((t - a) / (b - a)) * (1 - ease((t - c) / (d - c)));
export const GARAGE_IDLE = Object.freeze({ slotS: 8, count: 3, hopM: .18, flightS: .72 });

/** Pure sampling keeps the acting identical at 30 and 120 fps. */
export function garageIdleAt(time, { gentle = false, motionOff = false } = {}) {
  const out = { name: 'rest', face: 'happy', wave: 0, waveTime: 0, yaw: 0, roll: 0, pitch: 0, lift: 0, squash: 0, antenna: 0 };
  if (motionOff || !Number.isFinite(time) || time < 0) return out;
  const slot = Math.floor(time / GARAGE_IDLE.slotS) % GARAGE_IDLE.count, t = time % GARAGE_IDLE.slotS;
  const k = gentle ? .5 : 1;
  if (slot === 0) {
    out.name = 'wave';
    out.wave = pulse(t, .6, 1.2, 4, 4.9) * k;
    out.waveTime = ease((t - .6) / 4.3);
    out.antenna = -.06 * out.wave;
  } else if (slot === 1) {
    out.name = 'curious';
    const left = pulse(t, .8, 1.8, 2.3, 3.2), right = pulse(t, 2.9, 3.9, 4.6, 5.8);
    out.yaw = (.30 * left - .34 * right) * k;
    out.roll = (-.075 * left + .09 * right) * k;
    out.pitch = -.025 * (left + right) * k;
    out.antenna = .12 * (left + right) * k;
    if (t > .95 && t < 5.5) out.face = 'neutral';
    else if (t >= 5.5 && t < 6.1) out.face = 'wink';
  } else {
    out.name = 'hop';
    const airT = t - 1.6, u = clamp(airT / GARAGE_IDLE.flightS);
    out.lift = airT > 0 && u < 1 ? GARAGE_IDLE.hopM * 4 * u * (1 - u) * k : 0;
    const prep = pulse(t, .8, 1.28, 1.4, 1.6), land = pulse(t, 2.26, 2.4, 2.48, 3.1);
    out.squash = (.065 * prep - .035 * Math.sin(Math.PI * u) + .095 * land) * k;
    out.pitch = (.035 * prep - .025 * Math.sin(Math.PI * u)) * k;
    out.antenna = (-.12 * prep + .08 * Math.sin(Math.PI * u) - .09 * land) * k;
    if (t >= 1.55 && t < 2.9) out.face = 'cheer';
  }
  return out;
}
