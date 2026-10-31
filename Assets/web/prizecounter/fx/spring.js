/* fx/spring.js - the numbers behind the juice (lane B3-W3). Pure: no DOM, no three. */

/* One exact step of a damped spring pulling s.x back to 0 (s.v = velocity). Exact, so any dt is stable: a long
 * frame lands the spring further along its own curve instead of overshooting it. zeta >= 1 is treated as critical. */
export function springStep(s, omega, zeta, dt) {
  if (!(dt > 0)) return s;
  const x = s.x, v = s.v;
  if (zeta < 1) {
    const a = zeta * omega, wd = omega * Math.sqrt(1 - zeta * zeta), e = Math.exp(-a * dt);
    const c = Math.cos(wd * dt), sn = Math.sin(wd * dt);
    s.x = e * (x * c + ((v + a * x) / wd) * sn);
    s.v = e * (v * c - ((a * v + omega * omega * x) / wd) * sn);
  } else {
    const e = Math.exp(-omega * dt), k = v + omega * x;
    s.x = e * (x + k * dt);
    s.v = e * (v - omega * k * dt);
  }
  return s;
}

export const resting = (s, eps = 1e-3) => Math.abs(s.x) < eps && Math.abs(s.v) < eps * 10;

/* The page settings to one level. motion off wins (cuts); Gentle visuals = fades only; reduced motion caps at less.
 * amp scales every move (less = half), dust and shake only when something really moves. */
export function fxLevel({ motion = 'full', gentle = false, reducedMotion = false } = {}) {
  const m = motion === 'off' ? 'off' : motion === 'less' || reducedMotion ? 'less' : 'full';
  if (m === 'off') return { mode: 'off', motion: 'off', amp: 0, dust: false, shake: false, fade: false };
  if (gentle) return { mode: 'gentle', motion: m, amp: 0, dust: false, shake: false, fade: true };
  return { mode: m, motion: m, amp: m === 'full' ? 1 : 0.5, dust: true, shake: true, fade: false };
}

export const between = (rng, [lo, hi]) => lo + (hi - lo) * rng();
