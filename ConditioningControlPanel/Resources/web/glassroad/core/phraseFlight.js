/* Shared phrase crossing baseline. The camera follows this grade, not each airborne arc. */
const clamp01 = x => Math.max(0, Math.min(1, x));
export const phraseEase = x => { x = clamp01(x); return x * x * (3 - 2 * x); };
export const PHRASE_FRACTION = .42;
const frames = new WeakMap();
export function phraseFlightPoints(track, zone) {
  let cache = frames.get(track);
  if (!cache) { cache = new Map(); frames.set(track, cache); }
  if (!cache.has(zone)) {
    const a = track.frameAt(zone.gapS0).p[1], b = track.frameAt(zone.gapS1).p[1];
    cache.set(zone, [{ s: zone.gapS0, y: a },
      { s: zone.gapS0 + (zone.gapS1 - zone.gapS0) * PHRASE_FRACTION,
        y: a + (b - a) * PHRASE_FRACTION - 2 * Math.sin(PHRASE_FRACTION * Math.PI) },
      { s: zone.gapS1 + 8, y: track.frameAt(zone.gapS1 + 8).p[1] }]);
  }
  return cache.get(zone);
}
export function phraseCameraGuide(track, s, out = {}) {
  const zone = track?.aerials?.find(z => s >= z.gapS0 && s < z.gapS1 + 12);
  if (!zone) return null;
  const points = phraseFlightPoints(track, zone), [a, b] = s < points[1].s ? points : points.slice(1);
  const u = clamp01((s - a.s) / (b.s - a.s));
  out.y = s > points[2].s ? track.frameAt(s).p[1] : a.y + (b.y - a.y) * phraseEase(u);
  out.weight = phraseEase((s - zone.gapS0) / 8) * phraseEase((zone.gapS1 + 12 - s) / 10);
  return out;
}
