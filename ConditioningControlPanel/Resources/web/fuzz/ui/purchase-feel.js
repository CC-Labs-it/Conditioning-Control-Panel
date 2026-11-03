/* Purchase feedback lives above the world and never moves an input's hit box.
 * All animations belong to the world's pause sweep; no extra clocks or rewards. */
import { iconFor } from './icons.js';
import { PLUCKS } from '../audio/kit.js';

export function goalState(previous, affordable, announced = false) {
  return { affordable, announced: announced || (previous === false && affordable),
    announce: previous === false && affordable && !announced };
}

export function createPurchaseFeel({ doc, engine, gentle, reduced }) {
  const layer = doc.getElementById('l-fx');
  const live = new Set();
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
  `;
  doc.head.appendChild(sheet);

  function clear() {
    pending = null;
    for (const a of [...live]) a.cancel();
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
      if (r && r.width < w * .8 && r.height < h * .6) return r;
    }
    return rect(doc.getElementById(item.fps ? 'counter' : 'btn'));
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
    if (!active() || item.kind === 'end') return;
    const from = before?.from || rect(doc.querySelector(`#shop [data-id="${item.id}"]`));
    const to = destination(item, before);
    const major = item.kind === 'fork' || ['taste2', 'glossy', 'overlay'].includes(item.id);
    if (!from || !to || reduced()) return;
    const travel = motion();
    // The plain page gets only its ordinary pressed state until movement is bought.
    if (!travel) { if (gentle() && ['stray', 'float', 'confetti'].some(enabled)) halo(to, major, false); return; }
    if (live.size >= 12) return;
    const ghost = doc.createElement('div');
    ghost.className = 'purchase-ghost';
    ghost.setAttribute('aria-hidden', 'true');
    ghost.style.cssText = `left:${from.x - 23}px;top:${from.y - 23}px;width:46px;height:46px;`;
    ghost.appendChild(iconFor(doc, item));
    layer.appendChild(ghost);
    const dx = to.x - from.x, dy = to.y - from.y;
    animate(ghost, [
      { transform: 'scale(.88)', opacity: .9 },
      { transform: 'scale(1.08)', opacity: 1, offset: .17 },
      { transform: `translate(${dx * .45}px,${dy * .45 - 22}px) scale(.9)`, opacity: 1, offset: .55 },
      { transform: `translate(${dx}px,${dy}px) scale(.35)`, opacity: 0 },
    ], { duration: major ? 560 : 440, easing: 'cubic-bezier(.2,.65,.4,1)' });
    halo(to, major, true, major ? 300 : 230);
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
