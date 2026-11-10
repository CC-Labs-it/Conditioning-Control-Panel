/* ui/unlocks/fate/themes.js - the new cards of the theme fate sets (handoff-1110/TC-FATE-CARDS-BRIEF.md): ft_bubble
 * (Bimbodoll seed), ft_scan (Unit Complete seed), ft_tag (Unit Complete card 4). The moved theme rows (bi_gloss,
 * bi_giggle, bi_pics, dr_hive, dr_update) keep their entries in their theme files. Numbers and the pure rules:
 * ./themes/logic.js.
 *
 * ft_bubble  one pink bubble (assets/img/bubble.png) in the bottom-left corner; a little bigger after every purchase,
 *            bobs, holds a faint reflection of the page colour; a pat makes it wobble. When Bimbodoll starts it drifts
 *            into the button and fades as the ending's own bubble takes over from there.
 * ft_scan    once a minute of game time a thin green line sweeps down the page; each thing it crosses pays a tick and
 *            gets a brief green edge. It always leaves a gap round the Name Tag.
 * ft_tag     a pinned paper name tag (the unit number once Unit Number renamed the player). Drag it anywhere. Unit
 *            Complete lifts this tag for its last shot (unit-complete-name.js).
 *
 * Cosmetic randomness is Math.random; ctx.rng is never touched. Gentle / reduced motion: fades only. */

import { frameRect, LITE } from '../../perf.js';
import { sfx } from '../basics/fx.js';
import {
  laterBuys, bubbleSize, SCAN_EVERY_MS, SCAN_SWEEP_S, SCAN_SWEEP_SOFT_S, SCAN_CAP, SCAN_CAP_LITE,
  planScan, lineSegments, scanPay, tagLabel, placeTag,
} from './themes/logic.js';

const GREEN = '#3dff8e';
const MONO = 'Consolas,"Courier New",monospace';
const HAND = '"Segoe Print","Comic Sans MS","Chalkboard SE",cursive';
const calm = (ctx) => ctx.gentle || ctx.reduced;
const finePointer = (doc) => !!doc.defaultView?.matchMedia?.('(pointer: fine)').matches;
const fresh = (ctx) => { const o = ctx.state.order; return !ctx.restored && o[o.length - 1] === ctx.item.id; };

/* ---------------------------------------------------------------------------------------------------------------- */
/* Little Bubble                                                                                                      */
/* ---------------------------------------------------------------------------------------------------------------- */

const BUBBLE_CSS = `
.ftb{position:absolute;left:var(--ftb-x,22px);bottom:calc(var(--dock-h,0px) + 26px + env(safe-area-inset-bottom,0px));
  width:var(--ftb-s,44px);height:var(--ftb-s,44px);pointer-events:none;
  transition:width .7s cubic-bezier(.3,1.6,.5,1),height .7s cubic-bezier(.3,1.6,.5,1);}
.ftb.soft{transition:width 1.2s ease,height 1.2s ease;}
.ftb.pat{pointer-events:auto;cursor:pointer;}
.ftb>.ftb-in{position:absolute;inset:0;}
.ftb .ftb-body{position:absolute;inset:14%;border-radius:50%;
  background:radial-gradient(ellipse 58% 26% at 50% 80%,color-mix(in srgb,var(--page,#ffdcee) 70%,#fff) 0,transparent 100%),
    radial-gradient(circle at 38% 34%,rgba(255,226,242,.55) 0,rgba(255,122,192,.42) 58%,rgba(232,70,160,.62) 100%);
  box-shadow:inset 0 0 0 1px rgba(214,60,150,.35),0 3px 8px rgba(190,40,130,.18);}
.ftb .ftb-art{position:absolute;inset:0;background:url("assets/img/bubble.png") center/100% 100% no-repeat;}
.ftb .ftb-glint{position:absolute;left:29%;top:25%;width:16%;height:9%;border-radius:50%;background:rgba(255,255,255,.85);transform:rotate(-32deg);}
`;

function littleBubble(ctx) {
  const doc = ctx.world.ownerDocument;
  ctx.css(BUBBLE_CSS);
  const el = ctx.el('div', '', null, 'over');
  el.className = 'ftb';
  el.dataset.ftBubble = '';
  el.setAttribute('aria-hidden', 'true');
  el.style.setProperty('--ftb-x', LITE ? '12px' : '22px');
  const inner = ctx.el('div', '', null, el); inner.className = 'ftb-in';
  ctx.el('i', '', null, inner).className = 'ftb-body';
  const art = ctx.el('i', '', null, inner); art.className = 'ftb-art';
  ctx.el('i', '', null, inner).className = 'ftb-glint';

  /* Pattable only where it cannot sit on a shop tile: a fine pointer on a page wide enough to keep the corner free. */
  const pattable = () => finePointer(doc) && ctx.bounds().w >= 900;
  el.classList.toggle('pat', pattable());
  el.classList.toggle('soft', calm(ctx));

  const size = () => bubbleSize(laterBuys(ctx.state.order), LITE);
  const resize = () => el.style.setProperty('--ftb-s', `${size()}px`);
  resize();

  let bob = null, turn = null;
  const motion = () => {
    bob?.cancel(); turn?.cancel(); bob = turn = null;
    if (calm(ctx)) return;
    bob = ctx.anim(inner, [{ translate: '0 0' }, { translate: '0 -5px' }, { translate: '0 0' }], { duration: 5600, iterations: Infinity, easing: 'ease-in-out' });
    turn = ctx.anim(art, [{ rotate: '0deg' }, { rotate: '360deg' }], { duration: 48000, iterations: Infinity });
  };
  motion();
  let wasCalm = calm(ctx);
  ctx.every(1000, () => {
    if (calm(ctx) !== wasCalm) { wasCalm = calm(ctx); el.classList.toggle('soft', wasCalm); motion(); }
    el.classList.toggle('pat', pattable());
  });

  /* Arrival: it blows up from nothing. */
  if (fresh(ctx)) {
    ctx.anim(el, calm(ctx) ? [{ opacity: 0 }, { opacity: 1 }] : [{ opacity: 0, scale: '.2' }, { opacity: 1, scale: '1.15', offset: 0.7 }, { opacity: 1, scale: '1' }],
      { duration: calm(ctx) ? 700 : 900, easing: 'ease-out' });
  }

  ctx.on('buy', resize);
  ctx.on('fork', resize);

  let lastPat = -Infinity;
  el.addEventListener('pointerdown', (e) => {
    if (!e.isTrusted || ctx.paused) return;
    e.stopPropagation();
    if (calm(ctx)) ctx.anim(inner, [{ opacity: 1 }, { opacity: 0.6 }, { opacity: 1 }], { duration: 700 });
    else ctx.anim(el, [{ scale: '1 1' }, { scale: '1.18 .84' }, { scale: '.9 1.1' }, { scale: '1.05 .96' }, { scale: '1 1' }], { duration: 560, easing: 'ease-out' });
    if (ctx.activeMs - lastPat > 1500) { lastPat = ctx.activeMs; sfx(ctx, 'pop/Pop.mp3', 0.12); }
  });

  /* Bimbodoll: the little bubble drifts into the button and the ending's bubble grows from there. The ending layer keeps
   * it out of the swallow (the take skips #l-ending). Any other ending: it stays until the page goes. */
  ctx.on('ending', ({ id } = {}) => {
    if (id !== 'bi_end' || !el.isConnected) return;
    bob?.cancel(); turn?.cancel();
    const from = el.getBoundingClientRect(), wrap = ctx.parts.buttonWrap;
    const to = wrap ? wrap.getBoundingClientRect() : null;
    el.classList.remove('pat');
    el.style.transition = 'none';
    ctx.layer('ending').appendChild(el);
    if (calm(ctx) || !to?.width || !from.width) {
      ctx.anim(el, [{ opacity: 1 }, { opacity: 0 }], { duration: 700, fill: 'forwards' });
      return;
    }
    const dx = (to.left + to.width / 2) - (from.left + from.width / 2), dy = (to.top + to.height / 2) - (from.top + from.height / 2);
    const k = Math.max(0.5, (Math.max(to.width, to.height) * 1.1 + 12) / from.width);
    ctx.anim(el, [
      { translate: '0 0', scale: '1', opacity: 1 },
      { translate: `${(dx * 0.55).toFixed(1)}px ${(dy * 0.5 - 30).toFixed(1)}px`, scale: (1 + (k - 1) * 0.4).toFixed(3), opacity: 1, offset: 0.55 },
      { translate: `${dx.toFixed(1)}px ${dy.toFixed(1)}px`, scale: k.toFixed(3), opacity: 0 },
    ], { duration: 900, easing: 'ease-in-out', fill: 'forwards' });
  });
  return {};
}

/* ---------------------------------------------------------------------------------------------------------------- */
/* Scan Line                                                                                                          */
/* ---------------------------------------------------------------------------------------------------------------- */

const SCAN_CSS = `
.fts-line{position:absolute;left:0;top:0;width:100%;height:0;pointer-events:none;will-change:transform;}
.fts-seg{position:absolute;top:-1px;height:2px;background:${GREEN};box-shadow:0 0 6px 1px rgba(61,255,142,.75),0 0 16px 3px rgba(61,255,142,.28);}
.fts-line.soft .fts-seg{top:-28px;height:56px;background:linear-gradient(180deg,transparent,rgba(61,255,142,.16) 45%,rgba(61,255,142,.24) 50%,rgba(61,255,142,.16) 55%,transparent);box-shadow:none;}
.fts-hit{position:absolute;left:0;top:0;border:1.5px solid ${GREEN};border-radius:3px;box-sizing:border-box;pointer-events:none;
  box-shadow:0 0 8px rgba(61,255,142,.55),inset 0 0 6px rgba(61,255,142,.3);opacity:0;}
.fts-skip{position:absolute;left:0;top:0;padding:1px 5px;border:1px solid ${GREEN};border-radius:2px;background:rgba(7,11,9,.82);
  color:${GREEN};font:700 10px/1.3 ${MONO};letter-spacing:.08em;pointer-events:none;opacity:0;white-space:nowrap;}
`;

/** The page objects one sweep can cross, read once when it starts (never in the frame loop). */
function scanCandidates(ctx) {
  const out = [];
  const add = (el, clip = null) => {
    if (!el || el.hidden || !el.isConnected || el.dataset?.ftTag != null) return;
    const r = frameRect(el);
    if (r.width && r.height) out.push({ el, rect: r, clip });
  };
  add(ctx.parts.counter);
  add(ctx.parts.buttonWrap);
  const shop = ctx.parts.shop;
  if (shop && !shop.hidden) {
    const clip = frameRect(shop);
    for (const row of shop.querySelectorAll('.row')) add(row, clip);
  }
  for (const name of ['meters', 'banner', 'screens']) for (const c of ctx.layer(name).children) add(c);
  for (const d of ctx.world.querySelectorAll('#l-side section.dk')) add(d);
  return out;
}

function scanLine(ctx) {
  ctx.css(SCAN_CSS);
  const fx = ctx.layer('fx');
  const line = ctx.el('div', '', null, fx); line.className = 'fts-line'; line.hidden = true; line.setAttribute('aria-hidden', 'true');
  const segs = [ctx.el('i', '', null, line), ctx.el('i', '', null, line)];
  for (const s of segs) s.className = 'fts-seg';
  const skipMark = ctx.el('div', '', 'SKIP', fx); skipMark.className = 'fts-skip'; skipMark.hidden = true;
  const hits = Array.from({ length: LITE ? 6 : 10 }, () => { const b = ctx.el('div', '', null, fx); b.className = 'fts-hit'; b.hidden = true; return b; });
  let hitN = 0, sweep = null, stopFrame = null;

  const hideAll = () => {
    stopFrame?.(); stopFrame = null; sweep = null;
    line.hidden = true; skipMark.hidden = true;
    for (const b of hits) b.hidden = true;
  };

  function flash(rect) {
    const b = hits[hitN++ % hits.length];
    b.hidden = false;
    b.style.transform = `translate(${(rect.left - 2).toFixed(1)}px,${(rect.top - 2).toFixed(1)}px)`;
    b.style.width = `${(rect.width + 4).toFixed(1)}px`; b.style.height = `${(rect.height + 4).toFixed(1)}px`;
    const soft = calm(ctx);
    const a = ctx.anim(b, soft ? [{ opacity: 0 }, { opacity: 0.8 }, { opacity: 0 }] : [{ opacity: 0 }, { opacity: 1, offset: 0.15 }, { opacity: 0 }], { duration: soft ? 1100 : 650, easing: 'ease-out' });
    b._ftsAnim = a;
    a.finished?.then(() => { if (b._ftsAnim === a) b.hidden = true; }, () => {});
  }

  function skipped(tag) {
    skipMark.hidden = false;
    skipMark.style.transform = `translate(${Math.round(Math.max(4, tag.left - 6))}px,${Math.round(Math.max(4, tag.top - 18))}px) rotate(-6deg)`;
    const a = ctx.anim(skipMark, [{ opacity: 0 }, { opacity: 1, offset: 0.2 }, { opacity: 1, offset: 0.7 }, { opacity: 0 }], { duration: calm(ctx) ? 1600 : 1300 });
    a.finished?.then(() => { skipMark.hidden = true; }, () => {});
    const el = ctx.world.querySelector('[data-ft-tag]');
    if (el && !calm(ctx)) ctx.anim(el, [{ transform: 'rotate(0deg)' }, { transform: 'rotate(-4deg)' }, { transform: 'rotate(3deg)' }, { transform: 'rotate(0deg)' }], { duration: 600 });
  }

  function paintSegs(list) {
    segs.forEach((s, i) => {
      const seg = list[i];
      s.hidden = !seg;
      if (seg) { s.style.left = `${seg[0].toFixed(1)}px`; s.style.width = `${Math.max(0, seg[1] - seg[0]).toFixed(1)}px`; }
    });
  }

  function start() {
    if (sweep || ctx.paused) return;
    const { w, h } = ctx.bounds();
    const tagEl = ctx.world.querySelector('[data-ft-tag]');
    const tr = tagEl && !tagEl.hidden ? frameRect(tagEl) : null;
    const skip = tr?.width ? { left: tr.left, top: tr.top, right: tr.right, bottom: tr.bottom } : null;
    const plan = planScan(scanCandidates(ctx), { w, h, cap: LITE ? SCAN_CAP_LITE : SCAN_CAP, skip });
    const soft = calm(ctx);
    sweep = { t: 0, dur: soft ? SCAN_SWEEP_SOFT_S : SCAN_SWEEP_S, plan, next: 0, w, h, skip, skipDone: !skip, soft, gap: null };
    line.classList.toggle('soft', soft);
    line.style.transform = 'translateY(-8px)';
    line.hidden = false;
    sfx(ctx, 'sfx/ui_equip.mp3', 0.12);
    stopFrame = ctx.frame(step);
  }

  function step(dt) {
    const s = sweep;
    if (!s) return;
    s.t += dt;
    const p = Math.min(1, s.t / s.dur), y = p * (s.h + 8) - 4;
    line.style.transform = `translateY(${y.toFixed(1)}px)`;
    const list = s.soft ? [[0, s.w]] : lineSegments(y, s.w, s.skip);
    const key = list.map((q) => q.join(':')).join('|');
    if (key !== s.gap) { s.gap = key; paintSegs(list); }
    if (!s.skipDone && y >= (s.skip.top + s.skip.bottom) / 2) { s.skipDone = true; skipped(s.skip); }
    while (s.next < s.plan.length && s.plan[s.next].y <= y) {
      const hit = s.plan[s.next++];
      flash(hit.rect);
      const got = ctx.pay(scanPay(ctx.rates().fps), 'ft_scan');
      if (got > 0 && (!LITE || s.next % 2 === 0)) {
        ctx.float(Math.min(s.w - 40, hit.rect.right - 10), hit.rect.top + 2, `+${ctx.fmt(got)}`, `color:${GREEN};font:700 13px ${MONO};text-shadow:0 1px 2px rgba(0,0,0,.6);`);
      }
    }
    if (p >= 1) { stopFrame?.(); stopFrame = null; sweep = null; line.hidden = true; }
  }

  if (fresh(ctx)) ctx.after(2500, start);
  ctx.every(SCAN_EVERY_MS, start);
  ctx.on('ending', hideAll);
  /* dev probe (local hosts only): start a sweep now */
  const view = ctx.world.ownerDocument.defaultView;
  if (view && /^(127\.0\.0\.1|localhost)$/.test(view.location?.hostname || '')) {
    view.__ftScan = { start, get sweep() { return sweep && { t: sweep.t, plan: sweep.plan.length, next: sweep.next }; } };
    ctx.cleanup(() => { if (view.__ftScan?.start === start) delete view.__ftScan; });
  }
  return { off: hideAll };
}

/* ---------------------------------------------------------------------------------------------------------------- */
/* Name Tag                                                                                                           */
/* ---------------------------------------------------------------------------------------------------------------- */

const TAG_CSS = `
[data-ft-tag]{position:absolute;left:0;top:0;width:max-content;max-width:min(42vw,190px);pointer-events:auto;touch-action:none;
  cursor:grab;user-select:none;-webkit-user-select:none;transform-origin:50% 0;rotate:-3deg;
  filter:drop-shadow(1px 2px 0 rgba(0,0,0,.28));}
[data-ft-tag].held{cursor:grabbing;}
[data-ft-tag] .ftt-card{position:relative;margin-top:7px;padding:0 0 7px;border-radius:6px;background:#fffdf6;border:2px solid #2b2328;overflow:hidden;text-align:center;}
[data-ft-tag] .ftt-head{display:block;padding:3px 10px 2px;background:#d2263a;color:#fff;font:800 13px/1.05 system-ui,sans-serif;letter-spacing:.12em;}
[data-ft-tag] .ftt-head small{display:block;font:600 9px/1.2 system-ui,sans-serif;letter-spacing:.04em;opacity:.9;}
[data-ft-tag] .ftt-name{display:block;padding:6px 10px 0;color:#1d1a22;font:700 17px/1.1 ${HAND};overflow-wrap:anywhere;}
[data-ft-tag].small{max-width:88px;}
[data-ft-tag].small .ftt-head{padding:2px 5px 1px;font-size:10px;letter-spacing:.08em;}
[data-ft-tag].small .ftt-head small{font-size:8px;}
[data-ft-tag].small .ftt-name{padding:4px 5px 0;font-size:12px;}
[data-ft-tag].small .ftt-card{padding-bottom:5px;}
[data-ft-tag] .ftt-pin{position:absolute;left:50%;top:0;width:12px;height:12px;margin-left:-6px;border-radius:50%;z-index:2;
  background:radial-gradient(circle at 35% 30%,#ff8d8d,#c3122a 60%,#6d0715);box-shadow:0 1px 1px rgba(0,0,0,.4);}
`;

function nameTagCard(ctx) {
  ctx.css(TAG_CSS);
  /* Read again on the timer: Unit Number can arrive (or roll its number) after the tag. */
  const labelNow = () => tagLabel({
    displayName: ctx.identity?.displayName, hasSerial: ctx.has('dr_serial'),
    serial: ctx.state.store?.dr_serial?.serial, seed: ctx.state.seed,
  });
  const label = labelNow();
  const tag = ctx.el('div', '', null, 'over');
  tag.dataset.ftTag = '';
  tag.dataset.ftLabel = label;
  tag.setAttribute('role', 'img');
  tag.setAttribute('aria-label', `Name tag: ${label}`);
  tag.title = 'Drag me';
  ctx.el('i', '', null, tag).className = 'ftt-pin';
  const card = ctx.el('div', '', null, tag); card.className = 'ftt-card';
  const head = ctx.el('b', '', null, card); head.className = 'ftt-head';
  head.append('HELLO');
  ctx.el('small', '', null, head).textContent = 'my name is';
  const name = ctx.el('span', '', null, card); name.className = 'ftt-name';
  name.textContent = label;

  let drag = null;
  /* Where it sits: a saved spot, else just left of the button (or under it when there is no room). Measured on a timer,
   * never per frame. */
  const place = () => {
    const { w, h } = ctx.bounds();
    tag.classList.toggle('small', w < 640);
    const tw = tag.offsetWidth || 120, th = tag.offsetHeight || 64;
    let spot = ctx.store.get('spot');
    if (!spot) {
      const wrap = ctx.parts.buttonWrap?.getBoundingClientRect();
      if (wrap?.width) {
        spot = wrap.left - 16 >= tw + 6
          ? { fx: (wrap.left - tw - 18) / w, fy: (wrap.top + wrap.height / 2 - th / 2) / h }
          : { fx: 6 / w, fy: (wrap.bottom + 10) / h };
      } else spot = { fx: 0.04, fy: 0.3 };
    }
    const at = placeTag(spot, { w, h, tw, th });
    tag.style.translate = `${at.x}px ${at.y}px`;
    return at;
  };
  let at = place();
  ctx.every(2500, () => {
    const now = labelNow();
    if (now !== tag.dataset.ftLabel) { tag.dataset.ftLabel = now; name.textContent = now; tag.setAttribute('aria-label', `Name tag: ${now}`); }
    if (!drag) at = place();
  });

  if (fresh(ctx)) {
    ctx.anim(tag, calm(ctx)
      ? [{ opacity: 0 }, { opacity: 1 }]
      : [{ opacity: 0, transform: 'translateY(-40px) rotate(-14deg)' }, { opacity: 1, transform: 'translateY(4px) rotate(5deg)', offset: 0.55 }, { opacity: 1, transform: 'rotate(-2deg)', offset: 0.8 }, { opacity: 1, transform: 'none' }],
    { duration: calm(ctx) ? 600 : 900, easing: 'ease-out' });
  }

  /* Drag by its own pointer capture: no window listeners. */
  const end = (save) => {
    if (!drag) return;
    tag.classList.remove('held');
    if (save) {
      const { w, h } = ctx.bounds();
      ctx.store.set('spot', { fx: at.x / w, fy: at.y / h });
    }
    drag = null;
  };
  tag.addEventListener('pointerdown', (e) => {
    if (!e.isTrusted || ctx.paused || e.button > 0) return;
    e.preventDefault(); e.stopPropagation();
    drag = { id: e.pointerId, ox: e.clientX - at.x, oy: e.clientY - at.y };
    tag.setPointerCapture?.(e.pointerId);
    tag.classList.add('held');
  });
  tag.addEventListener('pointermove', (e) => {
    if (!drag || e.pointerId !== drag.id) return;
    if (ctx.paused) { end(true); return; }
    const { w, h } = ctx.bounds();
    at = placeTag({ fx: (e.clientX - drag.ox) / w, fy: (e.clientY - drag.oy) / h }, { w, h, tw: tag.offsetWidth, th: tag.offsetHeight });
    tag.style.translate = `${at.x}px ${at.y}px`;
  });
  tag.addEventListener('pointerup', () => end(true));
  tag.addEventListener('pointercancel', () => end(true));
  ctx.on('pause', () => end(true));
  ctx.on('suspend', () => end(true));
  ctx.on('ending', () => end(false));
  return {};
}

export default {
  ft_bubble: { mount: littleBubble },
  ft_scan: { mount: scanLine },
  ft_tag: { mount: nameTagCard },
};
