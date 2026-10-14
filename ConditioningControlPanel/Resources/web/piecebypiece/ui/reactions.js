/* ============================================================================
 * ui/reactions.js - the crowd: six preset reactions, never free text.
 *
 * Spectators press one of six (owner, 2026-10-14): `fire`, `wow`, `laugh`,
 * `clap`, `heart`, `eyes`. A press is `watch-react { id }` on the bus; lane
 * WATCH batches the presses and posts them. What the room sent comes back as
 * `watch-reactions { counts }` (one 3 s bucket, nameless counts) for the
 * stands AND for the players, and floats up the right edge of the screen.
 * Under reduced motion nothing rises: the bucket shows as one still row.
 *
 *   createReactions({ bus, bar, floats, still }) -> { setWatching, dispose, debug }
 *
 * `bar` gets the six buttons (spectators only), `floats` is the lane they rise
 * in. Either may be missing: the crowd is quieter, never broken.
 * ==========================================================================*/

import { setShown, isShown } from './fade.js';

export const REACTIONS = Object.freeze([
  Object.freeze({ id: 'fire', glyph: '\u{1F525}', label: 'Fire' }),
  Object.freeze({ id: 'wow', glyph: '\u{1F62E}', label: 'Wow' }),
  Object.freeze({ id: 'laugh', glyph: '\u{1F602}', label: 'Laugh' }),
  Object.freeze({ id: 'clap', glyph: '\u{1F44F}', label: 'Clap' }),
  Object.freeze({ id: 'heart', glyph: '❤️', label: 'Heart' }),
  Object.freeze({ id: 'eyes', glyph: '\u{1F440}', label: 'Eyes' }),
]);
export const REACTION_IDS = Object.freeze(REACTIONS.map((r) => r.id));
const GLYPH = Object.fromEntries(REACTIONS.map((r) => [r.id, r.glyph]));

export const REACT_TUNING = Object.freeze({
  bucketMs: 3000,   // one bucket's floats are spread over this
  cap: 14,          // floats per bucket, at most
  riseMs: 2400,     // one float, bottom to gone (styles.css .react-float)
  stillMs: 2600,    // the reduced-motion row stays this long
});

/** Known ids with a positive whole count, nothing else. Pure. */
export function cleanCounts(counts) {
  const out = {};
  if (!counts || typeof counts !== 'object') return out;
  for (const id of REACTION_IDS) {
    const n = Math.trunc(Number(counts[id]));
    if (Number.isFinite(n) && n > 0) out[id] = Math.min(n, 9999);
  }
  return out;
}

/**
 * Which floats one bucket becomes: every id gets at least one, the rest of the
 * cap is shared by count, and they are spread over the bucket so the room
 * reads as a stream rather than a burst. Pure (rand is injectable).
 * @returns {Array<{ id, glyph, atMs, x }>} x in 0..1 across the lane
 */
export function floatPlan(counts, { cap = REACT_TUNING.cap, spanMs = REACT_TUNING.bucketMs, rand = Math.random } = {}) {
  const c = cleanCounts(counts);
  const ids = Object.keys(c);
  if (!ids.length) return [];
  const total = ids.reduce((s, id) => s + c[id], 0);
  const room = Math.max(ids.length, cap);
  const shuffle = (a) => { for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(rand() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; } return a; };
  const extra = [];
  for (const id of ids) {
    const share = total <= room ? c[id] : Math.max(1, Math.round((c[id] / total) * room));
    for (let i = 1; i < share; i++) extra.push(id);
  }
  // one of each first, then the rest by share up to the cap; shuffled so no id arrives as a block
  const kept = shuffle(ids.concat(shuffle(extra)).slice(0, room));
  const gap = kept.length > 1 ? spanMs / kept.length : 0;
  return kept.map((id, i) => ({ id, glyph: GLYPH[id], atMs: Math.round(i * gap + rand() * gap * 0.5), x: Math.round(rand() * 100) / 100 }));
}

/** The reduced-motion row: "glyph count" per id, in the fixed order. Pure. */
export function stillLine(counts) {
  const c = cleanCounts(counts);
  return REACTION_IDS.filter((id) => c[id]).map((id) => GLYPH[id] + ' ' + c[id]).join('   ');
}

export function createReactions({ bus, bar = null, floats = null, still = () => false } = {}) {
  const unbind = [];
  const undom = [];
  const timers = new Set();
  let watching = false;
  let shown = 0;          // floats put up, for the harness
  let pressed = 0;
  const later = (fn, ms) => { const id = setTimeout(() => { timers.delete(id); fn(); }, ms); timers.add(id); return id; };

  if (bar && typeof document !== 'undefined') {
    bar.textContent = '';
    for (const r of REACTIONS) {
      const b = document.createElement('button');
      b.type = 'button';
      b.className = 'react-btn';
      b.dataset.react = r.id;
      b.title = r.label;
      b.setAttribute('aria-label', r.label);
      b.textContent = r.glyph;
      bar.appendChild(b);
    }
    const onClick = (e) => {
      const b = e.target && e.target.closest ? e.target.closest('[data-react]') : null;
      if (!b || !watching) return;
      e.preventDefault();
      pressed++;
      try { bus && bus.emit('watch-react', { id: b.dataset.react }); } catch { /* no bus */ }
      // the press answers at once; the room's count comes back with the bucket
      b.classList.remove('sent');
      void b.offsetWidth;
      b.classList.add('sent');
      later(() => b.classList.remove('sent'), 360);
    };
    bar.addEventListener('click', onClick);
    undom.push(() => bar.removeEventListener('click', onClick));
  }

  function play(counts) {
    if (!floats || typeof document === 'undefined') return;
    if (still()) {
      const line = stillLine(counts);
      if (!line) return;
      const row = document.createElement('span');
      row.className = 'react-still';
      row.textContent = line;
      floats.querySelectorAll('.react-still').forEach((n) => n.remove());
      floats.appendChild(row);
      shown++;
      later(() => row.remove(), REACT_TUNING.stillMs);
      return;
    }
    for (const f of floatPlan(counts)) {
      later(() => {
        const n = document.createElement('span');
        n.className = 'react-float';
        n.textContent = f.glyph;
        n.style.setProperty('--x', String(f.x));
        floats.appendChild(n);
        shown++;
        later(() => n.remove(), REACT_TUNING.riseMs + 100);
      }, f.atMs);
    }
  }

  if (bus && typeof bus.on === 'function') {
    try { unbind.push(bus.on('watch-reactions', (p) => { try { play(p && p.counts); } catch (e) { console.warn('[pbp/reactions] ' + (e && e.message)); } })); } catch { /* no listeners */ }
  }

  return {
    /** The bar is the stands' only; the floats are everyone's. */
    setWatching(on) {
      watching = !!on;
      setShown(bar, watching, still());
    },
    play,
    dispose() {
      for (const off of unbind) { try { off(); } catch { /* gone */ } }
      for (const off of undom) { try { off(); } catch { /* gone */ } }
      for (const id of timers) clearTimeout(id);
      timers.clear();
      if (floats) floats.textContent = '';
    },
    debug: () => ({ watching, bar: isShown(bar), buttons: bar ? bar.querySelectorAll('[data-react]').length : 0, shown, pressed }),
  };
}

export default createReactions;
