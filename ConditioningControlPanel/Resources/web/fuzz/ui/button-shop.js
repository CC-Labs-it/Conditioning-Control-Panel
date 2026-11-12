import { whole } from '../sim/format.js';
import { BY_ID } from '../data/items.js';
import { addWavePress, chosenWave, drawWave, ownedWave, waveFields, SHOP_WAVES } from './wave-ribbon.js';
import { KINETIC_STUDIES, isKineticWave } from './kinetic-waves.js';
import { COPY_CHOICES, COPY_PRICES, COPY_SIZES, EMI_CHOICES, bouncingLook, chosenCopy, copyPrice, emiPose, ownedCopy, renderBounce } from './bouncing-art.js';
import { makeFace, SKIN_ITEMS, SKIN_NAMES } from './button-faces.js';
import { FACE_HTML } from './button-face-art.js';
import { liveOn } from '../sim/switches.js';
import { THEME_TASTES } from './unlocks/theme-skins.js';
import { iconFor } from './icons.js';
import { plainText } from './rich-text.js';
import { sfx, squish, tools } from './unlocks/basics/fx.js';
import { gateLine, gateOf, gateOpen, markSeen, newMark, rowOpen, SHOP_GATES } from './shop-gates.js';

/* ui/button-shop.js - the Button Shop is a store (owner 2026-11-07): styles and ribbons are bought here with a live preview,
 * a price that fills as the player saves, a stamp on the buy. The door is a single button icon beside the Cursor Shop's
 * cursor icon. Styles bought here live in store.bubble.faces, ribbons in store.bubble.waveStyles. */

/* Sizes are not looks (owner 2026-11-08): Big and Tiny layer on any style through #btn[data-size]. */
export const SIZE_ITEMS = Object.freeze({ bigbtn: 'big', tinybtn: 'tiny' });
const SIZES = new Set(Object.values(SIZE_ITEMS));
const ITEM_OF = Object.fromEntries(Object.entries(SKIN_ITEMS).map(([item, skin]) => [skin, item]));
const THEME_OF = Object.fromEntries(Object.entries(THEME_TASTES).filter(([, t]) => t.skin).map(([theme, t]) => [t.skin, theme]));
/* Short shop lines for the faces we know; a face added later falls back to its unlock's own description. */
const HINTS = {
  'bi-lips': 'A sign-in button that already knows your name.', 'dr-hex': 'Download the firmware. Then install the user.',
  'tr-pendulum': 'Tap on the notch for a reading.', worn: 'Every tap starts one more episode.',
  padlock: 'Open a ticket. Read the reply.', censorbar: 'Tap to uncover a comment about the person clicking.',
};
/* Styles the shop sells or shows. A theme face comes with its theme's own unlock; the shop shows it once that theme is in the run.
 * The theme faces come from the data (SKIN_ITEMS, THEME_TASTES, the face art), so a new theme face shows up here by itself. */
const BASE_STYLES = [
  { id: 'round', name: 'Plain', hint: 'The button as it came. Honest work.', price: 0 },
  { id: 'bubble', name: 'CCP Bubble', hint: 'A soap bubble that pops when pressed.', price: 0, item: 'bubble' },
  { id: 'human-check', name: 'Human Check', hint: 'Tick the box. Prove you are a person, again and again.', price: 1500 },
  /* EMI (owner 2026-11-08): twice Human Check, since she reacts to every pet; still under the first kinetic ribbon. */
  { id: 'emi', name: 'EMI', hint: 'Pet her with every tap. She likes a steady hand.', price: 3000 },
];
/* Bought styles: always in the wardrobe list, owned only once store.bubble.faces says so. */
const SOLD_FACES = BASE_STYLES.filter((s) => s.price > 0).map((s) => s.id);
const themeFaces = [...new Set([...Object.values(SKIN_ITEMS), ...Object.values(THEME_TASTES).map((t) => t.skin)])]
  .filter((skin) => skin && Object.hasOwn(FACE_HTML, skin) && !BASE_STYLES.some((b) => b.id === skin));
export const FACE_STYLES = Object.freeze([...BASE_STYLES, ...themeFaces.map((id) => {
  const item = BY_ID[ITEM_OF[id]];
  return { id, name: SKIN_NAMES[id] || item?.name || id, hint: HINTS[id] || (item?.what ? plainText(item.what) : 'Comes with its theme.') };
})]);
const STYLE_BY_ID = Object.fromEntries(FACE_STYLES.map((s) => [s.id, s]));

/** The size the button wears: the player's pick when that row is owned and switched on, else the latest size row bought. */
export function buttonSize(run) {
  const live = (item) => Boolean(run.owned?.[item]) && liveOn(run.off || {}, item);
  const pick = run.store?.bubble?.size;
  if (pick === 'normal') return '';
  const item = Object.keys(SIZE_ITEMS).find((k) => SIZE_ITEMS[k] === pick);
  if (item && live(item)) return pick;
  return live('tinybtn') ? 'tiny' : live('bigbtn') ? 'big' : '';
}

/** Saves from before the shop sold Human Check keep it when they were wearing it. Answers the faces to save, or null. */
export function grandfatherFaces(bag) {
  if (!bag || bag.choice !== 'human-check' || bag.faces?.['human-check'] === true) return null;
  return { ...bag.faces, 'human-check': true };
}

const bag = (ctx) => ctx.state.store.bubble || {};
const boughtFace = (ctx, id) => bag(ctx).faces?.[id] === true;
const wavesFor = (doc) => {
  const studies = isKineticWave(doc.documentElement.dataset.waveStudy) && !doc.documentElement.hasAttribute('data-combined-preview');
  return studies ? KINETIC_STUDIES : SHOP_WAVES;
};

/** Something here is for sale, not owned, and affordable: the door wears a ready dot. */
export function buttonShopReady(ctx, owned) {
  const fuzz = ctx.state.fuzz, have = new Set(owned);
  /* The wardrobe list always names the sold faces; only a purchase owns them. */
  for (const id of SOLD_FACES) have.delete(id);
  if (FACE_STYLES.some((s) => s.price > 0 && !have.has(s.id) && !boughtFace(ctx, s.id) && fuzz >= s.price && rowOpen(ctx.state, 'style', s.id))) return true;
  if (gateOpen(ctx.state, 'copies') && Object.entries(COPY_PRICES).some(([field, row]) => Object.entries(row).some(([v, p]) => p > 0 && fuzz >= p && !ownedCopy(bag(ctx), field, v)))) return true;
  return SHOP_WAVES.some((w) => w.price > 0 && !ownedWave(bag(ctx), w.id) && fuzz >= w.price && rowOpen(ctx.state, 'wave', w.id));
}

/** A square icon door in the tool row: the button for the Button Shop, the arrow for the Cursor Shop. */
export function shopDoor(ctx, kind, onPress) {
  const doc = ctx.button.el.ownerDocument;
  const b = ctx.el('button', '', null, tools(ctx));
  b.type = 'button';
  b.className = 'fz-door';
  b.dataset.shopDoor = kind;
  const name = kind === 'cursor' ? 'Cursor Shop' : 'Button Shop';
  b.setAttribute('aria-label', name);
  b.title = name;
  b.append(iconFor(doc, { id: kind === 'cursor' ? 'cursor' : 'start' }));
  b.addEventListener('click', () => { squish(ctx, b, 0.6); onPress(b); });
  return b;
}

export const buttonShopCss = `
#fz-tools .fz-door { position:relative; width:44px; height:44px; padding:0; display:inline-grid; place-items:center; vertical-align:middle;
  background:var(--btn); border:1px solid var(--line); border-radius:12px 10px 13px 9px; cursor:pointer; touch-action:manipulation;
  transition:translate .15s ease-out, box-shadow .15s; box-shadow:0 2px 0 var(--line); }
#fz-tools .fz-door svg.ico { width:36px; height:36px; pointer-events:none; }
#fz-tools .fz-door:hover { translate:0 -2px; box-shadow:0 4px 0 var(--line); }
#fz-tools .fz-door:active { translate:0 1px; box-shadow:none; }
#fz-tools .fz-door[hidden] { display:none; }
#fz-tools .fz-door[data-ready]::after { content:""; position:absolute; right:-3px; top:-3px; width:11px; height:11px; border-radius:50%;
  background:#e8395f; border:2px solid #fff; box-shadow:0 0 0 1px #0004; }
body.gentle #fz-tools .fz-door, #fz-tools .fz-door { transition-duration:.15s; }
@media (prefers-reduced-motion: reduce) { #fz-tools .fz-door:hover, #fz-tools .fz-door:active { translate:none; } }
.card.button-shop { width:min(760px,calc(100vw - 24px)); max-width:min(760px,calc(100vw - 24px)); max-height:calc(100dvh - 72px); overflow:auto; overflow-x:hidden; }
.card.button-shop h2::after { display:none; }
.bshop { padding-bottom:14px; }
.bshop-gallery { display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:16px 14px; padding:16px 0 0; }
.bshop .bshop-card { position:relative; display:grid; grid-template-columns:none; grid-template-rows:auto 1fr auto; gap:7px; align-items:start; padding:0; border:0; }
.bshop .bshop-stage { position:relative; display:grid; place-items:center; width:100%; aspect-ratio:1.7; overflow:hidden; grid-row:auto; }
.bshop-stage > * { grid-area:1/1; }
.bshop-stage .fz-face { zoom:.6; }
.bshop-stage .fz-face .tc-oracle { zoom:.62; }
.bshop-stage .fz-face.fz-bubble { zoom:1; width:70px; height:70px; }
.bshop-plain { display:inline-grid; place-items:center; min-width:96px; min-height:44px; padding:6px 20px; border-radius:16px; border:2px solid #49364d;
  background:var(--btn-fill,#ffb3d1); color:var(--btn-ink,#3a2440); box-shadow:0 4px 0 #49364d; font:700 17px/1 var(--bubble); }
.bshop-card[data-locked] .bshop-stage > :not(.bshop-lock) { filter:grayscale(.75) brightness(.55); }
.bshop-lock { align-self:start; justify-self:end; margin:6px; padding:2px 7px; border-radius:5px; background:#2c2435; color:#f9edf4; font:600 11px/1.4 "Segoe UI",sans-serif; }
.bshop-stamp { position:absolute; z-index:2; right:6px; top:6px; padding:1px 7px; border:2.5px solid #b8325f; border-radius:5px 3px 6px 4px;
  color:#b8325f; background:#fff6f0cc; font:700 14px/1.2 var(--bubble); letter-spacing:.06em; transform:rotate(-9deg); pointer-events:none; }
.bshop .cursor-kit-action { position:relative; isolation:isolate; overflow:hidden; }
.bshop .cursor-kit-action[data-price]::before { content:""; position:absolute; inset:0; z-index:-1; background:#f4bfd2; transform-origin:left;
  transform:scaleX(var(--fill,0)); transition:transform .3s ease-out; }
.bshop .cursor-kit-action[data-price][data-afford]::before { background:#bfe5cf; }
.bshop .cursor-kit-action[data-afford] { border-color:#3d6b55; }
.bshop-spark { position:absolute; z-index:3; width:8px; height:8px; margin:-4px 0 0 -4px; border-radius:50%; pointer-events:none; }
.bshop-wave { display:grid; gap:10px; padding-top:16px; }
.bshop-sample { width:100%; height:122px; aspect-ratio:auto; display:grid; grid-template-rows:122px; place-items:center; background:#221930; border-radius:0; overflow:hidden; }
.bshop-sample > * { grid-area:1/1; }
.bshop-sample canvas { width:100%; height:122px; }
.bshop-sample button { z-index:1; min-height:38px; padding:4px 16px; border:2px solid #f2d9ff; background:#382748; color:#fff; border-radius:19px; font:700 15px/1 var(--bubble); cursor:pointer; touch-action:manipulation; }
.bshop-sample button:active { background:#614574; }
.bshop-buy { display:grid; grid-template-columns:minmax(0,1fr) auto; gap:4px 10px; align-items:center; }
.bshop-buy .cursor-kit-copy { min-width:0; }
.bshop-buy .cursor-kit-action { min-height:38px; padding:4px 14px; color:#32273e; background:#ded2e7; border:1px solid #70627a; border-radius:4px 7px 3px 6px / 7px 3px 6px 3px;
  box-shadow:1px 1.5px #87718a; font:600 14px/1.25 "Segoe UI",sans-serif; cursor:pointer; }
.bshop-buy .cursor-kit-action:disabled { border-color:#c2b4b9; background:#ebe5d9; color:#6e626a; box-shadow:none; cursor:default; }
.bshop .cursor-kit-action[data-worn] { background:transparent; border-color:transparent; color:#345d51; box-shadow:none; }
.bshop-choices { display:grid; grid-template-columns:repeat(5,minmax(0,1fr)); gap:8px; }
.bshop-choices button { display:grid; gap:2px; padding:0 0 5px; overflow:hidden; color:var(--print); background:#efe6dd; border:1.5px solid #b3a3ad;
  border-radius:6px 8px 5px 7px; font:600 12px/1.15 "Segoe UI",sans-serif; cursor:pointer; text-align:center; min-width:0; }
.bshop-choices button small { color:var(--quiet); font-size:11px; }
.bshop-choices button[data-owned] small { color:#2f6b52; }
.bshop-choices button[aria-pressed=true] { border-color:#883454; box-shadow:0 0 0 2px #883454; }
.bshop-choices canvas { display:block; height:30px; width:100%; margin-bottom:3px; background:#241c34; pointer-events:none; }
.bshop .button-bounce-options { padding-top:10px; }
.bshop .button-bounce-options button { background:#efe6dd; color:var(--print); border:1.5px solid #b3a3ad; border-radius:6px 8px 5px 7px; font:600 13px/1.2 "Segoe UI",sans-serif; }
.bshop .button-bounce-options button[aria-pressed=true] { outline:0; border-color:#883454; box-shadow:0 0 0 2px #883454; }
.bshop .button-bounce-options button { position:relative; isolation:isolate; overflow:hidden; }
.bshop .button-bounce-options button[data-price]::before { content:""; position:absolute; inset:0; z-index:-1; background:#f4bfd2; transform-origin:left;
  transform:scaleX(var(--fill,0)); transition:transform .3s ease-out; }
.bshop .button-bounce-options button[data-price][data-afford]::before { background:#bfe5cf; }
.bshop .button-bounce-options button[data-afford] { border-color:#3d6b55; }
.bshop .button-bounce-options button small { display:block; color:var(--quiet); font-size:11px; }
.bshop .button-bounce-options button[data-owned] small { color:#2f6b52; }
.bshop .button-bounce-options [data-bounce-art=face] { position:relative; width:100px; height:34px; }
.bshop .cursor-kit-tabs button { white-space:nowrap; }
.bshop h3 { margin:14px 0 4px; font:700 19px/1.3 var(--hand); }
.bshop-size { display:flex; align-items:center; gap:12px; padding:14px 0 0; }
.bshop-size strong { font:700 19px/1 var(--hand); color:var(--print); }
.bshop-size-seg { display:inline-flex; border:1.5px solid #70627a; border-radius:7px 5px 8px 5px; overflow:hidden; background:#efe6dd; }
.bshop-size-seg button { min-height:36px; min-width:72px; padding:4px 14px; border:0; border-left:1.5px solid #b3a3ad; background:transparent; color:var(--print);
  font:600 14px/1 "Segoe UI",sans-serif; cursor:pointer; touch-action:manipulation; }
.bshop-size-seg button:first-child { border-left:0; }
.bshop-size-seg button[aria-pressed=true] { background:#883454; color:#fff; }
.bshop-size-seg button:disabled { color:#a1959c; cursor:default; }
@media (max-width:590px) {
  .bshop-gallery { grid-template-columns:repeat(2,minmax(0,1fr)); gap:12px 10px; padding-top:10px; }
  .bshop .bshop-card { border-top:0; padding:0; }
  .bshop .bshop-card .cursor-kit-copy strong { font-size:16px; }
  .bshop .bshop-card .cursor-kit-copy small { font-size:11.5px; }
  .bshop-stage .fz-face { zoom:.42; }
  .bshop-choices { grid-template-columns:repeat(3,minmax(0,1fr)); }
  .bshop .cursor-kit-tabs { gap:10px; }
  .bshop .cursor-kit-tabs button { font-size:17px; padding:2px 3px; }
  .bshop-size-seg button { min-width:0; padding:4px 12px; }
  .bshop-sample { grid-template-rows:96px; height:96px; } .bshop-sample canvas { height:96px; }
}
`;

export function openButtonShop(ctx, { tab, skinOptions, chooseSkin, onClose }) {
  const doc = ctx.button.el.ownerDocument;
  function node(tag, parent, text, className) {
    const el = doc.createElement(tag);
    if (text) el.textContent = text;
    if (className) el.className = className;
    if (tag === 'button') el.type = 'button';
    parent?.append(el);
    return el;
  }
  if (!['Styles', 'Wave', 'Bouncing copies'].includes(tab)) tab = 'Styles';
  const body = node('div', null, '', 'cursor-kit bshop');
  const tabs = node('nav', body, '', 'cursor-kit-tabs'), content = node('div', body);
  tabs.setAttribute('aria-label', 'Button Shop sections');
  const footer = node('div', body, '', 'cursor-kit-footer');
  const balance = node('span', footer, '', 'cursor-kit-balance');
  const status = node('small', footer, '', 'cursor-kit-status');
  status.setAttribute('role', 'status');
  const quiet = () => ctx.gentle || ctx.reduced;
  const studies = isKineticWave(doc.documentElement.dataset.waveStudy) && !doc.documentElement.hasAttribute('data-combined-preview');
  const styles = wavesFor(doc);
  let selected = styles.find((s) => s.id === (studies ? doc.documentElement.dataset.waveStudy : chosenWave(bag(ctx)))) || styles[0];
  let clock = 1.8, elapsed = 0, slow = 0, sample, action, heading, hint, renderKey = '', stopFrame;
  const hits = [], choices = new Map(), tabButtons = new Map();
  let cards = [];

  /* ---- juice ---- */
  function sparks(host) {
    if (quiet()) { ctx.anim(host, [{ opacity: 0.4 }, { opacity: 1 }], { duration: 420 }); return; }
    const r = host.getBoundingClientRect(), colors = ['#ff7eb6', '#ffd46d', '#8fe3c4', '#b78cff'];
    for (let i = 0; i < 14; i++) {
      const s = node('span', host, '', 'bshop-spark');
      s.style.left = r.width / 2 + 'px'; s.style.top = r.height / 2 + 'px'; s.style.background = colors[i % 4];
      const a = (i / 14) * Math.PI * 2 + Math.random() * 0.4, d = 40 + Math.random() * 46;
      const m = ctx.anim(s, [{ transform: 'translate(0,0) scale(1)', opacity: 1 }, { transform: `translate(${Math.cos(a) * d}px,${Math.sin(a) * d}px) scale(.3)`, opacity: 0 }], { duration: 560 + Math.random() * 200, easing: 'cubic-bezier(.2,.7,.3,1)' });
      m.onfinish = m.oncancel = () => s.remove();
    }
  }
  function stamp(card, fresh) {
    let st = card.querySelector('.bshop-stamp');
    if (!st) st = node('span', card, 'OWNED', 'bshop-stamp');
    if (!fresh) return;
    if (quiet()) ctx.anim(st, [{ opacity: 0 }, { opacity: 1 }], { duration: 400 });
    else ctx.anim(st, [{ transform: 'rotate(-9deg) scale(2.4)', opacity: 0 }, { transform: 'rotate(-9deg) scale(.9)', opacity: 1, offset: 0.7 }, { transform: 'rotate(-9deg) scale(1)' }], { duration: 360, easing: 'ease-in' });
  }
  function bought(card, name) {
    sfx(ctx, 'sfx/reveal_chime.mp3', 0.45);
    status.textContent = name + ' is yours.';
    sparks(card); stamp(card, true);
  }
  function priceButton(button, price) {
    if (!price) { delete button.dataset.price; button.style.removeProperty('--fill'); button.removeAttribute('data-afford'); return; }
    button.dataset.price = price;
    const fill = Math.min(1, ctx.state.fuzz / price);
    button.style.setProperty('--fill', fill.toFixed(3));
    button.toggleAttribute('data-afford', fill >= 1);
  }

  /* ---- Styles ---- */
  function styleState(style, owned) {
    if (owned.has(style.id) || boughtFace(ctx, style.id)) return 'owned';
    if (style.price > 0) return rowOpen(ctx.state, 'style', style.id) ? 'shop' : 'gated';
    return 'locked';
  }
  function visibleStyles(owned) {
    const picked = [ctx.pick('f5'), ctx.state.store.taste2?.theme].filter(Boolean);
    const list = FACE_STYLES.filter((s) => {
      if (owned.has(s.id) || s.price > 0) return true;
      if (s.id === 'round') return true;
      if (s.item) return false;
      return picked.includes(THEME_OF[s.id]);
    });
    /* Anything else the player owns (a size skin from the shop rows) still gets a card. */
    for (const id of owned) if (!STYLE_BY_ID[id] && !SIZES.has(id)) list.push({ id, name: (skinOptions().find(([s]) => s === id) || [id, id])[1], hint: 'From your unlocks.', price: 0 });
    return list;
  }
  function preview(stage, id) {
    const face = makeFace(doc, id);
    if (face) stage.append(face);
    else node('span', stage, 'Click', 'bshop-plain');
  }
  /* ---- Size: Normal / Big / Tiny, shown once a size row is owned, layered on any style ---- */
  function buildSize() {
    const rows = Object.entries(SIZE_ITEMS).filter(([item]) => ctx.has(item));
    if (!rows.length) return;
    const bar = node('div', content, '', 'bshop-size');
    bar.setAttribute('role', 'group'); bar.setAttribute('aria-label', 'Button size');
    node('strong', bar, 'Size');
    const seg = node('div', bar, '', 'bshop-size-seg');
    const choices = [['normal', 'Normal'], ...rows.map(([item, size]) => [size, size === 'big' ? 'Big' : 'Tiny', item])];
    const paint = () => {
      const now = buttonSize(ctx.state) || 'normal';
      for (const b of seg.children) b.setAttribute('aria-pressed', String(b.dataset.size === now));
    };
    for (const [size, label, item] of choices) {
      const b = node('button', seg, label);
      b.dataset.size = size;
      if (item && !ctx.switches.isOn(item)) { b.disabled = true; b.title = 'Switched off in the Control Panel'; }
      b.onclick = () => {
        if (ctx.paused || b.disabled) return;
        ctx.store.set('size', size);
        ctx.emit('button-size', { size });
        squish(ctx, ctx.button.el);
        sfx(ctx, 'sfx/ui_equip.mp3', 0.4);
        if (!quiet()) ctx.anim(b, [{ scale: '1' }, { scale: '1.12 .9' }, { scale: '1' }], { duration: 260, easing: 'ease-out' });
        paint();
        doc.defaultView.setTimeout(() => doc.defaultView.dispatchEvent(new Event('resize')), 450);
      };
    }
    paint();
  }
  function buildStyles() {
    buildSize();
    const owned = new Set(skinOptions().map(([id]) => id));
    for (const size of SIZES) owned.delete(size);
    for (const id of SOLD_FACES) {
      owned.delete(id);
      if (doc.documentElement.hasAttribute('data-combined-preview')) owned.add(id);
    }
    const gallery = node('div', content, '', 'bshop-gallery');
    for (const style of visibleStyles(owned)) {
      const card = node('div', gallery, '', 'cursor-kit-row bshop-card');
      card.setAttribute('role', 'group'); card.setAttribute('aria-label', style.name);
      card.dataset.style = style.id;
      const stage = node('div', card, '', 'cursor-kit-print bshop-stage');
      stage.setAttribute('aria-hidden', 'true');
      preview(stage, style.id);
      const copy = node('div', card, '', 'cursor-kit-copy');
      node('strong', copy, style.name); node('small', copy, style.hint);
      const act = node('button', card, '', 'cursor-kit-action');
      act.dataset.buttonSkin = style.id;
      const item = ITEM_OF[style.id];
      act.onclick = () => {
        if (ctx.paused || act.disabled) return;
        const state = styleState(style, owned);
        if (state === 'shop') {
          if (ctx.state.fuzz < style.price) return;
          if (ctx.spend(style.price, 'face_' + style.id) !== style.price) return;
          ctx.store.set('faces', { ...bag(ctx).faces, [style.id]: true });
          bought(card, style.name);
        } else if (state === 'locked' || state === 'gated') return;
        chooseSkin(style.id);
        squish(ctx, ctx.button.el);
        sfx(ctx, 'sfx/ui_equip.mp3', 0.4);
        if (!quiet()) ctx.anim(stage, [{ scale: '1' }, { scale: '1.06 .94' }, { scale: '1' }], { duration: 300, easing: 'ease-out' });
        syncStyles();
      };
      cards.push({ style, card, act, owned, item });
      if (boughtFace(ctx, style.id)) stamp(card, false);
    }
    syncStyles();
  }
  function syncStyles() {
    const worn = ctx.button.el.dataset.skin;
    for (const { style, card, act, owned, item } of cards) {
      const state = styleState(style, owned);
      const shut = state === 'locked' || state === 'gated';
      card.toggleAttribute('data-locked', shut);
      if (shut && !card.querySelector('.bshop-lock')) node('span', card.querySelector('.bshop-stage'), 'Locked', 'bshop-lock');
      else if (!shut) card.querySelector('.bshop-lock')?.remove();
      let text;
      if (state === 'locked') text = 'Comes with ' + (BY_ID[item]?.name || 'its theme');
      else if (state === 'gated') text = gateLine(gateOf('style', style.id));
      else if (state === 'shop') {
        const short = Math.max(0, style.price - ctx.state.fuzz);
        text = short ? whole(short) + ' short' : 'Buy · ' + whole(style.price);
      } else text = worn === style.id ? 'Equipped' : 'Equip';
      if (act.textContent !== text) act.textContent = text;
      act.disabled = state === 'locked' || state === 'gated' || (state === 'owned' && worn === style.id) || (state === 'shop' && ctx.state.fuzz < style.price);
      act.toggleAttribute('data-worn', state === 'owned' && worn === style.id);
      priceButton(act, state === 'shop' ? style.price : 0);
    }
  }

  /* ---- Wave ---- */
  function redraw(force = false) {
    if (!sample?.isConnected || !sample.clientWidth) return;
    const w = sample.clientWidth, h = sample.clientHeight;
    const dpr = Math.min(2, doc.defaultView.devicePixelRatio || 1);
    const key = [w, h, dpr, selected.id].join('/');
    if (key === renderKey && quiet() && !force) return;
    renderKey = key;
    if (sample.width !== Math.round(w * dpr) || sample.height !== Math.round(h * dpr)) { sample.width = Math.round(w * dpr); sample.height = Math.round(h * dpr); }
    const g = sample.getContext('2d'); g.setTransform(sample.width / w, 0, 0, sample.height / h, 0, 0);
    drawWave(g, w, h, { style: selected.id, time: clock, hits, preview: true });
  }
  function equippedWave(id) {
    const b = bag(ctx);
    return !doc.documentElement.dataset.waveStudy && chosenWave(b) === id && Boolean(b.waveStyle || ctx.has('a_hum'));
  }
  function syncWave() {
    if (tab !== 'Wave' || !action) return;
    for (const [id, b] of choices) {
      const owned = ownedWave(bag(ctx), id);
      b.toggleAttribute('data-owned', owned);
      const tag = b.querySelector('small'), style = styles.find((s) => s.id === id);
      const t = equippedWave(id) ? 'On button' : owned ? 'Owned' : studies || rowOpen(ctx.state, 'wave', id) ? whole(style.price) : 'Locked';
      if (tag.textContent !== t) tag.textContent = t;
    }
    if (studies) {
      action.disabled = doc.documentElement.dataset.waveStudy === selected.id;
      action.textContent = action.disabled ? 'On button' : 'Try on button';
      return;
    }
    const owned = ownedWave(bag(ctx), selected.id), equipped = equippedWave(selected.id);
    const open = rowOpen(ctx.state, 'wave', selected.id, owned);
    const short = Math.max(0, selected.price - ctx.state.fuzz);
    const text = equipped ? 'Equipped' : owned ? 'Equip' : !open ? gateLine(gateOf('wave', selected.id)) : short ? whole(short) + ' short' : 'Buy · ' + whole(selected.price);
    if (action.textContent !== text) action.textContent = text;
    action.disabled = Boolean(equipped || !open || (!owned && short));
    action.toggleAttribute('data-worn', equipped);
    priceButton(action, owned || !open ? 0 : selected.price);
  }
  function select(style) {
    selected = style; hits.length = 0;
    heading.textContent = style.name; hint.textContent = style.description;
    for (const [id, b] of choices) b.setAttribute('aria-pressed', String(id === style.id));
    syncWave(); redraw(true);
  }
  function paintSwatches() {
    for (const [id, button] of choices) drawWave(button.querySelector('canvas').getContext('2d'), 230, 44, { style: id, time: 1.7, hits: [], preview: true });
    redraw(true);
  }
  function buildWave() {
    const wrap = node('div', content, '', 'bshop-wave');
    const stage = node('div', wrap, '', 'cursor-kit-print bshop-sample');
    sample = node('canvas', stage); sample.setAttribute('aria-hidden', 'true');
    const test = node('button', stage, 'Test press');
    test.onclick = () => { if (!quiet()) addWavePress(hits, clock); };
    const buy = node('div', wrap, '', 'bshop-buy');
    const copy = node('div', buy, '', 'cursor-kit-copy');
    heading = node('strong', copy); hint = node('small', copy);
    action = node('button', buy, '', 'cursor-kit-action');
    action.onclick = () => {
      if (ctx.paused || action.disabled) return;
      if (studies) { doc.documentElement.dataset.waveStudy = selected.id; syncWave(); return; }
      if (!ownedWave(bag(ctx), selected.id)) {
        if (ctx.state.fuzz < selected.price || !rowOpen(ctx.state, 'wave', selected.id)) return;
        if (ctx.spend(selected.price, 'wave_' + selected.id) !== selected.price) return;
        ctx.store.set('waveStyles', { ...bag(ctx).waveStyles, [selected.id]: true });
        sfx(ctx, 'sfx/reveal_chime.mp3', 0.45);
        status.textContent = selected.name + ' is yours.';
        sparks(stage);
      } else sfx(ctx, 'sfx/ui_equip.mp3', 0.4);
      delete doc.documentElement.dataset.waveStudy;
      ctx.store.set('waveStyle', selected.id);
      ctx.emit('wave-equipped', { id: selected.id });
      squish(ctx, ctx.button.el);
      if (!quiet()) addWavePress(hits, clock);
      syncWave();
    };
    const grid = node('div', wrap, '', 'bshop-choices');
    for (const style of styles) {
      const b = node('button', grid); b.dataset.waveChoice = style.id;
      const thumb = node('canvas', b); thumb.width = 230; thumb.height = 44; thumb.setAttribute('aria-hidden', 'true');
      node('span', b, style.name); node('small', b, '');
      b.onclick = () => select(style); choices.set(style.id, b);
    }
    select(selected);
    paintSwatches();
    const field = waveFields(doc);
    if (!field.complete) field.addEventListener('load', paintSwatches, { once: true });
  }

  /* ---- Bouncing copies: Match button, Mixed faces and Normal are free; the rest are bought once (bag.copyOwned) ---- */
  let copyButtons = [];
  function syncCopies() {
    if (tab !== 'Bouncing copies') return;
    for (const { button, field, value, tag } of copyButtons) {
      const price = copyPrice(field, value), owned = ownedCopy(bag(ctx), field, value);
      const short = Math.max(0, price - ctx.state.fuzz);
      const t = owned ? (chosenCopy(bag(ctx), field) === value ? 'On' : price ? 'Owned' : 'Free') : short ? whole(short) + ' short' : whole(price);
      if (tag.textContent !== t) tag.textContent = t;
      if (owned) { delete button.dataset.price; button.removeAttribute('data-afford'); button.style.removeProperty('--fill'); }
      else { button.dataset.price = price; button.style.setProperty('--fill', String(Math.min(1, ctx.state.fuzz / price))); button.toggleAttribute('data-afford', !short); }
      button.toggleAttribute('data-owned', owned);
    }
  }
  function buildCopies() {
    copyButtons = [];
    const b0 = bag(ctx);
    const skin = ctx.button.el.dataset.skin;
    const fits = [];
    function group(label, list, field) {
      const current = chosenCopy(b0, field);
      node('h3', content, label);
      const grid = node('div', content, '', 'button-bounce-options');
      for (const [value, title] of Object.entries(list)) {
        const button = node('button', grid);
        button.setAttribute('aria-pressed', String(value === current));
        button.dataset[field] = value;
        const icon = node('span', button);
        /* The sample shows the choice as if owned, so a player sees what they buy. */
        const look = { ...b0, [field]: value, copyOwned: { ...b0.copyOwned, [field + ':' + value]: true } };
        const kind = field === 'copySize' ? bouncingLook(b0, skin, false, Boolean(ctx.button.el.dataset.artFace)) : bouncingLook(look, skin, false, Boolean(ctx.button.el.dataset.artFace));
        renderBounce(icon, kind, emiPose(look, 0), '', 'T&C', ctx.button.el, kind === 'face' ? skin : '');
        if (field === 'copySize') icon.style.scale = String({ small: .55, normal: .8, big: 1 }[value]);
        if (kind === 'face') { icon.style.overflow = 'hidden'; fits.push([icon, 1]); }
        node('span', button, title);
        const tag = node('small', button, '');
        copyButtons.push({ button, field, value, tag });
        button.onclick = () => {
          if (ctx.paused) return;
          const price = copyPrice(field, value);
          if (!ownedCopy(bag(ctx), field, value)) {
            if (ctx.state.fuzz < price) { status.textContent = whole(price - ctx.state.fuzz) + ' short.'; return; }
            if (ctx.spend(price, 'copy_' + field + '_' + value) !== price) return;
            ctx.store.set('copyOwned', { ...bag(ctx).copyOwned, [field + ':' + value]: true });
            sfx(ctx, 'sfx/reveal_chime.mp3', 0.45);
            status.textContent = title + ' is yours.';
          } else sfx(ctx, 'sfx/ui_equip.mp3', 0.35);
          ctx.store.set(field, value);
          ctx.emit('bounce-equipped', { [field]: value });
          buildTab('Bouncing copies');
          const again = content.querySelector('[data-' + ({ emiPose: 'emi-pose', copySize: 'copy-size' }[field] || 'bouncer') + '="' + value + '"]');
          again?.focus();
          if (price && again) sparks(again);
        };
      }
    }
    group('Bouncing artwork', COPY_CHOICES, 'bouncer');
    if (chosenCopy(b0, 'bouncer') === 'emi') group('EMI expression', EMI_CHOICES, 'emiPose');
    group('Size', COPY_SIZES, 'copySize');
    syncCopies();
    /* Face samples are measured once the card is on screen, then fit by height inside a wide slot and centred, so a
     * wide face (the Bambi Cloud login) still reads instead of shrinking to a sliver. */
    if (fits.length) doc.defaultView.requestAnimationFrame(() => {
      for (const [icon, k] of fits) {
        const art = icon.firstElementChild;
        if (!art || !art.offsetWidth || !art.offsetHeight) continue;
        const sw = icon.clientWidth || 100, sh = icon.clientHeight || 34;
        const z = Math.min(sh / art.offsetHeight, sw / art.offsetWidth) * k;
        art.style.transform = 'scale(' + z + ')';
        art.style.left = ((sw - art.offsetWidth * z) / 2) + 'px';
        art.style.top = ((sh - art.offsetHeight * z) / 2) + 'px';
      }
    });
  }

  /* A part whose gate is not owned yet shows one plain line instead of its rows (owner 2026-11-08). */
  const shutLine = (key) => node('p', content, gateLine(key) + '.', 'shop-gate');
  function marks() {
    for (const [label, b] of tabButtons) if (fresh.some((k) => SHOP_GATES[k].tab === label)) newMark(b);
    if (fresh.includes('emi')) newMark(content.querySelector('[data-style="emi"]'));
    if (fresh.includes('kinetic')) for (const [id, b] of choices) if (gateOf('wave', id) === 'kinetic') newMark(b);
  }
  function buildTab(name) {
    tab = name; sample = null; action = null; choices.clear(); cards = []; content.replaceChildren();
    for (const [label, b] of tabButtons) b.setAttribute('aria-pressed', String(label === name));
    if (name === 'Wave') { if (studies || gateOpen(ctx.state, 'wave') || gateOpen(ctx.state, 'kinetic')) buildWave(); else shutLine('wave'); }
    else if (name === 'Bouncing copies') { if (gateOpen(ctx.state, 'copies')) buildCopies(); else shutLine('copies'); }
    else buildStyles();
    marks();
  }
  for (const name of ['Styles', 'Wave', 'Bouncing copies']) {
    const b = node('button', tabs, name);
    b.onclick = () => { if (tab !== name) { status.textContent = ''; buildTab(name); } };
    tabButtons.set(name, b);
  }
  const paintBalance = () => { const t = whole(ctx.state.fuzz) + ' ' + ctx.brand.currency; if (balance.textContent !== t) balance.textContent = t; };
  /* Opening the shop meets what is new; the marks stay up for this visit. */
  let seen = ctx.store.get('shopSeen');
  if (!seen) { seen = {}; ctx.store.set('shopSeen', seen); }
  const fresh = markSeen(ctx.state, 'button', seen);
  buildTab(tab);
  paintBalance();
  return ctx.modal({
    title: 'Button Shop', className: 'cursor-shop-window button-shop', node: body, noDefault: true, buttons: [{ label: 'Done' }],
    onOpen() {
      redraw(true);
      stopFrame = ctx.frame((dt) => {
        elapsed += dt; slow += dt;
        if (slow >= 0.2) { slow = 0; paintBalance(); if (tab === 'Styles') syncStyles(); else if (tab === 'Bouncing copies') syncCopies(); else syncWave(); }
        if (tab !== 'Wave' || elapsed < 1 / 30) return;
        if (quiet()) hits.length = 0; else clock += elapsed;
        elapsed = 0; redraw();
      });
    },
    onClose() { if (choices.size) waveFields(doc).removeEventListener('load', paintSwatches); stopFrame?.(); onClose(); },
  });
}
