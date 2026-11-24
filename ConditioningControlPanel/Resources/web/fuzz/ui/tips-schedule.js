/* ui/tips-schedule.js - the first-run tips, pure: which tip is due, one on screen at a time, once per device.
 * No DOM. ui/tips.js reads the page into a facts object every half second and draws what tick() answers.
 *
 * facts = {
 *   blocked,    a card, the terms card, the intro, the pause card or the Guide is up (or the game is paused/ended)
 *   hardBlocked the same without counting the fork card (the choices tip sits beside it)
 *   fork,       a fork card is on screen
 *   objects,    dock objects showing
 *   overlap,    two of them overlap
 *   sounds,     discovered mixer sources
 *   clock,      run clock in ms
 *   fpsvAt,     run clock at the first {cur} per Second buy, or null
 *   guide,      the Guide is owned
 *   has,        { id: true } for every tip whose target is on the page now
 *   done,       [ids] the player just did the thing a tip teaches
 *   holdMiss,   a touch just dragged a casing without holding first (the 'keep' tip)
 * } */

export const TIP_IDS = Object.freeze(['move', 'resize', 'sound', 'choices', 'active', 'map', 'wake']);
/* Tips that answer something the player just tried (owner 2026-11-09): 'keep' follows a touch that dragged a casing
 * without holding first. They jump the queue, show once per device, and the Guide replay leaves them out. */
export const SITUATIONAL_TIPS = Object.freeze(['keep']);
export const ALL_TIP_IDS = Object.freeze([...TIP_IDS, ...SITUATIONAL_TIPS]);

export const TIP_TIMING = Object.freeze({
  showMs: 7000,        // each tip fades on its own after about 7 s
  gapMs: 3500,         // a few seconds between two tips
  replayGapMs: 900,    // the Guide's replay runs them closer together
  minSeenMs: 2500,     // a tip cut short before this comes back later
  activeDelayMs: 8000, // the active tip waits a little after the first {cur} per Second buy
  activeAtMs: 120000,  // or comes around minute 2
  wakeAtMs: 120000,    // Wake Up: about 2 minutes in, once three objects are out
  wakeObjects: 3,
});

/* Doing the thing early means the tip is not needed. Tapping is what everyone does, so it never cancels the active tip. */
const EARLY_DONE = new Set(['move', 'resize', 'sound', 'map', 'wake']);

export function triggered(id, f, t = TIP_TIMING) {
  switch (id) {
    case 'move': return f.objects >= 1;
    case 'resize': return f.objects >= 2 || !!f.overlap;
    case 'sound': return f.sounds >= 1;
    case 'choices': return !!f.fork;
    case 'active': return (Number.isFinite(f.fpsvAt) && f.clock - f.fpsvAt >= t.activeDelayMs) || f.clock >= t.activeAtMs;
    case 'map': return !!f.guide;
    case 'wake': return f.clock >= t.wakeAtMs && f.objects >= t.wakeObjects;
    case 'keep': return !!f.holdMiss;
    default: return false;
  }
}

export function createTipSchedule({ seen = {}, timing = {} } = {}) {
  const t = { ...TIP_TIMING, ...timing };
  const seenSet = new Set(Object.keys(seen || {}).filter((id) => ALL_TIP_IDS.includes(id) && seen[id]));
  const did = new Set();
  let queue = [];          // { id, replay }
  let current = null;      // { id, at, replay }
  let lastEnd = -Infinity;
  let lastWasReplay = false;

  const queued = (id) => queue.some((q) => q.id === id && !q.replay);
  const blockedFor = (id, f, replay) => {
    if (id === 'choices' && !replay) return !f.fork || !!f.hardBlocked;
    if (id === 'choices' && f.fork) return !!f.hardBlocked;
    return !!f.blocked;
  };

  function hide(now, counted, reason) {
    const c = current;
    current = null;
    lastEnd = now;
    lastWasReplay = c.replay;
    if (counted) seenSet.add(c.id);
    else if (!c.replay) queue.unshift({ id: c.id, replay: false });
    return { hide: c.id, seen: counted, reason };
  }

  function tick(now, f = {}) {
    const has = f.has || {};
    for (const id of f.done || []) did.add(id);

    /* 1. New triggers join the queue in the order they fire. Something the player already did is not taught. */
    for (const id of ALL_TIP_IDS) {
      if (seenSet.has(id) || queued(id) || (current && current.id === id && !current.replay)) continue;
      if (EARLY_DONE.has(id) && did.has(id)) { seenSet.add(id); continue; }
      if (!triggered(id, f, t)) continue;
      // An answer to what the player just tried goes first: later it would answer nothing.
      if (SITUATIONAL_TIPS.includes(id)) queue.unshift({ id, replay: false });
      else queue.push({ id, replay: false });
    }
    queue = queue.filter((q) => {
      if (q.replay || !EARLY_DONE.has(q.id) || !did.has(q.id)) return true;
      seenSet.add(q.id);
      return false;
    });

    /* 2. The tip on screen: gone when its time is up, when the player does it, or when something covers its place. */
    if (current) {
      const age = now - current.at;
      if ((f.done || []).includes(current.id) && EARLY_DONE.has(current.id)) return hide(now, true, 'done');
      if (current.id === 'choices' && !current.replay && !f.fork) return hide(now, age >= t.minSeenMs || did.has('choices'), 'closed');
      if (blockedFor(current.id, f, current.replay)) return hide(now, age >= t.minSeenMs, 'blocked');
      if (!current.replay && !has[current.id]) return hide(now, age >= t.minSeenMs, 'lost');
      if (age >= t.showMs) return hide(now, true, 'time');
      return null;
    }

    /* 3. Nothing up: the first queued tip that can show, after the gap. A missing target waits in the queue. */
    const gap = lastWasReplay && queue[0]?.replay ? t.replayGapMs : t.gapMs;
    if (now - lastEnd < gap) return null;
    for (let i = 0; i < queue.length; i++) {
      const q = queue[i];
      if (blockedFor(q.id, f, q.replay)) continue;
      if (!q.replay && !has[q.id]) continue;
      queue.splice(i, 1);
      current = { id: q.id, at: now, replay: q.replay };
      return { show: q.id, replay: q.replay, target: !!has[q.id] };
    }
    return null;
  }

  /** The Guide's Tips button: all seven again, in order, ahead of anything still waiting. */
  function replay(now) {
    const out = current ? hide(now, true, 'replay') : null;
    queue = [...TIP_IDS.map((id) => ({ id, replay: true })), ...queue.filter((q) => !q.replay)];
    lastEnd = -Infinity;
    return out;
  }

  return {
    tick, replay,
    get current() { return current ? { ...current } : null; },
    get queue() { return queue.map((q) => q.id); },
    get seen() { return Object.fromEntries([...seenSet].map((id) => [id, 1])); },
  };
}
