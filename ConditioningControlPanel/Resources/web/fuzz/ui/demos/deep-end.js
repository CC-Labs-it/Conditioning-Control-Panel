/* Demo for The Deep End (mini_deep): a small 4x4 dive. Tiles slide, equal depths sink together, a new depth flashes its name.
 * Registered by importing this module beside the other demo modules in ui/fork.js. */
import { DEMOS } from './kit.js';
import { createBoard, slide, spawn, deal, canMove, deepest, DEPTHS } from '../unlocks/arcade/deep-end/board.js';

DEMOS.mini_deep = (k) => {
  k.page('dark');
  const S = 112, G = 3, C = (S - G * 5) / 4, X = (k.W - S) / 2, Y = 26;
  const tint = (t) => `color-mix(in srgb,#ff7fbf ${100 - (t - 1) * 10}%,#0b0d2a)`;
  k.add(X, Y, `width:${S}px;height:${S}px;border-radius:7px;background:#2a2340;`);
  const label = k.add(0, 6, `width:${k.W}px;text-align:center;font-weight:700;font-size:12px;`, '');
  const word = k.add(0, Y + S / 2 - 12, `width:${k.W}px;text-align:center;font:900 20px/1 sans-serif;letter-spacing:.12em;color:#fff;opacity:0;text-shadow:0 0 10px #ff7fbf;`, '');
  const views = new Map();
  let b = deal(createBoard(4)), best = deepest(b), step = 0;
  const at = (i) => G + i * (C + G);
  function draw() {
    for (const [id, el] of views) if (!b.tiles.some((t) => t.id === id)) { el.remove(); views.delete(id); }
    for (const t of b.tiles) {
      let el = views.get(t.id);
      if (!el) {
        el = k.add(0, 0, `width:${C}px;height:${C}px;border-radius:4px;display:grid;place-items:center;color:#fff;font:800 11px sans-serif;transition:transform .14s ease-out,background .2s;`, '');
        el.style.left = X + 'px'; el.style.top = Y + 'px';
        views.set(t.id, el);
      }
      el.style.transform = `translate(${at(t.c)}px,${at(t.r)}px)`;
      el.style.background = tint(t.tier);
      el.textContent = String(t.tier);
    }
    label.textContent = DEPTHS[deepest(b)];
  }
  draw();
  const dirs = ['left', 'down', 'right', 'down', 'left', 'up'];
  k.every(700, () => {
    if (!canMove(b) || step > 60) { for (const el of views.values()) el.remove(); views.clear(); b = deal(createBoard(4)); best = deepest(b); step = 0; draw(); return; }
    let out = slide(b, dirs[step++ % dirs.length]);
    if (!out.moved) out = slide(b, dirs[step++ % dirs.length]);
    if (out.moved) spawn(b);
    draw();
    const deep = deepest(b);
    if (deep > best && deep >= 3) {
      best = deep;
      word.textContent = DEPTHS[deep].toUpperCase();
      k.anim(word, [{ opacity: 0 }, { opacity: .95, offset: .25 }, { opacity: 0 }], 900);
    }
  }, true);
};
