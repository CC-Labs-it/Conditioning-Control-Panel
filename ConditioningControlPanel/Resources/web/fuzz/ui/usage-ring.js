/* ui/usage-ring.js - the day's online picture meter on the web host (app.cclabs.app/tc).
 * A small grey bevel ring beside the account chip, shown only while the pictures come from Scrolller and the host
 * has sent a usedPercent. Percent only, never a size (the share is ours to tune). Hover or tap opens a small card;
 * from 80% it carries the Prime line when the host says a bigger share exists (frame.upgrade). No animation. */

import { openAllowedLink } from './links.js';

export const PRIME_URL = 'https://app.cclabs.app/dashboard/subscription';
export const RING_NUDGE_AT = 80;

export const RING_COPY = Object.freeze({
  label: 'Online pictures today: {p}% used',
  used: '{p}% of today\'s online pictures used.',
  spent: 'Today\'s online pictures are used up. Local files still work.',
  reset: 'Resets at midnight.',
  nudge: 'Need more pictures? Prime gets the most pictures a day.',
  nudgeX: 'Need more pictures? Prime gets {x}x more pictures a day.',
  button: 'See Prime',
});

/** What the ring shows for a meter reading. Pure. `upgradeX` (host, from the live shares) names the
 * multiple when it is a whole number of 2 or more; otherwise the plain Prime line. */
export function usageRingState({ source, percent, upgrade = false, upgradeX = null, web = false } = {}) {
  const visible = !!web && source === 'scrolller' && Number.isInteger(percent) && percent >= 0 && percent <= 100;
  if (!visible) return { visible: false };
  const p = String(percent);
  const spent = percent >= 100;
  return {
    visible: true,
    percent,
    spent,
    label: RING_COPY.label.replace('{p}', p),
    line: spent ? RING_COPY.spent : RING_COPY.used.replace('{p}', p),
    reset: RING_COPY.reset,
    nudge: upgrade === true && percent >= RING_NUDGE_AT,
    nudgeLine: Number.isInteger(upgradeX) && upgradeX >= 2 ? RING_COPY.nudgeX.replace('{x}', String(upgradeX)) : RING_COPY.nudge,
    tone: spent ? 'spent' : percent >= RING_NUDGE_AT ? 'high' : 'ok',
  };
}

/** Mounts the ring into #game-controls, right after the account chip (or first when there is none). Answers a
 * refresh function; call it on 'pictures' and 'picture-meter'. The top controls sit outside inPress(), so the
 * Prime button checks that its own click is real. */
export function mountUsageRing({ doc = document, win = doc.defaultView, pictures, web = false, send = () => {} }) {
  const controls = doc.getElementById('game-controls');
  if (!controls || !web || doc.getElementById('usage-ring')) return () => {};
  const wrap = doc.createElement('div');
  wrap.id = 'usage-ring';
  wrap.hidden = true;
  const ring = doc.createElement('button');
  ring.type = 'button';
  ring.className = 'ur-ring';
  ring.setAttribute('aria-expanded', 'false');
  ring.setAttribute('aria-controls', 'usage-card');
  const dial = doc.createElement('span'); dial.className = 'ur-dial'; dial.setAttribute('aria-hidden', 'true');
  const num = doc.createElement('b'); dial.append(num);
  ring.append(dial);
  const card = doc.createElement('section');
  card.id = 'usage-card';
  card.className = 'ur-card';
  card.hidden = true;
  const line = doc.createElement('p'); line.className = 'ur-line';
  const reset = doc.createElement('p'); reset.className = 'ur-reset';
  const nudge = doc.createElement('div'); nudge.className = 'ur-nudge';
  const nudgeText = doc.createElement('p');
  const prime = doc.createElement('button'); prime.type = 'button'; prime.textContent = RING_COPY.button;
  nudge.append(nudgeText, prime);
  card.append(line, reset, nudge);
  wrap.append(ring, card);
  const chip = doc.getElementById('account-chip');
  if (chip && chip.parentNode === controls) chip.after(wrap); else controls.prepend(wrap);

  let pinned = false;
  const setOpen = (open) => {
    card.hidden = !open;
    ring.setAttribute('aria-expanded', open ? 'true' : 'false');
  };
  ring.addEventListener('pointerenter', (ev) => { if (ev.pointerType === 'mouse') setOpen(true); });
  wrap.addEventListener('pointerleave', (ev) => { if (ev.pointerType === 'mouse' && !pinned) setOpen(false); });
  ring.addEventListener('click', () => { pinned = !pinned; setOpen(pinned); });
  doc.addEventListener('pointerdown', (ev) => {
    if (!pinned || wrap.contains(ev.target)) return;
    pinned = false; setOpen(false);
  }, true);
  prime.addEventListener('click', (ev) => openAllowedLink(PRIME_URL, {
    inPress: () => ev.isTrusted, hosted: true, send, win,
  }));

  return function refresh() {
    const s = usageRingState({ source: pictures?.source, percent: pictures?.usedPercent, upgrade: pictures?.meterUpgrade,
      upgradeX: pictures?.meterUpgradeX, web });
    wrap.hidden = !s.visible;
    if (!s.visible) { pinned = false; setOpen(false); return; }
    wrap.dataset.tone = s.tone;
    dial.style.setProperty('--ur-p', String(s.percent));
    num.textContent = String(s.percent);
    ring.title = s.label;
    ring.setAttribute('aria-label', s.label);
    line.textContent = s.line;
    reset.textContent = s.reset;
    nudgeText.textContent = s.nudgeLine;
    nudge.hidden = !s.nudge;
  };
}
