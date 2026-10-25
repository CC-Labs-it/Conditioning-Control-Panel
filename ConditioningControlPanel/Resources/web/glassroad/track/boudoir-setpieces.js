/* Pure authored interaction stations and one race-clock pose authority for every client. */
import { getBoudoirLayout } from './boudoir-layout.js';
import { keepGatePhase } from '../sim/keepGates.js';
import { pendulumBeatPhase } from '../sim/keepChains.js';
import { beatCycle, machineryRearming, gearBeatTravel } from './machineryBeat.js';
const TAU = Math.PI * 2, ease = u => { u = Math.max(0, Math.min(1, u)); return u * u * (3 - 2 * u); };
export const BOUDOIR_HAZARD = Object.freeze({ pressWidthM: 14, pressDepthM: 8, pressHeightM: 3.2,
  brushAmplitudeM: 2.9, brushRadiusM: 1.12, brushDepthM: 2.5, brushCycleS: 6.4,
  brushArmM: 8.6, brushPivotY: 10.25, brushImpulseMps: 5.2,
  mirrorCycleS: 9.6, mirrorLiftM: 8.4, mirrorWidthM: 7, mirrorHeightM: 6,
  ribbonCycleS: 12, ribbonAccelMps2: 10, ribbonDragMps2: 7, ribbonMaxMps: 42 });
export function powderPressPhase(row, timeS, beat = null) { return keepGatePhase(row, timeS, {}, beat); }
export function mascaraHeads(row) { return row.heads || [row]; }
export function mascaraPhase(row, timeS, beat = null) {
  const head = mascaraHeads(row)[0], K = BOUDOIR_HAZARD;
  const armM = head.armM ?? K.brushArmM, pivotY = head.pivotY ?? K.brushPivotY, amplitudeM = head.amplitudeM ?? K.brushAmplitudeM;
  if (beat) {
    const p = pendulumBeatPhase(head, head.cycleS || K.brushCycleS, beat), angleMax = Math.asin(amplitudeM / armM);
    const angle = p.swing * angleMax, angleRate = p.speed * angleMax;
    return { x: (head.x || 0) + Math.sin(angle) * armM, y: pivotY + p.lift - Math.cos(angle) * armM, z: head.longitudinalM || 0,
      vx: Math.cos(angle) * armM * angleRate, vy: Math.sin(angle) * armM * angleRate + p.liftSpeed, angle,
      pivotX: head.x || 0, pivotY: pivotY + p.lift, armM, radiusM: head.radiusM ?? K.brushRadiusM,
      depthM: head.depthM ?? K.brushDepthM, warning: p.safe || Math.abs(p.swing) < .7, safe: p.safe };
  }
  const rate = TAU / (head.cycleS || K.brushCycleS), t = Math.max(0, Number.isFinite(timeS) ? timeS : 0) + (head.offsetS || 0);
  const a = t * rate, angleMax = Math.asin(amplitudeM / armM), angle = Math.sin(a) * angleMax, angleRate = Math.cos(a) * angleMax * rate;
  return { x: (head.x || 0) + Math.sin(angle) * armM, y: pivotY - Math.cos(angle) * armM, z: head.longitudinalM || 0,
    vx: Math.cos(angle) * armM * angleRate, vy: Math.sin(angle) * armM * angleRate, angle,
    pivotX: head.x || 0, pivotY, armM, radiusM: head.radiusM ?? K.brushRadiusM, depthM: head.depthM ?? K.brushDepthM,
    warning: Math.abs(Math.cos(a)) > .55 };
}
export function ribbonPhase(row, timeS, beat = null) {
  if (beat) {
    const period = Math.max(4, Math.ceil(12 / beat.beatS / 4) * 4), offset = Math.round((row.offsetS || 0) / beat.beatS);
    const absolute = beat.beat + offset, u = beatCycle(absolute, period), safe = machineryRearming(beat,offset,period);
    const phase = safe ? 'warn'
      : u < period / 2 ? 'forward' : u < period / 2 + 2 ? 'warn' : u < period - 2 ? 'reverse' : 'clear';
    // Integrate the signed belt travel. Reversals no longer teleport every arrow to a new position.
    const travel = b => {
      const p = beatCycle(b, period), forward = Math.min(p, period / 2), reverse = Math.max(0, Math.min(p - period / 2 - 2, period / 2 - 4));
      return Math.floor(b / period) * 4 + gearBeatTravel(forward) - gearBeatTravel(reverse);
    };
    const start = beat.rearming ? beatCycle(-offset, period) : 0;
    const travelM = (travel(Math.max(start, beat.beat) + offset) - travel(start + offset)) * 3;
    return { phase, direction: phase === 'forward' ? 1 : phase === 'reverse' ? -1 : 0, timeS: u * beat.beatS, travelM };
  }
  const t = ((Math.max(0, Number.isFinite(timeS) ? timeS : 0) + (row.offsetS || 0)) % 12 + 12) % 12;
  const phase = t < 5.5 ? 'forward' : t < 7.5 ? 'warn' : t < 10.5 ? 'reverse' : 'clear';
  return { phase, direction: phase === 'forward' ? 1 : phase === 'reverse' ? -1 : 0, timeS: t };
}
/** The next mirror lane opens before the old lane closes, so a full-width wall never forms. */
export function mirrorGatePhase(row, timeS, beat = null) {
  if (beat) {
    const offset = Math.round((row.offsetS || 0) / beat.beatS), absolute = beat.beat + offset, cycle = Math.floor(absolute / 16), t = beatCycle(absolute,16);
    const openLane = ((cycle + (row.sequence || 0)) % 3 + 3) % 3, nextLane = (openLane + 1) % 3, previousLane = (openLane + 2) % 3;
    if (machineryRearming(beat,offset)) return { liftM:8.4,openLane,nextLane,phase:'warn',blocking:false,warning:true,cycleS:t*beat.beatS };
    const lane = row.lane ?? 0; let liftM = lane === openLane ? 8.4 : 0;
    if (lane === previousLane && t < 2) liftM = 8.4 * (1 - ease(t / 2));
    if (lane === nextLane && t >= 13) liftM = 8.4 * ease((t - 13) / 3);
    // Return from the all-open pickup or seek pose once, never on later cycles.
    const elapsed = beat.beat - (beat.rearming ? beatCycle(-offset,16) : 0), returning = elapsed >= 0 && elapsed < 2;
    if (returning) liftM = 8.4 + (liftM - 8.4) * ease(elapsed / 2);
    return { liftM,openLane,nextLane,phase:lane === openLane ? 'clear' : returning ? 'lower' : lane === nextLane && t >= 10 ? 'warn' : 'hold',
      blocking:liftM < 1.6,warning:returning || t >= 10,cycleS:t*beat.beatS };
  }
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
    .map(([id, u], i) => {
      const station = { id: 'mascara-sweep-' + i, kind: 'sweeper', s: point(id, u), x: 0, w: 17.2, depthM: 8.5, offsetS: i * 1.1 };
      const pivots = i % 2 ? [-4.5, 0, 4.5] : [-3.5, 3.5];
      station.heads = pivots.map((x, j) => ({ id: station.id + '-head-' + j, stationId: station.id, kind: 'sweeper', s: station.s, x,
        longitudinalM: pivots.length === 3 ? (j - 1) * 3 : (j - .5) * 5.4, radiusM: 1.12, depthM: 2.5,
        armM: 8.6, pivotY: 10.25, amplitudeM: 2.9, cycleS: 5.8 + j * .6, offsetS: i * 1.1 + j * 2.15 }));
      return station;
    });
  const conveyors = [['vanity-grid', .42, .86], ['wardrobe-bridge', .15, .86], ['bath-approach', .1, .9]]
    .map(([id, a, b], i) => ({ id: 'ribbon-conveyor-' + i, kind: 'conveyor', s0: point(id, a), s1: point(id, b), x: 0, w: 12, offsetS: i * 2.2 }));
  const mirrorGates = [45, 100, 155].flatMap((distance, station) => [-9, 0, 9].map((x, lane) => ({
    id: 'mirror-shutter-' + station + '-' + lane, station, lane, sequence: station, kind: 'mirror', s: find('drawer-hall').s0 + distance,
    x, w: 7, depthM: 1.2, offsetS: station * .7 })));
  return { presses, sweepers, conveyors, mirrorGates };
}
