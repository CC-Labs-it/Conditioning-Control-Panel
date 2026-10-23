/* ============================================================================
 * shared/source-credit.js - the Back Room's one "via Scrolller" mark.
 *
 * Owner rule (2026-10-17): wherever Scrolller media is on screen the player can
 * see it came "via Scrolller". ONE mark for the room, never one per picture: a
 * small DOM line in the bottom-left corner over the 3D room and every station,
 * ~75% opacity, no glow, pointer-events none, opacity fade only (200 ms in and
 * out, the same under reduced motion and Calm: it never moves and never pulses).
 *
 * Telling a picture is Scrolller's, in order (CONTRACT section 5):
 *   - the host's `src: 'online'` on a dealt gif (a .temp copy on ccp.assets is
 *     still Scrolller's, the src says so);
 *   - a url on scrolller.com or one of its subdomains;
 *   - the web playtest's transcoding hop `/api/clip?u=<encoded url>` whose
 *     inner url is Scrolller's.
 *
 * Surfaces hold the mark by key and drop it when their pictures go:
 *   room/screens.js  'room-walls' while the wall gallery carries a Scrolller picture
 *   room/loader.js   'seat:<station>' while a sit-down's deal carries one
 * Inert without a document, so tests and stripped hosts boot as before.
 * Copied in spirit from dtrh/shared/sourceCredit.js: a station never imports
 * from another web tree.
 * ==========================================================================*/

export const CREDIT_TEXT = 'via Scrolller';
export const FADE_MS = 200;

/** True when the url's host is scrolller.com or one of its subdomains. */
export function isScrolllerUrl(url) {
  if (typeof url !== 'string' || !url) return false;
  let u;
  try { u = new URL(url, 'https://ccp.game/'); } catch (_) { return false; }
  const host = u.hostname.toLowerCase();
  if (host === 'scrolller.com' || host.endsWith('.scrolller.com')) return true;
  // the web playtest's hop: /api/clip?u=<the clip url, encoded> (room/clip-source.js)
  if (/\/api\/clip$/i.test(u.pathname)) {
    const inner = u.searchParams.get('u');
    if (inner && inner !== url) {
      try {
        const h = new URL(inner).hostname.toLowerCase();
        return h === 'scrolller.com' || h.endsWith('.scrolller.com');
      } catch (_) { return false; }
    }
  }
  return false;
}

/** A dealt gif {url, src} (CONTRACT section 5). */
export function isScrolllerGif(g) {
  if (!g || typeof g !== 'object') return false;
  if (g.src === 'online') return true;
  if (g.src === 'fallback') return false;
  return isScrolllerUrl(g.url);
}

/** Does a `media` frame deal at least one Scrolller picture? */
export function dealHasScrolller(frame) {
  return !!(frame && Array.isArray(frame.gifs) && frame.gifs.some(isScrolllerGif));
}

/** The pure state: a set of holding surfaces. */
export function createCreditHolds() {
  const holds = new Set();
  return {
    set(key, on) { if (on) holds.add(String(key)); else holds.delete(String(key)); return holds.size > 0; },
    clear() { holds.clear(); },
    visible() { return holds.size > 0; },
  };
}

function createMark() {
  const holds = createCreditHolds();
  const hasDom = typeof document !== 'undefined' && !!document.createElement;
  let el = null, shown = false;

  function ensure() {
    if (el || !hasDom || !document.body) return el;
    el = document.createElement('div');
    el.className = 'br-src-credit';
    el.textContent = CREDIT_TEXT;
    el.setAttribute('aria-hidden', 'true');
    el.style.cssText = [
      'position:fixed', 'left:12px', 'bottom:max(3px, env(safe-area-inset-bottom))', 'z-index:2147483000',
      'font:500 11px/1.3 system-ui,"Segoe UI",sans-serif', 'letter-spacing:0.02em',
      'color:#fff', 'text-shadow:0 1px 2px rgba(0,0,0,0.7)',
      'opacity:0', 'transition:opacity ' + FADE_MS + 'ms linear',
      'pointer-events:none', 'user-select:none', 'white-space:nowrap',
    ].join(';');
    document.body.appendChild(el);
    return el;
  }

  function sync() {
    const want = holds.visible();
    if (want === shown || !hasDom) return;
    shown = want;
    const m = ensure();
    if (m) m.style.opacity = want ? '0.75' : '0';
  }

  return {
    hold(key) { holds.set(key, true); sync(); },
    drop(key) { holds.set(key, false); sync(); },
    /** Hold or drop `key` by whether this deal carries a Scrolller picture. */
    follow(key, frame) { holds.set(key, dealHasScrolller(frame)); sync(); },
    clear() { holds.clear(); sync(); },
    visible: () => holds.visible(),
  };
}

/** The page's one mark. */
export const sourceCredit = createMark();
