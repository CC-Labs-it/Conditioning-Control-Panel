/* ============================================================================
 * planCard.js - the run summary's plan row (conversion lane G1, brief C10).
 *
 * "Open today. Basic keeps it open." under the payout, only when the host says
 * this run came from a Daily Gift (`payout-result` carries `daily_gift: true`,
 * DtrhHostService adds it with `plan: 'basic'`). A host that never sends the
 * field never shows the row.
 *
 * DtRH is a trance game, so the row is the calmest thing on the card: it waits
 * until the payout rows have landed, then fades in. No flash, no sting, no
 * sound, never during a run (the payout only arrives after run-ended), and it
 * sits under "fall again / wake up", never in front of them.
 *
 * The price shows only when the init payload carried C1 `plans`. The count
 * (C5) goes to the host as a `funnel` frame, or as a beacon on an https page,
 * or nowhere. No DOM at import.
 * ==========================================================================*/

export const PLAN_FALLBACK = Object.freeze({
  basic: Object.freeze({ name: 'Basic', url: 'https://www.patreon.com/checkout/CodeBambi?rid=27604786' }),
  prime: Object.freeze({ name: 'Prime', url: 'https://www.patreon.com/checkout/CodeBambi?rid=27681505' }),
});
export const FUNNEL_URL = 'https://codebambi-proxy.vercel.app/v2/public/funnel';
export const DAILY_GIFT_LINE = 'Open today. Basic keeps it open.';
export const PLAN_FEATURE = 'dtrh';
/** How long after the payout lands the row starts to fade in: the payout's own count is ~1.2 s. */
export const PLAN_ROW_DELAY_MS = 1600;

const PLAN_HOSTS = ['www.patreon.com', 'patreon.com', 'subscribestar.adult', 'app.cclabs.app'];

export function safePlanUrl(u) {
  try {
    const x = new URL(String(u || ''));
    return x.protocol === 'https:' && PLAN_HOSTS.includes(x.hostname) ? x.href : '';
  } catch (e) { return ''; }
}

/** The plan to offer: `price` is '' unless the host's payload carried one. */
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

/** Does this payout carry the row? Only a run the host says came from a Daily Gift, and never a dry run. */
export function planRowFor(payout) {
  if (!payout || typeof payout !== 'object') return null;
  if (payout.daily_gift !== true || payout.dryRun) return null;
  return { key: 'basic', line: DAILY_GIFT_LINE };
}

export function funnelLine(event, a = '', b = '') {
  if (event !== 'gate_shown' && event !== 'cta_clicked') return '';
  const clean = (v) => String(v ?? '').toLowerCase().replace(/[^a-z0-9_]/g, '').slice(0, 32);
  return [event, 'page', clean(a), clean(b)].join('|');
}

/** Fire and forget; returns the road taken ('host' | 'beacon' | 'none'). */
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

/** The row's element: one line, one link, the price when there is one. */
export function buildPlanRow({ doc = globalThis.document, row, plans, env = globalThis } = {}) {
  const plan = planOf(plans, row && row.key);
  const box = doc.createElement('div');
  box.className = 'cf-recap-plan';
  box.setAttribute('role', 'group');
  box.setAttribute('aria-label', plan.name);
  const p = doc.createElement('p');
  p.className = 'cf-recap-plan-line';
  p.textContent = (row && row.line) || DAILY_GIFT_LINE;
  box.append(p);
  const a = doc.createElement('a');
  a.className = 'sf-btn sf-btn-ghost cf-recap-plan-btn';
  a.href = plan.url; a.target = '_blank'; a.rel = 'noopener noreferrer';
  a.textContent = 'Get ' + plan.name;
  // Hosted (the desktop app): the host opens the checkout. Letting the link open itself there
  // raised a bare WebView2 popup, signed out and outside the host's throttle (review 2026-10-13).
  a.addEventListener('click', (e) => { if (postFunnel('cta_clicked', PLAN_FEATURE, 'patreon', env, plan.key) === 'host' && e && e.preventDefault) e.preventDefault(); });
  box.append(a);
  if (plan.price) {
    const s = doc.createElement('span');
    s.className = 'cf-recap-plan-price';
    s.textContent = plan.price;
    box.append(s);
  }
  return box;
}
