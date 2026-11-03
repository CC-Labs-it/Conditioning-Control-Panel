import { dealBox, fitPiece, restoreBox } from './toy-box-model.js';
import { sfx } from '../basics/fx.js';

const art = (id) => `assets/toy-box/${id}.png`;
const names = { pawn: 'Pawn', rook: 'Rook', knight: 'Knight', bishop: 'Bishop', queen: 'Queen', king: 'King' };

export function mountToyBox(ctx) {
  const doc = ctx.button.el.ownerDocument;
  const root = ctx.el('div', 'width:100%;max-width:260px;margin:auto;pointer-events:auto;', null, ctx.dock({ title: 'Toy Box' }));
  root.dataset.arcade = 'toy_box';
  ctx.css(`
    [data-arcade=toy_box] .tb-box{position:relative;padding:9px;border:2px solid var(--accent-deep);border-radius:12px;background:color-mix(in srgb,var(--niche-soft) 70%,var(--panel));box-shadow:0 4px 0 color-mix(in srgb,var(--niche) 35%,var(--panel));}
    [data-arcade=toy_box] .tb-holes,[data-arcade=toy_box] .tb-tray{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:7px;}
    [data-arcade=toy_box] .tb-hole{position:relative;min-height:82px;border:1px solid color-mix(in srgb,var(--ink) 15%,transparent);border-radius:8px;background:color-mix(in srgb,var(--ink) 8%,var(--panel));padding:2px;touch-action:manipulation;}
    [data-arcade=toy_box] .tb-hole::before{content:'';display:block;margin:auto;width:54px;height:76px;background:color-mix(in srgb,var(--ink) 65%,var(--panel));mask:var(--piece) center/contain no-repeat;-webkit-mask:var(--piece) center/contain no-repeat;}
    [data-arcade=toy_box] .tb-hole.filled::before{visibility:hidden;}
    [data-arcade=toy_box] img{width:54px;height:76px;object-fit:contain;pointer-events:none;user-select:none;}
    [data-arcade=toy_box] .tb-hole img{position:absolute;inset:2px;margin:auto;}
    [data-arcade=toy_box] .tb-tray{margin-top:12px;min-height:68px;grid-template-columns:repeat(4,minmax(0,1fr));gap:3px;}
    [data-arcade=toy_box] .tb-tray img{width:100%;max-width:44px;height:62px;}
    [data-arcade=toy_box] .tb-piece{min-height:68px;padding:2px;border:2px solid transparent;border-radius:8px;background:var(--panel);cursor:grab;touch-action:none;}
    [data-arcade=toy_box] .tb-piece.selected{border-color:var(--niche);background:var(--accent-soft);}
    [data-arcade=toy_box] .tb-piece:focus-visible,[data-arcade=toy_box] .tb-hole:focus-visible{outline:2px solid var(--ink);outline-offset:2px;}
    [data-arcade=toy_box] .tb-note{min-height:19px;margin:8px 0 0;text-align:center;font-size:12px;color:var(--ink);}
    [data-arcade=toy_box] .tb-lid{position:absolute;inset:0;display:grid;place-items:center;border-radius:9px;background:var(--niche);color:var(--niche-ink);font-weight:800;font-size:18px;pointer-events:none;transform-origin:50% 0;}
  `);
  const make = (tag, className, parent, text) => {
    const e = doc.createElement(tag); e.className = className;
    if (text != null) e.textContent = text;
    parent.appendChild(e); return e;
  };
  let state = restoreBox(ctx.store.get('box')) || dealBox(0, ctx.rng), selected = null, drag = null, lidAnimation = null, boxAnimation = null, lidMoves = false;
  const save = () => ctx.store.set('box', state);
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
  function closeBox(credited = null) {
    clearDrag(); choose(null);
    note.textContent = credited == null ? 'Next box' : `+${ctx.fmt(credited)} ${ctx.brand.currencyPlural}`;
    const lid = make('div', 'tb-lid', box, 'Packed');
    const calm = ctx.gentle || ctx.reduced;
    lidMoves = !calm;
    lidAnimation = ctx.anim(lid, calm ? [{ opacity: 0 }, { opacity: 1 }] : [{ transform: 'perspective(400px) rotateX(-95deg)', opacity: .4 }, { transform: 'perspective(400px) rotateX(0deg)', opacity: 1 }], { duration: calm ? 1200 : 400, easing: 'ease-out', fill: 'both' });
    if (!calm) ctx.after(700, () => {
      if (!ctx.gentle && !ctx.reduced) boxAnimation = ctx.anim(box, [{ transform: 'perspective(400px) rotateY(0)' }, { transform: 'perspective(400px) rotateY(90deg)' }], { duration: 350, easing: 'ease-in', fill: 'forwards' });
    });
    ctx.after(calm ? 1600 : 1100, () => {
      lidAnimation?.cancel(); lidAnimation = null;
      boxAnimation?.cancel(); boxAnimation = null;
      state = dealBox(state.round + 1, ctx.rng); save(); render();
      if (!ctx.gentle && !ctx.reduced) boxAnimation = ctx.anim(box, [{ transform: 'perspective(400px) rotateY(-90deg)' }, { transform: 'perspective(400px) rotateY(0)' }], { duration: 350, easing: 'ease-out' });
    });
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
      const credited = ctx.pay(Math.max(rates.fps, rates.click * 2, 2) * 12, 'toy_box', { x: r.left + r.width / 2, y: r.top + r.height / 2 });
      sfx(ctx, 'sfx/rank_settle.mp3', .3);
      closeBox(credited);
    } else note.textContent = `${state.placed.length} of ${state.pieces.length}`;
  }
  function render() {
    holes.replaceChildren(); tray.replaceChildren(); box.querySelector('.tb-lid')?.remove(); controls.clear(); slots.clear();
    choose(null);
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
        if (!drag.ghost) { drag.ghost = doc.createElement('img'); drag.ghost.src = art(id); drag.ghost.style.cssText = 'position:fixed;width:54px;height:76px;object-fit:contain;pointer-events:none;left:0;top:0;'; ctx.layer('over').appendChild(drag.ghost); }
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
  if (state.phase === 'closing') closeBox();
  ctx.on('pause', clearDrag); ctx.on('suspend', clearDrag);
  ctx.frame(() => {
    if (ctx.gentle || ctx.reduced) {
      if (lidAnimation && lidMoves) { lidAnimation.cancel(); lidAnimation = null; lidMoves = false; }
      if (boxAnimation) { boxAnimation.cancel(); boxAnimation = null; }
    }
  });
  ctx.cleanup(clearDrag);
  return { off: clearDrag };
}
