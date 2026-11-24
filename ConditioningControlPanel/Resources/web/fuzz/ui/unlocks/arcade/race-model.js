/* Bonus timing is separate from the recorded race. Only active game time counts. */
export const RACE_BONUS_MS = 35000;
export function restoreRace(saved) {
  return { leftMs: Number.isFinite(saved?.leftMs) ? Math.max(0, Math.min(RACE_BONUS_MS, saved.leftMs)) : RACE_BONUS_MS };
}
export function advanceRace(state, dt) {
  if (Number.isFinite(dt) && dt > 0) state.leftMs = Math.max(0, state.leftMs - dt);
  return state.leftMs === 0;
}
export function takeRaceBonus(state) {
  if (state.leftMs !== 0) return false;
  state.leftMs = RACE_BONUS_MS;
  return true;
}

/* Journey upgrades (owner 2026-11-10): rank 1 Pick the Course, rank 2 Nitro (the reward timer fills 30% faster). */
export const NITRO = 1.3;
export const raceTimerRate = (rank) => rank >= 2 ? NITRO : 1;
/** Real seconds left on the reward timer at this rate, for the button text. */
export const raceSecondsLeft = (state, rate = 1) => Math.ceil(state.leftMs / Math.max(1, rate) / 1000);

/**
 * The course that plays next. `picked` (Pick the Course) is a course index the player chose; it keeps playing until
 * they choose another or go back to the rotation (null). A picked course that cannot play falls back to the rotation.
 * `blocked(i)` says a course's clip is resting after a failure. Answers -1 when every course is blocked.
 */
export function nextCourse(current, picked, count, blocked = () => false) {
  if (Number.isInteger(picked) && picked >= 0 && picked < count && !blocked(picked)) return picked;
  for (let n = 1; n <= count; n++) {
    const i = (current + n) % count;
    if (!blocked(i)) return i;
  }
  return -1;
}

/*
 * Recorded stretches (assets/race/clip.json lists the same set with the metres and song seconds of each). The play
 * order is stretch-major, so two clips in a row are never the same course and every course shows up within five clips.
 * A clip file is `<course>-<stretch>.mp4` with a `<course>-<stretch>.jpg` poster beside it.
 */
export const RACE_STRETCHES = Object.freeze({
  threshold: Object.freeze(['a', 'b', 'c', 'd']),
  mainframe: Object.freeze(['a', 'b', 'c', 'd']),
  boudoir: Object.freeze(['a', 'b', 'c', 'd']),
  keep: Object.freeze(['a', 'b', 'c', 'd']),
  goonworks: Object.freeze(['a', 'b', 'c', 'd']),
});
export function raceClipList(stretches = RACE_STRETCHES) {
  const courses = Object.keys(stretches), out = [];
  const most = Math.max(0, ...courses.map((c) => stretches[c].length));
  for (let k = 0; k < most; k++) for (const course of courses) {
    const s = stretches[course][k];
    if (s) out.push(Object.freeze({ course, stretch: s, id: course + '-' + s }));
  }
  return Object.freeze(out);
}
export const RACE_CLIPS = raceClipList();

/**
 * The clip that plays after `current` (an index into `clips`). With a picked course (Pick the Course, a course id) the
 * next stretch of that course plays, so a pick still varies; a picked course whose clips all rest falls back to the
 * rotation. `blocked(i)` says clip i is resting after a failure. Answers -1 when every clip rests.
 */
export function nextClip(current, picked, clips = RACE_CLIPS, blocked = () => false) {
  const n = clips.length;
  if (!n) return -1;
  const from = Number.isInteger(current) && current >= 0 ? current % n : n - 1;
  if (picked && clips.some((c) => c.course === picked)) {
    for (let k = 1; k <= n; k++) { const i = (from + k) % n; if (clips[i].course === picked && !blocked(i)) return i; }
  }
  for (let k = 1; k <= n; k++) { const i = (from + k) % n; if (!blocked(i)) return i; }
  return -1;
}

/** Where a run's footage starts: the saved clip, else a random one (a fresh run does not always open on Threshold). */
export function firstClip(savedId, rng = Math.random, clips = RACE_CLIPS) {
  const i = clips.findIndex((c) => c.id === savedId);
  if (i >= 0) return i;
  if (!clips.length) return -1;
  const r = Number(rng());
  return Math.min(clips.length - 1, Math.floor((Number.isFinite(r) ? Math.max(0, r) : 0) * clips.length));
}

export function raceCanPlay({ paused, hidden, gentle, visible, dead, failed }) {
  return !paused && !hidden && !gentle && visible && !dead && !failed;
}
