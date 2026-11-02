/* ui/links.js - the only real link the game may open: https://cclabs.app and its paths. Exact host, no port, no login
 * part, so lookalikes like cclabs.app.example.com or cclabs.app@example.com are refused. Pure. */

export function allowedLink(url) {
  let u;
  try { u = new URL(String(url)); } catch { return null; }
  if (u.protocol !== 'https:' || u.hostname !== 'cclabs.app' || u.port || u.username || u.password) return null;
  return u.href;
}
