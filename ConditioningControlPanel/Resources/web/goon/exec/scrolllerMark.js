/**
 * "via Scrolller" corner mark (owner, 2026-10-17: credit Scrolller wherever its media is on screen).
 *
 * ONE small mark per surface, never one per item. It shows while at least one Scrolller item is
 * on screen and never for the player's own files. The page tells it two ways:
 *   note()          an online item was just put on screen; the mark holds for MARK_HOLD_MS after
 *                   the last note (canvas-drawn flashes and bubbles cannot be watched in the DOM).
 *   pin(key, on)    a surface that stays up while it plays Scrolller media (a duel, a card table).
 *   watch()         polls the DOM (img/video src, inline background) for Scrolller URLs on screen.
 * Brand text, not a loc key. No link: this mark sits over play, and pages never open URLs.
 * pointer-events none, fade in AND out over MARK_FADE_MS (fade only, no flicker), tiny, no glow.
 *
 * The same small module is copied into each page tree that needs it (a page never imports from
 * another web tree): goon/exec, piecebypiece/ramp, intake/core, arcademy/provider. Keep them equal.
 */

export const SCROLLLER_TEXT = 'via Scrolller';
export const MARK_HOLD_MS = 6000;
export const MARK_FADE_MS = 200;

/** True when a URL is Scrolller's: host scrolller.com or any subdomain, or the Discord proxy path. */
export function isScrolllerUrl(u) {
  if (typeof u !== 'string' || !u) return false;
  let host = '';
  let path = '';
  try { const x = new URL(u); host = x.hostname.toLowerCase(); path = x.pathname; } catch { return false; }
  if (host === 'scrolller.com' || host.endsWith('.scrolller.com')) return true;
  return path.startsWith('/scrolller-media/');
}

/** True for an entry id shaped `scrolller/<sub>/<postId>` (the host's ScrolllerCredit.FromId shape). */
export function isScrolllerId(id) {
  if (typeof id !== 'string') return false;
  const p = id.split('/');
  return p.length >= 3 && p[0].toLowerCase() === 'scrolller' && !!p[1] && !!p[2];
}

/** The mark's text: plain, or with the subreddit when one is known and well formed. */
export function markText(sub) {
  const s = typeof sub === 'string' ? sub.replace(/^r\//i, '') : '';
  return /^[A-Za-z0-9_]{2,40}$/.test(s) ? SCROLLLER_TEXT + ' · r/' + s : SCROLLLER_TEXT;
}

/** The pure rule: shown while any surface is pinned or the last note is younger than the hold. */
export function markShouldShow({ pins = 0, lastNoteAt = 0, now = 0, holdMs = MARK_HOLD_MS } = {}) {
  if (pins > 0) return true;
  return lastNoteAt > 0 && now >= lastNoteAt && now - lastNoteAt < holdMs;
}

/** Elements that can show a Scrolller URL straight from the DOM (img/video src, inline background). */
export const DOM_SELECTOR = 'img[src*="scrolller" i],video[src*="scrolller" i],source[src*="scrolller" i],[style*="scrolller" i]';

/** True when an element is laid out, inside the viewport and not see-through. */
export function isOnScreen(el, win) {
  try {
    const t = el && el.tagName === 'SOURCE' ? el.parentElement : el;
    if (!t || !t.isConnected) return false;
    const r = t.getBoundingClientRect();
    if (!(r.width > 2 && r.height > 2)) return false;
    const w = win || (typeof window !== 'undefined' ? window : null);
    if (w && (r.right < 0 || r.bottom < 0 || r.left > w.innerWidth || r.top > w.innerHeight)) return false;
    const cs = w && w.getComputedStyle ? w.getComputedStyle(t) : null;
    return !cs || (cs.visibility !== 'hidden' && Number(cs.opacity) > 0.05);
  } catch { return false; }
}

/** True when any on-screen element of the document shows a Scrolller URL (the pure DOM read). */
export function domShowsScrolller(doc, win) {
  if (!doc || typeof doc.querySelectorAll !== 'function') return false;
  let list;
  try { list = doc.querySelectorAll(DOM_SELECTOR); } catch { return false; }
  for (const el of list) {
    const src = el.getAttribute('src') || '';
    const style = el.getAttribute('style') || '';
    if (!(isScrolllerUrl(src) || /url\(\s*["']?https?:\/\/[^"')]*scrolller/i.test(style) || /scrolller-media\//i.test(style))) continue;
    if (isOnScreen(el, win)) return true;
  }
  return false;
}

const STYLE_ID = 'scr-mark-style';
const CSS = [
  '.scr-mark{position:fixed;bottom:8px;z-index:2147480000;pointer-events:none;user-select:none;',
  'font:500 11px/1.2 system-ui,-apple-system,"Segoe UI",sans-serif;letter-spacing:.02em;color:#fff;',
  'background:rgba(0,0,0,.45);padding:2px 7px;border-radius:4px;opacity:0;',
  'transition:opacity ' + MARK_FADE_MS + 'ms ease-out;}',
  '.scr-mark.is-bl{left:8px}.scr-mark.is-br{right:8px}',
  '.scr-mark.is-on{opacity:.75}',
  'html[data-motion="off"] .scr-mark,html[data-gg-motion="off"] .scr-mark{transition:none}',
].join('');

/**
 * The mark for one page. Safe without a document (node tests): it then only keeps state.
 * @param {object} [o]
 * @param {Document} [o.doc]        default globalThis.document
 * @param {HTMLElement} [o.parent]  default doc.body
 * @param {'bl'|'br'} [o.corner]    bottom-left (default) or bottom-right
 * @param {number} [o.holdMs]
 * @param {() => number} [o.now]
 */
export function createScrolllerMark({ doc, parent, corner = 'bl', holdMs = MARK_HOLD_MS, now = () => Date.now() } = {}) {
  const d = doc || (typeof document !== 'undefined' ? document : null);
  const pins = new Set();
  let lastAt = 0;
  let shown = false;
  let timer = null;
  let poll = null;
  let el = null;
  let sub = '';

  function ensureEl() {
    if (el || !d || !d.createElement) return el;
    try {
      if (d.head && !d.getElementById(STYLE_ID)) {
        const st = d.createElement('style');
        st.id = STYLE_ID;
        st.textContent = CSS;
        d.head.appendChild(st);
      }
      el = d.createElement('div');
      el.className = 'scr-mark ' + (corner === 'br' ? 'is-br' : 'is-bl');
      el.setAttribute('aria-hidden', 'true');
      el.textContent = markText(sub);
      (parent || d.body).appendChild(el);
    } catch { el = null; }
    return el;
  }

  function sync() {
    if (timer) { clearTimeout(timer); timer = null; }
    const t = now();
    const want = markShouldShow({ pins: pins.size, lastNoteAt: lastAt, now: t, holdMs });
    if (want !== shown) {
      shown = want;
      const e = want ? ensureEl() : el;
      if (e) {
        if (want) { e.textContent = markText(sub); void e.offsetWidth; }
        e.classList.toggle('is-on', want);
      }
    }
    if (shown && !pins.size) timer = setTimeout(sync, Math.max(50, holdMs - (t - lastAt)));
  }

  const api = {
    /** An online item went on screen now. `s` = its subreddit when known. */
    note(s) { lastAt = now(); if (typeof s === 'string') sub = s; sync(); },
    /** A surface that plays Scrolller media while it is up. */
    pin(key, on) { if (on) pins.add(String(key)); else pins.delete(String(key)); sync(); },
    /** Drop everything (match over, surface closed). Fades out. */
    clear() { pins.clear(); lastAt = 0; sync(); },
    shown: () => shown,
    /** Watch the DOM for Scrolller URLs on screen (img/video/background); pins 'dom' while seen. */
    watch(intervalMs = 700) {
      if (poll || !d || typeof setInterval !== 'function') return;
      const w = d.defaultView || (typeof window !== 'undefined' ? window : null);
      const look = () => {
        const seen = domShowsScrolller(d, w);
        if (seen !== pins.has('dom')) api.pin('dom', seen);
      };
      poll = setInterval(look, Math.max(250, intervalMs));
      look();
    },
    destroy() {
      if (poll) { clearInterval(poll); poll = null; }
      if (timer) { clearTimeout(timer); timer = null; }
      pins.clear(); lastAt = 0; shown = false;
      if (el) { try { el.remove(); } catch { /* gone */ } el = null; }
    },
  };
  return api;
}

let shared = null;
/** The page's one mark (lazily made). */
export function pageMark(opts) {
  if (!shared) shared = createScrolllerMark(opts);
  return shared;
}
