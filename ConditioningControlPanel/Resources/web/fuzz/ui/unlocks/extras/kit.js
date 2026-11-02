/* ui/unlocks/extras/kit.js - small helpers the extras lane shares: a slot for a little widget (a rail on a wide screen,
 * a strip along the bottom on a phone), one caption dock, and a raw element maker for things that come and go often
 * (ctx.el keeps a reference to everything it makes until the unlock dies). Nothing here runs at import time. */

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

/** The bottom dock: captions above a strip of little widgets. Lives in `over`, ignores the pointer except its widgets. */
function bottom(ctx) {
  if (dock?.root.isConnected) return dock;
  const root = h('div', 'position:absolute;left:0;right:0;bottom:0;z-index:2;display:flex;flex-direction:column;align-items:center;gap:4px;padding:0 8px max(6px,env(safe-area-inset-bottom,0));pointer-events:none;', null, ctx.layer('over'));
  const lines = h('div', 'display:flex;flex-direction:column;align-items:center;gap:3px;width:100%;', null, root);
  const strip = h('div', 'display:flex;justify-content:safe center;align-items:flex-end;gap:6px;max-width:100%;overflow-x:auto;scrollbar-width:none;pointer-events:auto;', null, root);
  dock = { root, lines, strip };
  return dock;
}

/** Where the dock's reserved space starts: every caption row counts even while it is between lines. */
export function dockTop(ctx) {
  const H = ctx.bounds().h;
  if (!dock?.root.isConnected) return H;
  const strip = dock.strip.childElementCount ? dock.strip.offsetHeight + 4 : 0;
  return H - strip - dock.lines.childElementCount * 34 - 8;
}

/** A place for a small widget. The rail on a wide screen, otherwise the bottom strip, and the shop scrolls clear of it. */
export function slot(ctx, css = '') {
  const wide = ctx.bounds().w >= 1100;
  const base = 'pointer-events:auto;flex:none;box-sizing:border-box;';
  if (wide) return { el: ctx.el('div', base + css, null, ctx.layer('side')), wide };
  ctx.css('#shop{padding-bottom:84px}');
  return { el: ctx.el('div', base + css, null, bottom(ctx).strip), wide };
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
