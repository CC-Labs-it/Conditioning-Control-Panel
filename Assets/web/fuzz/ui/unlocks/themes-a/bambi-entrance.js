const CSS = `
  .bi-dome-entrance {
    position:absolute; inset:0; z-index:12; pointer-events:none; background:#000;
  }
  .bi-dome-controls {
    position:absolute; bottom:max(16px,calc(env(safe-area-inset-bottom,0px) + 10px));
    left:50%; transform:translateX(-50%); z-index:5;
    display:flex; flex-wrap:wrap; align-items:center; justify-content:center; gap:5px 14px;
    box-sizing:border-box; width:max-content; max-width:calc(100% - 24px);
    padding:7px 15px; border:1px solid #ffcee173; border-radius:22px;
    background:linear-gradient(135deg,#54283cde,#35192dd9); color:#fff0f6;
    box-shadow:0 2px 12px #30122540; font:500 12px/1.35 system-ui,sans-serif;
    user-select:none; cursor:default; pointer-events:auto; text-align:center; opacity:1;
    transition:opacity .6s ease;
  }
  .bi-dome-controls span { white-space:nowrap; }
  .bi-dome-controls b { color:#fff; font-weight:700; }
  .bi-dome-controls[data-idle] { opacity:.46; }
  .bi-dome-controls[data-idle]:focus { opacity:1; }
  .bi-dome-controls:focus-visible { outline:2px solid #ffdae9; outline-offset:3px; }
  @media (hover:hover) { .bi-dome-controls[data-idle]:hover { opacity:1; } }
  @media (max-width:580px) {
    .bi-dome-controls { padding:6px 10px; gap:3px 9px; font-size:11px; }
  }
  @media (pointer:coarse) {
    .bi-dome-stage .bambi-flight-controls { bottom:max(8%,calc(env(safe-area-inset-bottom,0px) + 65px)) !important; }
  }
  @media (prefers-reduced-motion:reduce) { .bi-dome-controls { transition:none; } }
`;

/** Mount before the first scene draw to bridge the black at the end of the absorb. */
export function createBubbleEntrance(ctx, stage) {
  ctx.css(CSS);
  const veil = ctx.el('div', '', null, stage);
  veil.className = 'bi-dome-entrance';
  veil.setAttribute('aria-hidden', 'true');
  const pill = ctx.el('div', '', null, stage);
  pill.className = 'bi-dome-controls';
  pill.setAttribute('role', 'note');
  pill.setAttribute('aria-label', 'Movement controls');
  pill.tabIndex = 0;
  const touch = stage.ownerDocument.defaultView.matchMedia('(pointer:coarse)').matches;
  const controls = touch
    ? [['Left thumb', 'to move'], ['Right thumb', 'to look'], ['↑ / ↓', 'for height']]
    : [['WASD', 'to move'], ['Move mouse', 'to look'], ['Space', 'to rise'], ['C', 'to dive']];
  for (const [key, action] of controls) {
    const group = ctx.el('span', '', null, pill);
    ctx.el('b', '', key, group);
    group.append(stage.ownerDocument.createTextNode(' ' + action));
  }
  let revealed = false, disposed = false, fade = null, cancelIdle = null;
  function reveal() {
    if (revealed || disposed) return;
    revealed = true;
    // The actual rendered scene shows progressively through this veil.
    fade = ctx.anim(veil, [
      { opacity:1, backgroundColor:'#000' },
      { opacity:.78, backgroundColor:'#3b122b', offset:.32 },
      { opacity:0, backgroundColor:'#501a35' },
    ], { duration:ctx.reduced || ctx.gentle ? 950 : 1650, easing:'ease-in-out', fill:'forwards' });
    fade.finished.then(() => veil.remove(), () => {});
    cancelIdle = ctx.after(5500, () => { pill.dataset.idle = ''; });
  }
  function dispose() {
    if (disposed) return;
    disposed = true;
    cancelIdle?.();
    fade?.cancel();
    veil.remove();
    pill.remove();
  }
  ctx.cleanup(dispose);
  return { reveal, dispose };
}
