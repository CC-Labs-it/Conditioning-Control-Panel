// Use the same curved support and displaced liquid as the scene, keeping depth occlusion.
import { makeFrame } from '../track/frames.js';
import { ambientWaterHeight } from './waterMotion.js';
import { WATER_VOLUME } from './waterMath.js';
export function createDecalSampler(track, waterVolume = null) {
  const wet = {};
  return (s, x, out = makeFrame(), fork = null) => {
    if (fork) fork.surfaceAt(s, x, out);
    else if (track.surfaceAt) track.surfaceAt(s, x, out);
    else { track.frameAt(s, out); for (let k = 0; k < 3; k++) out.p[k] += out.b[k] * x; }
    if (!fork && waterVolume?.field.sample(s, x, wet).wet) {
      const h = WATER_VOLUME.liftM + wet.height + ambientWaterHeight(out.p, waterVolume.uniforms.t.value);
      for (let k = 0; k < 3; k++) out.p[k] += out.n[k] * h;
    }
    return out;
  };
}

/** Fit between boost footprints where room exists; full-width pads use the raised text layer. */
export function clearWordLane(track, s, x, width, len) {
  const room = Math.max(0, (track.frameAt(s).w - width) / 2 - .45), lap = track.lengthM;
  let spans = [[-room, room]];
  for (const p of track.boosts || []) {
    const d = ((p.s + p.len / 2 - s + lap * 1.5) % lap) - lap / 2;
    if (Math.abs(d) > (p.len + len) / 2 + .4) continue;
    const lo = p.x - (p.w + width) / 2 - .4, hi = p.x + (p.w + width) / 2 + .4;
    spans = spans.flatMap(([a,b]) => hi < a || lo > b ? [[a,b]] : [[a,Math.min(b,lo)],[Math.max(a,hi),b]].filter(([l,r]) => r >= l));
  }
  return spans.length ? spans.map(([a,b]) => Math.max(a,Math.min(b,x))).sort((a,b) => Math.abs(a-x)-Math.abs(b-x))[0] : Math.max(-room,Math.min(room,x));
}
