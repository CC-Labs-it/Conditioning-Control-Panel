/* Pure authored interaction stations and one race-clock pose authority for every client. */
import { getBoudoirLayout } from './boudoir-layout.js';
import { keepGatePhase } from '../sim/keepGates.js';
const TAU = Math.PI * 2, ease = u => { u = Math.max(0, Math.min(1, u)); return u * u * (3 - 2 * u); };
export const BOUDOIR_HAZARD = Object.freeze({ pressWidthM: 14, pressDepthM: 8, pressHeightM: 3.2,
  brushAmplitudeM: 6.5, brushRadiusM: 2.3, brushDepthM: 6, brushCycleS: 7.2,
  mirrorCycleS: 9.6, mirrorLiftM: 8.4, mirrorWidthM: 7, mirrorHeightM: 6,
  ribbonCycleS: 12, ribbonAccelMps2: 10, ribbonDragMps2: 7, ribbonMaxMps: 42 });
export function powderPressPhase(row, timeS) { return keepGatePhase(row, timeS); }
export function mascaraPhase(row, timeS) {
  const t = Math.max(0, Number.isFinite(timeS) ? timeS : 0) + (row.offsetS || 0), a = t * TAU / BOUDOIR_HAZARD.brushCycleS;
  return { x: (row.x || 0) + Math.sin(a) * BOUDOIR_HAZARD.brushAmplitudeM, y: 2.4,
    vx: Math.cos(a) * BOUDOIR_HAZARD.brushAmplitudeM * TAU / BOUDOIR_HAZARD.brushCycleS, angle: Math.sin(a) * .4,
    warning: Math.abs(Math.cos(a)) > .55 };
}
export function ribbonPhase(row, timeS) {
  const t = ((Math.max(0, Number.isFinite(timeS) ? timeS : 0) + (row.offsetS || 0)) % 12 + 12) % 12;
  const phase = t < 5.5 ? 'forward' : t < 7.5 ? 'warn' : t < 10.5 ? 'reverse' : 'clear';
  return { phase, direction: phase === 'forward' ? 1 : phase === 'reverse' ? -1 : 0, timeS: t };
}
/** The next mirror lane opens before the old lane closes, so a full-width wall never forms. */
export function mirrorGatePhase(row, timeS) {
  const absolute = Math.max(0, Number.isFinite(timeS) ? timeS : 0) + (row.offsetS || 0), cycle = Math.floor(absolute / 9.6), t = absolute % 9.6;
  const openLane = ((cycle + (row.sequence || 0)) % 3 + 3) % 3, nextLane = (openLane + 1) % 3, previousLane = (openLane + 2) % 3;
  const lane = row.lane ?? 0; let liftM = lane === openLane ? 8.4 : 0;
  if (lane === previousLane && t < 1.2) liftM = 8.4 * (1 - ease(t / 1.2));
  if (lane === nextLane && t >= 7.8) liftM = 8.4 * ease((t - 7.8) / 1.8);
  return { liftM, openLane, nextLane, phase: lane === openLane ? 'clear' : lane === nextLane && t >= 6.4 ? 'warn' : 'hold',
    blocking: liftM < 1.6, warning: t >= 6.4, cycleS: t };
}
export function getBoudoirSetpieces(track, layout = getBoudoirLayout(track)) {
  if (!layout) return { presses: [], sweepers: [], conveyors: [], mirrorGates: [] };
  const find = id => layout.landmarks.find(p => p.id === id), point = (id, u) => { const p = find(id); return p.s0 + (p.s1 - p.s0) * u; };
  const presses = [['powder-return', .25], ['powder-return', .65], ['upper-gallery', .35], ['upper-gallery', .8],
    ['bath-approach', .25], ['bath-approach', .55], ['bath-approach', .8], ['compacts-exit', .4]]
    .map(([id, u], i) => ({ id: 'powder-press-' + i, kind: 'press', s: point(id, u), x: 0, w: 14, depthM: 8, offsetS: i * 1.35 }));
  const sweepers = [['ribbon-weave', .3], ['ribbon-weave', .8], ['shelf-weave-cross', .3], ['shelf-weave-cross', .72], ['pearl-slalom-cross', .28], ['pearl-slalom-cross', .75],
    ['shelf-climb', .17], ['shelf-climb', .39], ['shelf-climb', .61], ['shelf-climb', .83],
    // The two drop paths use the opposite quarter of the descending helix.
    ['wardrobe-descent', 130 / 810], ['wardrobe-descent', 230 / 810], ['wardrobe-descent', 490 / 810], ['wardrobe-descent', 590 / 810]]
    .map(([id, u], i) => ({ id: 'mascara-sweep-' + i, kind: 'sweeper', s: point(id, u), x: 0, w: 17.6, depthM: 6, offsetS: i * 1.1 }));
  const conveyors = [['vanity-grid', .42, .86], ['wardrobe-bridge', .15, .86], ['bath-approach', .1, .9]]
    .map(([id, a, b], i) => ({ id: 'ribbon-conveyor-' + i, kind: 'conveyor', s0: point(id, a), s1: point(id, b), x: 0, w: 12, offsetS: i * 2.2 }));
  const mirrorGates = [45, 100, 155].flatMap((distance, station) => [-9, 0, 9].map((x, lane) => ({
    id: 'mirror-shutter-' + station + '-' + lane, station, lane, sequence: station, kind: 'mirror', s: find('drawer-hall').s0 + distance,
    x, w: 7, depthM: 1.2, offsetS: station * .7 })));
  return { presses, sweepers, conveyors, mirrorGates };
}
