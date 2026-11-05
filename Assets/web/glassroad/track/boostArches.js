// Quiet, supported driving lines get small boost runs. Song words keep their own surfaces.
import { goonworksSetpieces } from './goonworks-setpieces.js';
import { getBoudoirSetpieces } from './boudoir-setpieces.js';
import { getKeepLayout } from './keep-layout.js';
export const BOOST_ARCH_REVISION = 1;
const CACHE = new WeakMap(), HAZARDS = new WeakMap(), THEMES = new Set(['goonworks', 'boudoir', 'keep']);
const wrap = (s, L) => (s % L + L) % L;
const gap = (a, b, L) => Math.min(wrap(a - b, L), wrap(b - a, L));
function hazards(track) {
  if (HAZARDS.has(track)) return HAZARDS.get(track);
  const rows = [...(track.moving?.traps || [])];
  if (track.id === 'goonworks') rows.push(...goonworksSetpieces(track));
  if (track.id === 'boudoir') {
    const plan = getBoudoirSetpieces(track);
    rows.push(...plan.presses, ...plan.sweepers, ...plan.mirrorGates);
  }
  if (track.id === 'keep') { const plan = getKeepLayout(track); rows.push(...plan.gates, ...plan.chains); }
  HAZARDS.set(track, rows); return rows;
}

/** The entire approach and small-boost recovery follow the actual curved road. */
export function safeArchStation(track, s, x, halfWidth = 3) {
  const L = track.lengthM;
  for (let d = -12; d <= 100; d += 4) {
    const at = wrap(s + d, L), f = track.frameAt(at);
    if (!f.flags.includes('rail') || f.flags.includes('jump') || f.flags.includes('magnet') || f.n[1] < .86
      || Math.abs(f.t[1]) > .3 || f.w / 2 < Math.abs(x) + halfWidth + 2) return false;
    if ((track.ramps || []).some(r => gap(r.lipS, at, L) < r.lenM + 16)
      || hazards(track).some(r => gap(r.s, at, L) < (r.len || r.depthM || r.depth || 8) + 16)
      || (track.items || []).some(r => r.kind === 'box' && gap(r.s, at, L) < 12)) return false;
    for (const lateral of [x - halfWidth, x, x + halfWidth]) {
      const p = track.surfaceAt?.(at, lateral);
      if (!p) continue;
      const q = track.query(...p.p, p.seg);
      if (!q.onRoad || !q.railed || Math.abs(q.height) > .2 || gap(q.s, at, L) > .5) return false;
    }
  }
  return true;
}

export function boostArchPlan(track) {
  if (CACHE.has(track)) return CACHE.get(track);
  const arches = [], replaced = [];
  const add = (s, x, run) => arches.push({ id: arches.length, s: wrap(s, track.lengthM), x, halfWidth: 3, height: 5.6, run });
  if (THEMES.has(track.id)) {
    const groups = new Map();
    (track.items || []).forEach((it, i) => {
      if (it.kind !== 'bubble' || it.liftM) return;
      const key = it.node + ':' + it.x;
      if (!groups.has(key)) groups.set(key, []);
      groups.get(key).push({ ...it, i });
    });
    for (const rows of groups.values()) {
      rows.sort((a, b) => a.s - b.s);
      if (rows.length < 3 || !rows.every(r => safeArchStation(track, r.s, r.x))) continue;
      const run = 'bubbles-' + rows[0].node;
      for (let i = 0; i < rows.length; i += 2) add(rows[i].s, rows[i].x, run);
      replaced.push(...rows.map(r => r.i));
    }
    // Goonworks has no plain pickup strings. Borrow the opposite lane of proven pad corridors.
    if (track.id === 'goonworks') for (const pad of track.boosts || []) {
      if (arches.length >= 6 || arches.some(a => gap(a.s, pad.s, track.lengthM) < 180)) continue;
      const x = -(pad.x || -6), stations = [pad.s, pad.s + 24, pad.s + 48];
      if (stations.every(s => safeArchStation(track, s, x))) {
        const run = 'lock-' + arches.length;
        for (const s of stations) add(s, x, run);
      }
    }
  }
  const plan = { arches, replaced };
  CACHE.set(track, plan);
  return plan;
}
