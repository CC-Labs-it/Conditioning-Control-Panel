/* A collected bank becomes a short ascending phrase, then one receipt lands at the counter.
 * Payment already happened. This show never owns money and can be interrupted freely. */
import { docOf, sfx } from './fx.js';
import { frameRect } from '../../perf.js';

export function collectionBeats(levels) {
  const count = Math.min(6, Math.max(1, Math.floor(Number.isFinite(levels) ? levels : 1)));
  return Array.from({ length: count }, (_, i) => ({ at: i * 120, note: [3, 5, 7, 10, 12, 15][i] }));
}

export function createCollectionFeel(ctx) {
  const nodes = new Set(), anims = new Set(), timers = new Set();
  let receipt = null, calm = ctx.gentle || ctx.reduced, current = null;
  const stop = () => {
    for (const cancel of timers) cancel();
    timers.clear();
    for (const a of anims) a.cancel();
    anims.clear();
    for (const e of nodes) e.remove();
    nodes.clear();
    receipt = null; current = null;
  };
  const after = (ms, fn) => {
    const cancel = ctx.after(ms, () => { timers.delete(cancel); fn(); });
    timers.add(cancel);
  };
  const make = (css, text) => {
    const e = docOf(ctx).createElement('div');
    e.style.cssText = `position:absolute;left:0;top:0;pointer-events:none;${css}`;
    if (text != null) e.textContent = text;
    ctx.layer('over').appendChild(e); nodes.add(e);
    return e;
  };
  const animate = (e, frames, opts, remove = false) => {
    const a = ctx.anim(e, frames, opts);
    anims.add(a);
    a.onfinish = a.oncancel = () => { anims.delete(a); if (remove) { nodes.delete(e); e.remove(); } };
    return a;
  };
  function show({ levels, amount, x, y }) {
    stop();
    calm = ctx.gentle || ctx.reduced;
    current = { levels, amount, x, y };
    const { w, h } = ctx.bounds();
    x = Math.max(78, Math.min(w - 78, x));
    y = Math.max(70, Math.min(h - 60, y + 25));
    const size = Math.min(1, (levels - 1) / 5);
    receipt = make(`transform:translate(${x}px,${y}px) translate(-50%,-50%);padding:7px 12px;border:1px solid var(--niche);border-radius:12px;background:var(--toast-bg);color:var(--toast-ink);font-weight:800;font-size:${18 + size * 6}px;white-space:nowrap;box-shadow:0 3px 0 color-mix(in srgb,var(--niche) 25%,transparent);`, `+${ctx.fmt(amount)}`);
    receipt.dataset.collectionReceipt = '';
    if (calm) {
      /* At the maximum 4x clock, each fade still takes at least 250 ms. */
      animate(receipt, [{ opacity: 0 }, { opacity: 1, offset: .45 }, { opacity: 1, offset: .55 }, { opacity: 0 }], { duration: 2400 }, true);
      sfx(ctx, 'pluck/wood_10.wav', .35);
      after(2400, () => { current = null; receipt = null; });
      return;
    }
    animate(receipt, [{ opacity: 0, scale: '.85' }, { opacity: 1, scale: '1.05', offset: .65 }, { opacity: 1, scale: '1' }], { duration: 220, easing: 'ease-out' });
    const beats = collectionBeats(levels);
    for (const [i, beat] of beats.entries()) after(beat.at, () => {
      if (ctx.gentle || ctx.reduced || !receipt) return;
      sfx(ctx, `pluck/wood_${String(beat.note).padStart(2, '0')}.wav`, .32 + size * .12);
      for (let j = 0; j < 5; j++) {
        const angle = ((j / 5) * Math.PI * 2) + i * .35;
        const reach = 25 + i * 8;
        const e = make(`width:5px;height:5px;border-radius:1px;background:var(${j % 2 ? '--niche-2' : '--niche'});`);
        animate(e, [{ transform: `translate(${x}px,${y}px) rotate(45deg) scale(.5)`, opacity: 0 }, { opacity: 1, offset: .15 }, { transform: `translate(${x + Math.cos(angle) * reach}px,${y + Math.sin(angle) * reach}px) rotate(135deg) scale(.2)`, opacity: 0 }], { duration: 440, easing: 'ease-out' }, true);
      }
    });
    after(beats.at(-1).at + 220, () => {
      if (!receipt || ctx.gentle || ctx.reduced) return;
      const r = frameRect(ctx.parts.counter);
      const tx = r.left + r.width / 2, ty = r.top + r.height / 2;
      animate(receipt, [{ transform: `translate(${x}px,${y}px) translate(-50%,-50%)`, opacity: 1 }, { transform: `translate(${tx}px,${ty}px) translate(-50%,-50%) scale(.65)`, opacity: 0 }], { duration: 420, easing: 'cubic-bezier(.5,0,.8,.4)' }, true);
      after(420, () => {
        if (ctx.gentle || ctx.reduced) return;
        animate(ctx.parts.counter, [{ scale: '1' }, { scale: `${1.05 + size * .03}`, offset: .3 }, { scale: '1' }], { duration: 320, easing: 'ease-out' });
        sfx(ctx, 'sfx/rank_settle.mp3', .35);
        receipt = null; current = null;
      });
    });
  }
  ctx.frame(() => {
    const next = ctx.gentle || ctx.reduced;
    if (next && !calm && current) show(current);
    calm = next;
  });
  ctx.cleanup(stop);
  return { show };
}
