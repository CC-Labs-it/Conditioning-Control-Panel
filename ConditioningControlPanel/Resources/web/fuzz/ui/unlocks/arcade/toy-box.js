import { advanceBox, BOX_FLIP, BOX_PRIZE_BASE, boxPerks, boxPrizeMult, boxPrizeSeconds, boxWindow, createToyBox, fitPiece, restoreBox, toyBoxRank } from './toy-box-model.js';
import { sfx } from '../basics/fx.js';
import { autopilotShare, peekRate, prizeMult } from '../prestige/boosts.js';  // prestige hooks (Renewal Sky, boosts lane)
import { lostKeyRate } from '../../../sim/prestige-endings.js';  // prestige hook (Renewal Sky, ending stars)

const art = (id) => `assets/toy-box/${id}.png`;
const names = { pawn: 'Pawn', rook: 'Rook', knight: 'Knight', bishop: 'Bishop', queen: 'Queen', king: 'King' };
// Journey rank 1 Golden Piece: the same approved piece art, tinted gold (no new art). A still tint, so Gentle needs nothing.
const GOLD_FILTER = 'sepia(1) saturate(3.2) hue-rotate(-8deg) brightness(1.08) drop-shadow(0 0 2px #fff6c8) drop-shadow(0 0 4px #e8b021)';

// Bounds of the visible piece in each 180 x 240 capture. Ignore the transparent
// camera margins when fitting both the silhouette and the placed piece.
const pieceBounds = {
  pawn: [31, 26, 118, 189], rook: [38, 20, 109, 201], knight: [33, 20, 99, 201],
  bishop: [45, 20, 93, 201], queen: [60, 20, 67, 201], king: [50, 19, 81, 202],
};
function shapeImage(doc, id) {
  const ns = 'http://www.w3.org/2000/svg';
  const svg = doc.createElementNS(ns, 'svg');
  svg.setAttribute('viewBox', pieceBounds[id].join(' '));
  svg.setAttribute('class', 'tb-shape');
  svg.setAttribute('aria-hidden', 'true');
  const image = doc.createElementNS(ns, 'image');
  image.setAttribute('href', art(id));
  image.setAttribute('width', '180');
  image.setAttribute('height', '240');
  svg.appendChild(image);
  return svg;
}

export function mountToyBox(ctx) {
  const doc = ctx.button.el.ownerDocument;
  const dock = ctx.dock({ title: 'Toy Box', width: 282, reserveHeight: 315, onMin(hidden) { if (hidden) clearDrag(); } });
  const root = ctx.el('div', 'width:100%;max-width:282px;margin:auto;pointer-events:auto;', null, dock);
  root.dataset.dockSurface = '';
  root.dataset.arcade = 'toy_box';
  ctx.css(`
    [data-arcade=toy_box] [hidden]{display:none!important;}
    [data-arcade=toy_box].tb-flipping{overflow:clip;}
    [data-arcade=toy_box] .tb-box{position:relative;width:100%;aspect-ratio:282/174;background:url("assets/glossy/toy-box-open.webp") center/100% 100% no-repeat;}
    [data-arcade=toy_box] .tb-holes{position:absolute;inset:13% 8% 15%;display:grid;grid-auto-flow:column;grid-auto-columns:minmax(0,1fr);gap:6px;}
    [data-arcade=toy_box] .tb-hole,[data-arcade=toy_box] .tb-piece{position:relative;display:grid;place-items:center;box-sizing:border-box;min-width:0;min-height:0;border:0;border-radius:0;padding:0;background:none;box-shadow:none;opacity:1;touch-action:manipulation;}
    [data-arcade=toy_box] .tb-shape{display:block;width:100%;height:100%;min-width:0;min-height:0;pointer-events:none;user-select:none;}
    [data-arcade=toy_box] .tb-hole:not(.filled) .tb-shape{filter:brightness(0) saturate(100%) invert(28%) sepia(16%) saturate(750%) hue-rotate(344deg) drop-shadow(0 1px .5px #fff9);}
    [data-arcade=toy_box] .tb-hole.filled .tb-shape{filter:drop-shadow(0 2px 1px #513a3d40);transform-origin:center 80%;}
    [data-arcade=toy_box] .tb-tray{display:grid;grid-auto-flow:column;grid-auto-columns:minmax(0,1fr);gap:4px;margin:5px 5% 0;}
    [data-arcade=toy_box] .tb-piece{justify-self:center;width:100%;max-width:94px;height:86px;cursor:grab;touch-action:none;}
    [data-arcade=toy_box] .tb-piece.carrying .tb-shape{visibility:hidden;}
    [data-arcade=toy_box] .tb-piece.selected .tb-shape{filter:drop-shadow(0 0 2px #fff) drop-shadow(0 0 4px var(--accent));}
    [data-arcade=toy_box] .tb-piece.gold .tb-shape,[data-arcade=toy_box] .tb-hole.gold.filled .tb-shape{filter:${GOLD_FILTER};}
    [data-arcade=toy_box] .tb-piece.gold.selected .tb-shape{filter:${GOLD_FILTER} drop-shadow(0 0 3px var(--accent));}
    [data-arcade=toy_box] .tb-hole.gold:not(.filled) .tb-shape{filter:brightness(0) saturate(100%) invert(28%) sepia(16%) saturate(750%) hue-rotate(344deg) drop-shadow(0 0 1.5px #f2c23a) drop-shadow(0 0 3px #e8b021);}
    [data-arcade=toy_box] .tb-piece:focus-visible,[data-arcade=toy_box] .tb-hole:focus-visible{outline:2px solid var(--ink);outline-offset:2px;}
    [data-arcade=toy_box] .tb-dust{position:absolute;pointer-events:none;border-radius:50%;background:#fff3d7;box-shadow:0 .6px 0 #926f54;}
    [data-arcade=toy_box] .tb-note{min-height:14px;margin:3px 0 0;text-align:center;font-size:11px;color:var(--ink);}
    [data-arcade=toy_box] .tb-lid{position:absolute;left:5.5%;top:11%;width:89%;aspect-ratio:985/536;background:url("assets/glossy/toy-box-lid.webp") center/contain no-repeat;pointer-events:none;transform-origin:50% 0;}
    [data-arcade=toy_box] .tb-lid span{position:absolute;top:100%;left:50%;transform:translateX(-50%);padding:1px 6px;border-radius:6px;background:var(--panel);color:var(--ink);font-weight:700;font-size:10px;white-space:nowrap;}
    [data-arcade=toy_box] .tb-clock{margin:0 0 3px;font-size:12px;font-weight:700;color:var(--ink);font-variant-numeric:tabular-nums;text-align:center;}
    [data-arcade=toy_box] .tb-window{height:4px;margin-bottom:5px;overflow:hidden;border-radius:3px;background:var(--panel);}
    [data-arcade=toy_box] .tb-window i{display:block;height:100%;background:linear-gradient(90deg,var(--niche),var(--niche-2));transform-origin:0 50%;}
  `);
  const make = (tag, className, parent, text) => {
    const e = doc.createElement(tag); e.className = className;
    if (text != null) e.textContent = text;
    parent.appendChild(e); return e;
  };
  let state = restoreBox(ctx.store.get('box')) || createToyBox(ctx.rng), selected = null, drag = null, boxAnimation = null;
  let lastClock = ctx.state.clock, shownClock = '', shownFill = '';
  const save = () => ctx.store.set('box', state);
  const clock = make('p', 'tb-clock', root), windowMeter = make('div', 'tb-window', root), windowFill = make('i', '', windowMeter);
  const box = make('div', 'tb-box', root), holes = make('div', 'tb-holes', box), tray = make('div', 'tb-tray', root);
  const lid = make('div', 'tb-lid', box), lidLabel = make('span', '', lid);
  const note = make('p', 'tb-note', root, 'Match the shapes');
  note.setAttribute('aria-live', 'polite');
  const controls = new Map(), slots = new Map();
  function clearDrag() {
    const held = drag;
    drag = null;
    if (!held) return;
    held.ghost?.remove();
    held.button.classList.remove('carrying');
    if (held.button.hasPointerCapture(held.id)) held.button.releasePointerCapture(held.id);
  }

  function paintCarry() {
    const d = drag;
    if (!d?.ghost) return;
    d.ghost.style.transform = `translate(${d.x-d.grabX}px,${d.y-d.grabY}px) rotate(${d.angle.toFixed(2)}deg)`;
  }

  function swingCarry(dt) {
    const d = drag;
    if (!d?.ghost || dt <= 0) return;
    if (ctx.gentle || ctx.reduced) {
      d.angle = d.angularSpeed = d.velocity = 0;
    } else {
      // Arcademy EMI uses 4 degrees per horizontal px/ms, 20% smoothing and a 6 degree cap.
      const velocity = (d.x-d.sampleX)/(dt*1000);
      d.velocity += (velocity-d.velocity)*(1-Math.pow(.8, dt*60));
      const target = Math.max(-6, Math.min(6, -4*d.velocity));
      // A damped pendulum about the actual grab point, integrated independently of frame rate.
      const error = d.angle-target, sin = Math.sin(17*dt), cos = Math.cos(17*dt), decay = Math.exp(-11*dt);
      const arc = (d.angularSpeed + 11*error)/17;
      d.angle = Math.max(-6, Math.min(6, target + decay*(error*cos + arc*sin)));
      d.angularSpeed = decay*(d.angularSpeed*cos - (17*error + 11*arc)*sin);
    }
    d.sampleX = d.x;
    paintCarry();
  }
  const choose = (id) => {
    selected = id;
    for (const [key, button] of controls) { button.classList.toggle('selected', key === id); button.setAttribute('aria-pressed', String(key === id)); }
    note.textContent = id ? `Find the ${names[id].toLowerCase()}` : 'Match the shapes';
  };
  const lidName = () => state.outcome === 'packed' ? 'Packed' : toyBoxRank(ctx.state) >= 2 ? 'Big Box' : 'Toy Box';
  function setReady(on) {
    dock.dock?.setReady(on, 'Box open: match the pieces');
  }
  function paintClock() {
    const text = state.phase === 'play' ? `Open | ${state.windowLeft.toFixed(1)} s` : state.phase === 'closing' ? (state.outcome === 'packed' ? 'Packed' : 'Next time') : `Opens in ${Math.ceil(state.nextIn)} s`;
    if (shownClock !== text) { clock.textContent = text; shownClock = text; }
    const fill = `scaleX(${state.phase === 'play' ? (state.windowLeft / boxWindow(state)).toFixed(3) : 0})`;
    if (fill !== shownFill) { windowFill.style.transform = fill; shownFill = fill; }
    windowMeter.style.visibility = state.phase === 'play' ? 'visible' : 'hidden';
  }
  function paintClosing() {
    // Sample the same active clock that ends the round, including when resuming a saved flip.
    const elapsed = BOX_FLIP - state.flipLeft;
    const calm = ctx.gentle || ctx.reduced;
    const packed = state.outcome === 'packed';
    const progress = (start, duration) => Math.max(0, Math.min(1, (elapsed - start) / duration));
    const closing = 1 - (1 - progress(packed ? .12 : 0, .32)) ** 3;
    lid.style.opacity = String(closing);
    lid.style.transform = calm || closing === 1 ? '' : `perspective(400px) rotateX(${-90 * (1 - closing)}deg)`;

    const t = packed && !calm ? progress(.40, .65) : 0;
    const turn = t * t * (3 - 2 * t);
    const lift = -8 * Math.sin(Math.PI * t) ** 2;
    // The near edge of a turning box projects past its own width: shrink it a little mid-turn and clip the toy while it turns,
    // so the dock never grows scrollbars for a frame (owner 2026-11-07).
    const flipping = t > 0 && t < 1, squeeze = 1 - .07 * Math.sin(Math.PI * t);
    box.style.transform = flipping ? `perspective(550px) translateY(${lift}px) rotateY(${360 * turn}deg) scale(${squeeze.toFixed(3)})` : '';
    root.classList.toggle('tb-flipping', flipping);
    lidLabel.style.transform = turn > .25 && turn < .75 ? 'translateX(-50%) scaleX(-1)' : '';
  }
  function closeBox(credited = null) {
    clearDrag(); choose(null);
    boxAnimation?.cancel(); boxAnimation = null;
    setReady(false); root.dataset.phase = state.phase;
    holes.hidden = state.outcome !== 'packed'; tray.hidden = true;
    lid.hidden = false; lidLabel.textContent = lidName();
    note.textContent = credited == null ? 'A fresh box next time' : `+${ctx.fmt(credited)} ${ctx.brand.currencyPlural}`;
    paintClosing();
    paintClock();
  }
  const fitAnimations = new Set();
  function fitAnimation(el, frames, duration, done) {
    const animation = ctx.anim(el, frames, { duration, easing: 'ease-out' });
    fitAnimations.add(animation);
    const finish = () => { fitAnimations.delete(animation); done?.(); };
    animation.finished.then(finish, finish);
  }
  function settlePiece(slot, piece, tilt) {
    sfx(ctx, 'sfx/thud.mp3', .18);
    if (ctx.gentle || ctx.reduced) return;
    fitAnimation(slot.querySelector('.tb-shape'), [
      { transform: `translateY(-4px) rotate(${tilt || -3}deg)` },
      { transform: 'translateY(1px) rotate(2deg) scale(1.035,.96)', offset: .28 },
      { transform: 'rotate(-.85deg)', offset: .66 },
      { transform: 'none' },
    ], 270);
    const [, , width, height] = pieceBounds[piece];
    const fit = Math.min(slot.clientWidth / width, slot.clientHeight / height);
    const floor = (slot.clientHeight + height * fit) / 2 - 1;
    for (let i = 0; i < 6; i++) {
      const side = i % 2 ? 1 : -1, reach = 5 + (i >> 1) * 3;
      const grain = make('i', 'tb-dust', slot);
      grain.style.left = `${(slot.clientWidth + side * width * fit * .48) / 2}px`;
      grain.style.top = `${floor}px`;
      grain.style.width = grain.style.height = `${1.5 + (i % 3) * .55}px`;
      fitAnimation(grain, [
        { transform: 'translate(0,0) scale(.7)', opacity: 0 },
        { transform: `translate(${side * reach * .45}px,${-3-i%3}px)`, opacity: .75, offset: .3 },
        { transform: `translate(${side * reach}px,1px) scale(.4)`, opacity: 0 },
      ], 280 + (i % 3) * 32, () => grain.remove());
    }
  }
  function place(piece, hole, tilt = 0) {
    if (ctx.paused) return;
    const result = fitPiece(state, piece, hole);
    if (!result.fitted) { note.textContent = 'Try another shape'; return; }
    state = result.box; save(); choose(null);
    const button = controls.get(piece), slot = slots.get(hole);
    button.style.visibility = 'hidden'; button.disabled = true;
    slot.classList.add('filled'); slot.disabled = true;
    settlePiece(slot, piece, tilt);
    if (result.complete) {
      ctx.emit('sticker', { key: 'toybox' });   // Sticker Book: Checkmate
      const rates = ctx.rates(), r = root.getBoundingClientRect();
      // Journey ranks: Golden Piece doubles a box once its gold piece is in, Big Box pays 1.5x (boxPrizeMult).
      const credited = ctx.pay(Math.max(rates.fps, rates.click * 2, 2) * boxPrizeSeconds(state.completed - 1) * boxPrizeMult(state) * prizeMult(ctx.prestige), 'toy_box', { x: r.left + r.width / 2, y: r.top + r.height / 2 });
      ctx.after(180, () => sfx(ctx, 'sfx/rank_settle.mp3', .22));
      closeBox(credited);
    } else note.textContent = `${state.placed.length} of ${state.pieces.length}${piece === state.gold ? ' | Gold piece in' : ''}`;
    if (piece === state.gold) sfx(ctx, 'sfx/chime1.mp3', .14);
  }
  function render() {
    boxAnimation?.cancel(); boxAnimation = null;
    box.style.transform = lid.style.transform = lid.style.opacity = lidLabel.style.transform = '';
    clearDrag(); root.dataset.phase = state.phase; setReady(state.phase === 'play');
    holes.replaceChildren(); tray.replaceChildren(); controls.clear(); slots.clear();
    choose(null);
    const shut = state.phase !== 'play';
    const closingPacked = state.phase === 'closing' && state.outcome === 'packed';
    holes.hidden = shut && !closingPacked; tray.hidden = shut; lid.hidden = !shut;
    box.classList.toggle('closed', shut);
    paintClock();
    if (shut) {
      lidLabel.textContent = lidName();
      note.textContent = `${state.completed} packed | Next prize x${(boxPrizeSeconds(state.completed) / BOX_PRIZE_BASE).toFixed(2)}`;
      if (state.phase === 'closing') paintClosing();
      if (!closingPacked) return;
    }
    for (const id of state.pieces) {
      const b = make('button', 'tb-hole', holes); b.type = 'button'; b.dataset.toySlot = id;
      b.appendChild(shapeImage(doc, id)); b.setAttribute('aria-label', `${names[id]} shape`);
      b.addEventListener('click', () => { if (selected && !ctx.paused) place(selected, id); });
      if (state.placed.includes(id)) { b.classList.add('filled'); b.disabled = true; }
      if (state.gold === id) b.classList.add('gold');
      slots.set(id, b);
    }
    if (shut) return;
    for (const id of state.tray) {
      const b = make('button', 'tb-piece', tray); b.type = 'button'; b.dataset.dockInteractive = ''; b.dataset.toyPiece = id; b.setAttribute('aria-label', state.gold === id ? `Gold ${names[id].toLowerCase()}, doubles this box` : names[id]);
      if (state.gold === id) b.classList.add('gold');
      b.appendChild(shapeImage(doc, id));
      if (state.placed.includes(id)) { b.style.visibility = 'hidden'; b.disabled = true; }
      controls.set(id, b);
      b.addEventListener('click', (e) => { if (e.detail === 0 && !ctx.paused && state.phase === 'play') choose(id); });
      b.addEventListener('pointerdown', (e) => {
        if (drag || ctx.paused || state.phase !== 'play' || e.button > 0 || !e.isPrimary) return;
        e.preventDefault(); choose(id); b.setPointerCapture(e.pointerId);
        const rect = b.getBoundingClientRect();
        drag = {
          id: e.pointerId, piece: id, button: b, startX: e.clientX, startY: e.clientY,
          x: e.clientX, y: e.clientY, grabX: e.clientX-rect.left, grabY: e.clientY-rect.top,
          width: rect.width, height: rect.height, sampleX: e.clientX, velocity: 0, angle: 0,
          angularSpeed: 0, moved: false, ghost: null,
        };
      });
      b.addEventListener('pointermove', (e) => {
        if (!drag || drag.id !== e.pointerId || ctx.paused) return;
        if (!drag.moved && Math.hypot(e.clientX-drag.startX, e.clientY-drag.startY) < 6) return;
        drag.moved = true; drag.x = e.clientX; drag.y = e.clientY;
        if (!drag.ghost) {
          drag.ghost = shapeImage(doc, id);
          drag.ghost.classList.add('tb-held-piece');
          if (state.gold === id) drag.ghost.style.filter = GOLD_FILTER;
          drag.ghost.style.cssText = `position:fixed;left:0;top:0;width:${drag.width}px;height:${drag.height}px;transform-origin:${drag.grabX}px ${drag.grabY}px;pointer-events:none;`;
          ctx.layer('fx').appendChild(drag.ghost);
          b.classList.add('carrying');
        }
        paintCarry();
      });
      b.addEventListener('pointerup', (e) => {
        if (!drag || drag.id !== e.pointerId) return;
        const moving = drag.moved, tilt = drag.angle; clearDrag();
        if (!moving || ctx.paused) return;
        const within = (el, padding) => {
          const r = el.getBoundingClientRect();
          return e.clientX >= r.left - padding && e.clientX <= r.right + padding && e.clientY >= r.top - padding && e.clientY <= r.bottom + padding;
        };
        const hit = [...slots].find(([, el]) => within(el, 0)) || [...slots].find(([, el]) => within(el, 4));
        if (hit) place(id, hit[0], tilt);
      });
      const cancelPointer = e => { if (drag?.id === e.pointerId) clearDrag(); };
      b.addEventListener('pointercancel', cancelPointer);
      b.addEventListener('lostpointercapture', cancelPointer);
    }
  }
  save(); render();
  ctx.on('pause', clearDrag); ctx.on('suspend', clearDrag);
  ctx.frame(() => {
    /* The HUD clock is active real game time, so unrelated speed upgrades cannot shorten the window. */
    const elapsed = Math.max(0, ctx.state.clock - lastClock) / 1000;
    lastClock = ctx.state.clock;
    swingCarry(elapsed);
    const before = state.phase, result = advanceBox(state, elapsed * (state.phase === 'closed' ? peekRate(ctx.prestige) * lostKeyRate(ctx.prestige) : 1), ctx.rng, boxPerks(toyBoxRank(ctx.state)));  // prestige hooks: Peek (luck_2/5), Lost Key ending (lo_end); Journey ranks read live per deal
    state = result.box; save();
    if (result.timedOut && autopilotShare(ctx.prestige)) { const rt = ctx.rates(); ctx.pay(Math.max(rt.fps, rt.click * 2, 2) * boxPrizeSeconds(state.completed) * prizeMult(ctx.prestige) * autopilotShare(ctx.prestige), 'toy_box'); }  // prestige hook: Autopilot (idle_6)
    if (result.timedOut && state.phase === 'closing') closeBox();
    else if (before !== state.phase || result.opened) render();
    if (result.opened) {
      note.textContent = 'Match the shapes';
      sfx(ctx, 'sfx/chime1.mp3', .16);
      if (!ctx.gentle && !ctx.reduced) boxAnimation = ctx.anim(box, [{ scale: '.96' }, { scale: '1.025', offset: .5 }, { scale: '1' }], { duration: 340, easing: 'ease-out' });
    }
    if (state.phase === 'closing') paintClosing();
    paintClock();
    if (ctx.gentle || ctx.reduced) {
      for (const animation of fitAnimations) animation.cancel();
      if (boxAnimation) { boxAnimation.cancel(); boxAnimation = null; }
    }
  });
  const off = () => { clearDrag(); setReady(false); };
  ctx.cleanup(off);
  return { off };
}
