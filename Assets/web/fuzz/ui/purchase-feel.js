/* Purchase feedback lives above the world and never moves an input's hit box.
 * All animations belong to the world's pause sweep; no extra clocks or rewards. */
import { iconFor } from './icons.js';
import { offerBadge, paintBadges } from './repeat-badge.js';
import { PLUCKS } from '../audio/kit.js';
import { CONFIG } from '../data/config.js';

/* Landing sparkles: hand-drawn paper stars and confetti flecks in the theme colours, under half the particle ceiling.
 * Cosmetic Math.random only; the run RNG is never touched and nothing pays. No shadows, so nothing can turn one. */
export const SPARK_ROOM = Math.max(6, Math.floor((CONFIG.MAX_PARTICLES || 40) / 2));
const SPARK_INKS = ['var(--accent)', 'var(--accent-2)', 'var(--accent-soft)', 'var(--niche)', '#fff3b8'];
const SPARK_LINE = '#2b2230';
/** How many sparkles a burst may still spawn under the ceiling. */
export const sparkBudget = (live, want, room = SPARK_ROOM) => Math.max(0, Math.min(want, room - live));
function starSvg(fill) {
  const p = [];
  for (let i = 0; i < 8; i++) {
    const a = -Math.PI / 2 + i * Math.PI / 4 + (Math.random() - .5) * .2;
    const r = i % 2 ? 2.8 + Math.random() * 1.3 : 9.2 + Math.random() * 1.8;
    p.push(`${(12 + Math.cos(a) * r).toFixed(1)} ${(12 + Math.sin(a) * r).toFixed(1)}`);
  }
  // A second, thinner pass a hair off the first reads as crayon going round twice.
  return `<svg viewBox="0 0 24 24" width="100%" height="100%" style="overflow:visible"><path d="M${p.join(' L')}Z" style="fill:${fill}" stroke="${SPARK_LINE}" stroke-width="1.5" stroke-linejoin="round"/>`
    + `<path d="M${p.slice(1, 6).join(' L')}" fill="none" stroke="${SPARK_LINE}" stroke-width=".7" stroke-linecap="round" opacity=".5" transform="translate(.7 .5)"/></svg>`;
}

export function goalState(previous, affordable, announced = false) {
  return { affordable, announced: announced || (previous === false && affordable),
    announce: previous === false && affordable && !announced };
}

export function createPurchaseFeel({ doc, engine, gentle, reduced, dock = null }) {
  const layer = doc.getElementById('l-fx');
  const live = new Set(), sparks = new Set();
  let pending = null, lastNote = -Infinity;
  const enabled = (id) => !!engine.run.owned[id] && engine.switches.isOn(id);
  const motion = () => !gentle() && ['stray', 'float', 'confetti', 'bubble'].some(enabled);
  const active = () => !engine.paused && !engine.suspended && !engine.stopped;
  const colour = () => enabled('colour') || engine.run.picks.f5 ? 'var(--niche-text)' : 'var(--ink)';

  const sheet = doc.createElement('style');
  sheet.textContent = `
    #goal .row.ready { animation: none; }
    #goal .row.near .ico { opacity: .85; }
    #goal .row.ready .tag { color: var(--ink); }
    #goal .row.ready { box-shadow: inset 0 0 0 1px var(--ink); }
    .purchase-ghost { position:absolute;pointer-events:none;display:grid;place-items:center;
      border:1px solid var(--line);background:var(--panel);color:var(--ink);border-radius:10px; }
    .purchase-ghost .ico { font-size:28px;width:32px;height:32px;object-fit:contain;line-height:1; }
    .purchase-ring { position:absolute;pointer-events:none;border:2px solid;border-radius:10px; }
    .purchase-spark { position:absolute;pointer-events:none;will-change:transform,opacity; }
    .purchase-fleck { position:absolute;pointer-events:none;border:1.2px solid ${SPARK_LINE};will-change:transform,opacity; }
    .purchase-shine { position:absolute;pointer-events:none;border-radius:10px;border:2px solid var(--accent);
      outline:1.5px dashed var(--accent-2);outline-offset:2px;
      background:linear-gradient(105deg,transparent 30%,rgba(255,255,255,.6) 47%,color-mix(in srgb,var(--accent-soft) 40%,transparent) 54%,transparent 70%) no-repeat;
      background-size:300% 100%;background-position:130% 0; }
    .purchase-shine.calm { background:none;outline:0; }
  `;
  doc.head.appendChild(sheet);

  function clear() {
    pending = null;
    for (const a of [...live, ...sparks]) a.cancel();
  }
  function sparkAnimate(node, frames, opts) {
    if (!node?.animate) { node?.remove(); return; }
    const a = node.animate(frames, opts);
    sparks.add(a);
    a.onfinish = a.oncancel = () => { sparks.delete(a); node.remove(); };
  }
  /* A quick burst from a landing spot: stars pop out and spin, flecks pop and drop a little. About 0.5-0.8 s. */
  function burst(r, { major = false, small = false, delay = 0 } = {}) {
    if (!r) return;
    const n = sparkBudget(sparks.size, small ? 7 : major ? 13 : 10);
    const big = !small && r.width > 70 && r.height > 40;
    for (let i = 0; i < n; i++) {
      const star = i % 3 !== 2;
      const angle = (i / n) * Math.PI * 2 + (Math.random() - .5) * .8;
      // On a large object half the pieces start on its outline, so the whole new thing reads as marked.
      const rim = big && i % 2 === 0;
      const x0 = r.x + (rim ? Math.cos(angle) * r.width / 2 : (Math.random() - .5) * Math.min(r.width, 40) * .5);
      const y0 = r.y + (rim ? Math.sin(angle) * r.height / 2 : (Math.random() - .5) * Math.min(r.height, 30) * .5);
      const dist = (small ? 14 : 24) + Math.random() * (small ? 16 : major ? 42 : 30);
      const dx = Math.cos(angle) * dist, dy = Math.sin(angle) * dist * (small ? .7 : 1) - (small ? 6 : 0);
      const fill = SPARK_INKS[Math.floor(Math.random() * SPARK_INKS.length)];
      const node = doc.createElement('div');
      node.setAttribute('aria-hidden', 'true');
      let size, frames;
      if (star) {
        size = (small ? 8 : 10) + Math.random() * (small ? 5 : 8);
        node.className = 'purchase-spark';
        node.innerHTML = starSvg(fill);
        const spin = (Math.random() < .5 ? -1 : 1) * (90 + Math.random() * 160);
        frames = [
          { opacity: 0, transform: 'translate(0,0) scale(.2) rotate(0deg)' },
          { opacity: 1, transform: `translate(${dx * .55}px,${dy * .55}px) scale(1.15) rotate(${spin * .45}deg)`, offset: .3 },
          { opacity: 0, transform: `translate(${dx}px,${dy}px) scale(.35) rotate(${spin}deg)` },
        ];
        node.style.cssText = `left:${x0 - size / 2}px;top:${y0 - size / 2}px;width:${size}px;height:${size}px;`;
      } else {
        const w = 4 + Math.random() * 4, h = 3 + Math.random() * 2;
        size = w;
        node.className = 'purchase-fleck';
        const tilt = Math.random() * 180;
        frames = [
          { opacity: 0, transform: `translate(0,0) rotate(${tilt}deg) scale(.4)` },
          { opacity: 1, transform: `translate(${dx * .6}px,${dy * .6}px) rotate(${tilt + 90}deg) scale(1)`, offset: .3 },
          { opacity: 0, transform: `translate(${dx}px,${dy + 14}px) rotate(${tilt + 200}deg) scale(.8)` },
        ];
        node.style.cssText = `left:${x0 - w / 2}px;top:${y0 - h / 2}px;width:${w}px;height:${h}px;background:${fill};border-radius:${Math.random() < .4 ? '50%' : '1px'};`;
      }
      layer.appendChild(node);
      sparkAnimate(node, frames, { duration: 520 + Math.random() * 280, delay: delay + Math.random() * 70, easing: 'cubic-bezier(.15,.7,.35,1)', fill: 'backwards' });
    }
  }
  /* The new object's outline in the theme colours with one light sweep across it; calm = a plain outline fade. */
  function shine(r, { calm = false, delay = 0 } = {}) {
    if (!r || sparks.size >= SPARK_ROOM + 4) return;
    const box = doc.createElement('div');
    box.className = `purchase-shine${calm ? ' calm' : ''}`;
    box.setAttribute('aria-hidden', 'true');
    box.style.cssText = `left:${r.left - 4}px;top:${r.top - 4}px;width:${r.width + 8}px;height:${r.height + 8}px;`;
    layer.appendChild(box);
    sparkAnimate(box, calm
      ? [{ opacity: 0 }, { opacity: .85, offset: .42 }, { opacity: 0 }]
      : [{ opacity: 0, transform: 'scale(.97)', backgroundPosition: '130% 0' },
        { opacity: 1, transform: 'scale(1.01)', offset: .25 },
        { opacity: .9, transform: 'scale(1)', offset: .65 },
        { opacity: 0, transform: 'scale(1.02)', backgroundPosition: '-30% 0' }],
    { duration: calm ? 760 : 820, delay, easing: 'ease-out', fill: 'backwards' });
  }
  function animate(node, frames, opts) {
    if (!node?.animate) { node?.remove(); return; }
    const a = node.animate(frames, opts);
    live.add(a);
    const done = () => { live.delete(a); node.remove(); };
    a.onfinish = a.oncancel = done;
  }
  function note(index, gain = .16) {
    if (!active() || !enabled('sound') || engine.real - lastNote < 220) return;
    lastNote = engine.real;
    engine.audio?.play(PLUCKS[index], { bus: 'sfx', gain });
  }
  function rect(node) {
    if (!node?.isConnected || node.hidden) return null;
    const r = node.getBoundingClientRect();
    const w = doc.defaultView.innerWidth, h = doc.defaultView.innerHeight;
    if (r.width < 16 || r.height < 12 || r.right <= 0 || r.bottom <= 0 || r.left >= w || r.top >= h) return null;
    return { left: r.left, top: r.top, width: r.width, height: r.height, x: r.left + r.width / 2, y: r.top + r.height / 2 };
  }
  function prepare(item, row) {
    pending = { id: item.id, from: rect(row), nodes: new Set(doc.querySelectorAll('.scr, .dk, .dk-chip')) };
  }
  function destination(item, before) {
    // Dock chips are the visible home on phones; the same slot is a panel on desktop.
    const owned = [...doc.querySelectorAll('[data-fz-owner]')].filter((n) => n.dataset.fzOwner === item.id);
    const additions = before ? [...doc.querySelectorAll('.scr, .dk-chip, .dk')].filter((n) => !before.nodes.has(n)) : [];
    const scoped = [...(engine.scopes.get(item.id)?.els || [])];
    const w = doc.defaultView.innerWidth, h = doc.defaultView.innerHeight;
    for (const node of [...additions, ...owned, ...scoped]) {
      if (['STYLE', 'LINK'].includes(node.tagName) || layer.contains(node)) continue;
      const r = rect(node);
      if (r && r.width < w * .8 && r.height < h * .6) return Object.assign(r, { node });
    }
    const home = doc.getElementById(item.fps ? 'counter' : 'btn');
    return Object.assign(rect(home) || {}, { node: home });
  }
  function halo(r, major, travel, delay = 0) {
    if (!r || live.size >= 14) return;
    const ring = doc.createElement('div');
    ring.className = 'purchase-ring';
    ring.style.cssText = `left:${r.left - 5}px;top:${r.top - 5}px;width:${r.width + 10}px;height:${r.height + 10}px;border-color:${colour()};`;
    layer.appendChild(ring);
    animate(ring, travel
      ? [{ opacity: 0, transform: 'scale(.94)' }, { opacity: .75, transform: 'scale(1)', offset: .28 }, { opacity: 0, transform: `scale(${major ? 1.12 : 1.06})` }]
      : [{ opacity: 0 }, { opacity: .6, offset: .4 }, { opacity: 0 }],
    { duration: major ? 620 : 480, delay, easing: 'ease-out', fill: 'backwards' });
  }
  function bought(item) {
    const before = pending?.id === item.id ? pending : null;
    pending = null;
    // Tabs this purchase added wear NEW until the player meets them, even when nothing below animates.
    const tabs = before ? [...doc.querySelectorAll('.dk-chip')].filter((n) => !before.nodes.has(n) && dock?.markNew?.(n)) : [];
    if (!active() || item.kind === 'end') return;
    const from = before?.from || rect(doc.querySelector(`#shop [data-id="${item.id}"]`));
    const to = destination(item, before);
    const major = item.kind === 'fork' || ['taste2', 'glossy', 'overlay'].includes(item.id);
    if (!from || !to?.width) return;
    const travel = motion();
    const tabRects = tabs.map((n) => Object.assign(rect(n) || {}, { node: n })).filter((r) => r.width && (Math.abs(r.x - to.x) > 2 || Math.abs(r.y - to.y) > 2));
    // Gentle visuals and reduced motion: a short outline fade on the new thing and its tab, no particles.
    if (!travel || reduced()) {
      if ((gentle() || reduced()) && ['stray', 'float', 'confetti', 'bubble'].some(enabled)) {
        halo(to, major, false);
        for (const r of tabRects) shine(r, { calm: true });
      }
      // The plain page gets only its ordinary pressed state until movement is bought.
      return;
    }
    if (live.size >= 12) return;
    const ghost = doc.createElement('div');
    ghost.className = 'purchase-ghost';
    ghost.setAttribute('aria-hidden', 'true');
    ghost.style.cssText = `left:${from.x - 23}px;top:${from.y - 23}px;width:46px;height:46px;`;
    // Repeat buys fly with their badge (owner 2026-11-08). bought() runs after the buy, so a growing item's
    // first copy (owning 1 now) flies without a plus.
    const ico = doc.createElement('span');
    ico.className = 'rep-ico';
    ico.appendChild(iconFor(doc, item));
    const badge = offerBadge(item, engine.run);
    paintBadges(ico, badge?.kind === 'plus' && (engine.run.owned[item.id] || 0) < 2 ? null : badge);
    ghost.appendChild(ico);
    layer.appendChild(ghost);
    const dx = to.x - from.x, dy = to.y - from.y;
    animate(ghost, [
      { transform: 'scale(.88)', opacity: .9 },
      { transform: 'scale(1.08)', opacity: 1, offset: .17 },
      { transform: `translate(${dx * .45}px,${dy * .45 - 22}px) scale(.9)`, opacity: 1, offset: .55 },
      { transform: `translate(${dx}px,${dy}px) scale(.35)`, opacity: 0 },
    ], { duration: major ? 560 : 440, easing: 'cubic-bezier(.2,.65,.4,1)' });
    // The landing: sparkles where the icon arrives, the new object's outline shimmers, then each new tab gets its own.
    // Each outline is measured when it starts, not at the buy: a new object is still being placed and the shop row it
    // came from still collapsing at the buy, so a rect taken then sat slightly off the object (owner 2026-11-08).
    const land = (major ? 560 : 440) - 50;
    const at = (r) => (r.node && rect(r.node)) || r;
    const later = (ms, fn) => doc.defaultView.setTimeout(() => { if (active()) fn(); }, ms);
    later(major ? 300 : 230, () => halo(at(to), major, true));
    later(land - 40, () => { const r = at(to); shine(r); later(40, () => burst(r, { major })); });
    later(land + 120, () => { for (const t of tabRects) { const r = at(t); shine(r); burst(r, { small: true }); } });
  }
  function ready(row) {
    if (!active()) return;
    note(4, .1);
    if (!motion() || reduced()) return;
    const r = rect(row);
    if (r) halo(r, false, true);
  }
  return { prepare, bought, ready, clear };
}
