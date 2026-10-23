/* end-card.js - THE PLAN ROW under a result (conversion lane G1, brief C10).
 *
 * One line of copy and one button that opens the plan's checkout. It sits BELOW a result that is already on
 * screen (the Breakout demo card, THE BANK after a big win) and never in front of the way out. Neutral UI:
 * no motion of its own beyond a fade, no sound, no spiral.
 *
 *   plans    the host's C1 payload (`GET /config/plans`), when it sent one. Without it the button still opens
 *            the compiled checkout url and NO PRICE is shown (brief: "no price unless `plans` is present").
 *   funnel   `cta_clicked` / `gate_shown` (C5). Hosted: `chrome.webview.postMessage({type:'funnel', ...})`,
 *            the host forwards it (C8) or logs it as unhandled. On the web: `navigator.sendBeacon` to the
 *            proxy, https pages only. No host and no network: a no-op. Never a user id, never awaited.
 *
 * The button is a real <a target=_blank>: middle-clickable, needs no host round-trip, and a host that routes
 * new-window requests to the browser gets it for free. The module is DOM-free at import, so node can test it.
 * ==========================================================================*/

/** C1's compiled fallback: the checkout links preselect the tier. Never a bare patreon.com/CodeBambi. */
export const PLAN_FALLBACK = Object.freeze({
  basic: Object.freeze({ name: 'Basic', url: 'https://www.patreon.com/checkout/CodeBambi?rid=27604786' }),
  prime: Object.freeze({ name: 'Prime', url: 'https://www.patreon.com/checkout/CodeBambi?rid=27681505' }),
});

/** C5's route. The site and web pages use the same proxy every other page here talks to. */
export const FUNNEL_URL = 'https://codebambi-proxy.vercel.app/v2/public/funnel';

const PLAN_HOSTS = ['www.patreon.com', 'patreon.com', 'subscribestar.adult', 'app.cclabs.app'];
const FUNNEL_EVENTS = ['gate_shown', 'cta_clicked'];

/** An https checkout link on one of the three payment hosts, else ''. A payload is data, never a redirect. */
export function safePlanUrl(u) {
  try {
    const x = new URL(String(u || ''));
    return x.protocol === 'https:' && PLAN_HOSTS.includes(x.hostname) ? x.href : '';
  } catch (e) { return ''; }
}

/** The plan to offer, from the host's payload or the fallback. `price` is '' unless `plans` carried one. */
export function planOf(plans, key) {
  const k = key === 'basic' ? 'basic' : 'prime';
  const fb = PLAN_FALLBACK[k];
  const p = plans && typeof plans === 'object' && plans[k] && typeof plans[k] === 'object' ? plans[k] : null;
  const url = (p && safePlanUrl(p.patreon_url)) || fb.url;
  const name = p && typeof p.name === 'string' && /^[A-Za-z][A-Za-z ]{0,15}$/.test(p.name) ? p.name : fb.name;
  const monthly = p ? String(p.monthly ?? '') : '';
  const cur = plans && /^[A-Z]{3}$/.test(String(plans.currency || '')) ? String(plans.currency) : 'USD';
  const price = /^\d{1,3}(\.\d{1,2})?$/.test(monthly) ? `${cur} ${monthly} a month` : '';
  return { key: k, name, url, price };
}

/** C5's body: `event|page|a|b`, every field cut to the server's allowlist shape. '' = do not send. */
export function funnelLine(event, a = '', b = '') {
  if (!FUNNEL_EVENTS.includes(event)) return '';
  const clean = v => String(v ?? '').toLowerCase().replace(/[^a-z0-9_]/g, '').slice(0, 32);
  return [event, 'page', clean(a), clean(b)].join('|');
}

/** Fire and forget. Returns which road it took ('host' | 'beacon' | 'none') so a test can see it. */
export function postFunnel(event, a = '', b = '', env = globalThis, plan = '') {
  const line = funnelLine(event, a, b);
  if (!line) return 'none';
  try {
    const wv = env && env.chrome && env.chrome.webview;
    if (wv && typeof wv.postMessage === 'function') {
      const [, , ca, cb] = line.split('|');
      // With a plan named, the HOST opens that plan's checkout in the browser (its own links, its own throttle).
      wv.postMessage(plan ? { type: 'funnel', event, a: ca, b: cb, plan } : { type: 'funnel', event, a: ca, b: cb });
      return 'host';
    }
    const nav = env && env.navigator, loc = env && env.location;
    if (nav && typeof nav.sendBeacon === 'function' && loc && loc.protocol === 'https:') {
      nav.sendBeacon(FUNNEL_URL, line);
      return 'beacon';
    }
  } catch (e) { /* counting is never load-bearing */ }
  return 'none';
}

/** The viewer's tier when the host said it (0 | 1 | 2), else null. Unknown never shows an offer. */
export function viewerTier(src) {
  const t = src && typeof src === 'object' ? src.tier : undefined;
  return Number.isInteger(t) && t >= 0 && t <= 9 ? t : null;
}

let cssLoaded = false;
/** The row's own stylesheet, once per document. */
export function loadEndCardCss(doc = globalThis.document) {
  if (cssLoaded || !doc || !doc.head) return;
  cssLoaded = true;
  try {
    if (doc.querySelector && doc.querySelector('link[data-br-end-card]')) return;
    const link = doc.createElement('link');
    link.rel = 'stylesheet'; link.href = new URL('./end-card.css', import.meta.url).href;
    link.setAttribute('data-br-end-card', '');
    doc.head.append(link);
  } catch (e) { /* the row still works unstyled */ }
}

/**
 * The row: `<div class="br-end-card [cls]"><p>line</p><a>Get Prime</a><span>price</span></div>`.
 * `line` '' leaves the copy out (the Breakout card already says "Included with Prime" above it).
 * The click counts `cta_clicked` and lets the link do its own opening.
 */
export function buildEndCard({ doc = globalThis.document, line = '', plan, feature, cls = '', env = globalThis } = {}) {
  const p = plan || planOf(null, 'prime');
  const root = doc.createElement('div');
  root.className = 'br-end-card' + (cls ? ' ' + cls : '');
  root.setAttribute('role', 'group');
  root.setAttribute('aria-label', p.name);
  if (line) {
    const t = doc.createElement('p'); t.className = 'br-end-card-line'; t.textContent = line;
    root.append(t);
  }
  const a = doc.createElement('a');
  a.className = 'br-end-card-btn';
  a.href = p.url; a.target = '_blank'; a.rel = 'noopener noreferrer';
  a.textContent = 'Get ' + p.name;
  // Hosted (the desktop app): the host opens the checkout. Letting the link open itself there
  // raised a bare WebView2 popup, signed out and outside the host's throttle (review 2026-10-13).
  a.addEventListener('click', (e) => { if (postFunnel('cta_clicked', feature, 'patreon', env, p.key) === 'host' && e && e.preventDefault) e.preventDefault(); });
  root.append(a);
  if (p.price) {
    const s = doc.createElement('span'); s.className = 'br-end-card-price'; s.textContent = p.price;
    root.append(s);
  }
  return root;
}
