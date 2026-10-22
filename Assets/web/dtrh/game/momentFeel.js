/* ============================================================================
 * momentFeel.js - feel pass, wave 2: the beats of a run.
 *
 * The countdown breath, the chamber banner settling like ink in water, the warm
 * edge bloom on a streak milestone, and the recap's staggered rows + count-up.
 * A trance game: everything here eases in and eases out, nothing flashes and
 * nothing moves the camera. Amplitudes follow shared/motion.js (reduced = half,
 * off = opacity fades only).
 *
 * Listens to window 'dtrh:feel' (kind 'moment') and sends kind 'tick' {t} while
 * a recap number counts. It imports no other lane's module.
 * ==========================================================================*/

import { motionScale } from '../shared/motion.js';

// ---- tunables (all of them; adjust by eye) ----
/** Countdown number: how long one 3 / 2 / 1 breath lasts (the beats are 1000 ms apart). */
export const COUNT_BEAT_MS = 900;
/** Countdown number: how far it swells from small to rest, as a scale delta, at full motion. */
export const COUNT_SWELL = 0.14;
/** GO: how far it spreads outward as it dissolves, as a scale delta, at full motion. */
export const GO_SPREAD = 0.55;
/** GO: blur at the end of the dissolve, px, at full motion (one small text node, not the screen). */
export const GO_BLUR_PX = 6;
/** Chamber banner: ink-in and ink-out time, ms each. */
export const BANNER_INK_MS = 720;
/** Chamber banner: blur it settles from / leaves into, px, at full motion. */
export const BANNER_BLUR_PX = 9;
/** Chamber banner: how much larger it starts before settling, as a scale delta, at full motion. */
export const BANNER_SPREAD = 0.06;
/** Chamber banner: total life when the node carries no --hold (matches announceRegion's 3000 ms hold). */
export const BANNER_HOLD_MS = 3000;
/** Edge bloom: warm amber, never white. "r,g,b". */
export const BLOOM_RGB = '255,176,112';
/** Edge bloom: where the clear centre ends, percent of the radius (nothing is drawn inside it). */
export const BLOOM_CLEAR_PCT = 58;
/** Edge bloom: fade in, hold, fade out, ms. */
export const BLOOM_IN_MS = 420;
export const BLOOM_HOLD_MS = 260;
export const BLOOM_OUT_MS = 1500;
/** Edge bloom: peak opacity for a x10 milestone, the step per further x10, and the ceiling. */
export const BLOOM_BASE = 0.22;
export const BLOOM_STEP = 0.04;
export const BLOOM_MAX = 0.6;
/** Edge bloom: peak opacity for the first big streak threshold (then BLOOM_STEP * 2 per tier). */
export const BLOOM_BIG = 0.42;
/** Recap: the count-up length, ms, and the delay between rows arriving. */
export const RECAP_COUNT_MS = 1200;
export const RECAP_ROW_STAGGER_MS = 90;
/** Recap: wait before the first row (the surface wash is still lifting), and one row's fade, ms. */
export const RECAP_ROWS_DELAY_MS = 500;
export const RECAP_ROW_MS = 520;
/** Recap: how far a row rises as it arrives, px, at full motion. */
export const RECAP_ROW_RISE_PX = 10;
/** Recap: the shortest gap between two 'tick' feel events, ms. */
export const RECAP_TICK_GAP_MS = 70;

const EASE_SOFT = 'cubic-bezier(0.22, 0.61, 0.36, 1)';

// ---- pure helpers (unit tested) ----
export function clamp01(v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
export function easeOutCubic(t) { const u = 1 - clamp01(t); return 1 - u * u * u; }

/** The number a count-up shows at progress t (0..1). Lands exactly on total at t >= 1. */
export function countValue(total, t) {
  const n = Number(total) || 0;
  if (t >= 1) return n;
  return Math.round(n * easeOutCubic(t));
}

/** Peak opacity of the edge bloom for a streak moment; 0 = no bloom. */
export function bloomPeak(event, data) {
  const d = data || {};
  if (event === 'combo-milestone') {
    const tier = Math.max(1, Math.floor((Number(d.combo) || 0) / 10));
    return Math.min(BLOOM_MAX, BLOOM_BASE + BLOOM_STEP * (tier - 1));
  }
  if (event === 'combo-big') {
    const t = Number(d.threshold) || 0;
    const tier = t >= 100 ? 3 : t >= 50 ? 2 : t >= 25 ? 1 : 0;
    return Math.min(BLOOM_MAX, BLOOM_BIG + BLOOM_STEP * 2 * tier);
  }
  return 0;
}

/** When row i of the recap starts to arrive, ms after the card goes up. */
export function rowDelay(i, base = RECAP_ROWS_DELAY_MS) { return Math.max(0, base) + Math.max(0, i) * RECAP_ROW_STAGGER_MS; }

/** The countdown beat as a 'count' feel detail: n = 3, 2, 1, then 0 on GO. */
export function countDetail(beat) {
  const go = beat === 'GO!';
  return { kind: 'count', n: go ? 0 : (Number(beat) || 0), go };
}

// ---- the DOM half ----
export function createMomentFeel(hud) {
  const canAnimate = typeof Element !== 'undefined' && typeof Element.prototype.animate === 'function';
  let bloomEl = null, bloomAnim = null;
  const counters = new Set();

  /** One countdown beat. Returns false when the caller should fall back to its CSS beat. */
  function beat(el, { go = false, ms = COUNT_BEAT_MS } = {}) {
    if (!canAnimate) return false;
    const m = motionScale();
    for (const a of el.getAnimations ? el.getAnimations() : []) { if (a.id === 'mf-beat') a.cancel(); }
    let frames;
    if (go) {
      const end = 1 + GO_SPREAD * m;
      frames = [
        { opacity: 0, transform: `scale(${1 - COUNT_SWELL * m})`, filter: 'blur(0px)', offset: 0 },
        { opacity: 1, transform: 'scale(1)', filter: 'blur(0px)', offset: 0.3 },
        { opacity: 0, transform: `scale(${end})`, filter: `blur(${GO_BLUR_PX * m}px)`, offset: 1 },
      ];
    } else {
      frames = [
        { opacity: 0, transform: `scale(${1 - COUNT_SWELL * m})`, offset: 0 },
        { opacity: 1, transform: 'scale(1)', offset: 0.42 },
        { opacity: 0, transform: `scale(${1 + COUNT_SWELL * 0.4 * m})`, offset: 1 },
      ];
    }
    const a = el.animate(frames, { duration: ms, easing: 'ease-in-out', fill: 'both' });
    a.id = 'mf-beat';
    return true;
  }

  /** The chamber banner (the newest depth announce): ink in, hold, ink out the same way. */
  function settleBanner() {
    if (!canAnimate || !hud) return;
    const all = hud.querySelectorAll('.cf-announce--depth');
    const el = all[all.length - 1];
    if (!el) return;
    const m = motionScale();
    const hold = parseFloat(el.style.getPropertyValue('--hold')) || BANNER_HOLD_MS;
    const total = hold + 600;   // the HUD removes the node at hold + 900
    const k = Math.min(0.4, BANNER_INK_MS / total);
    const far = { opacity: 0, transform: `scale(${1 + BANNER_SPREAD * m})`, filter: `blur(${BANNER_BLUR_PX * m}px)` };
    const near = { opacity: 1, transform: 'scale(1)', filter: 'blur(0px)' };
    el.animate([
      { ...far, offset: 0, easing: EASE_SOFT },
      { ...near, offset: k },
      { ...near, offset: 1 - k, easing: 'ease-in' },
      { ...far, offset: 1 },
    ], { duration: total, fill: 'both' });
  }

  /** The warm vignette at the screen edge. One layer, reused; a new bloom takes over the old one. */
  function bloom(peak) {
    if (!canAnimate || !hud || !(peak > 0)) return;
    if (!bloomEl) {
      bloomEl = document.createElement('div');
      bloomEl.className = 'mf-edge-bloom';
      bloomEl.setAttribute('aria-hidden', 'true');
      bloomEl.style.cssText = 'position:absolute;inset:0;pointer-events:none;opacity:0;z-index:3;'
        + `background:radial-gradient(ellipse at center, rgba(${BLOOM_RGB},0) ${BLOOM_CLEAR_PCT}%, rgba(${BLOOM_RGB},0.55) 100%);`;
      hud.appendChild(bloomEl);
    }
    const p = Math.min(BLOOM_MAX, peak) * Math.max(0.5, motionScale());
    let from = 0;
    if (bloomAnim) {
      try { from = parseFloat(getComputedStyle(bloomEl).opacity) || 0; } catch (e) { from = 0; }
      bloomAnim.cancel();
    }
    const total = BLOOM_IN_MS + BLOOM_HOLD_MS + BLOOM_OUT_MS;
    const top = Math.max(from, p);
    bloomAnim = bloomEl.animate([
      { opacity: from, offset: 0, easing: EASE_SOFT },
      { opacity: top, offset: BLOOM_IN_MS / total },
      { opacity: top, offset: (BLOOM_IN_MS + BLOOM_HOLD_MS) / total, easing: 'ease-in-out' },
      { opacity: 0, offset: 1 },
    ], { duration: total, fill: 'both' });
    const mine = bloomAnim;
    mine.onfinish = () => { if (bloomAnim === mine) bloomAnim = null; };
  }

  /** Recap rows arrive one after another (a short rise at full motion, a plain fade at off). */
  function staggerRows(container, base = RECAP_ROWS_DELAY_MS) {
    if (!canAnimate || !container) return;
    const rise = RECAP_ROW_RISE_PX * motionScale();
    Array.from(container.children).forEach((row, i) => {
      row.animate([
        { opacity: 0, transform: `translateY(${rise}px)` },
        { opacity: 1, transform: 'translateY(0)' },
      ], { duration: RECAP_ROW_MS, delay: rowDelay(i, base), easing: EASE_SOFT, fill: 'backwards' });
    });
  }

  /** Count el's text up to total. render(v) builds the text; finalText is what it must end on. */
  function countUp(el, total, render, finalText, { delay = 0 } = {}) {
    const n = Number(total) || 0;
    const raf = typeof requestAnimationFrame === 'function' ? requestAnimationFrame : null;
    if (!raf || n <= 0) { el.textContent = finalText; return; }
    const job = { id: 0, done: false };
    counters.add(job);
    el.textContent = render(0);
    let t0 = 0, lastTick = 0;
    const step = (now) => {
      if (job.done) return;
      if (!t0) t0 = now + delay;
      const t = clamp01((now - t0) / RECAP_COUNT_MS);
      if (now >= t0 && t < 1) {
        el.textContent = render(countValue(n, t));
        if (now - lastTick >= RECAP_TICK_GAP_MS) {
          lastTick = now;
          try { window.dispatchEvent(new CustomEvent('dtrh:feel', { detail: { kind: 'tick', t } })); } catch (e) { /* ignore */ }
        }
      }
      if (t >= 1) { el.textContent = finalText; job.done = true; counters.delete(job); return; }
      job.id = raf(step);
    };
    job.finish = () => { if (!job.done) { job.done = true; el.textContent = finalText; } };
    job.id = raf(step);
  }

  /** Land every running count on its real value at once (the recap is closing). */
  function finishCounts() {
    for (const job of counters) { try { cancelAnimationFrame(job.id); } catch (e) { /* ignore */ } job.finish(); }
    counters.clear();
  }

  const onFeel = (e) => {
    const d = e && e.detail;
    if (!d || d.kind !== 'moment') return;
    try {
      if (d.event === 'act-changed') settleBanner();
      else if (d.event === 'combo-milestone' || d.event === 'combo-big') bloom(bloomPeak(d.event, d));
    } catch (err) { /* feel must never break a run */ }
  };
  if (typeof window !== 'undefined') window.addEventListener('dtrh:feel', onFeel);

  return {
    beat, bloom, settleBanner, staggerRows, countUp, finishCounts,
    dispose() {
      if (typeof window !== 'undefined') window.removeEventListener('dtrh:feel', onFeel);
      finishCounts();
      if (bloomAnim) bloomAnim.cancel();
      if (bloomEl) bloomEl.remove();
      bloomEl = null; bloomAnim = null;
    },
  };
}
