/* ============================================================================
 * ui/screens/recapPlan.js - the recap's plan row (conversion lane G1, brief C10).
 *
 * "Host the next one." (Basic) under the recap's actions, when this account
 * cannot host. It never sits between the player and "Back to menu": recap.js
 * appends it after the actions row, like the standalone CTA.
 *
 * WHO SEES IT: only an account the host has said cannot host
 * (`session.caps.canHost === false`, GoonHostService.HostingAllowed()). An
 * unknown answer (standalone, older host) shows nothing, so a Basic player is
 * never sold what they already have.
 *
 * THE BUTTON: hosted, the app's own upgrade path (`actions.seePrime()` ->
 * `open-prime` -> TierGate), which the host already answers; standalone, a
 * real link to the plan's checkout. The price shows only when the host sent
 * the C1 `plans` payload (`session.plans`); without it, no price and the
 * compiled checkout link.
 *
 * COUNTING (C5): `gate_shown` when the row is built, `cta_clicked` on the
 * button. Hosted: a `funnel` frame through the bridge (the host forwards or
 * logs it as unhandled). Standalone on https: navigator.sendBeacon. No user id,
 * never awaited, never load-bearing.
 *
 * No DOM at import: node can hold every decision here.
 * ==========================================================================*/

export const PLAN_FALLBACK = Object.freeze({
  basic: Object.freeze({ name: 'Basic', url: 'https://www.patreon.com/checkout/CodeBambi?rid=27604786' }),
  prime: Object.freeze({ name: 'Prime', url: 'https://www.patreon.com/checkout/CodeBambi?rid=27681505' }),
});
export const FUNNEL_URL = 'https://codebambi-proxy.vercel.app/v2/public/funnel';
export const RECAP_PLAN_LINE = 'Host the next one.';
export const RECAP_PLAN_FEATURE = 'goon_host';

const PLAN_HOSTS = ['www.patreon.com', 'patreon.com', 'subscribestar.adult', 'app.cclabs.app'];

/** An https checkout link on one of the payment hosts, else ''. */
export function safePlanUrl(u) {
  try {
    const x = new URL(String(u || ''));
    return x.protocol === 'https:' && PLAN_HOSTS.includes(x.hostname) ? x.href : '';
  } catch (_e) { return ''; }
}

/** The plan to offer: `price` is '' unless the host's payload carried one. */
export function planOf(plans, key) {
  const k = key === 'prime' ? 'prime' : 'basic';
  const fb = PLAN_FALLBACK[k];
  const p = plans && typeof plans === 'object' && plans[k] && typeof plans[k] === 'object' ? plans[k] : null;
  const url = (p && safePlanUrl(p.patreon_url)) || fb.url;
  const name = p && typeof p.name === 'string' && /^[A-Za-z][A-Za-z ]{0,15}$/.test(p.name) ? p.name : fb.name;
  const monthly = p ? String(p.monthly ?? '') : '';
  const cur = plans && /^[A-Z]{3}$/.test(String(plans.currency || '')) ? String(plans.currency) : 'USD';
  const price = /^\d{1,3}(\.\d{1,2})?$/.test(monthly) ? `${cur} ${monthly} a month` : '';
  return { key: k, name, url, price };
}

/** Does this recap carry the row? Only on an explicit "cannot host" from the host. */
export function recapPlanWanted(session) {
  return !!(session && session.caps && session.caps.canHost === false);
}

/** C5's body, `event|page|a|b`, cut to the server's allowlist shape. '' = do not send. */
export function funnelLine(event, a = '', b = '') {
  if (event !== 'gate_shown' && event !== 'cta_clicked') return '';
  const clean = (v) => String(v ?? '').toLowerCase().replace(/[^a-z0-9_]/g, '').slice(0, 32);
  return [event, 'page', clean(a), clean(b)].join('|');
}

/**
 * Fire and forget. `send` is the bridge's send (hosted); `env` is globalThis.
 * Returns the road taken ('host' | 'beacon' | 'none').
 */
export function postFunnel(event, a, b, { hosted = false, send = null, env = globalThis } = {}) {
  const line = funnelLine(event, a, b);
  if (!line) return 'none';
  const [, , ca, cb] = line.split('|');
  try {
    if (hosted && typeof send === 'function') { send({ type: 'funnel', event, a: ca, b: cb }); return 'host'; }
    const nav = env && env.navigator, loc = env && env.location;
    if (!hosted && nav && typeof nav.sendBeacon === 'function' && loc && loc.protocol === 'https:') {
      nav.sendBeacon(FUNNEL_URL, line);
      return 'beacon';
    }
  } catch (_e) { /* counting is never load-bearing */ }
  return 'none';
}

/** The webview's own post, when there is one: the bridge module is boot's, so the row talks straight to it. */
export function hostSend(env = globalThis) {
  const wv = env && env.chrome && env.chrome.webview;
  return wv && typeof wv.postMessage === 'function' ? (m) => wv.postMessage(m) : null;
}
