/* Toys use physical silhouettes. All motion is a function of the host clock. */
export const TOYS = new Set(['pendulum', 'disc', 'tether', 'melt', 'snap', 'surge', 'bigSpiral']);
export const TOY_MODELS = Object.freeze({ pendulum: 'toy-pendulum-lod0', disc: 'toy-disc', tether: 'toy-tether-head', bigSpiral: 'toy-big-spiral' });
// Presentation metres only. Contact radii and targeting remain owned by the simulation.
export const TOY_SCALE = Object.freeze({ pendulum: 2.5, disc: 2.9, tether: 2.7, bigSpiral: 2.45 });
const HELD_SCALE = Object.freeze({ pendulum: 1.782, disc: 2.052, tether: 1.944, bigSpiral: 1.728 });
export const TOY_TRAIL = Object.freeze({ points: 26, stepM: .42, fadeS: .42, maxRetired: 16 });
const clamp = x => Math.max(0, Math.min(1, x));
const smooth = x => { const k = clamp(x); return k * k * (3 - 2 * k); };

export function toyScale(kind, held = false) {
  return (held ? HELD_SCALE[kind] : TOY_SCALE[kind]) || 1;
}

/** Release deformation stays centred on the real projectile, with no flight delay or aim offset. */
export function toyLaunchPose(age, quiet = false, out = {}) {
  const q = clamp(age / .4), spring = quiet ? 0 : Math.sin(q * Math.PI) * (1 - q);
  out.cross = 1 - .3 * spring;
  out.along = 1 + .85 * spring;
  out.alpha = smooth(age / (quiet ? .3 : .075));
  return out;
}

/** A readable tapered wake without pulsing. Quiet mode keeps the enlarged prop alone. */
export function toyTrailPoint(index, count, fade = 1, quiet = false) {
  const q = clamp((index + 1) / Math.max(1, count));
  return { size: .24 + .86 * q, alpha: quiet ? 0 : clamp(fade) * q * q * .78 };
}

export function toyPose(kind, time, still = false) {
  const t = still ? 0 : Math.max(0, time || 0);
  return { swing: kind === 'pendulum' ? Math.sin(t * 4.1) * .34 : 0,
    spin: kind === 'disc' ? t * 9 : kind === 'bigSpiral' ? t * 1.6 : 0,
    bank: kind === 'disc' ? .22 + (still ? 0 : Math.sin(t * 2.7) * .12) : 0 };
}

/** Slow IN/OUT also applies when photosafe refuses the bright accent. */
export function toyEnvelope(age, duration, gentle = false) {
  if (!(duration > 0) || age < 0 || age >= duration) return 0;
  const fade = Math.min(duration / 2, gentle ? .3 : .12);
  return smooth(age / fade) * smooth((duration - age) / fade);
}

export function snapRing(age, gentle = false, radius = 6) {
  const duration = gentle ? .9 : .6, q = clamp(age / duration);
  return { radius: radius * (gentle ? 1 : .16 + .84 * smooth(q)), alpha: toyEnvelope(age, duration, gentle) };
}

export function guardLook(k, gentle = false) {
  const left = clamp(Number.isFinite(k) ? k : 0);
  return { radius: 1.35, alpha: toyEnvelope(1 - left, 1, gentle) };
}

/** Physical disc faces are kept canted so their bright rim survives a chase view. */
export function toyLod(distance, phone = false) { return phone || distance > 28 ? 1 : 0; }
