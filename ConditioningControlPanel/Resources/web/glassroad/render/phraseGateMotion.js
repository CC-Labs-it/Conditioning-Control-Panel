const sat = v => Math.max(0, Math.min(1, v));
const ease = v => { const q = sat(v); return q * q * (3 - 2 * q); };
export const GATE_AMBIENT = 96, GATE_BURST = 48, GATE_CAPACITY = GATE_AMBIENT + GATE_BURST;
export function phraseLook(age, gentle = false) {
  const rise = gentle ? .5 : .3, fade = gentle ? .9 : .6;
  const enter = sat(age / rise), leave = sat((age - rise - 1) / fade);
  const alpha = age < rise ? ease(enter) : 1 - ease(leave);
  // One small overshoot, then a quiet readable hold. Departure accelerates away.
  const pop = enter < .72 ? .64 + .42 * (1 - (1 - enter / .72) ** 3) : 1.06 - .06 * ease((enter - .72) / .28);
  return { alpha, scale: gentle ? 1 : pop + .13 * leave * leave,
    y: gentle ? 0 : 16 * (1 - ease(enter)) - 22 * leave * leave,
    tilt: gentle ? 0 : -3 * (1 - ease(enter)),
    stretch: gentle ? 1 : 1 + .1 * Math.sin(enter * Math.PI * 2) * (1 - enter),
    echo: gentle ? 0 : .5 * Math.sin(Math.PI * leave), split: leave * leave * 22,
    sparks: gentle ? 0 : Math.max(Math.sin(Math.PI * enter), .75 * Math.sin(Math.PI * leave)),
    sparkTravel: leave > 0 ? 1 + leave : ease(enter) };
}
export function gatePulse(age, gentle = false) {
  if (age < 0) return { alpha: 0, spread: 1, travel: 0 };
  const life = gentle ? 1.8 : 1.25, u = sat(age / life);
  return { alpha: ease(age / (gentle ? .5 : .3)) * (1 - ease((age - .3) / (life - .3))) * (gentle ? .2 : .65),
    spread: 1 + (gentle ? .12 : .75) * ease(u), travel: gentle ? 0 : 38 * u };
}
// Fixed seeds and capacity: frame updates do not create particles or touch the simulation RNG.
export function gateMote(i, clock, width, hitAge = -1, gentle = false, out = {}) {
  const seed = (i * .61803398875) % 1, burst = i >= GATE_AMBIENT;
  let x, y, z, size;
  if (burst) {
    const a = (i - GATE_AMBIENT) / GATE_BURST * Math.PI * 2, u = sat(hitAge / 1.4);
    const envelope = hitAge < 0 || gentle ? 0 : ease(hitAge / .3) * (1 - ease((hitAge - .4) / 1));
    x = Math.cos(a) * (width * .34 + u * 6); y = 3.6 + Math.sin(a) * (3 + u * 3);
    z = -u * 44; size = (.09 + seed * .1) * envelope;
  } else {
    const phase = (seed + clock * .16) % 1;
    const envelope = Math.sin(phase * Math.PI) ** 2;
    const hash = Math.sin(i * 127.1 + 19.3) * 43758.5453;
    x = ((hash - Math.floor(hash)) * 2 - 1) * width * .48 + Math.sin(clock + i) * .45;
    y = .3 + phase * 7.4; z = Math.sin(i * 2.4 + clock * .45) * 4;
    size = (.07 + ((i * .37) % 1) * .1) * envelope;
  }
  Object.assign(out, { x, y, z, size }); return out;
}
