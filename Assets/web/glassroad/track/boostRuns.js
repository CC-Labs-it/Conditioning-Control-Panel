import { safeBoostTurn } from './boostSafety.js';
import { safeBoostCorridor } from './boostCorridor.js';

/** Reward each safe corridor with a short pad train, without forcing boosts into bends. */
export function extendBoostRuns(track, pads, authored = []) {
  const result = pads.slice(), L = track.lengthM;
  const wrap = s => (s % L + L) % L;
  const distance = (a, b) => Math.min(wrap(a - b), wrap(b - a));
  const proposals = pads.flatMap(source => [-40, -20].map(offset => ({ source, offset, fallback: false })));
  // An old pad near a corner can move back into the same district's straight.
  if (pads.length < 6) for (const source of authored) for (let offset = -300; offset <= 300; offset += 20) {
    proposals.push({ source, offset, fallback: true });
  }
  for (const { source, offset, fallback } of proposals) {
    if (fallback && result.length >= 12) break;
    const s = wrap(source.s + offset);
    if (result.some(p => Math.abs((p.x || 0) - (source.x || 0)) < (p.w + source.w) / 2
      && distance(p.s, s) < Math.max(p.len, source.len) + 8)) continue;
    const frame = track.frameAt(s);
    if (track.sectionAt(s) !== track.sectionAt(source.s) && Math.abs(s - source.s) < L / 2) continue;
    const pad = { ...source, s, node: frame.seg };
    let supported = true;
    for (let d = -8; d <= pad.len + 60; d += 2) {
      const f = track.frameAt(s + d);
      if (!f.flags.includes('rail') || f.flags.includes('jump') || f.flags.includes('magnet')
        || f.w / 2 < Math.abs(pad.x || 0) + pad.w / 2 + 2 || Math.abs(f.t[1]) > .3
        || track.fields.some(field => s + d >= field.fromS - 65 && s + d <= field.toS + 45)
        || track.courts.some(court => s + d >= court.fromS - 65 && s + d <= court.toS + 45)
        || track.aerials.some(a => s + d >= a.rampS0 - 85 && s + d <= a.landingS1 + 25)
        || track.jumps.some(j => s + d >= j.s - 85 && s + d <= j.landS + 45)
        || track.routes.some(r => s + d >= r.land.fromS - 15 && s + d <= r.land.toS + 25)
        || d <= pad.len && track.items.some(item => item.kind === 'box' && distance(item.s, wrap(s + d)) < 10)) { supported = false; break; }
    }
    if (supported && safeBoostTurn(track, pad) && safeBoostCorridor(track, pad)) result.push(pad);
  }
  return result.sort((a, b) => a.s - b.s || a.x - b.x);
}
