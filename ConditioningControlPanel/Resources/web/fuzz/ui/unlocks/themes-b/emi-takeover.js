/* ui/unlocks/themes-b/emi-takeover.js - EMI Takeover, the goon lane's ending take (owner notes 2026-11-09, v3 brief
 * handoff-1109/TC-EMI-TAKEOVER-V3-BRIEF.md). The run-up rows live in emi-swarm.js.
 *
 * go_end take, one timeline on the game clock (numbers in sim/takeover-end.js):
 *   fill   the front grid arrives row after row like pixels, each screen flashing pictures (under 3 Hz); behind it
 *          EMIs of different sizes pile in from where the swarm stood until no page shows through
 *   shut   every screen dark at once, about 1 s
 *   drop   pictures only on the grid pixels of DROP (DR over OP on a portrait phone)
 *   error  error sound, a small system dialog with the game logo, a short shake (none under Gentle)
 *   frozen every animation and sound stops
 *   giant  a giant EMI (the shipped idle sprite scaled whole) pops up, blinks, then ^_~ on body-smug
 *   close  the eyelid shuts to black (Gentle: a plain fade). Then the engine goes straight to the recap.
 *
 * No bubbles, nothing follows or gathers round the pointer. Pictures are DOM <img> from ctx.pictures, never a canvas.
 * Wake Up's group stays clear: no EMI is made under it. */

import { LITE } from '../../perf.js';
import * as T from '../../../sim/takeover.js';
import swarmRows, { liveSwarm } from './emi-swarm.js';
import { IDLE_FACES, rand, any, CSS, bakeFaces, sprite, controlsRect, morePictures, tilesOf } from './emi-kit.js';

const TAKE_CSS = `
.et-front,.et-back{position:absolute;inset:0;pointer-events:none;}
.et-dlg{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);width:min(340px,calc(100% - 32px));background:#ece9e2;color:#111;border:1px solid #2a2a2a;box-shadow:3px 4px 0 rgba(0,0,0,.45);font:13px/1.35 "Segoe UI",system-ui,sans-serif;z-index:6;}
.et-dlg .tb{display:flex;align-items:center;gap:8px;padding:4px 8px;background:#2a1f3d;color:#fff;font-weight:700;}
.et-dlg .tb span{flex:1;}
.et-dlg .tb i{font-style:normal;opacity:.8;}
.et-dlg .bd{display:flex;gap:12px;align-items:center;padding:12px 14px 6px;}
.et-dlg .bd img{position:static;width:72px;height:auto;flex:none;}
.et-dlg .bd p{margin:0 0 4px;}
.et-dlg .ft{display:flex;justify-content:flex-end;padding:4px 12px 10px;}
.et-dlg .ft b{min-width:64px;text-align:center;padding:3px 10px;border:1px solid #555;background:#f7f6f2;font-weight:400;}
`;

let aborted = false;
/** True when the last take ended on panic. */
export const emiTakeoverAborted = () => aborted;

export function emiTakeoverTake(ctx) {
  aborted = false;
  const doc = ctx.button.el.ownerDocument, view = doc.defaultView;
  const calm = !!(ctx.gentle || ctx.reduced);
  ctx.css(CSS);
  ctx.css(TAKE_CSS);
  bakeFaces(doc);
  const box = ctx.bounds();
  const W = box.w || view.innerWidth, Hfull = box.h || view.innerHeight;
  /* the bottom tab strip sits outside the page and stays where it is: the front grid ends above it */
  const strip = doc.getElementById('dock-strip');
  const H = Hfull - (strip && !strip.hidden ? strip.offsetHeight : 0);
  const walls = controlsRect(doc);
  const grid = T.dropGrid({ w: W, h: H, controls: walls });
  const N = grid.cells.length, size = grid.pitch;

  /* the swarm EMIs become the first back-layer EMIs, where they stand and at their size */
  const live = liveSwarm();
  const pts = live?.points?.() || [];
  const sz = live?.sizes?.() || [];
  const seeds = pts.map((p, i) => ({ x: p.x, y: p.y, size: sz[i] || size * 1.6 }));
  live?.hide?.();
  const pile = T.backPile({ w: W, h: Hfull, pitch: size, seeds, controls: walls, lite: LITE });
  const B = pile.length;
  const at = T.phaseStarts({ calm });

  return new Promise((resolve) => {
    let done = false;
    const finish = () => { if (done) return; done = true; delete view.__emiTakeoverProbe; resolve(); };
    ctx.on('suspend', (s) => { if (s?.on && s.reason === 'panic') { aborted = true; finish(); } });

    const root = ctx.el('div', 'position:absolute;inset:0;overflow:hidden;pointer-events:auto;background:rgba(22,17,13,0);', null, 'ending');
    const back = ctx.el('div', '', null, root); back.className = 'et-back';
    const front = ctx.el('div', '', null, root); front.className = 'et-front';
    /* the backing only ever shows under Wake Up's corner: everywhere else an EMI covers it */
    ctx.anim(root, [{ backgroundColor: 'rgba(22,17,13,0)' }, { backgroundColor: 'rgba(22,17,13,1)' }], { duration: T.TAKE.BACK_S * 1000, fill: 'both', easing: 'ease-in' });

    const pool = [];
    morePictures(ctx, pool, LITE ? 3 : 6);
    const tiles = tilesOf(ctx);
    const pic = (i) => (pool.length ? pool[i % pool.length].url : tiles[i % tiles.length]);
    const kinds = () => (pool.length || tiles.length);
    const credit = ctx.el('div', 'position:absolute;right:6px;bottom:4px;z-index:5;font:11px var(--font);color:#fff;text-shadow:0 0 2px #000,0 0 2px #000;', 'via Scrolller', root);
    credit.hidden = true;
    const showCredit = (on) => { credit.hidden = !(on && pool.some((p) => p.credit)); };

    const cells = new Array(N).fill(null), backs = new Array(B).fill(null);
    let gridIn = 0, backIn = 0;
    const flash = (s, gapBack) => {
      /* one screen's next flash: a picture, now and then her face */
      if (Math.random() < 0.2) s.show(null); else s.show(pic(Math.floor(rand(0, kinds() * 4))));
      s.at = t + T.flickerGap(Math.random, LITE, gapBack) / 1000;
    };
    const putFront = (i) => {
      const cell = grid.cells[i];
      const s = sprite(doc, size);
      s.el.style.transform = `translate(${cell.x.toFixed(1)}px,${cell.y.toFixed(1)}px)`;
      s.setFace(any(IDLE_FACES));
      s.lit = cell.lit;
      front.appendChild(s.el);
      cells[i] = s;
      if (calm) { s.show(pic(i)); ctx.anim(s.el, [{ opacity: 0 }, { opacity: 1 }], { duration: 400 }); }
      else flash(s, false);
    };
    const putBack = (k) => {
      const p = pile[k];
      const s = sprite(doc, p.size);
      s.el.style.transform = `translate(${p.x.toFixed(1)}px,${p.y.toFixed(1)}px)`;
      s.setFace(any(IDLE_FACES));
      back.appendChild(s.el);
      backs[k] = s;
      s.isBack = true;
      if (calm) { if (Math.random() < 0.4) s.show(pic(k)); if (!p.seed) ctx.anim(s.el, [{ opacity: 0 }, { opacity: 1 }], { duration: 500 }); }
      else { flash(s, true); if (!p.seed && !LITE) ctx.anim(s.el, [{ transform: `${s.el.style.transform} scale(.6)` }, { transform: s.el.style.transform }], { duration: 180, easing: 'ease-out' }); }
    };
    const every = (fn) => { for (const s of cells) if (s) fn(s); for (const s of backs) if (s) fn(s); };

    let t = 0, phase = 'fill', big = null, dlg = null, err = null, blinks = [], frozen = false, shook = false;
    const seedN = pile.filter((p) => p.seed).length;

    function shut() {
      every((s) => { s.dark(true); s.at = 0; });
      showCredit(false);
    }
    function drop() {
      /* every lit pixel of the word shows a picture; the rest stay dark, and step back so the word reads */
      const dim = (el) => { if (calm) ctx.anim(el, [{ opacity: 1 }, { opacity: 0.35 }], { duration: 500, fill: 'forwards' }); else el.style.opacity = '.35'; };
      dim(back);
      grid.cells.forEach((c, i) => {
        const s = cells[i];
        if (s && !c.lit) dim(s.el);
        if (!s || !c.lit) return;
        s.dark(false);
        s.show(pic(i));
        if (calm) ctx.anim(s.scr, [{ opacity: 0 }, { opacity: 1 }], { duration: 500 });
      });
      showCredit(true);
    }
    function error() {
      err = ctx.audio.play('assets/audio/sfx/ui_denied.mp3', { bus: 'sfx', gain: 0.8 });
      const d = T.errorDialog();
      dlg = doc.createElement('div');
      dlg.className = 'et-dlg';
      dlg.setAttribute('role', 'alertdialog');
      dlg.innerHTML = '<div class="tb"><span></span><i>x</i></div><div class="bd"><img alt=""><div></div></div><div class="ft"><b></b></div>';
      dlg.querySelector('.tb span').textContent = d.title;
      const logo = dlg.querySelector('.bd img');
      logo.src = 'assets/adcard/logo.webp';
      logo.alt = ctx.brand.game;
      const text = dlg.querySelector('.bd div');
      for (const line of d.lines) { const p = doc.createElement('p'); p.textContent = line; text.appendChild(p); }
      dlg.querySelector('.ft b').textContent = d.button;
      root.appendChild(dlg);
      if (calm) ctx.anim(dlg, [{ opacity: 0 }, { opacity: 1 }], { duration: 300 });
      else {
        ctx.anim(dlg, [{ transform: 'translate(-50%,-50%) scale(.85)' }, { transform: 'translate(-50%,-50%) scale(1)' }], { duration: 120, easing: 'ease-out' });
        /* a small shake, about 300 ms */
        ctx.anim(root, [{ transform: 'none' }, { transform: 'translate(5px,-2px)' }, { transform: 'translate(-6px,2px)' }, { transform: 'translate(4px,1px)' }, { transform: 'translate(-3px,-1px)' }, { transform: 'none' }], { duration: T.TAKE.SHAKE_S * 1000 });
        shook = true;
      }
    }
    function freeze() {
      /* no motion anywhere, sound included */
      frozen = true;
      ctx.audio.solo(true);
      err?.stop?.(0);
      for (const a of root.getAnimations({ subtree: true })) { if (a.effect?.target === root && a.effect.getKeyframes?.().some((k) => k.backgroundColor)) { a.finish(); continue; } a.pause(); }
    }
    function giant() {
      /* the shipped idle sprite scaled as a whole (never stretched): the full body when it fits */
      const bh = Math.min(Hfull * 0.96, W * 1.05), bw = bh * 859 / 869;
      const top = Math.max(0, (Hfull - bh) * 0.5);
      big = sprite(doc, bh, true);
      big.el.style.width = `${bw}px`;
      big.el.style.transform = `translate(${((W - bw) / 2).toFixed(1)}px,${top.toFixed(1)}px)`;
      big.el.style.zIndex = '8';
      big.setFace('0_0');
      root.appendChild(big.el);
      if (calm) ctx.anim(big.el, [{ opacity: 0 }, { opacity: 1 }], { duration: 400 });
      else ctx.anim(big.el, [{ opacity: 0, scale: 0.7 }, { opacity: 1, scale: 1.04, offset: 0.7 }, { opacity: 1, scale: 1 }], { duration: T.TAKE.POP_S * 1000, easing: 'ease-out' });
      blinks = T.blinkTimes().map((b) => ({ on: at.giant + b, off: at.giant + b + (calm ? 0.3 : T.TAKE.BLINK_LEN_S) }));
    }
    function cheeky() {
      big.setPose('body-smug');
      big.setFace('^_~');
    }
    function close() {
      if (calm) { const black = ctx.el('div', 'position:absolute;inset:0;z-index:9;background:#000;', null, root); ctx.anim(black, [{ opacity: 0 }, { opacity: 1 }], { duration: T.TAKE.GENTLE_CLOSE_S * 1000, fill: 'both' }); return; }
      /* the eyelid: black closes from the top and the bottom */
      for (const side of ['top', 'bottom']) {
        const lid = ctx.el('div', `position:absolute;left:0;right:0;${side}:0;z-index:9;height:50.5%;background:#000;transform-origin:50% ${side === 'top' ? '0' : '100%'};`, null, root);
        ctx.anim(lid, [{ transform: 'scaleY(0)' }, { transform: 'scaleY(1)' }], { duration: T.TAKE.CLOSE_S * 1000 * 0.6, delay: T.TAKE.CLOSE_S * 1000 * 0.3, fill: 'both', easing: 'ease-in' });
      }
    }

    view.__emiTakeoverProbe = () => ({
      phase, t, calm, front: N, frontIn: gridIn, back: B, backIn, seeds: seedN, stacked: grid.stacked, cols: grid.cols, rows: grid.rows,
      readable: grid.readable, pool: pool.length, bubbles: 0,
      lit: cells.filter((c) => c?.lit && c.pic && !c.scr.dataset.off).length, wantLit: grid.cells.filter((c) => c.lit).length,
      dark: [...cells, ...backs].filter((c) => c && c.scr.dataset.off).length, frozen, shook, dialog: !!dlg?.isConnected,
      big: !!big?.el.isConnected, face: big?.face || '', pose: big?.poseName || '', credit: !credit.hidden,
    });

    const enter = (name, fn) => { if (phase !== name) { phase = name; fn?.(); } };
    ctx.frame((dt) => {
      if (done) return;
      t += dt;
      if (t < at.shut) {
        /* the fill: front pixels row after row, the back pile behind, every lit screen on its own flash clock */
        let budget = LITE ? 10 : 24;   // never build the whole screen in one frame
        while (backIn < B && T.backAt(backIn, B, seedN) <= t && budget > 0) { putBack(backIn++); budget -= 1; }
        const want = T.gridCount(t, N);
        while (gridIn < want && budget > 0) { putFront(gridIn++); budget -= 1; }
        if (!calm) every((s) => { if (s.at && t >= s.at) flash(s, !!s.isBack); });
        if (!calm && t > 0.5) showCredit(true);
        if (calm && t > T.TAKE.GRID_START_S) showCredit(true);
      } else if (t < at.drop) {
        /* stragglers on a slow frame budget land now, then every screen goes dark at once */
        while (backIn < B) putBack(backIn++);
        while (gridIn < N) putFront(gridIn++);
        enter('shut', shut);
      } else if (t < at.error) enter('drop', drop);
      else if (t < at.frozen) enter('error', error);
      else if (t < at.giant) { if (phase === 'drop') enter('error', error); enter('frozen', freeze); }
      else if (t < at.cheeky) {
        if (!frozen) { if (!dlg) error(); freeze(); }
        enter('giant', giant);
        const b = blinks.find((x) => t >= x.on && t < x.off);
        big.setFace(b ? '-_-' : '0_0');
      } else if (t < at.close) { if (!big) giant(); enter('cheeky', cheeky); }
      else if (t < at.end) enter('close', close);
      else finish();
    });
  });
}

/* The rows live in emi-swarm.js; goon.js spreads this default. */
export default swarmRows;
