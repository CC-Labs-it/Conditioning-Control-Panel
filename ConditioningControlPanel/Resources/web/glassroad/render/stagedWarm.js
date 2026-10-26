/* render/stagedWarm.js - the phone tier's staged shader preparation, the deferred runner (plan: render/stagedWarmPlan.js,
 * poses and the split: render/stagedWarmPoses.js, which boot runs inside the warm).
 *
 * createDeferredWarm() prepares one group per step while the menu, the intro and the countdown play: only that
 * group's pieces are visible for the synchronous part of one compileAsync (the render list is taken there), so
 * nothing is ever drawn early, and their textures upload with them. Lights stay visible so every pipeline matches
 * the one the race frame asks for. Never used with mirror halls or cube targets. No three import: the renderer
 * loads this module under test stubs.
 */
import { STAGE } from './stagedWarmPlan.js';

const texturesOf = (m) => Object.values(m).filter((v) => v && v.isTexture);

/** One deferred group per step, one in flight. all: every renderable in the scene (they hide for the projection). */
export function createDeferredWarm(three, getPost, { all, groups, scene }) {
  let next = 0, busy = false, disposed = false;
  const log = [];
  async function run(g) {
    const t0 = performance.now(), mine = new Set(g.items), hidden = [], shown = [], unculled = [];
    let p;
    try {
      for (const o of all) if (o.visible && !mine.has(o)) { o.visible = false; hidden.push(o); }
      for (const o of g.items) {
        for (let q = o; q && q !== scene; q = q.parent) if (!q.visible) { q.visible = true; shown.push(q); }
        if (o.frustumCulled) { o.frustumCulled = false; unculled.push(o); }
      }
      p = getPost().compileStart();
    } finally {
      for (const o of hidden) o.visible = true;
      for (const o of shown) o.visible = false;
      for (const o of unculled) o.frustumCulled = true;
    }
    const syncMs = performance.now() - t0;
    try { await p; } catch (e) { console.warn('[glassroad/stagedWarm] ' + (e && e.message)); }
    for (const o of g.items) for (const m of [].concat(o.material || [])) if (m) for (const t of texturesOf(m)) { try { three.initTexture(t); } catch { /* drawn later */ } }
    log.push([Math.round(g.needT * 10) / 10, Math.round(syncMs), Math.round(performance.now() - t0)]);
  }
  return {
    /** Start the next group when none is in flight. */
    step() {
      if (disposed || busy || next >= groups.length) return;
      busy = true;
      const g = groups[next];
      run(g).finally(() => { busy = false; next++; });
    },
    /** True when a group needed before viewT + windowS is not prepared yet (the intro clock waits on it). */
    behind(viewT, windowS = STAGE.holdS) { return next < groups.length && groups[next].needT <= viewT + windowS; },
    get done() { return next >= groups.length; },
    get stats() { return { next, groups: groups.length, log: log.slice(-6), steps: log.length, maxSyncMs: log.reduce((m, x) => Math.max(m, x[1]), 0) }; },
    dispose() { disposed = true; },
  };
}
