/* ui/unlocks/moan.js - a crit from the player's own tap gets a recorded moan. One at a time: a crit during a clip stays silent. */

const CLIPS = [['short', 1440], ['soft', 1680], ['hum', 2320], ['breathy', 2960], ['rising', 3680], ['long', 4880]];
let busyUntil = 0, last = -1;
/** Owner 2026-11-08: far too frequent. After a moan the next crit waits 35 to 60 seconds, and even then only one crit in two gets one. */
const REST_MS = 35000, REST_SPREAD_MS = 25000, CHANCE = 0.5;
/** Owner 2026-11-07: the moans were far too loud. 40% of the old 0.4; the mixer source still lets a player raise them. */
const MOAN_GAIN = 0.16;

export function moan(ctx) {
  const now = performance.now();
  if (now < busyUntil || Math.random() >= CHANCE) return;   // cosmetic: stays off the run's seed
  let i = Math.floor(Math.random() * CLIPS.length);   // cosmetic: stays off the run's seed
  if (i === last) i = (i + 1) % CLIPS.length;
  last = i;
  busyUntil = now + CLIPS[i][1] + REST_MS + Math.random() * REST_SPREAD_MS;
  ctx.audio.play(`assets/audio/moans/${CLIPS[i][0]}.mp3`, { bus: 'voice', source: 'moans', sourceLabel: 'Lucky moans', gain: MOAN_GAIN, fadeInMs: 400, fadeOutMs: 180 });
}
