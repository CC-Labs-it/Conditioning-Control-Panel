// The physical toys and their visible parts share this deterministic race-clock description.
import { getGoonworksLayout } from './goonworks-layout.js';
import { beatCycle, gearBeatTravel, machineryRearming } from './machineryBeat.js';

import { trapStroke } from './trapArrangement.js';

// Contact is deliberately inset from the visible toy. Grazing a bevel is a near miss.
export const GOONWORKS_CONTACT = Object.freeze({ revision: 1, radius: .4, widthScale: .72,
  depthScale: .7, heightInset: .35, wheelInset: .7, gapPadding: 6 * Math.PI / 180 });

export const GOONWORKS_TOYS = Object.freeze({ kartRadius: .7, kartHeight: 1.6, stallS: .28, repeatS: 1.1,
  wheelRadius: 20, wheelCenterY: 12.5, wheelDepth: 2.4, wheelGap: 50 * Math.PI / 180, wheelOpenings: 4,
  cycleBeats: 4, wheelRadiansPerBeat: Math.PI / 2 });
const TAU = Math.PI * 2;
const cycle = (t, period) => ((t % period) + period) % period;
const ease = t => { t = Math.max(0, Math.min(1, t)); return t * t * (3 - 2 * t); };

export function goonworksSetpieces(track, layout = getGoonworksLayout(track)) {
  if (track.id !== 'goonworks' || !layout) return [];
  const rows = [], part = id => layout.byId[id];
  const add = (kind, id, range, u, spec) => {
    if (!range) return;
    const s = range.s0 + (range.s1 - range.s0) * u, f = track.frameAt(s);
    rows.push({ id, kind, s, section: range.id, fromS: range.s0, toS: range.s1,
      roadWidth: f.w, p: [...f.p], b: [...f.b], n: [...f.n], t: [...f.t], ...spec });
  };
  for (let i = 0; i < 3; i++) add('wheel', 'aperture-wheel-' + i, part('mill-hall'), .22 + i * .28,
    { x: 0, centerY: GOONWORKS_TOYS.wheelCenterY, radius: GOONWORKS_TOYS.wheelRadius,
      gap: GOONWORKS_TOYS.wheelGap, openings: GOONWORKS_TOYS.wheelOpenings, depth: GOONWORKS_TOYS.wheelDepth,
      phase: -Math.PI / 2 + i * 1.9, speed: (i % 2 ? -.8 : .8), bypass: 18.5 });
  for (let i = 0; i < 4; i++) add('shutter', 'screen-shutter-' + i, part('grotto-gallery'), .18 + i * .21,
    { x: 0, w: 10, h: 8.5, depth: 1.8, travel: 6.5, offsetS: i * 1.7, periodS: 8.2, bypass: 14.8 });
  for (let j = 0; j < 4; j++) for (let i = 0; i < 4; i++) add('block', 'quiz-block-' + j + '-' + i,
    part('quiz-ballroom'), .23 + j * .18, { x: -15 + i * 10, w: 8, depth: 6,
      height: 4.6, offsetS: -j * .72 - i * .16, periodS: 8.8, bypass: 23 });
  for (const row of rows) {
    // A raised wheel's full radius is at hub height. Reserve its actual sweep through a ground kart.
    const extent = row.kind === 'wheel' ? Math.abs(row.x) + Math.sqrt(Math.max(0, row.radius ** 2 - Math.max(0, row.centerY - GOONWORKS_TOYS.kartHeight) ** 2))
      : row.kind === 'shutter' ? row.travel + row.w / 2 : Math.abs(row.x) + row.w / 2;
    if (row.roadWidth / 2 - extent < 3) throw Error('GOONWORKS toy needs an outside bypass: ' + row.id);
    row.bypass = Math.min(row.bypass, row.roadWidth / 2 - 3);
  }
  return rows.sort((a, b) => a.s - b.s);
}

export function goonworksPose(row, timeS = 0, beat = null) {
  const arranged = trapStroke(row, beat);
  if (arranged && row.kind !== 'wheel' && row.kind !== 'shutter') {
    const height = row.height * arranged.extension;
    return { ...arranged, phase: arranged.phase === 'lower' ? (row.kind === 'flipper' ? 'flip' : 'rise')
      : arranged.phase === 'rise' ? 'lower' : arranged.phase, x: row.x, height, angle: row.kind === 'flipper' ? Math.asin(row.height / row.depth) * arranged.extension : 0,
      active: row.kind === 'flipper' ? arranged.phase === 'lower' : height > .12 };
  }
  // Slower continuous rotation with two-beat endpoint holds, shared with collision.
  if (beat?.arrangement) {
    const b = Math.max(0, beat.beat), whole = Math.floor(b / 4), u = beatCycle(b, 4);
    const held = whole + ease(u / 2);
    if (row.kind === 'wheel') {
      const angle = (beat.rearming ? -Math.PI / 2 : row.phase) + held * Math.sign(row.speed) * GOONWORKS_TOYS.wheelRadiansPerBeat;
      const down = apertureAngle(row, -Math.PI / 2 - angle);
      return { angle, phase: down < row.gap * .38 ? 'open' : 'turning', warning: down < row.gap * .65,
        active: true, x: row.x, height: row.radius * 2 };
    }
    const offset = Math.round((row.offsetS || 0) / beat.beatS);
    if (beat.rearming && machineryRearming(beat, offset, 8)) return { x: row.x - row.travel,
      height: row.h, active: true, warning: true, phase: 'warn', angle: 0 };
    const t = beatCycle(beat.beat + offset, 8), f = t < 2 ? 0 : t < 4 ? ease((t - 2) / 2) : t < 6 ? 1 : 1 - ease((t - 6) / 2);
    return { x: row.x + (2 * f - 1) * row.travel, height: row.h, active: true,
      warning: t < 2 || (t >= 4 && t < 6), phase: 'sweep', angle: 0 };
  }
  if (beat?.fallback) { timeS *= beat.pace; beat = null; }
  const time = Number.isFinite(timeS) ? Math.max(0, timeS) : 0;
  const offset = Math.round((row.offsetS || 0) / (beat?.beatS || 1));
  // Moving toys follow their authored pickup pose. Rising toys wait for a full warning cycle.
  const moving = row.kind === 'wheel' || row.kind === 'shutter';
  const period = moving ? GOONWORKS_TOYS.cycleBeats : 2;
  const waiting = beat && !moving && beat.beat < beatCycle(-offset, period);
  if (beat && (waiting || (beat.rearming && machineryRearming(beat, offset, period)))) return { angle: row.kind === 'wheel' ? -Math.PI / 2 : 0, phase: 'warn', warning: true,
    active: row.kind === 'wheel' || row.kind === 'shutter', x: row.x,
    height: row.kind === 'wheel' ? row.radius * 2 : row.kind === 'shutter' ? row.h : 0 };
  if (row.kind === 'wheel') {
    const angle = (beat?.rearming ? -Math.PI / 2 : row.phase) + (beat ? gearBeatTravel(Math.max(0, beat.beat)) * Math.sign(row.speed) * GOONWORKS_TOYS.wheelRadiansPerBeat : time * row.speed);
    const down = apertureAngle(row, -Math.PI / 2 - angle);
    return { angle, phase: down < row.gap * .38 ? 'open' : 'turning', warning: down < row.gap * .65,
      active: true, x: row.x, height: row.radius * 2 };
  }
  // Every rise lands on a whole beat; the clear offbeat remains an actual opening.
  const t = beat ? beatCycle(beat.beat + offset, period) : cycle(time + (row.offsetS || 0), row.periodS);
  if (row.kind === 'shutter') {
    const angle = (beat ? gearBeatTravel(t) / period : t / row.periodS) * TAU;
    return { x: row.x + Math.sin(angle) * row.travel, height: row.h, active: true,
      warning: Math.abs(Math.cos(angle)) > .7, phase: 'sweep', angle: 0 };
  }
  if (row.kind === 'block') {
    if (beat) {
      const phase = t < .35 ? 'clear' : t < .8 ? 'warn' : t < 1 ? 'rise' : t < 1.2 ? 'hold' : t < 1.5 ? 'lower' : 'clear';
      const height = phase === 'rise' ? row.height * ease((t - .8) / .2) : phase === 'hold' ? row.height
        : phase === 'lower' ? row.height * (1 - ease((t - 1.2) / .3)) : 0;
      return { x: row.x, height, phase, active: height > .12, warning: phase === 'warn', angle: 0 };
    }
    const phase = t < 3 ? 'clear' : t < 4.3 ? 'warn' : t < 5 ? 'rise' : t < 6.7 ? 'hold' : t < 7.6 ? 'lower' : 'clear';
    const height = phase === 'rise' ? row.height * ease((t - 4.3) / .7) : phase === 'hold' ? row.height
      : phase === 'lower' ? row.height * (1 - ease((t - 6.7) / .9)) : 0;
    return { x: row.x, height, phase, active: height > .12, warning: phase === 'warn', angle: 0 };
  }
  if (beat) {
    const phase = t < .35 ? 'rest' : t < .8 ? 'warn' : t < 1 ? 'flip' : t < 1.2 ? 'hold' : t < 1.5 ? 'lower' : 'rest';
    const lift = phase === 'flip' ? ease((t - .8) / .2) : phase === 'hold' ? 1 : phase === 'lower' ? 1 - ease((t - 1.2) / .3) : 0;
    return { x: row.x, height: row.height * lift, angle: Math.asin(row.height / row.depth) * lift,
      phase, active: phase === 'flip', warning: phase === 'warn' };
  }
  const phase = t < 3.5 ? 'rest' : t < 4.7 ? 'warn' : t < 5 ? 'flip' : t < 5.35 ? 'hold' : t < 6.2 ? 'lower' : 'rest';
  const lift = phase === 'flip' ? ease((t - 4.7) / .3) : phase === 'hold' ? 1 : phase === 'lower' ? 1 - ease((t - 5.35) / .85) : 0;
  return { x: row.x, height: row.height * lift, angle: Math.asin(row.height / row.depth) * lift,
    phase, active: phase === 'flip', warning: phase === 'warn' };
}

/** Angular distance to the closest of the four equally spaced opening centres. */
export function apertureAngle(row, angle) {
  const spacing = TAU / (row.openings || 1);
  return Math.abs(cycle(angle + spacing / 2, spacing) - spacing / 2);
}

/** A point is solid only inside a real spoke, never inside any of its four cutouts. */
export function apertureContains(row, pose, x, height, inset = 0, gapPadding = 0) {
  const dx = x - row.x, dy = height - row.centerY;
  if (dx * dx + dy * dy > (row.radius - inset) ** 2) return false;
  const angle = Math.atan2(dy, dx) - pose.angle;
  return apertureAngle(row, angle) >= row.gap / 2 + gapPadding;
}

export function goonworksLocal(st, row, positionIndex = 0) {
  const d = row.p.map((v, j) => st[positionIndex + j] - v);
  return { x: d.reduce((v, n, j) => v + n * row.b[j], 0),
    along: d.reduce((v, n, j) => v + n * row.t[j], 0), h: d.reduce((v, n, j) => v + n * row.n[j], 0) };
}
