/* ui/links.js - the only real links the game may open: https on cclabs.app, www.cclabs.app or app.cclabs.app and their
 * paths, plus the one CC Labs Discord invite and the two neal.fun credit links (exact urls). Exact host, no port, no login part, so lookalikes like
 * cclabs.app.example.com or cclabs.app@example.com are refused. Pure. */

export const LINK_HOSTS = Object.freeze(['cclabs.app', 'www.cclabs.app', 'app.cclabs.app']);
export const DISCORD_INVITE = 'https://discord.gg/YxVAMt4qaZ';
export const SITE_URL = 'https://cclabs.app/';
export const DESKTOP_URL = 'https://cclabs.app/ccp.html';
/** Credit to the game this one is shaped after (owner 2026-11-20). Exact urls only, never the whole host. */
export const CREDIT_LINKS = Object.freeze(['https://neal.fun/', 'https://neal.fun/stimulation-clicker/']);

export function allowedLink(url) {
  let u;
  try { u = new URL(String(url)); } catch { return null; }
  if (u.protocol !== 'https:' || u.port || u.username || u.password) return null;
  if (u.href === DISCORD_INVITE || CREDIT_LINKS.includes(u.href)) return u.href;
  if (!LINK_HOSTS.includes(u.hostname)) return null;
  return u.href;
}

/** The one way a link opens: allowed, inside a real press, hosted down the wire as `open-url`, unhosted a new tab. Answers whether it went. */
export function openAllowedLink(url, { inPress = () => false, hosted = false, send = () => {}, win = globalThis.window } = {}) {
  const href = allowedLink(url);
  if (!href || !inPress()) return false;
  if (hosted) send('open-url', { url: href });
  else win?.open(href, '_blank', 'noopener');
  return true;
}
