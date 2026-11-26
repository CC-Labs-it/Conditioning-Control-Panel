/* The Swipe Feed owns the tiles and all manual rewards. This upgrade only
 * requests a visual advance when the user has been viewing a decoded post. */
export function mountAutoScroll(ctx, feed) {
  const button = ctx.el('button', '', null, feed.fullscreen ? feed.hud : feed.footer);
  button.type = 'button';
  button.className = 'feed-auto';
  ctx.css('.feed-auto{display:inline-block;margin:2px 4px;padding:2px 6px;min-height:24px;border:1px solid currentColor;border-radius:5px;background:var(--panel);color:var(--ink);font:600 11px/1 system-ui;cursor:pointer;pointer-events:auto}.routes-full .feed-auto{position:absolute;right:6px;bottom:12px;}');
  let enabled = ctx.store.get('enabled') !== false;
  let lastClock, remaining;
  function restart() {
    lastClock = ctx.state.clock;
    remaining = 10000 + Math.floor(ctx.rng() * 5001);
  }
  function draw() {
    button.textContent = enabled ? 'Pause auto-scroll' : 'Resume auto-scroll';
  }
  button.addEventListener('pointerdown', event => event.stopPropagation());
  button.addEventListener('click', event => {
    event.stopPropagation();
    enabled = !enabled;
    ctx.store.set('enabled', enabled);
    restart(); draw();
  });
  feed.auto = {button, restart};
  restart(); draw();
  return {
    tick() {
      const now = ctx.state.clock, elapsed = Math.max(0, now - lastClock);
      lastClock = now;
      if (!enabled || !feed.autoCanCount()) return;
      remaining -= elapsed;
      if (remaining <= 0) feed.advanceAuto();
    },
    off() { feed.auto = null; },
  };
}
