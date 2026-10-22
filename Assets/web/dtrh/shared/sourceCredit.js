/* ============================================================================
 * sourceCredit.js - the "via Scrolller" mark for the descent page and the race.
 *
 * Owner rule (2026-10-17): wherever Scrolller media is on screen the player can
 * see it came "via Scrolller". ONE mark for the whole page, never one per card,
 * bottom-left, small, ~75% opacity, no glow, never takes a click.
 *
 * Two halves:
 *   - the PURE rule (isScrolllerUrl / isScrolllerEntry / creditText) and a pure
 *     clock (createCreditClock) that says whether the mark is up. Node-tested
 *     in tests/source-credit.test.mjs.
 *   - `sourceCredit`, the page's one mark. Inert without a document, so the
 *     page still boots under node and on the web build without the host.
 *
 * How a surface says "Scrolller is on screen":
 *   touch(ms)     - a card that lives a short while (flash cards, tube cards,
 *                   loops): the mark stays up `ms` after the LAST touch. Every
 *                   new Scrolller card only extends it, so it never blinks.
 *   hold(key) / drop(key) - a surface with a clear on/off (the race's DOM wall).
 *   clear()       - the run is over: every hold and touch goes at once.
 *
 * DtRH is a trance game: the mark is calm. Opacity only, one 200 ms fade in and
 * out, at every motion level ('off' is "opacity fades only", shared/motion.js).
 * It never pulses: a touch while it is up changes nothing on screen.
 * ==========================================================================*/

export const CREDIT_TEXT = 'via Scrolller';

/** The host's name marker on every online entry ("online30:Sub-123.webm",
 *  DtrhAssetManifest.cs). Scrolller is the app's only online source, and the
 *  marker survives on a copy kept on this machine (a ccp.assets url). */
const ONLINE_MARK_RE = /^online\d{0,3}:/i;
const SUB_RE = /^[A-Za-z0-9_]{2,32}$/;

/** True when the url's host is scrolller.com or one of its subdomains. */
export function isScrolllerUrl(url) {
  if (typeof url !== 'string' || !url) return false;
  let host = '';
  try { host = new URL(url).hostname.toLowerCase(); } catch (_) { return false; }
  return host === 'scrolller.com' || host.endsWith('.scrolller.com');
}

/** A manifest entry or a drawn pick: {name?, url?, online?}. */
export function isScrolllerEntry(e) {
  if (!e || typeof e !== 'object') return false;
  if (e.online === true) return true;
  if (typeof e.name === 'string' && ONLINE_MARK_RE.test(e.name)) return true;
  return isScrolllerUrl(e.url);
}

/** "via Scrolller", or "via Scrolller · r/<sub>" when a clean sub is known. */
export function creditText(sub) {
  return typeof sub === 'string' && SUB_RE.test(sub) ? CREDIT_TEXT + ' · r/' + sub : CREDIT_TEXT;
}

export const TOUCH_MS = 12000;
export const FADE_MS = 200;

/** The pure clock: holds by key plus a "seen until" time. No timers, no DOM. */
export function createCreditClock() {
  const holds = new Set();
  let until = 0;
  return {
    touch(now, ms = TOUCH_MS) {
      const t = now + Math.max(0, Number(ms) || 0);
      if (t > until) until = t;
    },
    hold(key) { holds.add(String(key)); },
    drop(key) { holds.delete(String(key)); },
    clear() { holds.clear(); until = 0; },
    visible(now) { return holds.size > 0 || now < until; },
    /** When a touch-only mark will go down (0 = never on its own). */
    expiresAt(now) { return holds.size > 0 || now >= until ? 0 : until; },
  };
}

function createMark() {
  const clock = createCreditClock();
  let el = null, shown = false, timer = 0;
  const hasDom = typeof document !== 'undefined' && !!document.createElement;

  function ensure() {
    if (el || !hasDom || !document.body) return el;
    el = document.createElement('div');
    el.className = 'src-credit';
    el.textContent = CREDIT_TEXT;
    el.setAttribute('aria-hidden', 'true');
    el.style.cssText = [
      'position:fixed', 'left:12px', 'bottom:3px', 'z-index:2147483000',
      'font:500 11px/1.3 system-ui,"Segoe UI",sans-serif', 'letter-spacing:0.02em',
      'color:#fff', 'text-shadow:0 1px 2px rgba(0,0,0,0.7)',
      'opacity:0', 'transition:opacity ' + FADE_MS + 'ms linear',
      'pointer-events:none', 'user-select:none', 'white-space:nowrap',
    ].join(';');
    document.body.appendChild(el);
    return el;
  }

  function sync() {
    if (!hasDom) return;
    const now = Date.now();
    const want = clock.visible(now);
    if (want !== shown) {
      shown = want;
      const m = ensure();
      if (m) m.style.opacity = want ? '0.75' : '0';
    }
    if (timer) { clearTimeout(timer); timer = 0; }
    const at = clock.expiresAt(now);
    if (at) timer = setTimeout(sync, Math.max(16, at - now + 5));
  }

  return {
    touch(ms) { clock.touch(Date.now(), ms); sync(); },
    hold(key) { clock.hold(key); sync(); },
    drop(key) { clock.drop(key); sync(); },
    clear() { clock.clear(); sync(); },
    visible() { return clock.visible(Date.now()); },
  };
}

/** The page's one mark (one document = one page = one mark). */
export const sourceCredit = createMark();
