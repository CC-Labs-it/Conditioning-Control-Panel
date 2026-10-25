/* Offline boost authoring only. Runtime uses the committed JSON unchanged, so hosts and
 * clients agree on every physical pad. Existing authored pads retain their exact order.
 */
import { loadTrack } from './load.js';
import { getKeepLayout } from './keep-layout.js';
import { scanZones } from '../sim/mainframeScan.js';
import { industryZones } from '../sim/mainframeIndustry.js';

export const BOOST_PLAN = Object.freeze({ lenM: 7.5, widthM: 10.5, spacingM: 18, runGapM: 68, lookAheadM: 60, itemClearM: 10 });
const wrap = (s, length) => ((s % length) + length) % length;
const distance = (a, b, length) => Math.min(wrap(a - b, length), wrap(b - a, length));

export function boostExclusions(track) {
  const spans = [];
  for (const c of [...(track.courts || []), ...(track.fields || [])]) spans.push([c.fromS - 65, c.toS + 25]);
  for (const j of track.jumps || []) spans.push([j.s - 85, j.landS + 45]);
  for (const route of track.routes || []) spans.push([route.land.fromS - 15, route.land.toS + 25]);
  if (track.id === 'keep') {
    const layout = getKeepLayout(track);
    for (const hall of layout.halls) spans.push([hall.s0 - 65, hall.s1 + 20]);
    for (const c of layout.chains) spans.push([c.s - 65, c.s + 20]);
  }
  if (track.id === 'mainframe') {
    const reactor = track.sections.find(section => section.id === 'spiral-core');
    // Leave the last address weave and reactor entry clear for braking and the bank handoff.
    if (reactor) spans.push([reactor.fromS - 140, reactor.fromS + 60]);
    for (const zone of scanZones(track)) spans.push([zone.s - 65, zone.s + zone.len + 20]);
    for (const zone of industryZones(track)) spans.push([zone.s - zone.len / 2 - 65, zone.s + zone.len / 2 + 20]);
  }
  return spans;
}

/** Physical support and braking room for a proposed pad. Can also validate external plans. */
export function safeBoostStation(track, s, { x = 0, w = BOOST_PLAN.widthM, len = BOOST_PLAN.lenM, exclusions = boostExclusions(track) } = {}) {
  const L = track.lengthM;
  for (const [a, b] of exclusions) for (const shift of [-L, 0, L]) if (s + len >= a + shift && s <= b + shift) return false;
  for (const item of track.items || []) {
    const ahead = wrap(item.s - s, L);
    if (ahead <= len + BOOST_PLAN.itemClearM || distance(item.s, s, L) < BOOST_PLAN.itemClearM) return false;
  }
  // A 36 m/s pad holds for one second, then releases. Keep the next 60m supported,
  // railed and free of tight bends so the player can brake before a technical section.
  const end = len + BOOST_PLAN.lookAheadM;
  for (let d = -8; ; d = Math.min(end, d + 5)) {
    const f = track.frameAt(s + d), before = track.frameAt(s + d - 2), after = track.frameAt(s + d + 2);
    if (!f.flags.includes('rail') || f.flags.includes('jump') || f.flags.includes('magnet') || Math.abs(f.t[1]) > .3 || f.n[1] < .82) return false;
    if (f.w / 2 < Math.abs(x) + w / 2 + 2) return false;
    const dot = before.t.reduce((sum, v, i) => sum + v * after.t[i], 0);
    const angle = Math.acos(Math.max(-1, Math.min(1, dot)));
    if (angle > 4 / 39) return false;
    for (const lateral of [x - w / 2, x, x + w / 2]) {
      const p = track.surfaceAt(s + d, lateral);
      const q = track.query(...p.p, p.seg);
      if (!q.onRoad || Math.abs(q.s - wrap(s + d, L)) > 1 || Math.abs(q.height) > .2) return false;
    }
    if (d === end) break; // include the exact endpoint even when the step does not divide it
  }
  return true;
}

/** Deterministic runs of 2-4 actual boost entries, with a visible gap between each pad. */
export function densifyBoosts(json) {
  const track = loadTrack(json), originals = json.boosts.map((p) => ({ ...p }));
  const exclusions = boostExclusions(track), candidates = [];
  const compact = track.id === 'threshold', spacing = compact ? 10 : BOOST_PLAN.spacingM;
  for (let node = 0; node < track.nodeCount; node++) {
    const s = track.nodeS[node];
    if (originals.some((p) => distance(s, track.nodeS[p.node], track.lengthM) < (compact ? 10 : 14))) continue;
    // Threshold's narrow side strips leave the existing assisted bypass beside
    // each broad centre pad clear, instead of forcing a hands-off kart onto a rail.
    const lanes = compact ? [-7.5, 0, 7.5] : [0];
    if (lanes.every((x) => safeBoostStation(track, s, { x, w: compact ? (x === 0 ? 6 : 2.25) : BOOST_PLAN.widthM, exclusions }))) candidates.push({ node, s });
  }
  const added = [], runs = [];
  let nextStart = compact ? 8 : 16;
  for (let i = 0; i < candidates.length; i++) {
    const first = candidates[i];
    if (first.s < nextStart) continue;
    const run = [first];
    const wanted = 2 + (runs.length % 3);
    for (let j = i + 1; j < candidates.length && run.length < wanted; j++) {
      const gap = candidates[j].s - run.at(-1).s;
      if (gap > 37) break;
      if (gap >= spacing) run.push(candidates[j]);
    }
    if (run.length < 2) continue;
    for (const { node } of run) for (const x of compact ? [-7.5, 0, 7.5] : [0])
      added.push({ node, x, w: compact ? (x === 0 ? 6 : 2.25) : BOOST_PLAN.widthM, len: BOOST_PLAN.lenM });
    runs.push(run.map((p) => p.node));
    nextStart = run.at(-1).s + BOOST_PLAN.runGapM;
  }
  return { boosts: [...originals, ...added], runs, originalCount: originals.length, addedCount: added.length };
}
