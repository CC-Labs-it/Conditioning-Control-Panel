/* ui/unlocks/fate/outside/door.js - Door Ajar (ft_door, Go Outside card 4). A thin strip of white light down the right edge,
 * as if a door there was left ajar, with a soft spill on the floor and a few dust motes. Every purchase after it opens the
 * door a little more (rules.js doorWidth, capped: never open). It is the white the Go Outside ending fades to: when that
 * ending starts, the light swings wide into the fade. It sits under the dock objects and takes no pointer.
 * Gentle and reduced motion: no motes, no swing; the width steps with a short fade instead of sliding. */

import { buysAfter, doorWidth } from './rules.js';
import { LITE } from '../../../perf.js';
import { fresh } from '../../basics/fx.js';

export function mountDoor(ctx) {
  const still = () => ctx.gentle || ctx.reduced;
  const root = ctx.el('div', 'position:absolute;top:0;right:0;bottom:0;width:0;pointer-events:none;', null, 'over');
  root.dataset.fate = 'door';
  const doc = root.ownerDocument;
  const part = (css) => { const e = doc.createElement('div'); e.style.cssText = `position:absolute;pointer-events:none;${css}`; root.appendChild(e); return e; };
  /* back to front: the wide glow, the floor spill, the core of the light, the door's edge, the motes */
  const glow = part('top:0;bottom:0;right:0;width:0;background:linear-gradient(to left, rgba(255,253,240,.55), rgba(255,253,240,.18) 55%, rgba(255,253,240,0));');
  const spill = part('right:0;bottom:calc(var(--dock-h, 0px) + env(safe-area-inset-bottom, 0px));height:46px;width:0;background:radial-gradient(ellipse 100% 100% at 100% 100%, rgba(255,252,236,.5), rgba(255,252,236,0) 70%);');
  const core = part('top:0;bottom:0;right:0;width:0;background:linear-gradient(to left, #fffefa, #fffdf2 55%, rgba(255,253,240,.7));box-shadow:0 0 18px 4px rgba(255,252,236,.55);');
  const edge = part('top:-2%;bottom:-2%;right:0;width:2px;background:rgba(42,33,48,.42);border-radius:2px;');
  const motes = part('top:0;bottom:0;right:0;width:0;overflow:hidden;');

  const SLIDE = 'width 1.4s cubic-bezier(.3,.7,.3,1), right 1.4s cubic-bezier(.3,.7,.3,1)';
  const setWidth = (w, slide) => {
    const t = slide ? SLIDE : 'none';
    for (const e of [glow, core, motes, spill, edge]) e.style.transition = t;
    core.style.width = `${w}px`;
    motes.style.width = `${w}px`;
    /* a phone's shop runs to the edge: a narrower halo there */
    glow.style.width = `${Math.round(LITE ? w * 1.8 + 6 : w * 3 + 14)}px`;
    spill.style.width = `${Math.round(LITE ? w * 4 + 16 : w * 6 + 30)}px`;
    edge.style.right = `${w}px`;
    root.dataset.width = String(w);
  };
  const target = () => doorWidth(buysAfter(ctx.state.order, ctx.item.id), LITE);

  let shown = 0;
  const open = (slide) => {
    const w = target();
    if (w === shown) return;
    shown = w;
    if (still()) { setWidth(w, false); if (slide) ctx.anim(root, [{ opacity: 0.55 }, { opacity: 1 }], { duration: 600, easing: 'ease-out' }); }
    else setWidth(w, slide);
  };
  if (fresh(ctx) && !still()) {
    /* it creaks open: from shut to its first width */
    setWidth(0, false);
    ctx.after(450, () => open(true));
  } else open(false);
  ctx.on('buy', () => open(true));

  /* dust in the light, very slow */
  const loops = [];
  if (!still()) {
    const n = LITE ? 2 : 4;
    for (let i = 0; i < n; i++) {
      const m = doc.createElement('div');
      const s = 2 + Math.round(Math.random() * 2);
      m.style.cssText = `position:absolute;left:${20 + Math.random() * 50}%;top:0;width:${s}px;height:${s}px;border-radius:50%;background:rgba(190,170,120,.55);`;
      motes.appendChild(m);
      const y0 = 20 + Math.random() * 70, ms = 14000 + Math.random() * 9000;
      loops.push(ctx.anim(m, [{ transform: `translate(0, ${y0}vh)`, opacity: 0 }, { transform: `translate(-2px, ${y0 - 8}vh)`, opacity: 1, offset: 0.3 }, { transform: `translate(1px, ${y0 - 16}vh)`, opacity: 1, offset: 0.7 }, { transform: `translate(0, ${y0 - 24}vh)`, opacity: 0 }], { duration: ms, delay: -Math.random() * ms, iterations: Infinity, easing: 'linear' }));
    }
  }
  /* Gentle switched on in the pause card: the dust goes from the resume on. */
  ctx.on('resume', () => { if (still() && loops.length) { for (const a of loops.splice(0)) { try { a.cancel(); } catch { /* gone */ } } motes.replaceChildren(); } });

  /* The Go Outside ending: the door swings wide into its white fade. Any other ending: the light just goes. */
  ctx.on('ending', (e) => {
    if (e?.id === 'outside' && !still()) {
      for (const el of [core, glow]) ctx.anim(el, [{ width: el.style.width }, { width: '100vw' }], { duration: 1300, easing: 'ease-in', fill: 'forwards' });
      ctx.anim(edge, [{ opacity: 1 }, { opacity: 0 }], { duration: 400, fill: 'forwards' });
    } else {
      ctx.anim(root, [{ opacity: 1 }, { opacity: e?.id === 'outside' ? 1 : 0 }], { duration: 600, fill: 'forwards' });
    }
  });
  return {};
}
