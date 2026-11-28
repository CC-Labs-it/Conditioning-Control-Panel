import { NARRATION_SUBTITLES } from './subtitles.js';
import { RIDE_RECORDINGS } from './narration-data.js';

const normalize = text => String(text).replace(/\s+/g, ' ').trim().toLowerCase();
// The opening image can pass while the narrator continues through the gallery.
// The reflex comparison creeps forward past one screen; it never parks for a recording.
const PROFILES = {
  r1: [
    { at: 0, z: 0 }, { seconds: 1.4, z: 0 },
    { phrase: 'I would tell you their name', z: 8 },
    { phrase: 'but the budget for this narration', z: 18 },
    { phrase: 'so I was not about to spend it', z: 43 },
    { phrase: 'you will be known as the Subject', z: 75 }, { at: 1, z: 90 },
  ],
  r2: [
    { at: 0, z: 102 }, { phrase: 'and stops the very', z: 103.6 },
    { phrase: 'I raised this with the writers', z: 105 },
    { phrase: 'that it was beneath', z: 106.8 },
    { phrase: 'pumping for the sake of a narrative', z: 122 }, { at: 1, z: 132 },
  ],
  r3: [
    { at: 0, z: 291 }, { phrase: 'The cheetah reaches', z: 300.6 },
    { phrase: 'The mantis shrimp strikes', z: 301.11 },
    { phrase: "moves quite as fast as the Subject's", z: 301.92 },
    { phrase: 'on hearing a floorboard creak', z: 302.34 },
    { phrase: 'and replaced, seamlessly, with a spreadsheet', z: 303.64 },
    { phrase: 'Again, in slow motion', z: 304.2 }, { at: 1, z: 304.7 },
  ],
  r4: [
    { at: 0, z: 370 }, { phrase: 'to a world of daylight', z: 409 },
    { phrase: 'to a world of daylight', edge: 'end', z: 413 },
    { phrase: 'Our research shows almost nobody', z: 417 },
    { phrase: 'Because to this particular audience', z: 449 },
    { phrase: '"wake up" is the single least', z: 461 },
    { phrase: 'language, narrowly', z: 482 },
    { phrase: 'and "we need to talk"', z: 495 }, { at: 1, z: 498.5 },
  ],
};

/** Resolve cart anchors from the same phrase boundaries as the captions and slides. */
export function pacingTimeline(id, duration, subtitles = NARRATION_SUBTITLES) {
  const cues = subtitles[id] || [];
  return (PROFILES[id] || []).flatMap(anchor => {
    const cue = anchor.phrase ? cues.find(([, , text]) => normalize(text).includes(normalize(anchor.phrase))) : null;
    const at = anchor.seconds ?? ((anchor.at ?? cue?.[anchor.edge === 'end' ? 1 : 0]) * duration);
    return Number.isFinite(at) ? [{ at, z: anchor.z }] : [];
  }).sort((a, b) => a.at - b.at).filter((point, index, points) => !index || point.at > points[index - 1].at);
}

// Monotone Hermite interpolation keeps velocity continuous without overshooting a screen.
export function samplePacing(points, time) {
  if (points.length < 2) return null;
  const slopes = points.slice(1).map((point, i) => (point.z - points[i].z) / (point.at - points[i].at));
  const tangent = index => {
    if (!index) return slopes[0];
    if (index === points.length - 1) return slopes.at(-1);
    const before = slopes[index - 1], after = slopes[index];
    return before > 0 && after > 0 ? 2 * before * after / (before + after) : 0;
  };
  if (time <= points[0].at) return { target: points[0].z, speed: tangent(0) };
  if (time >= points.at(-1).at) return { target: points.at(-1).z, speed: Math.max(.45, tangent(points.length - 1)) };
  const right = points.findIndex(point => point.at > time), left = right - 1;
  const a = points[left], b = points[right], span = b.at - a.at, t = (time - a.at) / span;
  const m0 = tangent(left), m1 = tangent(right), t2 = t * t, t3 = t2 * t;
  const target = (2 * t3 - 3 * t2 + 1) * a.z + (t3 - 2 * t2 + t) * span * m0 +
    (-2 * t3 + 3 * t2) * b.z + (t3 - t2) * span * m1;
  const speed = ((6 * t2 - 6 * t) * a.z + (3 * t2 - 4 * t + 1) * span * m0 +
    (-6 * t2 + 6 * t) * b.z + (3 * t2 - 2 * t) * span * m1) / span;
  return { target, speed: Math.max(0, speed) };
}

/** Read the media clock from the ride's existing physics tick, without another timer or RAF. */
export function createNarrationPacer(tunnel, read, { subtitles = NARRATION_SUBTITLES } = {}) {
  let disposed = false, cachedId = null, cachedDuration = 0, entryZ = 0, points = [], last = null;
  function provide(state) {
    if (disposed || state.docked) return null;
    const clip = read();
    if (!clip?.ride) {
      const next = RIDE_RECORDINGS.find(ride => ride.at > state.z);
      const entrySpeed = next?.id === 'r2' ? .35 : next?.id === 'r3' ? 2.5 : 2.4;
      const speed = next ? Math.min(5.5, Math.sqrt(entrySpeed ** 2 + Math.max(0, next.at - state.z) * 3.2)) : 5.5;
      last = { mode: 'travel', target: state.z, speed, minSpeed: 0, maxSpeed: 7, ceiling: 580 };
      return last;
    }
    if (cachedId !== clip.id || cachedDuration !== clip.duration) {
      if (cachedId !== clip.id) entryZ = state.z;
      cachedId = clip.id; cachedDuration = clip.duration;
      points = pacingTimeline(clip.id, clip.duration, subtitles);
      // The speech gap may carry the cart a few metres beyond the trigger.
      // Join from that real position instead of braking backwards to the written start.
      if (points.length && entryZ > points[0].z) {
        const diagnosticEntry = entryZ - points[0].z > 8;
        points[0] = { ...points[0], z: entryZ };
        for (let i = 1; i < points.length; i++) {
          const minimum = diagnosticEntry ? Math.max(.2, (points[i].at - points[i - 1].at) * .55) : .2;
          points[i] = { ...points[i], z: Math.max(points[i].z, points[i - 1].z + minimum) };
        }
      }
    }
    const sample = samplePacing(points, clip.time);
    if (!sample) return null;
    // Loading and stalled audio can brake the cart. Movement never pauses or seeks the recording.
    const playing = Boolean(clip.playing);
    const lead = clip.id === 'r3' ? .1 : 1.2;
    // The two Wake Up images are 86m apart. A brief faster transfer follows the
    // daylight phrase, then the allowance tapers before the conversation screen.
    const zoneCap = clip.id === 'r4' && state.z >= 413 ? Math.min(6.5, 4.5 + Math.max(0, 490 - state.z) * .3) : undefined;
    last = { mode: playing ? 'narration' : 'waiting', ...sample,
      speed: playing ? sample.speed : 0, minSpeed: playing && clip.time > 1.4 ? (clip.id === 'r3' ? .12 : .3) : 0,
      maxSpeed: 7, zoneCap, ceiling: sample.target + (playing ? lead : 0), id: clip.id };
    return last;
  }
  const release = tunnel.setPace?.(provide);
  return Object.freeze({
    provide,
    dispose() { if (disposed) return; disposed = true; release?.(); },
    debug: () => last ? { ...last } : null,
  });
}
