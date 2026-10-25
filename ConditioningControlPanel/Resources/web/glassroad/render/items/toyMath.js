/* Toys use physical silhouettes. All motion is a function of the host clock. */
export const TOYS = new Set(['pendulum', 'disc', 'tether', 'melt', 'snap', 'surge', 'bigSpiral']);
export const TOY_MODELS = Object.freeze({ pendulum: 'toy-pendulum-lod0', disc: 'toy-disc', tether: 'toy-tether-head', bigSpiral: 'toy-big-spiral' });
const clamp = x => Math.max(0, Math.min(1, x));
const smooth = x => { const k = clamp(x); return k * k * (3 - 2 * k); };

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
