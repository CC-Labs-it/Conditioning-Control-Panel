/* ============================================================================
 * seal.js - the game is Prime only until the anniversary (owner, 2026-11-14), then free for everyone.
 *
 * Before SEAL_OPEN_AT_MS the game starts only when a host says the player's plan is Prime (init caps.plan, the same
 * field ui/ads.js reads). A page opened on its own has no host and no plan, so it shows the sealed notice instead of
 * the contract. From the anniversary on, this file does nothing. A local preview is never sealed unless it asks.
 * The notice wears the legal window (ui/legal.css) so it reads as one more piece of paperwork.
 * ==========================================================================*/

export const SEAL_OPEN_AT_MS = Date.parse('2026-12-22T00:00:00Z');
export const SEAL_HOST_URL = 'https://app.cclabs.app/tc';
export const SEAL_PRIME_URL = 'https://app.cclabs.app/subscribe?plan=prime';

const LOCAL = new Set(['127.0.0.1', 'localhost']);

/** True when this page must show the notice instead of the game. `force` is the local preview's own switch. */
export function sealedFor(init, nowMs, hostname = '', force = false) {
  if (!(nowMs < SEAL_OPEN_AT_MS)) return false;
  if (LOCAL.has(hostname)) return force === true;
  return init?.caps?.plan !== 'prime';
}

/** What is left until the anniversary, as the four figures the notice shows. */
export function sealCountdown(nowMs) {
  const left = Math.max(0, Math.floor((SEAL_OPEN_AT_MS - nowMs) / 1000));
  return { days: Math.floor(left / 86400), hours: Math.floor((left % 86400) / 3600), minutes: Math.floor((left % 3600) / 60), seconds: left % 60, done: left === 0 };
}

const two = (n) => String(n).padStart(2, '0');

/** Put the notice on the page. Returns a function that takes it down. When the clock runs out the page reloads. */
export function showSeal(doc, { now = () => Date.now(), reload = () => doc.defaultView?.location.reload() } = {}) {
  const link = doc.createElement('link');
  link.rel = 'stylesheet';
  link.href = 'ui/seal.css';
  doc.head.appendChild(link);

  const btn = doc.getElementById('btn');
  if (btn) btn.disabled = true;

  const root = doc.createElement('div');
  root.className = 'tc seal';
  root.innerHTML = '<div class="win small" role="dialog" aria-modal="true" aria-labelledby="seal-h">'
    + '<div class="tbar"><span>Terms &amp; Conditioning - Notice</span><button class="fakex" type="button" disabled aria-hidden="true">&#215;</button></div>'
    + '<div class="body">'
    + '<h1 id="seal-h">These terms are sealed.</h1>'
    + '<p class="intro">They open to everyone on <b>22 December</b>. Until then the contents are <span class="bar">withheld from the public</span> and may be read by Prime members only.</p>'
    + '<div class="seal-paper" aria-hidden="true"><i style="width:94%"></i><i style="width:81%"></i><i style="width:88%"></i><i style="width:47%"></i><span class="seal-stamp">SEALED</span></div>'
    + '<div class="seal-clock" role="timer" aria-label="Time until 22 December">'
    + '<div><b data-u="days">--</b><span>days</span></div><div><b data-u="hours">--</b><span>hours</span></div>'
    + '<div><b data-u="minutes">--</b><span>min</span></div><div><b data-u="seconds">--</b><span>sec</span></div></div>'
    + '<div class="foot"><div class="btns">'
    + `<a class="seal-go" href="${SEAL_HOST_URL}" target="_top" rel="noopener">I have Prime</a>`
    + `<a class="seal-link" href="${SEAL_PRIME_URL}" target="_top" rel="noopener">Get Prime</a>`
    + '</div><p class="seal-fine">Anything you already agreed to on this device is kept.</p></div>'
    + '</div></div>';
  doc.body.appendChild(root);

  const cells = {};
  for (const b of root.querySelectorAll('[data-u]')) cells[b.dataset.u] = b;
  const tick = () => {
    const c = sealCountdown(now());
    cells.days.textContent = String(c.days);
    cells.hours.textContent = two(c.hours);
    cells.minutes.textContent = two(c.minutes);
    cells.seconds.textContent = two(c.seconds);
    if (c.done) { clearInterval(timer); reload(); }
  };
  const timer = setInterval(tick, 1000);
  timer?.unref?.();
  tick();
  return () => { clearInterval(timer); root.remove(); link.remove(); if (btn) btn.disabled = false; };
}
