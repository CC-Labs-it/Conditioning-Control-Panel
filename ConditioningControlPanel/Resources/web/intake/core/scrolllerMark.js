/* "via Scrolller" corner mark (owner, 2026-10-17). ONE mark per page while at least one Scrolller
 * item is on screen, never for the player's own files. note() = an online item just went on screen
 * (holds MARK_HOLD_MS: canvas draws cannot be watched); pin(key, on) = a surface playing Scrolller
 * media while it is up; watch() = poll the DOM for Scrolller urls on img/video/background.
 * Brand text, not a loc key. No link (it sits over play). pointer-events none, fade in AND out.
 * Copied as is into goon/exec, piecebypiece/ramp, intake/core, arcademy/provider (a page never
 * imports from another web tree): keep the copies equal. */

export const SCROLLLER_TEXT = 'via Scrolller';
export const MARK_HOLD_MS = 6000;
export const MARK_FADE_MS = 200;

/** Scrolller's: host scrolller.com or a subdomain, or the Discord Activity proxy path. */
export function isScrolllerUrl(u) {
  if (typeof u !== 'string' || !u) return false;
  try {
    const x = new URL(u);
    const h = x.hostname.toLowerCase();
    return h === 'scrolller.com' || h.endsWith('.scrolller.com') || x.pathname.startsWith('/scrolller-media/');
  } catch { return false; }
}

/** The pure rule: any pinned surface, or a note younger than the hold. */
export function markShouldShow({ pins = 0, lastNoteAt = 0, now = 0, holdMs = MARK_HOLD_MS } = {}) {
  if (pins > 0) return true;
  return lastNoteAt > 0 && now >= lastNoteAt && now - lastNoteAt < holdMs;
}

export const DOM_SELECTOR = 'img[src*="scrolller" i],video[src*="scrolller" i],source[src*="scrolller" i],[style*="scrolller" i]';
const BG_URL = /url\(\s*["']?([^"')]+)/gi;

/** Laid out, inside the viewport and not see-through. */
export function isOnScreen(el, win) {
  try {
    const t = el && el.tagName === 'SOURCE' ? el.parentElement : el;
    if (!t || !t.isConnected) return false;
    const r = t.getBoundingClientRect();
    if (!(r.width > 2 && r.height > 2)) return false;
    if (win && (r.right < 0 || r.bottom < 0 || r.left > win.innerWidth || r.top > win.innerHeight)) return false;
    const cs = win && win.getComputedStyle ? win.getComputedStyle(t) : null;
    return !cs || (cs.visibility !== 'hidden' && Number(cs.opacity) > 0.05);
  } catch { return false; }
}

/** Any on-screen element of the document showing a Scrolller url. */
export function domShowsScrolller(doc, win) {
  if (!doc || typeof doc.querySelectorAll !== 'function') return false;
  let list;
  try { list = doc.querySelectorAll(DOM_SELECTOR); } catch { return false; }
  for (const el of list) {
    let hit = isScrolllerUrl(el.getAttribute('src') || '');
    if (!hit) for (const m of String(el.getAttribute('style') || '').matchAll(BG_URL)) if (isScrolllerUrl(m[1])) { hit = true; break; }
    if (hit && isOnScreen(el, win)) return true;
  }
  return false;
}

const STYLE_ID = 'scr-mark-style';
const CSS = '.scr-mark{position:fixed;bottom:8px;left:8px;z-index:2147480000;pointer-events:none;user-select:none;'
  + 'font:500 11px/1.2 system-ui,"Segoe UI",sans-serif;color:#fff;background:rgba(0,0,0,.45);padding:2px 7px;'
  + 'border-radius:4px;opacity:0;transition:opacity ' + MARK_FADE_MS + 'ms ease-out}.scr-mark.is-br{left:auto;right:8px}'
  + '.scr-mark.is-on{opacity:.75}html[data-motion="off"] .scr-mark,html[data-gg-motion="off"] .scr-mark{transition:none}';

/** One page's mark. Without a document (node tests) it only keeps state. corner 'bl' | 'br'. */
export function createScrolllerMark({ doc, corner = 'bl', holdMs = MARK_HOLD_MS, now = () => Date.now() } = {}) {
  const d = doc || (typeof document !== 'undefined' ? document : null);
  const pins = new Set();
  let lastAt = 0, shown = false, timer = null, poll = null, el = null;

  function ensureEl() {
    if (el || !d || !d.createElement || !d.body) return el;
    try {
      if (d.head && !d.getElementById(STYLE_ID)) {
        const st = d.createElement('style');
        st.id = STYLE_ID; st.textContent = CSS; d.head.appendChild(st);
      }
      el = d.createElement('div');
      el.className = 'scr-mark' + (corner === 'br' ? ' is-br' : '');
      el.setAttribute('aria-hidden', 'true');
      el.textContent = SCROLLLER_TEXT;
      d.body.appendChild(el);
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
      if (e) { if (want) void e.offsetWidth; e.classList.toggle('is-on', want); }
    }
    if (shown && !pins.size) timer = setTimeout(sync, Math.max(50, holdMs - (t - lastAt)));
  }

  const api = {
    note() { lastAt = now(); sync(); },
    pin(key, on) { if (on) pins.add(String(key)); else pins.delete(String(key)); sync(); },
    clear() { pins.clear(); lastAt = 0; sync(); },
    shown: () => shown,
    watch(intervalMs = 700) {
      if (poll || !d || typeof setInterval !== 'function') return;
      const w = d.defaultView || (typeof window !== 'undefined' ? window : null);
      const look = () => { const seen = domShowsScrolller(d, w); if (seen !== pins.has('dom')) api.pin('dom', seen); };
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
/** The page's one mark, made on first use. */
export function pageMark(opts) {
  if (!shared) shared = createScrolllerMark(opts);
  return shared;
}
