/* ui/unlocks/arcade/deep-end.js - The Deep End, the Arcademy 2048 dive, as a small dock grid (owner 2026-11-07: "port the
 * deep end game in a small grid recycling the scroller images we already got, keep the effect logic, spawning flashes or
 * subliminals when we get high points").
 * Equal tiles sink together; every tier is a trance depth and wears one of the pictures the run already holds (one picture per
 * tier, frozen for the dive; plain depth colour when pictures are off). A new depth pays and, from Drifting down, throws a
 * subliminal of the depth's name, a picture flash, or the spiral over the page (the slot machine's effects). A locked board
 * resurfaces: the depth is paid, fresh water, the clock runs on. Input: swipe or drag on the board, or arrows / WASD while it
 * has focus. Escape is never used here. Pays inside the side budget (source mini_deep). Rules: ./deep-end/board.js. */

import { createBoard, restoreBoard, snapshot, slide, spawn, deal, canMove, deepest, DEPTHS, TIER_MAX } from './deep-end/board.js';
import { slotEffects } from './slot-effects.js';
import { sfx } from '../basics/fx.js';

const N = 4, BOARD = 212, GAP = 5, CELL = (BOARD - GAP * (N + 1)) / N;
const MOVE_MS = 120, SWIPE_PX = 18, GRAB_MAX = 6;
const EFFECT_GAP_MS = 20000;   // one page-wide effect at most every 20 s of play
const EFFECT_FROM = 5;         // Drifting: the first depth that reaches past the board
const KEYS = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right', w: 'up', s: 'down', a: 'left', d: 'right', W: 'up', S: 'down', A: 'left', D: 'right' };
const CHAIN_WORDS = ['SINK', 'DEEPER', 'LET GO', 'SOFTER', 'DOWN'];

const CSS = `
.mini-deep{position:relative;width:${BOARD + 12}px;margin:auto;padding:4px 6px 6px;color:var(--ink);font:600 12px/1.25 var(--font,system-ui);user-select:none}
.mde-head{display:flex;align-items:baseline;justify-content:space-between;gap:6px;margin:0 2px 4px;font-variant-numeric:tabular-nums}
.mde-depth{font-weight:800;font-size:13px;color:color-mix(in srgb,var(--niche-text,var(--ink)) 70%,var(--ink))}
.mde-stats{font-size:11px;opacity:.75;white-space:nowrap}
.mde-board{position:relative;width:${BOARD}px;height:${BOARD}px;border-radius:10px;overflow:hidden;touch-action:none;cursor:grab;outline:none;
  background:color-mix(in srgb,var(--niche,#d9a) 18%,#1a1630);box-shadow:inset 0 2px 0 #0003,0 0 0 2px #0000;transition:box-shadow .2s}
.mde-board:focus-visible{box-shadow:inset 0 2px 0 #0003,0 0 0 2px var(--niche,#d9a)}
.mde-board.held{cursor:grabbing}
.mde-cell{position:absolute;width:${CELL}px;height:${CELL}px;border-radius:7px;background:#ffffff14}
.mde-tile{position:absolute;left:0;top:0;width:${CELL}px;height:${CELL}px;border-radius:7px;overflow:hidden;
  transition:transform ${MOVE_MS}ms ease-out,opacity .2s;will-change:transform;
  background:color-mix(in oklab,var(--niche,#e38ab5) 22%,var(--dc,#7aa));box-shadow:0 2px 0 #0004}
.mde-tile .mde-face{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;pointer-events:none}
.mde-tile .mde-tint{position:absolute;inset:0;background:color-mix(in oklab,var(--niche,#e38ab5) 22%,var(--dc,#7aa));opacity:calc(.22 + var(--d) * .45)}
.mde-tile.plain .mde-tint{opacity:1}
.mde-tile .mde-num{position:absolute;right:4px;bottom:2px;font:800 15px/1 var(--font,system-ui);color:#fff;text-shadow:0 1px 2px #000a,0 0 6px #0008}
.mde-tile .mde-name{position:absolute;left:4px;top:3px;font:700 8.5px/1 var(--font,system-ui);letter-spacing:.02em;color:#fff;text-shadow:0 1px 2px #000c;opacity:.9}
.mde-tile.deep .mde-face{animation:mde-drift 9s ease-in-out infinite alternate}
@keyframes mde-drift{from{transform:scale(1.04)}to{transform:scale(1.16) translate(-3%,2%)}}
.mde-word{position:absolute;inset:0;display:grid;place-items:center;pointer-events:none;font:900 26px/1 var(--font,system-ui);letter-spacing:.12em;color:#fff;text-shadow:0 0 12px var(--niche,#e38ab5),0 2px 3px #000a;opacity:0}
.mde-line{min-height:15px;margin:4px 2px 0;font-size:11px;text-align:center;opacity:.85}
.mde-credit{position:absolute;right:8px;top:4px;padding:1px 4px;background:#211a32;color:#fff;font:10px system-ui;pointer-events:none}
.mini-deep.calm .mde-tile{transition:opacity .3s}
.mini-deep.calm .mde-tile.deep .mde-face,.mini-deep.still .mde-tile.deep .mde-face{animation:none}
.mini-deep.still .mde-tile{transition:none}
`;

const pos = (i) => GAP + i * (CELL + GAP);
/* The water: shallow aqua, through blue and violet, to near black at Blackout. */
const depthColour = (tier) => `hsl(${188 + (tier - 1) * 9} ${58 - (tier - 1) * 2}% ${74 - (tier - 1) * 6.4}%)`;
const pad2 = (n) => String(Math.max(0, Math.min(17, n))).padStart(2, '0');

export function mountDeepEnd(ctx) {
  ctx.css(CSS);
  let visible = true;
  const dock = ctx.dock({ title: 'The Deep End', edge: 'right', width: BOARD + 24, reserveHeight: BOARD + 70, onMin(value) {
    visible = !value;
    if (value) effects.clear();
  } });
  visible = dock.dock?.visible !== false;
  const doc = dock.ownerDocument;
  const add = (tag, cls, text, parent) => { const el = ctx.el(tag, '', text, parent); if (cls) el.className = cls; return el; };
  const root = add('section', 'mini-deep', null, dock);
  root.dataset.arcade = 'mini_deep';
  root.dataset.dockSurface = '';
  const head = add('div', 'mde-head', null, root);
  const depthLabel = add('span', 'mde-depth', '', head);
  const stats = add('span', 'mde-stats', '', head);
  const board = add('div', 'mde-board', null, root);
  board.tabIndex = 0;
  board.dataset.dockInteractive = '';
  board.setAttribute('role', 'application');
  board.setAttribute('aria-label', 'The Deep End board. Swipe, or use the arrow keys. Equal tiles sink together.');
  for (let i = 0; i < N * N; i++) {
    const cell = add('div', 'mde-cell', null, board);
    cell.style.transform = `translate(${pos(i % N)}px,${pos(Math.floor(i / N))}px)`;
  }
  const word = add('div', 'mde-word', '', board);
  const line = add('p', 'mde-line', 'Swipe. Equal tiles sink together.', root);
  line.setAttribute('aria-live', 'polite');
  const credit = add('small', 'mde-credit', 'via Scrolller', root);
  credit.hidden = true;
  root.title = 'Swipe or use the arrow keys. Every new depth pays. Drag the casing to move.';

  const effects = slotEffects(ctx);
  const saved = ctx.store.get('deep') || {};
  const b = restoreBoard(saved.board, N);
  let best = Number(saved.best) || 0, score = Number(saved.score) || 0, dive = Number(saved.dive) || 1;
  let diveBest = Number(saved.diveBest) || 0;
  if (!b.tiles.length) { deal(b, ctx.rng); diveBest = deepest(b); }
  const persist = () => ctx.store.set('deep', { board: snapshot(b), best, score, dive, diveBest });

  const views = new Map();          // tile id -> element
  let faces = [];                   // tier -> { row, lease } for this dive
  let lockedUntil = 0, queued = null, resurfacing = false, lastEffect = -Infinity, alive = true;
  const calm = () => ctx.gentle || ctx.reduced;
  const busy = () => !alive || ctx.paused || !!ctx.state.ended || resurfacing;

  /* ---- faces: one picture per tier, frozen for the dive ------------------------------------------------------------ */
  function releaseFaces() {
    for (const f of faces) f?.lease?.release();
    faces = [];
    credit.hidden = true;
  }
  function dealFaces() {
    releaseFaces();
    const source = ctx.pictures?.source;
    if (source !== 'own' && source !== 'scrolller') return;
    const rows = ctx.pictures.stillsForDisplay(24, { allowScrolller: true }).filter((r) => r.kind === 'picture');
    if (!rows.length) return;
    const offset = (dive * 5) % rows.length;
    for (let t = 1; t <= TIER_MAX; t++) faces[t] = { row: rows[(offset + t - 1) % rows.length], lease: null };
  }
  function faceFor(tier) {
    const f = faces[tier];
    if (!f) return null;
    if (!f.lease) {
      try { f.lease = ctx.pictures.acquire(f.row); } catch { faces[tier] = null; return null; }
    }
    if (f.row.credit) credit.hidden = false;
    return f.lease.url;
  }

  /* ---- view ------------------------------------------------------------------------------------------------------- */
  function paintTile(el, tier) {
    el.style.setProperty('--d', String((tier - 1) / (TIER_MAX - 1)));
    el.style.setProperty('--dc', depthColour(tier));
    el.dataset.tier = String(tier);
    el.classList.toggle('deep', tier >= 6);
    const url = faceFor(tier);
    let img = el.querySelector('.mde-face');
    el.classList.toggle('plain', !url);
    if (url) {
      if (!img) { img = doc.createElement('img'); img.className = 'mde-face'; img.alt = ''; img.referrerPolicy='strict-origin'; img.decoding = 'async'; el.prepend(img); }
      if (img.getAttribute('src') !== url) img.src = url;
    } else img?.remove();
    el.querySelector('.mde-num').textContent = String(tier);
    el.querySelector('.mde-name').textContent = DEPTHS[tier];
    el.setAttribute('aria-label', DEPTHS[tier]);
  }
  function place(el, r, c) { el.style.transform = `translate(${pos(c)}px,${pos(r)}px)`; }
  function viewFor(t, fresh) {
    let el = views.get(t.id);
    if (el) return el;
    el = doc.createElement('div');
    el.className = 'mde-tile';
    el.innerHTML = '<span class="mde-tint"></span><span class="mde-name"></span><span class="mde-num"></span>';
    board.insertBefore(el, word);
    views.set(t.id, el);
    paintTile(el, t.tier);
    place(el, t.r, t.c);
    if (fresh && !ctx.reduced) ctx.anim(el, [{ opacity: 0, scale: .4 }, { opacity: 1, scale: 1 }], { duration: ctx.gentle ? 320 : 160, easing: 'ease-out' });
    return el;
  }
  function drawAll() {
    for (const [id, el] of views) if (!b.tiles.some((t) => t.id === id)) { el.remove(); views.delete(id); }
    for (const t of b.tiles) { const el = viewFor(t, false); paintTile(el, t.tier); place(el, t.r, t.c); }
    drawHead();
  }
  function drawHead() {
    const deep = deepest(b);
    depthLabel.textContent = DEPTHS[deep] || DEPTHS[1];
    stats.textContent = `Best ${DEPTHS[best] || '-'}  ·  ${ctx.fmt(score)}`;
    root.classList.toggle('calm', !!ctx.gentle && !ctx.reduced);
    root.classList.toggle('still', !!ctx.reduced);
  }
  const say = (text) => { line.textContent = text; };

  /* ---- the dive loop ---------------------------------------------------------------------------------------------- */
  function move(dir) {
    if (busy()) return;
    if (ctx.state.clock < lockedUntil) { queued = dir; return; }
    const result = slide(b, dir);
    if (!result.moved) { bump(dir); return; }
    lockedUntil = ctx.state.clock + MOVE_MS;
    for (const s of result.slid) { const el = views.get(s.id); if (el) place(el, s.r, s.c); }
    const victims = result.merges.map((m) => m.victim);
    const survivors = result.merges.map((m) => m.survivor);
    const born = spawn(b, ctx.rng);
    ctx.after(MOVE_MS, () => {
      if (!alive) return;
      for (const id of victims) { views.get(id)?.remove(); views.delete(id); }
      for (const id of survivors) {
        const t = b.tiles.find((x) => x.id === id), el = views.get(id);
        if (!t || !el) continue;
        paintTile(el, t.tier);
        if (!calm()) ctx.anim(el, [{ scale: 1 }, { scale: 1.12, offset: .45 }, { scale: 1 }], { duration: 170, easing: 'ease-out' });
      }
      if (born) viewFor(born, true);
      settle(result);
    });
  }
  function settle(result) {
    const merged = result.merges;
    if (merged.length) {
      const top = Math.max(...merged.map((m) => m.tier));
      score += merged.reduce((s, m) => s + 2 ** m.tier, 0);
      sfx(ctx, `pluck/wood_${pad2(15 - top)}.wav`, .07);   // deeper sinks lower
      if (merged.length >= 3) chain(merged.length, top);
    }
    const deep = deepest(b);
    if (deep > diveBest) { diveBest = deep; newDepth(deep); }
    persist();
    drawHead();
    if (!canMove(b) || deep >= TIER_MAX) resurface(deep >= TIER_MAX);
    else if (queued) { const q = queued; queued = null; move(q); }
  }
  function boardPoint() {
    const r = board.getBoundingClientRect();
    return { x: r.left + r.width / 2, y: r.top + r.height / 3 };
  }
  function chain(count, top) {
    const rates = ctx.rates();
    ctx.pay(Math.max(rates.fps * 2 * count, rates.click * 3 * count), 'mini_deep', boardPoint());
    flashWord(CHAIN_WORDS[(count + top) % CHAIN_WORDS.length]);
  }
  function flashWord(text) {
    word.textContent = text;
    if (ctx.reduced) return;
    ctx.anim(word, ctx.gentle
      ? [{ opacity: 0 }, { opacity: .8, offset: .35 }, { opacity: 0 }]
      : [{ opacity: 0, transform: 'scale(1.4)' }, { opacity: .95, transform: 'scale(1)', offset: .2 }, { opacity: .9, offset: .55 }, { opacity: 0, transform: 'scale(.9)' }],
    { duration: ctx.gentle ? 1400 : 900, easing: 'ease-out' });
  }
  function newDepth(tier) {
    if (tier < 3) return;
    const rates = ctx.rates();
    const got = ctx.pay(Math.max(rates.fps * 4 * tier, rates.click * 6 * tier), 'mini_deep', boardPoint());
    if (tier >= 10) ctx.emit('sticker', { key: 'deep10' });   // Sticker Book: Out of Your Depth (Trench)
    const lifetime = tier > best;
    if (lifetime) best = tier;
    say(`${lifetime ? 'A new lifetime depth' : 'New depth'}: ${DEPTHS[tier]}. +${ctx.fmt(got)} ${ctx.brand.currencyPlural}`);
    sfx(ctx, 'sfx/chime2.mp3', .12);
    if (tier >= EFFECT_FROM) highPoint(tier);
  }
  /* The effect logic: past Drifting, a new depth reaches past the board. Words first, a picture flash from Sinking, the spiral
   * from Submerged. At most one page-wide effect every EFFECT_GAP_MS of play; the rest stay on the board. */
  function highPoint(tier) {
    const now = ctx.state.clock;
    if (!visible || !root.getClientRects().length || now - lastEffect < EFFECT_GAP_MS) { flashWord(DEPTHS[tier].toUpperCase()); return; }
    lastEffect = now;
    const deepTile = b.tiles.find((t) => t.tier === tier);
    const el = deepTile && views.get(deepTile.id);
    const r = (el || board).getBoundingClientRect();
    const scene = { portrait: () => ({ x: r.left + r.width / 2, y: r.top + r.height / 2, w: r.width, h: r.height }) };
    const face = faces[tier]?.row;
    const pictures = ctx.pictures?.source === 'own' || ctx.pictures?.source === 'scrolller';
    if (tier >= 8) effects.start('spiral', scene, null, DEPTHS[tier].toUpperCase());
    else if (tier >= 6 && pictures) effects.start('picture', scene, { preferred: face ? { url: face.url } : null }, DEPTHS[tier].toUpperCase());
    else effects.start('words', scene, null, DEPTHS[tier].toUpperCase());
  }
  function resurface(ceiling) {
    resurfacing = true;
    queued = null;
    const deep = deepest(b);
    const rates = ctx.rates();
    const got = ctx.pay(Math.max(rates.fps * 6 * deep, rates.click * 8 * deep), 'mini_deep', boardPoint());
    say(ceiling ? 'Tier eleven. The ladder ends here, warm.' : `The board locked. ${DEPTHS[deep]} is earned. +${ctx.fmt(got)} ${ctx.brand.currencyPlural}`);
    if (ceiling) effects.start('spiral', null, null, 'BLACKOUT');
    sfx(ctx, 'sfx/thud.mp3', .1);
    for (const el of views.values()) {
      if (ctx.reduced) el.style.opacity = '.25';
      else ctx.anim(el, [{ opacity: 1 }, { opacity: .15 }], { duration: ctx.gentle ? 900 : 600, fill: 'forwards', easing: 'ease-in' });
    }
    ctx.after(ceiling ? 2200 : 1300, () => {
      if (!alive) return;
      for (const el of views.values()) el.remove();
      views.clear();
      dive += 1; diveBest = 0;
      deal(b, ctx.rng);
      diveBest = deepest(b);
      dealFaces();
      drawAll();
      for (const el of views.values()) if (!ctx.reduced) ctx.anim(el, [{ opacity: 0 }, { opacity: 1 }], { duration: ctx.gentle ? 500 : 240 });
      resurfacing = false;
      persist();
      ctx.after(1600, () => { if (alive && !resurfacing) say('Fresh water.'); });
    });
  }
  function bump(dir) {
    if (calm()) return;
    const [dy, dx] = { up: [-1, 0], down: [1, 0], left: [0, -1], right: [0, 1] }[dir];
    ctx.anim(board, [{ translate: '0 0' }, { translate: `${dx * 3}px ${dy * 3}px`, offset: .3 }, { translate: '0 0' }], { duration: 180, easing: 'ease-out' });
  }

  /* ---- input: the board owns its own drag; the casing still moves the dock ---------------------------------------------- */
  let grab = null;
  board.addEventListener('pointerdown', (e) => {
    if (e.button > 0) return;
    board.focus({ preventScroll: true });
    grab = { id: e.pointerId, x: e.clientX, y: e.clientY };
    try { board.setPointerCapture(e.pointerId); } catch {}
    board.classList.add('held');
  });
  board.addEventListener('pointermove', (e) => {
    if (!grab || grab.id !== e.pointerId || calm()) return;
    const dx = Math.max(-GRAB_MAX, Math.min(GRAB_MAX, (e.clientX - grab.x) / 6));
    const dy = Math.max(-GRAB_MAX, Math.min(GRAB_MAX, (e.clientY - grab.y) / 6));
    const horiz = Math.abs(dx) >= Math.abs(dy);
    for (const el of views.values()) el.style.translate = horiz ? `${dx}px 0` : `0 ${dy}px`;
  });
  const release = (e) => {
    if (!grab || grab.id !== e.pointerId) return;
    const dx = e.clientX - grab.x, dy = e.clientY - grab.y;
    grab = null;
    board.classList.remove('held');
    for (const el of views.values()) el.style.translate = '';
    if (e.type !== 'pointerup' || Math.max(Math.abs(dx), Math.abs(dy)) < SWIPE_PX) return;
    move(Math.abs(dx) > Math.abs(dy) ? (dx > 0 ? 'right' : 'left') : (dy > 0 ? 'down' : 'up'));
  };
  board.addEventListener('pointerup', release);
  board.addEventListener('pointercancel', release);
  board.addEventListener('keydown', (e) => {
    if (e.ctrlKey || e.altKey || e.metaKey) return;   // Ctrl/Alt+arrows belong to the dock
    const dir = KEYS[e.key];
    if (!dir) return;
    e.preventDefault();
    e.stopPropagation();
    move(dir);
  });

  /* ---- lifecycle -------------------------------------------------------------------------------------------------- */
  ctx.on('pictures', () => { releaseFaces(); dealFaces(); drawAll(); });
  ctx.on('pictures-added', () => { if (!faces.length) { dealFaces(); drawAll(); } });
  const stop = () => { effects.clear(); queued = null; };
  ctx.on('pause', () => { queued = null; });
  ctx.on('suspend', stop);
  ctx.on('ending', stop);
  let wasCalm = calm();
  ctx.frame(() => {
    if (wasCalm !== calm()) { wasCalm = calm(); effects.clear(); drawHead(); }
    if (queued && ctx.state.clock >= lockedUntil && !busy()) { const q = queued; queued = null; move(q); }
    effects.draw();
  });
  dealFaces();
  drawAll();
  persist();
  if (!canMove(b)) ctx.after(400, () => { if (alive && !resurfacing) resurface(false); });
  ctx.cleanup(() => { alive = false; effects.clear(); releaseFaces(); views.clear(); });
  return { root, show() { dock.dock?.setVisible(true); dock.dock?.bringToFront(); board.focus({ preventScroll: true }); } };
}

/* Test seam: the pure model, so tests can drive whole dives without a page. */
export { createBoard };
