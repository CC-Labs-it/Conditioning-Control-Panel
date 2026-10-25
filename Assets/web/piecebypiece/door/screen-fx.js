/* ============================================================================
 * door/screen-fx.js - how a front-door screen arrives and leaves.
 *
 * One shared pair so the menu, the lobby, the ladder and the pictures picker
 * all move the same way (JUICE-PLAYBOOK section 2):
 *
 *   screenIn(el, opts?)  -> Promise   the children land one after another:
 *                                     up from 10 px, a touch small, a little
 *                                     overshoot, 35 ms apart, 260 ms each
 *   screenOut(el, opts?) -> Promise   the whole screen goes at once, quicker
 *                                     than it came (160 ms), up and away
 *   leaveGhost(el, opts?) -> Promise  screenOut on a frozen copy of el laid
 *                                     exactly over it, so the caller can swap
 *                                     the real contents at once (the door's
 *                                     render() stays synchronous) while the old
 *                                     screen still visibly goes somewhere
 *
 * Reduced motion (opts.still, an ancestor with .still, or the OS query) is a
 * plain 120 ms fade both ways: the state, not the travel.
 *
 * Compositor-only: opacity and transform. The pure numbers are in FX below
 * and inPlan()/outPlan() so node can test them without a DOM.
 * ==========================================================================*/

export const FX = Object.freeze({
  inMs: 260,          // one child's arrival
  staggerMs: 35,      // between children
  staggerCap: 9,      // the tenth child and later land with the ninth
  outMs: 160,         // exits are quicker than entrances
  calmMs: 120,        // reduced motion: a plain fade
  rise: 10,           // px a child rises as it lands
  from: 0.97,         // scale a child grows from
  lift: 8,            // px the old screen lifts as it goes
  // THUD, toned down: a small overshoot that settles (the full THUD is the card's own reveal)
  easeIn: 'cubic-bezier(.2, 1.25, .4, 1)',
  easeOut: 'cubic-bezier(.4, 0, 1, 1)',
});

/** Delay for child i. */
export function staggerAt(i) { return Math.min(Math.max(0, i | 0), FX.staggerCap) * FX.staggerMs; }

/** Keyframes + timing for child i coming in. */
export function inPlan(i, still = false) {
  if (still) return { frames: [{ opacity: 0 }, { opacity: 1 }], timing: { duration: FX.calmMs, delay: 0, easing: 'linear', fill: 'backwards' } };
  return {
    frames: [
      { opacity: 0, transform: `translateY(${FX.rise}px) scale(${FX.from})` },
      { opacity: 1, transform: 'none' },
    ],
    timing: { duration: FX.inMs, delay: staggerAt(i), easing: FX.easeIn, fill: 'backwards' },
  };
}

/** Keyframes + timing for a whole screen going out. */
export function outPlan(still = false) {
  if (still) return { frames: [{ opacity: 1 }, { opacity: 0 }], timing: { duration: FX.calmMs, easing: 'linear', fill: 'forwards' } };
  return {
    frames: [
      { opacity: 1, transform: 'none' },
      { opacity: 0, transform: `translateY(-${FX.lift}px) scale(0.985)` },
    ],
    timing: { duration: FX.outMs, easing: FX.easeOut, fill: 'forwards' },
  };
}

function calm(el, opts) {
  if (opts && typeof opts.still === 'boolean') return opts.still;
  try { if (el && el.closest && el.closest('.still')) return true; } catch { /* detached */ }
  try { return !!(globalThis.matchMedia && globalThis.matchMedia('(prefers-reduced-motion: reduce)').matches); } catch { return false; }
}

const done = (a) => (a && a.finished ? a.finished.catch(() => {}) : Promise.resolve());

/** The children of el land in turn. Resolves when the last one has. */
export function screenIn(el, opts = {}) {
  if (!el || typeof el.animate !== 'function') return Promise.resolve();
  const still = calm(el, opts);
  const kids = Array.from(el.children || []).filter((k) => typeof k.animate === 'function');
  const list = kids.length ? kids : [el];
  const runs = list.map((k, i) => { const p = inPlan(i, still); try { return done(k.animate(p.frames, p.timing)); } catch { return Promise.resolve(); } });
  return Promise.all(runs).then(() => {});
}

/** The whole of el goes. Resolves when it has (el is left invisible; the caller removes or replaces it). */
export function screenOut(el, opts = {}) {
  if (!el || typeof el.animate !== 'function') return Promise.resolve();
  const p = outPlan(calm(el, opts));
  try { return done(el.animate(p.frames, p.timing)); } catch { return Promise.resolve(); }
}

/* ---------------------------------------------------------------- the count
 * A stat tile's number rolls up from 0 instead of just being there (Law XII:
 * value must be seen to move). Ease-out, so it races then settles; a tick
 * cue at most every ROLL.tickMs, never one per frame. Text around the number
 * ("74 moves") stays as it is; a value with no number in it ("pawn", "26:17")
 * is not rolled, it only lands with its tile. */
export const ROLL = Object.freeze({ ms: 650, maxMs: 900, perUnitMs: 22, tickMs: 70 });

/** How long a roll to `to` takes: short numbers roll short. */
export function rollMs(to) { const n = Math.abs(Number(to) || 0); return n <= 0 ? 0 : Math.min(ROLL.maxMs, Math.max(260, Math.min(ROLL.ms, n * ROLL.perUnitMs) + 200)); }

/** The value shown at time t (ms) of a roll to `to`. Whole numbers only; lands exactly. */
export function rollValue(t, to) {
  const ms = rollMs(to);
  if (!ms || t >= ms) return Math.round(Number(to) || 0);
  const k = Math.max(0, t) / ms;
  return Math.round((Number(to) || 0) * (1 - Math.pow(1 - k, 3)));
}

/** "74 moves" -> { pre: '', n: 74, post: ' moves' }; "26:17" and "pawn" -> null. */
export function splitCount(text) {
  const m = /^(\D*?)(\d{1,6})(\D*)$/.exec(String(text ?? ''));
  if (!m || /[:.]/.test(m[3].slice(0, 1))) return null;
  return { pre: m[1], n: Number(m[2]), post: m[3] };
}

/**
 * Roll el's number up from 0 after `delay` ms. onTick(value) for the cue.
 * Reduced motion shows the number at once. Returns a stop function.
 */
export function countUp(el, opts = {}) {
  const parts = el ? splitCount(el.textContent) : null;
  if (!parts || parts.n === 0) return () => {};
  if (calm(el, opts) || typeof requestAnimationFrame !== 'function') return () => {};
  const paint = (v) => { el.textContent = parts.pre + v + parts.post; };
  paint(0);
  let raf = 0, start = 0, lastTick = -1e9, stopped = false;
  const step = (now) => {
    if (stopped) return;
    if (!start) start = now;
    const t = now - start - (opts.delay || 0);
    const v = t < 0 ? 0 : rollValue(t, parts.n);
    paint(v);
    if (t >= 0 && v > 0 && now - lastTick >= ROLL.tickMs && typeof opts.onTick === 'function') { lastTick = now; try { opts.onTick(v); } catch { /* sound is optional */ } }
    if (t < rollMs(parts.n)) raf = requestAnimationFrame(step);
    else { paint(parts.n); if (typeof opts.onDone === 'function') opts.onDone(); }
  };
  raf = requestAnimationFrame(step);
  return () => { stopped = true; cancelAnimationFrame(raf); paint(parts.n); };
}

/**
 * A frozen copy of el, laid over it in its offsetParent, goes out and removes
 * itself. The caller is free to replace el's contents straight after.
 */
export function leaveGhost(el, opts = {}) {
  if (!el || !el.parentNode || typeof el.cloneNode !== 'function') return Promise.resolve();
  let ghost = null;
  try {
    ghost = el.cloneNode(true);
    ghost.removeAttribute('id');
    for (const n of ghost.querySelectorAll('[id]')) n.removeAttribute('id');
    ghost.classList.add('fx-ghost');
    ghost.setAttribute('aria-hidden', 'true');
    ghost.inert = true;
    Object.assign(ghost.style, {
      position: 'absolute', left: el.offsetLeft + 'px', top: el.offsetTop + 'px',
      width: el.offsetWidth + 'px', height: el.offsetHeight + 'px', margin: '0', pointerEvents: 'none',
    });
    el.parentNode.appendChild(ghost);   // after el: a querySelector on the parent still finds the live screen first
  } catch { if (ghost) ghost.remove(); return Promise.resolve(); }
  return screenOut(ghost, { still: calm(el, opts), ...opts }).then(() => ghost.remove());
}

/**
 * A short one-shot keyframe on el: 'pop' (a chip turning on), 'knock' (the
 * card taking a stamp), 'nope' (a refused press). Off under reduced motion.
 */
export const KICKS = Object.freeze({
  pop: { frames: [{ transform: 'scale(1)' }, { transform: 'scale(1.12)', offset: 0.35 }, { transform: 'scale(0.98)', offset: 0.7 }, { transform: 'scale(1)' }], ms: 300 },
  knock: { frames: [{ transform: 'translateY(0)' }, { transform: 'translateY(4px) scale(0.995)', offset: 0.25 }, { transform: 'translateY(-1px)', offset: 0.6 }, { transform: 'translateY(0)' }], ms: 320 },
  nope: { frames: [{ transform: 'translateX(0)' }, { transform: 'translateX(-4px)', offset: 0.25 }, { transform: 'translateX(4px)', offset: 0.5 }, { transform: 'translateX(-2px)', offset: 0.75 }, { transform: 'translateX(0)' }], ms: 250 },
});
export function kick(el, name, opts = {}) {
  const k = KICKS[name];
  if (!k || !el || typeof el.animate !== 'function' || calm(el, opts)) return Promise.resolve();
  try { return done(el.animate(k.frames, { duration: k.ms, easing: 'ease-out', delay: opts.delay || 0 })); } catch { return Promise.resolve(); }
}
