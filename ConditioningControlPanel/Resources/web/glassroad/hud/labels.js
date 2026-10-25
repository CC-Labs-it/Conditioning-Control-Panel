/* hud/labels.js - device-aware button names for the Controls card and the pause hints, pure (no
 * DOM, no three; tested in tests/hud-pad.test.mjs). Xbox names, PlayStation names, button numbers
 * for any other pad (through its layout, so the number is the one the pad itself reports), and the
 * keyboard. The names follow whatever the player used last.
 */
import { PROFILES } from '../input/padmap.js';

const NAMED = {
  xbox: { name: 'Xbox', south: 'A', east: 'B', west: 'X', north: 'Y', lb: 'LB', rb: 'RB', ls: 'Left stick click', rs: 'Right stick click', lt: 'LT', rt: 'RT', start: 'Start' },
  ps: { name: 'PlayStation', south: 'Cross', east: 'Circle', west: 'Square', north: 'Triangle', lb: 'L1', rb: 'R1', ls: 'L3', rs: 'R3', lt: 'L2', rt: 'R2', start: 'Options' },
};

/** Button names for a pad family; 'generic' reads 'Button n' (1-based) through the layout. */
export function padLabels(family = 'xbox', profile = 'standard') {
  if (NAMED[family]) return { ...NAMED[family] };
  const p = PROFILES[profile] || PROFILES.standard;
  const b = (i) => 'Button ' + (i + 1);
  const t = (x) => (x.axis != null ? 'Axis ' + (x.axis + 1) : b(x.b));
  return { name: 'Pad', south: b(p.south), east: b(p.east), west: b(p.west), north: b(p.north), lb: b(p.lb), rb: b(p.rb), ls: b(p.ls), rs: b(p.rs), lt: t(p.lt), rt: t(p.rt), start: b(p.start) };
}

export const KEY_LABELS = Object.freeze({ name: 'Keys', steer: 'A D or arrows', throttle: 'W or Up', brake: 'S or Down', hop: 'Space or Shift, hold', item: 'E', drop: 'Hold S, then E', pause: 'Esc' });

/** Rows of the Controls card: [action, keys, pad, touch]. */
export function controlRows(family = 'xbox', profile = 'standard') {
  const p = padLabels(family, profile), k = KEY_LABELS;
  return [
    ['Steer', k.steer, 'Left stick', 'Drag on the left half'],
    ['Look around', 'Hold right mouse and drag', 'Right stick', ''],
    ['Chase / first person', 'C', p.rs, 'View button'],
    ['Look behind', 'Hold V', 'Hold ' + p.ls, 'Hold Rear button'],
    ['Throttle', k.throttle, p.rt, 'Automatic'],
    ['Brake', k.brake, p.lt, ''],
    ['Hop and drift', k.hop, p.south + ', hold', 'Hold the right half'],
    ['Use item', k.item, p.west + ' or ' + p.rb, 'Tap the item'],
    ['Drop it behind', k.drop, 'Stick down, then ' + p.west, ''],
    ['Pause', k.pause, p.start, 'Pause button'],
  ];
}

/**
 * What the cards show for the device in use.
 * @param {{ last?: string, pad?: { family?: string, profile?: string } | null }} d
 * @returns {{ last: 'keyboard'|'pad'|'touch', family: string, profile: string, padName: string }}
 */
export function deviceView(d = {}) {
  const last = d.last === 'pad' || d.last === 'touch' ? d.last : 'keyboard';
  const family = (d.pad && d.pad.family) || 'xbox', profile = (d.pad && d.pad.profile) || 'standard';
  return { last, family, profile, padName: padLabels(family, profile).name };
}

/** The pause card hint line for the device used last: '' on touch (the buttons speak for themselves). */
export function pauseHint(d = {}) {
  const v = deviceView(d);
  if (v.last === 'touch') return '';
  if (v.last === 'keyboard') return 'Arrows move, Enter picks';
  const p = padLabels(v.family, v.profile);
  return p.south + ' picks, ' + p.start + ' resumes';
}
