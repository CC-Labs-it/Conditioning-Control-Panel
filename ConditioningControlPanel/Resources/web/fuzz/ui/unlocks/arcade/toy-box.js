import { advanceBox, BOX_WINDOW, boxPrizeSeconds, createToyBox, fitPiece, restoreBox } from './toy-box-model.js';
import { sfx } from '../basics/fx.js';

const art = (id) => `assets/toy-box/${id}.png`;
const names = { pawn: 'Pawn', rook: 'Rook', knight: 'Knight', bishop: 'Bishop', queen: 'Queen', king: 'King' };

export function mountToyBox(ctx) {
  const doc = ctx.button.el.ownerDocument;
  const root = ctx.el('div', 'width:100%;max-width:260px;margin:auto;pointer-events:auto;', null, ctx.dock({ title: 'Toy Box' }));
  root.dataset.arcade = 'toy_box';
  ctx.css(`
    [data-arcade=toy_box] .tb-box{position:relative;padding:6px;border:2px solid var(--accent-deep);border-radius:12px;background:color-mix(in srgb,var(--niche-soft) 70%,var(--panel));box-shadow:0 4px 0 color-mix(in srgb,var(--niche) 35%,var(--panel));}
    [data-arcade=toy_box] [hidden]{display:none!important;}
    [data-arcade=toy_box] .tb-holes,[data-arcade=toy_box] .tb-tray{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:5px;}
    [data-arcade=toy_box] .tb-hole{position:relative;min-height:82px;border:1px solid color-mix(in srgb,var(--ink) 15%,transparent);border-radius:8px;background:color-mix(in srgb,var(--ink) 8%,var(--panel));padding:2px;touch-action:manipulation;}
    [data-arcade=toy_box] .tb-hole::before{content:'';display:block;margin:auto;width:54px;height:76px;background:color-mix(in srgb,var(--ink) 65%,var(--panel));mask:var(--piece) center/contain no-repeat;-webkit-mask:var(--piece) center/contain no-repeat;}
    [data-arcade=toy_box] .tb-hole.filled::before{visibility:hidden;}
    [data-arcade=toy_box] img{width:54px;height:76px;object-fit:contain;pointer-events:none;user-select:none;}
    [data-arcade=toy_box] .tb-hole img{position:absolute;inset:2px;margin:auto;}
    [data-arcade=toy_box] .tb-tray{margin-top:6px;min-height:68px;grid-template-columns:repeat(4,minmax(0,1fr));gap:3px;}
    [data-arcade=toy_box] .tb-tray img{width:100%;max-width:44px;height:62px;}
    [data-arcade=toy_box] .tb-piece{min-height:68px;padding:2px;border:2px solid transparent;border-radius:8px;background:var(--panel);cursor:grab;touch-action:none;}
    [data-arcade=toy_box] .tb-piece.selected{border-color:var(--niche);background:var(--accent-soft);}
    [data-arcade=toy_box] .tb-piece:focus-visible,[data-arcade=toy_box] .tb-hole:focus-visible{outline:2px solid var(--ink);outline-offset:2px;}
    [data-arcade=toy_box] .tb-note{min-height:16px;margin:4px 0 0;text-align:center;font-size:11px;color:var(--ink);}
    [data-arcade=toy_box] .tb-lid{position:absolute;inset:0;display:grid;place-items:center;border-radius:9px;background:var(--niche);color:var(--niche-ink);font-weight:800;font-size:18px;pointer-events:none;transform-origin:50% 0;}
    [data-arcade=toy_box] .tb-box.closed{min-height:64px;}
    [data-arcade=toy_box] .tb-box.closed .tb-lid{background:linear-gradient(90deg,transparent 43%,color-mix(in srgb,var(--niche-2) 65%,white) 43% 57%,transparent 57%),linear-gradient(0deg,transparent 40%,color-mix(in srgb,var(--niche-2) 65%,white) 40% 60%,transparent 60%),var(--niche);}
    [data-arcade=toy_box] .tb-lid span{padding:5px 10px;background:var(--panel);color:var(--ink);border:1px solid var(--line);border-radius:8px;}
    [data-arcade=toy_box] .tb-clock{margin:0 0 3px;font-size:12px;font-weight:700;color:var(--ink);font-variant-numeric:tabular-nums;text-align:center;}
    [data-arcade=toy_box] .tb-window{height:4px;margin-bottom:5px;overflow:hidden;border-radius:3px;background:var(--panel);}
    [data-arcade=toy_box]:not([data-phase=play]) .tb-window{display:none;}
    [data-arcade=toy_box] .tb-window i{display:block;height:100%;background:linear-gradient(90deg,var(--niche),var(--niche-2));transform-origin:0 50%;}
    [data-arcade=toy_box][data-phase=play] .tb-box{outline:2px solid var(--niche-2);outline-offset:2px;}
    .dk-chip[data-fz-owner=toy_box].tb-ready{outline:2px solid var(--niche);outline-offset:-2px;background:var(--accent-soft);}
    .dk-chip[data-fz-owner=toy_box].tb-ready::after{content:'Open';font-size:10px;font-weight:800;}
    .dk[data-fz-owner=toy_box].tb-ready>.dk-t{background:var(--accent-soft);}
  `);
  const make = (tag, className, parent, text) => {
    const e = doc.createElement(tag); e.className = className;
    if (text != null) e.textContent = text;
    parent.appendChild(e); return e;
  };
  let state = restoreBox(ctx.store.get('box')) || createToyBox(ctx.rng), selected = null, drag = null, lidAnimation = null, boxAnimation = null, lidMoves = false;
  let lastClock = ctx.state.clock, shownClock = '', shownFill = '', flipped = false;
  const save = () => ctx.store.set('box', state);
  const clock = make('p', 'tb-clock', root), windowMeter = make('div', 'tb-window', root), windowFill = make('i', '', windowMeter);
  const box = make('div', 'tb-box', root), holes = make('div', 'tb-holes', box), tray = make('div', 'tb-tray', root);
  const note = make('p', 'tb-note', root, 'Match the shapes');
  note.setAttribute('aria-live', 'polite');
  const controls = new Map(), slots = new Map();
  function clearDrag() {
    if (drag) { drag.ghost?.remove(); try { drag.button.releasePointerCapture(drag.id); } catch { /* already released */ } }
    drag = null;
  }
  const choose = (id) => {
    selected = id;
    for (const [key, button] of controls) { button.classList.toggle('selected', key === id); button.setAttribute('aria-pressed', String(key === id)); }
    note.textContent = id ? `Find the ${names[id].toLowerCase()}` : 'Match the shapes';
  };
  function setReady(on) {
    for (const e of doc.querySelectorAll('.dk-chip[data-fz-owner=toy_box],.dk[data-fz-owner=toy_box]')) e.classList.toggle('tb-ready', on);
  }
  function paintClock() {
    const text = state.phase === 'play' ? `Open | ${state.windowLeft.toFixed(1)} s` : state.phase === 'closing' ? (state.outcome === 'packed' ? 'Packed' : 'Next time') : `Opens in ${Math.ceil(state.nextIn)} s`;
    if (shownClock !== text) { clock.textContent = text; shownClock = text; }
    const fill = `scaleX(${state.phase === 'play' ? (state.windowLeft / BOX_WINDOW).toFixed(3) : 0})`;
    if (fill !== shownFill) { windowFill.style.transform = fill; shownFill = fill; }
    windowMeter.style.visibility = state.phase === 'play' ? 'visible' : 'hidden';
  }
  function closeBox(credited = null, animate = true) {
    clearDrag(); choose(null);
    boxAnimation?.cancel(); boxAnimation = null; flipped = false;
    setReady(false); root.dataset.phase = state.phase;
    note.textContent = credited == null ? 'A fresh box next time' : `+${ctx.fmt(credited)} ${ctx.brand.currencyPlural}`;
    box.querySelector('.tb-lid')?.remove();
    const lid = make('div', 'tb-lid', box); make('span', '', lid, state.outcome === 'packed' ? 'Packed' : 'Toy Box');
    if (!animate) return;
    const calm = ctx.gentle || ctx.reduced;
    lidMoves = !calm;
    lidAnimation = ctx.anim(lid, calm ? [{ opacity: 0 }, { opacity: 1 }] : [{ transform: 'perspective(400px) rotateX(-95deg)', opacity: .4 }, { transform: 'perspective(400px) rotateX(0deg)', opacity: 1 }], { duration: calm ? 1200 : 400, easing: 'ease-out', fill: 'both' });
    paintClock();
  }
  function place(piece, hole) {
    if (ctx.paused) return;
    const result = fitPiece(state, piece, hole);
    if (!result.fitted) { note.textContent = 'Try another shape'; return; }
    state = result.box; save(); choose(null);
    const button = controls.get(piece), slot = slots.get(hole);
    button.style.visibility = 'hidden'; button.disabled = true;
    slot.classList.add('filled'); slot.disabled = true;
    const img = make('img', '', slot); img.src = art(piece); img.alt = '';
    if (!ctx.gentle && !ctx.reduced) ctx.anim(img, [{ scale: '1.15', opacity: .4 }, { scale: '1', opacity: 1 }], { duration: 220, easing: 'ease-out' });
    if (result.complete) {
      const rates = ctx.rates(), r = root.getBoundingClientRect();
      const credited = ctx.pay(Math.max(rates.fps, rates.click * 2, 2) * boxPrizeSeconds(state.completed - 1), 'toy_box', { x: r.left + r.width / 2, y: r.top + r.height / 2 });
      sfx(ctx, 'sfx/rank_settle.mp3', .3);
      closeBox(credited);
    } else note.textContent = `${state.placed.length} of ${state.pieces.length}`;
  }
  function render() {
    lidAnimation?.cancel(); lidAnimation = null; boxAnimation?.cancel(); boxAnimation = null;
    flipped = false;
    clearDrag(); root.dataset.phase = state.phase; setReady(state.phase === 'play');
    holes.replaceChildren(); tray.replaceChildren(); box.querySelector('.tb-lid')?.remove(); controls.clear(); slots.clear();
    choose(null);
    const shut = state.phase !== 'play';
    holes.hidden = shut; tray.hidden = shut; box.classList.toggle('closed', shut);
    paintClock();
    if (shut) {
      const lid = make('div', 'tb-lid', box); make('span', '', lid, state.outcome === 'packed' ? 'Packed' : 'Toy Box');
      note.textContent = `${state.completed} packed | Next prize x${(boxPrizeSeconds(state.completed) / 12).toFixed(2)}`;
      return;
    }
    for (const id of state.pieces) {
      const b = make('button', 'tb-hole', holes); b.type = 'button'; b.dataset.toySlot = id;
      b.style.setProperty('--piece', `url("${art(id)}")`); b.setAttribute('aria-label', `${names[id]} shape`);
      b.addEventListener('click', () => { if (selected && !ctx.paused) place(selected, id); });
      if (state.placed.includes(id)) { b.classList.add('filled'); b.disabled = true; const img = make('img', '', b); img.src = art(id); img.alt = ''; }
      slots.set(id, b);
    }
    for (const id of state.tray) {
      const b = make('button', 'tb-piece', tray); b.type = 'button'; b.dataset.toyPiece = id; b.setAttribute('aria-label', names[id]);
      const img = make('img', '', b); img.src = art(id); img.alt = ''; img.draggable = false;
      if (state.placed.includes(id)) { b.style.visibility = 'hidden'; b.disabled = true; }
      controls.set(id, b);
      b.addEventListener('click', (e) => { if (e.detail === 0 && !ctx.paused && state.phase === 'play') choose(id); });
      b.addEventListener('pointerdown', (e) => {
        if (ctx.paused || state.phase !== 'play' || e.button > 0) return;
        e.preventDefault(); clearDrag(); choose(id); b.setPointerCapture(e.pointerId);
        drag = { id: e.pointerId, piece: id, button: b, x: e.clientX, y: e.clientY, moved: false, ghost: null };
      });
      b.addEventListener('pointermove', (e) => {
        if (!drag || drag.id !== e.pointerId || ctx.paused) return;
        if (!drag.moved && Math.hypot(e.clientX - drag.x, e.clientY - drag.y) < 6) return;
        drag.moved = true;
        if (!drag.ghost) { drag.ghost = doc.createElement('img'); drag.ghost.src = art(id); drag.ghost.style.cssText = 'position:fixed;width:54px;height:76px;object-fit:contain;pointer-events:none;left:0;top:0;'; ctx.layer('fx').appendChild(drag.ghost); }
        drag.ghost.style.transform = `translate(${e.clientX - 27}px,${e.clientY - 38}px)`;
      });
      b.addEventListener('pointerup', (e) => {
        if (!drag || drag.id !== e.pointerId) return;
        const moving = drag.moved; clearDrag();
        if (!moving || ctx.paused) return;
        const hit = [...slots].find(([, el]) => { const r = el.getBoundingClientRect(); return e.clientX >= r.left - 6 && e.clientX <= r.right + 6 && e.clientY >= r.top - 6 && e.clientY <= r.bottom + 6; });
        if (hit) place(id, hit[0]);
      });
      b.addEventListener('pointercancel', clearDrag);
    }
  }
  save(); render();
  ctx.on('pause', clearDrag); ctx.on('suspend', clearDrag);
  ctx.frame(() => {
    /* The HUD clock is active real game time, so unrelated speed upgrades cannot shorten the window. */
    const elapsed = Math.max(0, ctx.state.clock - lastClock) / 1000;
    lastClock = ctx.state.clock;
    const before = state.phase, result = advanceBox(state, elapsed, ctx.rng);
    state = result.box; save();
    if (result.timedOut && state.phase === 'closing') { closeBox(); holes.hidden = true; tray.hidden = true; box.classList.add('closed'); }
    else if (before !== state.phase || result.opened) render();
    if (result.opened) {
      note.textContent = 'Match the shapes';
      sfx(ctx, 'sfx/chime1.mp3', .16);
      if (!ctx.gentle && !ctx.reduced) boxAnimation = ctx.anim(box, [{ scale: '.96' }, { scale: '1.025', offset: .5 }, { scale: '1' }], { duration: 340, easing: 'ease-out' });
    }
    if (state.phase === 'closing' && state.outcome === 'packed' && state.flipLeft <= .4 && !flipped && !ctx.gentle && !ctx.reduced) {
      flipped = true;
      boxAnimation = ctx.anim(box, [{ transform: 'perspective(400px) rotateY(0)' }, { transform: 'perspective(400px) rotateY(90deg)' }], { duration: 350, easing: 'ease-in', fill: 'forwards' });
    }
    paintClock();
    if (ctx.gentle || ctx.reduced) {
      if (lidAnimation && lidMoves) { lidAnimation.cancel(); lidAnimation = null; lidMoves = false; }
      if (boxAnimation) { boxAnimation.cancel(); boxAnimation = null; }
    }
  });
  const off = () => { clearDrag(); setReady(false); };
  ctx.cleanup(off);
  return { off };
}
