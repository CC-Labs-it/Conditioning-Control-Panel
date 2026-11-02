/* render/stagedWarmPlan.js - the phone tier's staged shader preparation, the pure part (render/stagedWarm.js runs it).
 *
 * The boot warm on the phone tier prepares only what the first look needs behind the loading cover: the post chain,
 * the road and every scenery piece the opening intro take can show. Each other piece gets a need time, the earliest
 * view second at which a sampled camera pose (the intro takes, then a chase camera driven along the lap faster than
 * any kart) can see it, and is compiled in small groups during the menu, the intro and the countdown, in need order
 * and always STAGE.leadS ahead of the view clock. View time: 0 at the intro's first frame, the intro's own clock
 * through it, then the countdown, then race time. Pure, so tests run it under bare node.
 */

export const STAGE = Object.freeze({
  coverS: 0.5,        // pieces needed in the first half second of the intro are prepared behind the cover
  leadS: 2.5,         // a group is prepared while its need time is still this far ahead of the view clock
  holdS: 0.4,         // the intro clock waits when a group due inside this window is not ready yet
  perGroup: 4,        // materials new to the warm per deferred group (one group per step, small tasks)
  chaseMps: 45,       // the sampled chase camera, faster than top speed with a full boost (30 m/s x 1.3)
  chaseStepM: 25,     // chase pose spacing along the lap
  introStepS: 0.25,   // intro pose spacing
});

/** View seconds from race facts: the intro (0..introS), then the countdown (goS long), then race time. */
export function viewTime({ state, introElapsed = 0, introS = 10.6, goS = 3, goInS = 0, raceTimeS = 0, phase = '' }) {
  if (state === 'intro') return Math.max(0, introElapsed);
  if (state === 'race') return phase === 'countdown' || phase === 'ready' ? introS + Math.max(0, goS - Math.max(0, goInS)) : introS + goS + Math.max(0, raceTimeS);
  return -1;   // the menu (and anything before the intro): prepare ahead freely
}

/**
 * Split renderables into those prepared behind the cover and the deferred rest, grouped in need order.
 * entries: [{ item, needT, mats: [materialId], pinned }] (pinned = must be ready behind the cover: unknown runtime
 * visibility, never culled, or seen at once). Returns { now: [item], groups: [{ items: [item], needT }] }, every
 * deferred group bringing at most perGroup materials the warm has not met (a piece sharing only met materials joins
 * the group whose need time it falls in).
 */
export function planStages(entries, { coverS = STAGE.coverS, perGroup = STAGE.perGroup } = {}) {
  const now = [], later = [], seen = new Set();
  for (const e of entries) {
    if (e.pinned || !(e.needT > coverS)) { now.push(e.item); for (const m of e.mats) seen.add(m); }
    else later.push(e);
  }
  later.sort((a, b) => a.needT - b.needT);
  const groups = [];
  let cur = null, fresh = 0;
  for (const e of later) {
    let n = 0;
    for (const m of e.mats) if (!seen.has(m)) { seen.add(m); n++; }
    if (!cur || (n && fresh + n > perGroup)) { cur = { items: [], needT: e.needT }; groups.push(cur); fresh = 0; }
    cur.items.push(e.item); fresh += n;
  }
  return { now, groups };
}

/** The first group index not yet prepared whose need time falls inside viewT + windowS, or -1. */
export function dueIndex(groups, next, viewT, windowS) {
  return next < groups.length && groups[next].needT <= viewT + windowS ? next : -1;
}
