import { industryZones, industryPose } from '../sim/mainframeIndustry.js';
import { machineryBeatAt } from '../track/machineryBeat.js';

// One nearby impact per visible descent. Seeks, menu frames and catch-up never replay old impacts.
export function createPressContacts(track, emit, zones = industryZones(track)) {
  const presses = zones.filter(z => z.kind === 'piston'), seen = new Map();
  let lastTime = null;
  return {
    reset() { seen.clear(); lastTime = null; },
    update(timeS, position, { active = true, paused = false, gentle = false } = {}) {
      if (paused) return;
      if (!active || !Number.isFinite(timeS)) { seen.clear(); lastTime = null; return; }
      const continuous = lastTime !== null && timeS >= lastTime && timeS - lastTime < .3;
      const beat = machineryBeatAt(track, timeS);
      let closest = null, closestDistance = 48;
      for (const z of presses) {
        const pose = industryPose(z, timeS, beat);
        const index = beat ? pose.impactIndex : Math.floor((Math.max(0, timeS) + z.offsetS - 5.2) / 9);
        const previous = seen.get(z.id);
        seen.set(z.id, index);
        if (!continuous || !Number.isFinite(index) || !Number.isFinite(previous) || index !== previous + 1) continue;
        const distance = Math.hypot(...z.p.map((v, i) => v + z.b[i] * z.x - position[i]));
        if (distance < closestDistance) { closest = z; closestDistance = distance; }
      }
      lastTime = timeS;
      if (closest) emit?.('press-thud', { id: closest.id, gain: (1 - closestDistance / 48) ** 2, gentle });
    },
  };
}
