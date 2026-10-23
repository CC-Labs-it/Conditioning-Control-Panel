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
 * The polish pass (owner, 2026-10-14: "no idea if the emotes fire or not"):
 *   a press answers at once   the emoji pops out of its button and flies into
 *                             the board, blooming out where it lands, with its
 *                             own short sound (audio/react-cues.js via sfx.js
 *                             `react`). Reduced motion: no flight, the button
 *                             lights instead.
 *   anti-spam                 ui/reactions-guard.js: three in a row, then one
 *                             back every 1.5 s; an empty bucket greys the bar
 *                             with a draining line until a press is back. A
 *                             press on a grey bar shakes it (or blinks it,
 *                             reduced motion) and sends nothing.
 *   mute                      a speaker at the end of the bar = the reactions'
 *                             sounds on or off (ui/reactions-prefs.js `sound`,
 *                             also an Options row).
 *   the room's sound          a bucket plays at most REACT_TUNING.soundsPerBucket
 *                             cues, one per id, softer than a press, each as the
 *                             first float of its id appears.
 *   a seat's choice           Options "Play watchers' reactions" (prefs `show`,
 *                             on by default): off, a seat neither shows nor
 *                             plays what the stands send. The stands always
 *                             see their own room.
 *
 *   createReactions({ bus, bar, floats, still, sound, prefs, guard, toggles, layer })
 *     -> { setWatching, setStands, play, dispose, debug }
 *
 * `bar` gets the buttons (spectators only), `floats` is the lane they rise
 * in. Either may be missing: the crowd is quieter, never broken.
 * ==========================================================================*/

import { setShown, isShown } from './fade.js';
import { createGuard } from './reactions-guard.js';
import { reactPrefs, setReactPrefs, onReactPrefs } from './reactions-prefs.js';

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
const SPEAKER = '\u{1F50A}';
const MUTED = '\u{1F507}';

export const REACT_TUNING = Object.freeze({
  bucketMs: 3000,   // one bucket's floats are spread over this
  cap: 14,          // floats per bucket, at most
  riseMs: 2400,     // one float, bottom to gone (styles.css .react-float)
  stillMs: 2600,    // the reduced-motion row stays this long
  flyMs: 820,       // a press's emoji, button to board (styles.css .react-fly)
  sentMs: 360,      // the button's own pop
  calmMs: 520,      // reduced motion: the button stays lit this long instead
  nopeMs: 320,      // the shake of a press on a resting bar
  coolOutMs: 260,   // the bar's little lift as it wakes again
  soundsPerBucket: 3,
  roomPan: 0.35,    // the room's sounds sit a little right, where they float
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

/**
 * The sounds a bucket plays: the first float of each id, in time order, at
 * most `max`, never two closer than `minGapMs`. Pure.
 * @returns {Array<{ id, atMs }>}
 */
export function bucketSounds(plan, { max = REACT_TUNING.soundsPerBucket, minGapMs = 240 } = {}) {
  const out = [];
  const seen = new Set();
  const sorted = (Array.isArray(plan) ? plan : []).filter((f) => f && GLYPH[f.id]).slice().sort((a, b) => a.atMs - b.atMs);
  for (const f of sorted) {
    if (out.length >= max) break;
    if (seen.has(f.id)) continue;
    seen.add(f.id);
    const prev = out[out.length - 1];
    out.push({ id: f.id, atMs: prev ? Math.max(f.atMs, prev.atMs + minGapMs) : f.atMs });
  }
  return out;
}

/** The reduced-motion row: "glyph count" per id, in the fixed order. Pure. */
export function stillLine(counts) {
  const c = cleanCounts(counts);
  return REACTION_IDS.filter((id) => c[id]).map((id) => GLYPH[id] + ' ' + c[id]).join('   ');
}

/** The mute button's words. Pure. */
export function muteLabel(soundOn) {
  return soundOn ? 'Mute reaction sounds' : 'Reaction sounds are off';
}

/** sfx.js `react`, when the board has a sound kit. Never throws. */
function boardSound(id, opts = {}) {
  try { return !!globalThis.window?.PBP?.board?.sfx?.play?.('react', { id, ...opts }); } catch { return false; }
}

function lookupToggles() {
  if (typeof document === 'undefined' || !document.getElementById) return {};
  return { show: document.getElementById('game-react-show'), sound: document.getElementById('game-react-sound') };
}

export function createReactions({
  bus, bar = null, floats = null, still = () => false, sound = boardSound,
  prefs = { get: reactPrefs, set: setReactPrefs, on: onReactPrefs },
  guard = createGuard(), toggles, layer = null, rand = Math.random,
} = {}) {
  const unbind = [];
  const undom = [];
  const timers = new Set();
  const flying = new Set();
  let watching = false;   // the bar takes presses (the stands, game not over)
  let stands = false;     // in the stands at all (the room always shows there)
  let shown = 0;          // floats put up, for the harness
  let pressed = 0;
  let refused = 0;        // presses the guard kept back
  let heard = 0;          // sounds asked for (presses and the room)
  let flown = 0;
  let cooling = false;
  let coolTimer = 0;
  let p = { show: true, sound: true };
  try { p = prefs.get(); } catch { /* the defaults */ }
  const later = (fn, ms) => { const id = setTimeout(() => { timers.delete(id); fn(); }, ms); timers.add(id); return id; };
  const drop = (id) => { if (id) { clearTimeout(id); timers.delete(id); } };
  const hasDom = typeof document !== 'undefined';
  const welcome = () => stands || p.show;   // may the room be seen and heard here
  const say = (id, opts) => { heard++; try { sound(id, opts); } catch { /* no sound kit */ } };
  const flash = (node, cls, ms) => {
    if (!node || !node.classList) return;
    node.classList.remove(cls);
    void node.offsetWidth;   // restart the run
    node.classList.add(cls);
    later(() => node.classList.remove(cls), ms);
  };

  /* ---- the bar -------------------------------------------------------------- */

  let mute = null;
  let coolLine = null;
  function paintMute() {
    if (!mute) return;
    mute.textContent = p.sound ? SPEAKER : MUTED;
    mute.title = muteLabel(p.sound);
    mute.setAttribute('aria-label', muteLabel(p.sound));
    mute.setAttribute('aria-pressed', String(!p.sound));
    mute.classList.toggle('off', !p.sound);
  }
  function paintCooling() {
    if (!bar || !bar.classList) return;
    bar.classList.toggle('cooling', cooling);
    for (const b of bar.querySelectorAll('[data-react]')) b.setAttribute('aria-disabled', String(cooling));
  }

  /** The bar rests for ms: grey, a line draining under it, then a small lift as it wakes. */
  function cool(ms) {
    if (!(ms > 0)) return;
    drop(coolTimer);
    cooling = true;
    if (bar && bar.classList) {
      bar.classList.remove('cool-out');
      bar.style.setProperty('--cool-ms', Math.round(ms) + 'ms');
      if (coolLine) { coolLine.classList.remove('run'); void coolLine.offsetWidth; coolLine.classList.add('run'); }
    }
    paintCooling();
    coolTimer = later(() => {
      coolTimer = 0;
      cooling = false;
      paintCooling();
      if (!still()) flash(bar, 'cool-out', REACT_TUNING.coolOutMs);
    }, ms);
  }

  /** The press answers at once: the emoji leaves its button and flies into the board. */
  function fly(b, glyph) {
    if (still()) { flash(b, 'sent-still', REACT_TUNING.calmMs); return; }
    flash(b, 'sent', REACT_TUNING.sentMs);
    if (!hasDom || !b.getBoundingClientRect) return;
    const r = b.getBoundingClientRect();
    const W = globalThis.innerWidth || 1280;
    const H = globalThis.innerHeight || 800;
    const x0 = r.left + r.width / 2;
    const y0 = r.top + r.height / 2;
    const x1 = W * 0.5 + (rand() - 0.5) * W * 0.16;
    const y1 = H * 0.4 + (rand() - 0.5) * H * 0.1;
    const n = document.createElement('span');
    n.className = 'react-fly';
    n.setAttribute('aria-hidden', 'true');
    n.textContent = glyph;
    n.style.left = Math.round(x0) + 'px';
    n.style.top = Math.round(y0) + 'px';
    n.style.setProperty('--dx', Math.round(x1 - x0) + 'px');
    n.style.setProperty('--dy', Math.round(y1 - y0) + 'px');
    n.style.setProperty('--tilt', Math.round((rand() - 0.5) * 40) + 'deg');
    (layer || document.body).appendChild(n);
    flying.add(n);
    flown++;
    later(() => { n.remove(); flying.delete(n); }, REACT_TUNING.flyMs + 80);
  }

  if (bar && hasDom) {
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
    mute = document.createElement('button');
    mute.type = 'button';
    mute.className = 'react-mute';
    mute.dataset.reactMute = '1';
    bar.appendChild(mute);
    coolLine = document.createElement('i');
    coolLine.className = 'react-cool';
    coolLine.setAttribute('aria-hidden', 'true');
    bar.appendChild(coolLine);
    paintMute();

    const onClick = (e) => {
      const t = e.target && e.target.closest ? e.target : null;
      if (t && t.closest('[data-react-mute]')) {
        e.preventDefault();
        try { prefs.set({ sound: !p.sound }); } catch { /* no prefs */ }
        if (!still()) flash(mute, 'sent', REACT_TUNING.sentMs);
        return;
      }
      const b = t ? t.closest('[data-react]') : null;
      if (!b || !watching) return;
      e.preventDefault();
      const g = guard.take();
      if (!g.ok) {
        refused++;
        flash(bar, still() ? 'nope-still' : 'nope', REACT_TUNING.nopeMs);
        cool(g.waitMs);
        return;
      }
      pressed++;
      const id = b.dataset.react;
      try { bus && bus.emit('watch-react', { id }); } catch { /* no bus */ }
      if (p.sound) say(id, {});
      fly(b, GLYPH[id] || b.textContent);
      if (g.waitMs > 0) cool(g.waitMs);
    };
    bar.addEventListener('click', onClick);
    undom.push(() => bar.removeEventListener('click', onClick));
  }

  /* ---- Options: "Play watchers' reactions" and "Reaction sounds" ----------- */

  const tg = toggles === undefined ? lookupToggles() : (toggles || {});
  for (const key of ['show', 'sound']) {
    const box = tg[key];
    if (!box || !box.addEventListener) continue;
    const h = () => { try { prefs.set({ [key]: !!box.checked }); } catch { /* no prefs */ } };
    box.addEventListener('change', h);
    undom.push(() => box.removeEventListener('change', h));
  }
  try {
    unbind.push(prefs.on((next) => {
      p = { show: next.show !== false, sound: next.sound !== false };
      if (tg.show) tg.show.checked = p.show;
      if (tg.sound) tg.sound.checked = p.sound;
      paintMute();
      if (!welcome() && floats && floats.querySelectorAll) floats.querySelectorAll('.react-float, .react-still').forEach((n) => n.remove());
    }));
  } catch { /* no prefs */ }

  /* ---- the room ------------------------------------------------------------- */

  function play(counts) {
    if (!welcome()) return;   // a seat that turned the crowd off: nothing shows, nothing sounds
    if (still()) {
      const line = stillLine(counts);
      if (!line) return;
      const first = REACTION_IDS.find((id) => cleanCounts(counts)[id]);
      if (p.sound && first) say(first, { soft: true, pan: REACT_TUNING.roomPan });
      if (!floats || !hasDom) return;
      const row = document.createElement('span');
      row.className = 'react-still';
      row.textContent = line;
      floats.querySelectorAll('.react-still').forEach((n) => n.remove());
      floats.appendChild(row);
      shown++;
      later(() => row.remove(), REACT_TUNING.stillMs);
      return;
    }
    const plan = floatPlan(counts, { rand });
    if (p.sound) {
      for (const s of bucketSounds(plan)) later(() => { if (welcome() && p.sound) say(s.id, { soft: true, pan: REACT_TUNING.roomPan }); }, s.atMs);
    }
    if (!floats || !hasDom) return;
    for (const f of plan) {
      later(() => {
        if (!welcome()) return;
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
    try { unbind.push(bus.on('watch-reactions', (m) => { try { play(m && m.counts); } catch (e) { console.warn('[pbp/reactions] ' + (e && e.message)); } })); } catch { /* no listeners */ }
  }

  return {
    /** The bar is the stands' only (and only while the game runs); the floats are everyone's. */
    setWatching(on) {
      watching = !!on;
      if (watching) stands = true;
      setShown(bar, watching, still());
    },
    /** In the stands at all: the room shows there whatever a seat chose. */
    setStands(on) { stands = !!on; },
    play,
    dispose() {
      for (const off of unbind) { try { off(); } catch { /* gone */ } }
      for (const off of undom) { try { off(); } catch { /* gone */ } }
      unbind.length = 0; undom.length = 0;
      for (const id of timers) clearTimeout(id);
      timers.clear();
      for (const n of flying) { try { n.remove(); } catch { /* gone */ } }
      flying.clear();
      if (floats) floats.textContent = '';
    },
    debug: () => ({
      watching, stands, bar: isShown(bar), buttons: bar && bar.querySelectorAll ? bar.querySelectorAll('[data-react]').length : 0,
      shown, pressed, refused, heard, flown, cooling, show: p.show, sound: p.sound,
    }),
  };
}

export default createReactions;
