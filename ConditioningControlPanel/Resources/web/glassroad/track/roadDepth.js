// Render-only slab depth. The physical road stays at the original top surface.
import { visualClearanceSamples, capVisualDepth } from './visualDepth.js';

export function createRoadDepth(track) {
  const samples = visualClearanceSamples(track, { stepM: 8, headroomM: 4.5 });
  const cache = new Map();
  return s => {
    const wrapped = ((s % track.lengthM) + track.lengthM) % track.lengthM;
    if (cache.has(wrapped)) return cache.get(wrapped);
    const f = track.frameAt(wrapped);
    const wanted = track.id === 'goonworks' ? 1.8 : Math.max(.7, Math.min(1.2, f.w * .055));
    const depth = Math.max(.25, capVisualDepth({ ...f, s: wrapped, forkId: '' }, wanted, samples,
      { halfWidth: f.w / 2, halfLength: 2, motionM: 0, skipSpanM: 16 }));
    cache.set(wrapped, depth);
    return depth;
  };
}
