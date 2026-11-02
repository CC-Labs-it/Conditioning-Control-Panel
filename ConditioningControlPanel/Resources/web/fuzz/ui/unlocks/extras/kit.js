/* ui/unlocks/extras/kit.js - small helpers the extras lane shares: a slot for a little widget (ctx.dock), one caption
 * line above the dock strip, and a raw element maker for things that come and go often (ctx.el keeps a reference to
 * everything it makes until the unlock dies). Nothing here runs at import time. */

let dock = null;

export const FONT = '"Barlow", "Segoe UI", system-ui, sans-serif';

/** A plain element that ctx does not track. Its parent must be something ctx made, or it must be removed by hand. */
export function h(tag, css, text, parent) {
  const e = document.createElement(tag);
  if (css) e.style.cssText = css;
  if (text != null) e.textContent = text;
  if (parent) parent.appendChild(e);
  return e;
}

export const pick = (ctx, list) => list[Math.floor(ctx.rng() * list.length)];

/** Not the same line twice in a row. */
export function picker(ctx, list) {
  let last = -1;
  return () => {
    if (list.length < 2) return list[0];
    let i = Math.floor(ctx.rng() * list.length);
    if (i === last) i = (i + 1) % list.length;
    last = i;
    return list[i];
  };
}

/** Is a card up? Then nothing new should pop over it. */
export const cardUp = () => !!document.getElementById('cards')?.childElementCount;

/** Captions along the bottom. Lives in `over`, above the dock strip, and ignores the pointer. */
function bottom(ctx) {
  if (dock?.root.isConnected) return dock;
  const root = h('div', 'position:absolute;left:0;right:0;bottom:var(--dock-h,0px);z-index:2;display:flex;flex-direction:column;align-items:center;gap:4px;padding:0 8px max(6px,env(safe-area-inset-bottom,0));pointer-events:none;', null, ctx.layer('over'));
  const lines = h('div', 'display:flex;flex-direction:column;align-items:center;gap:3px;width:100%;', null, root);
  dock = { root, lines };
  return dock;
}

/** Where the reserved space along the bottom starts: every caption row counts even while it is between lines. */
export function dockTop(ctx) {
  const H = ctx.bounds().h, strip = document.getElementById('dock-strip')?.offsetHeight || 0;
  if (!dock?.root.isConnected) return H - strip;
  return H - strip - dock.lines.childElementCount * 34 - 8;
}

/** A dock slot has room on every screen: a rail on a wide page, the tray on a phone. So a widget always draws its full size. */
export const roomy = () => true;

/** A place for a small widget: ctx.dock, centred in its slot. */
export function slot(ctx, css = '') {
  const base = 'pointer-events:auto;flex:none;box-sizing:border-box;margin:0 auto;';
  return { el: ctx.el('div', base + css, null, ctx.dock()), wide: true };
}

/** One caption row per unlock. Silent audio shows here, so Sleep Story and friends still make sense. */
export function captions(ctx, icon = '') {
  const row = ctx.el('div', `display:none;max-width:min(560px,100%);padding:4px 10px;background:rgba(20,16,30,.86);color:#fff;font:600 15px/1.25 ${FONT};text-align:center;border-radius:4px;`, null, bottom(ctx).lines);
  ctx.css('#shop{padding-bottom:130px}');
  let hide = null;
  return {
    el: row,
    show(text, ms = 5000) {
      hide?.();
      /* One line at a time on a narrow page: three stacked lines would sit on the shop. */
      if (ctx.bounds().w < 1100) for (const other of row.parentElement.children) if (other !== row) other.style.display = 'none';
      row.style.display = 'block';
      row.textContent = ctx.brand.text(icon ? `${icon} ${text}` : text);
      ctx.anim(row, [{ opacity: 0 }, { opacity: 1 }], { duration: 350, easing: 'ease-out' });
      hide = ctx.after(ms, () => {
        ctx.anim(row, [{ opacity: 1 }, { opacity: 0 }], { duration: 350 }).onfinish = () => { row.style.display = 'none'; };
      });
    },
  };
}
