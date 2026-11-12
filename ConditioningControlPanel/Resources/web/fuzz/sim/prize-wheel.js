import { CONFIG } from '../data/config.js';
import { quoteSideLump } from './economy.js';
import { planLanding, easeOutQuart } from '../../backroom/stations/wheel/wheel.js';

const layout = [
  ['small', '0.5×', 30, 0.5, 'picture'],
  ['large', '5×', 8, 5, 'words', 'FOCUS'],
  ['nothing', 'Nothing', 20, 0, 'words', 'BLANK'],
  ['regular', '1×', 21, 1, 'picture'],
  ['jackpot', '10×', 4, 10, 'words', 'DEEPER'],
  ['double', '2×', 17, 2, 'words', 'RELAX'],
];
/* Journey rank 2 "Big Wedge" (owner 2026-11-10): the 10x wedge doubles, its odds double with it, and the extra width comes
 * from the other prize wedges. Nothing stays 20% and the wedge order stays, so the face only redraws its dividers. */
const BIG_WEIGHTS = Object.freeze({ small: 28, large: 8, nothing: 20, regular: 20, jackpot: 8, double: 16 });
function build(rows) {
  let percent = 0;
  return Object.freeze(rows.map(([id, label, weight, mult, effect, trigger]) => {
    const start = percent * 360 / 100;
    percent += weight;
    return Object.freeze({ id, label, weight, mult, effect, trigger, start, end: percent * 360 / 100 });
  }));
}
export const SECTORS = build(layout);
export const BIG_SECTORS = build(layout.map(([id, ...rest]) => [id, rest[0], BIG_WEIGHTS[id], ...rest.slice(2)]));
/** The wheel's Journey rank, read live: 1 = Respin, 2 = Big Wedge. Outside Journey there are no ranks. */
export const wheelRank = state => Math.max(0, Math.floor(Number(state?.store?.journey?.ranks?.wheel) || 0));
export const sectorsFor = big => big ? BIG_SECTORS : SECTORS;
/** Respin: Nothing hands back one free spin at once. A respin never earns another. */
export const earnsRespin = (round, rank) => !!round && round.id === 'nothing' && rank >= 1 && !round.respin;
export const SPIN_MS = 3400;
export const WHEEL_WAIT = 90;
export const wheelAngle = angle => ((angle % 360) + 360) % 360;

const RAD = Math.PI / 180;
export function rollWheel(random, from, releaseSpeed, sectors = SECTORS) {
  const sample = Math.min(1 - Number.EPSILON, Math.max(0, random)) * 360;
  const sector = sectors.find(s => sample < s.end);
  const big = sectors === BIG_SECTORS ? { big: true } : {};
  // Keep the pointer clear of dividers, including on the narrow jackpot wedge.
  const stop = sector.start + (sector.end - sector.start) * 0.20 + (sample - sector.start) * 0.60;
  if (Number.isFinite(releaseSpeed) && releaseSpeed !== 0) {
    const omega = Math.sign(releaseSpeed) * Math.min(.03, Math.max(.006, Math.abs(releaseSpeed)));
    const plan = planLanding({ from: from * RAD, omega, landing: -stop * RAD });
    return { id: sector.id, from, end: plan.to / RAD, duration: plan.ms, motion: 'fling', ...big };
  }
  const end = from + 4 * 360 + wheelAngle(-stop - from);
  return { id: sector.id, from, end, ...big };
}

/** A button spin winds up from rest. A manual spin leaves at the speed of the hand. */
export function wheelTravel(round, progress) {
  const fling = round.motion === 'fling';
  const fraction = fling ? easeOutQuart(progress) : 1 - (1 + 5 * progress) * (1 - progress) ** 5;
  const slope = fling ? 4 * (1 - progress) ** 3 : 30 * progress * (1 - progress) ** 4;
  const distance = round.end - round.from;
  return { degrees: distance * fraction, velocity: distance * RAD * slope / round.duration };
}

/** The shared budget can cap several different requests at the same payout.
 * Price the whole wheel against its jackpot so each printed multiple holds. */
export function wheelBase(run, rates) {
  const nominal = 30 * Math.max(rates.fps, rates.click * 2, 2) * run.pace;
  const jackpot = quoteSideLump(run, nominal * 10, 'wheel');
  return 2 * Math.floor(Math.min(jackpot, Number.MAX_SAFE_INTEGER) / 20);
}

/** Invert the monotone budget quote once per winning spin. Other toys may
 * have drawn from the budget since the displayed prize was locked. */
export function wheelRequest(run, prize) {
  if (prize <= 0) return 0;
  let low = prize, high = prize / CONFIG.SIDE_FLOOR;
  for (let i = 0; i < 54; i++) {
    const mid = (low + high) / 2;
    if (quoteSideLump(run, mid, 'wheel') >= prize) high = mid;
    else low = mid;
  }
  return high / run.pace;
}

export function restoreWheelSpin(saved) {
  const sectors = sectorsFor(saved?.big === true);
  if (!saved || !sectors.some(s => s.id === saved.id)) return null;
  const { base, elapsed, duration, from, end } = saved;
  if (!Number.isSafeInteger(base) || base < 0 || base > Number.MAX_SAFE_INTEGER / 10 ||
      !Number.isFinite(elapsed) || elapsed < 0 || !Number.isFinite(duration) ||
      !Number.isFinite(from) || from < 0 || from >= 360 || !Number.isFinite(end)) return null;
  const fling = saved.motion === 'fling';
  if (fling) {
    const distance = Math.abs(end - from);
    if (distance < 360 - 1e-7 || distance > 6 * 360 || (duration !== 400 && (duration < 3200 || duration > 5600))) return null;
  } else if (saved.motion != null || ![400, SPIN_MS].includes(duration) || end < from + 4 * 360 || end > from + 5 * 360) return null;
  const stop = wheelAngle(-end);
  if (sectors.find(s => stop < s.end)?.id !== saved.id) return null;
  return { id: saved.id, base, elapsed: Math.min(duration, elapsed), duration, from, end, ...(fling ? { motion: 'fling' } : {}),
    ...(saved.big === true ? { big: true } : {}), ...(saved.respin === true ? { respin: true } : {}) };
}
