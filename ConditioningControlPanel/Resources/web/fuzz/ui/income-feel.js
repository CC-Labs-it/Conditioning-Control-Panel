/* Income is paid continuously by the sim. These little packets only show which widgets are working. */
const budgets = new WeakMap();

export function createTrailBudget() {
  let next = 0, live = 0;
  return {
    take(now) {
      if (now < next || live >= 3) return null;
      next = now + 850;
      live += 1;
      let returned = false;
      return () => { if (!returned) { returned = true; live -= 1; } };
    },
    get live() { return live; },
  };
}

/** One source per screen/dock, with a page-wide budget. Never pays, plays audio or consumes the game's RNG. */
export function incomeFeel(ctx, source) {
  if (!(ctx.item.fps > 0)) return;
  const doc = ctx.world.ownerDocument;
  let budget = budgets.get(doc);
  if (!budget) { budget = createTrailBudget(); budgets.set(doc, budget); }
  const active = new Set();
  let pulse = null;
  let disposed = false;
  const enabled = () => ['colour', 'confetti', 'glossy'].some((id) => ctx.has(id) && ctx.switches.isOn(id));
  const stop = () => {
    for (const p of [...active]) { p.animation.cancel(); p.finish(); }
    pulse?.cancel(); pulse = null;
  };
  ctx.cleanup(() => { disposed = true; stop(); });
  // The source index varies the cadence without drawing from gameplay randomness.
  const index = Math.max(0, ctx.state.order.indexOf(ctx.item.id));
  ctx.every(3600 + (index % 7) * 310, () => {
    if (ctx.gentle || ctx.reduced || ctx.paused || !(ctx.rates().fps > 0)) return;
    if (!enabled()) return;
    const el = source();
    if (!el?.isConnected || !el.getClientRects().length) return;
    const r = el.getBoundingClientRect(), target = ctx.parts.counter?.getBoundingClientRect();
    const { w, h } = ctx.bounds();
    if (!target?.width || !r.width || !r.height || r.bottom <= 0 || r.top >= h || r.right <= 0 || r.left >= w) return;
    const release = budget.take(ctx.state.clock);
    if (!release) return;
    const x = Math.max(8, Math.min(w - 8, r.left + r.width / 2));
    const y = Math.max(8, Math.min(h - 8, r.top + Math.min(20, r.height / 2)));
    const tx = target.left + target.width / 2, ty = target.top + target.height / 2;
    const dot = ctx.el('span', 'position:absolute;left:0;top:0;width:7px;height:7px;border-radius:2px;background:var(--niche);border:1px solid var(--niche-ink);box-shadow:0 0 7px var(--niche-2);pointer-events:none;', null, 'over');
    dot.className = 'income-packet';
    dot.setAttribute('aria-hidden', 'true');
    const transform = (px, py, scale) => `translate(${px}px,${py}px) rotate(45deg) scale(${scale})`;
    const animation = ctx.anim(dot, [
      { transform: transform(x, y, .3), opacity: 0 },
      { transform: transform(x, y - 10, 1.2), opacity: .9, offset: .18 },
      { transform: transform((x + tx) / 2, Math.min(y, ty) - 28, 1), opacity: .9, offset: .58 },
      { transform: transform(tx, ty, .3), opacity: 0 },
    ], { duration: 950, easing: 'cubic-bezier(.3,0,.65,1)' });
    const packet = { animation, finish() { dot.remove(); release(); active.delete(packet); } };
    active.add(packet);
    animation.finished.then(() => {
      packet.finish();
      if (!disposed && !ctx.gentle && !ctx.paused && ctx.parts.counter?.isConnected) {
        pulse?.cancel();
        const a = ctx.anim(ctx.parts.counter, [{ opacity: .75 }, { opacity: 1 }], { duration: 280 });
        pulse = a;
        const settled = () => { if (pulse === a) pulse = null; };
        a.finished.then(settled, settled);
      }
    }, packet.finish);
  });
  ctx.frame(() => { if ((active.size || pulse) && (ctx.gentle || ctx.reduced || !enabled())) stop(); });
}
