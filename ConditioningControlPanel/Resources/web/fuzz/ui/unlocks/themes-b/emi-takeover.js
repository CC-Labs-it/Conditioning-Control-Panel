/* ui/unlocks/themes-b/emi-takeover.js - EMI Takeover, the goon lane's last stretch and its ending (owner 2026-11-09).
 *
 * Run-up, one swarm owned by go_auto: EMI wanders and taps (go_auto), keeps tips (go_will), the player's own manual taps
 * make copies (go_copy), then copies appear on their own and she walks to her own tiles and buys them (go_hive):
 * Transmission (screens show pictures), Amplifier (bigger), Unified Transmission (one picture on every screen).
 * Ending, go_end take: the screen fills with EMIs on a grid, their screens flicker pictures, the screens settle into the
 * word DROP, then one hard cut to the error card with a big EMI behind the text, the face, and the screen blinks shut.
 *
 * Only the shipped sprites (assets/img/emi) and her screen faces. Pictures are DOM <img> from ctx.pictures, never a
 * canvas. Wake Up's group is a wall for the swarm and no grid cell is made under it. Numbers and rules: sim/takeover.js. */

import { LITE } from '../../perf.js';
import * as T from '../../../sim/takeover.js';
import { EMI_BARKS } from '../../../data/content/emi-takeover.js';
import swarmRows, { liveSwarm } from './emi-swarm.js';
import { art, SWAY, IDLE_FACES, PINK, rand, any, CSS, pose, faces, sprites, bakeFaces, sprite, controlsRect, morePictures, tilesOf, bubble, placeBubble } from './emi-kit.js';

/* ---- the ending ---- */

let aborted = false;
/** True when the last take ended on panic: no crash card, no ending show. */
export const emiTakeoverAborted = () => aborted;

export function emiTakeoverTake(ctx) {
  aborted = false;
  const doc = ctx.button.el.ownerDocument, view = doc.defaultView;
  const calm = !!(ctx.gentle || ctx.reduced);
  ctx.css(CSS);
  bakeFaces(doc);
  const box = ctx.bounds();
  /* the bottom tab strip sits outside the page and stays where it is: the grid ends above it */
  const strip = doc.getElementById('dock-strip');
  const W = box.w || view.innerWidth, H = (box.h || view.innerHeight) - (strip && !strip.hidden ? strip.offsetHeight : 0);
  const walls = controlsRect(doc);
  const grid = T.dropGrid({ w: W, h: H, controls: walls });
  const live = liveSwarm();
  const seeds = live?.points() || [];
  const { order, from } = T.fillOrder(grid, seeds);
  live?.hide();
  const N = grid.cells.length, size = grid.pitch;

  return new Promise((resolve) => {
    let done = false;
    const finish = () => { if (done) return; done = true; delete view.__emiTakeoverProbe; resolve(); };
    ctx.on('suspend', (s) => { if (s?.on && s.reason === 'panic') { aborted = true; finish(); } });

    const root = ctx.el('div', 'position:absolute;inset:0;overflow:hidden;pointer-events:auto;background:rgba(22,17,13,0);', null, 'ending');
    const field = doc.createElement('div');
    field.className = 'et-box';
    root.appendChild(field);
    const talk = doc.createElement('div');
    talk.className = 'et-box';
    root.appendChild(talk);

    const pool = [];
    morePictures(ctx, pool, LITE ? 3 : 6);
    const tiles = tilesOf(ctx);
    const pic = (i) => (pool.length ? pool[i % pool.length].url : tiles[i % tiles.length]);
    const kinds = () => (pool.length || tiles.length);
    const credit = ctx.el('div', 'position:absolute;right:6px;bottom:4px;font:11px var(--font);color:#fff;text-shadow:0 0 2px #000,0 0 2px #000;', 'via Scrolller', root);
    credit.hidden = true;

    /* cells: one EMI each, made as the fill reaches it */
    const cells = new Array(N).fill(null);
    let filled = 0, next = 0, clicks = 0;
    const put = (i) => {
      if (cells[i]) return;
      const cell = grid.cells[i];
      const s = sprite(doc, size);
      s.el.style.transform = `translate(${cell.x.toFixed(1)}px,${cell.y.toFixed(1)}px)`;
      s.setFace(any(IDLE_FACES));
      field.appendChild(s.el);
      s.at = 0; s.lit = cell.lit; s.cx = cell.x; s.cy = cell.y;
      cells[i] = s;
      filled += 1;
      if (calm) ctx.anim(s.el, [{ opacity: 0 }, { opacity: 1 }], { duration: 400 });
      else if (!LITE) ctx.anim(s.el, [{ scale: 0.4 }, { scale: 1.12, offset: 0.7 }, { scale: 1 }], { duration: 200 });
    };
    /** The player's click makes one right there: the nearest empty cell that touches a filled one. */
    const boop = (x, y) => {
      let best = -1, d = Infinity;
      for (let k = next; k < order.length; k++) {
        const i = order[k];
        if (cells[i] || (from.get(i) >= 0 && !cells[from.get(i)])) continue;
        const c = grid.cells[i], q = Math.hypot(c.x + size / 2 - x, c.y + size / 2 - y);
        if (q < d) { d = q; best = i; }
      }
      if (best >= 0) put(best);
    };
    root.addEventListener('pointerdown', (ev) => {
      if (!ev.isTrusted || phase !== 'fill' || ctx.paused) return;
      clicks += 1;
      boop(ev.clientX, ev.clientY);
    });

    /* bubbles during the fill and the settle, none once the pictures start */
    let barks = T.newBarks();
    const bubs = [];
    let barkAt = 2.5;
    const speak = (poolName) => {
      const live_ = cells.filter(Boolean);
      if (!live_.length || !T.barkRoom(barks, { now: t, visible: bubs.length, lite: LITE, maxVisible: LITE ? 2 : 3 })) return;
      const line = T.pickBark(barks, EMI_BARKS.solo[poolName], { now: t });
      if (!line) return;
      barks = T.barkSaid(barks, line, { now: t });
      const s = any(live_), b = bubble(ctx, talk, line);
      b.until = t + T.barkLife(line) / 1000;
      placeBubble(b, s.cx, s.cy, size, W);
      s.setFace('^_^');
      bubs.push(b);
    };
    const hush = () => { for (const b of bubs.splice(0)) b.el.remove(); };

    let t = 0, phase = 'fill', phaseAt = 0, fillT = 0, chosen = 0, big = null, blinkAt = 0, blinkEnd = 0, typed = 0;
    const go = (name) => { phase = name; phaseAt = t; };
    const gap = () => T.flickerGap(Math.random, LITE) / 1000;

    function crash() {
      /* one hard cut: sound off, the grid dims, the card and the big EMI are there in the same frame */
      ctx.audio.solo(true);
      field.style.opacity = '.3';
      credit.hidden = true;
      /* the shipped idle sprite, scaled as a whole (never stretched): the full body when it fits, else her feet run off the bottom */
      const bh = Math.min(H * 0.98, W * 1.05), bw = bh * 859 / 869, top = Math.max(0, (H - bh) * 0.6);
      big = sprite(doc, bh, true);
      big.el.style.width = `${bw}px`;
      big.el.style.transform = `translate(${((W - bw) / 2).toFixed(1)}px,${top.toFixed(1)}px)`;
      big.setFace('0_0');
      root.appendChild(big.el);
      const card = doc.createElement('div');
      card.className = 'et-err';
      card.innerHTML = '<div class="eh"><img alt=""><b>ERROR SYS</b></div>';
      const logo = card.querySelector('img');
      logo.src = 'assets/adcard/logo.webp';
      logo.alt = ctx.brand.game;
      const lines = T.errorLines(N + 1).map((text) => { const ln = doc.createElement('div'); ln.className = 'ln'; card.appendChild(ln); return { ln, text }; });
      const set = (row) => { if (row.text) { const sp = doc.createElement('span'); sp.textContent = row.text; row.ln.appendChild(sp); } };
      big.lines = lines; big.set = set;
      if (calm) { lines.forEach(set); typed = lines.length; } else { set(lines[0]); typed = 1; }
      root.appendChild(card);
      /* the text never covers her face: it sits under her screen, or above it when there is more room there (a phone) */
      const faceTop = top + bh * 0.2946, faceBottom = top + bh * 0.6709, need = card.offsetHeight + (W <= 520 ? 38 : 0);   // empty lines already hold their height; a phone wraps two
      const above = faceTop - (walls ? walls.y + walls.h : 0), below = H - faceBottom;
      const y = below >= need + 8 || below >= above ? Math.min(faceBottom + 8, H - need - 4) : Math.max((walls ? walls.y + walls.h : 0) + 4, faceTop - need - 8);
      card.style.top = `${Math.max(0, y).toFixed(1)}px`;
      if (calm) for (const el of [big.el, card]) ctx.anim(el, [{ opacity: 0 }, { opacity: 1 }], { duration: 400 });
      blinkAt = t + rand(1.6, 2.6);
      root.addEventListener('pointerdown', () => { while (typed < lines.length) set(lines[typed++]); });   // a press skips the typing, never the beat
    }

    function close() {
      if (calm) { const black = ctx.el('div', 'position:absolute;inset:0;background:#000;', null, root); ctx.anim(black, [{ opacity: 0 }, { opacity: 1 }], { duration: T.TAKE.GENTLE_CLOSE_S * 1000, fill: 'both' }); return; }
      /* a 120 ms jolt, then black closes from the top and the bottom like an eyelid */
      ctx.anim(root, [{ transform: 'translateX(14px)', filter: 'hue-rotate(40deg)' }, { transform: 'translateX(-9px)', filter: 'none', offset: 0.6 }, { transform: 'none' }], { duration: 120 });
      for (const side of ['top', 'bottom']) {
        const lid = ctx.el('div', `position:absolute;left:0;right:0;${side}:0;height:50.5%;background:#000;transform-origin:50% ${side === 'top' ? '0' : '100%'};`, null, root);
        ctx.anim(lid, [{ transform: 'scaleY(0)' }, { transform: 'scaleY(1)' }], { duration: 160, delay: 130, fill: 'both', easing: 'ease-in' });
      }
    }

    view.__emiTakeoverProbe = () => ({
      phase, t, cells: N, filled, clicks, stacked: grid.stacked, cols: grid.cols, rows: grid.rows, bubbles: bubs.length, pool: pool.length,
      lit: cells.filter((c) => c?.lit && c.pic).length, wantLit: grid.cells.filter((c) => c.lit).length,
      dark: cells.filter((c) => c && !c.lit && c.scr.dataset.off).length, big: !!big?.el.isConnected, face: big?.face || '', readable: grid.readable,
    });

    ctx.frame((dt) => {
      if (done) return;
      t += dt;
      const at = t - phaseAt;
      for (let i = bubs.length - 1; i >= 0; i--) if (t >= bubs[i].until) { bubs[i].el.remove(); bubs.splice(i, 1); }

      if (phase === 'fill') {
        fillT += dt;
        const want = T.fillCount(T.fillClock(fillT, clicks), N);
        let budget = LITE ? 6 : 14;   // never build the whole grid in one frame
        while (filled < want && next < order.length && budget > 0) { if (!cells[order[next]]) { put(order[next]); budget -= 1; } next += 1; }
        while (next < order.length && cells[order[next]]) next += 1;
        if (t >= barkAt) { barkAt = t + rand(2.2, 4.5); speak('full_screen'); }
        if (filled >= N) go('settle');
      } else if (phase === 'settle') {
        if (!root.dataset.dark) { root.dataset.dark = '1'; ctx.anim(root, [{ backgroundColor: 'rgba(22,17,13,0)' }, { backgroundColor: 'rgba(22,17,13,.94)' }], { duration: 1200, fill: 'both' }); }
        if (bubs.length === 0 && at > 0.3 && at < 0.8) speak('pre_drop');
        if (at >= T.TAKE.SETTLE_S) {
          hush();
          for (const s of cells) { s.setFace('0_0'); s.at = t + rand(0, 0.6); }
          chosen = Math.floor(rand(0, kinds()));
          credit.hidden = !pool.some((p) => p.credit);
          go(calm ? 'gentle' : 'flicker');
        }
      } else if (phase === 'flicker') {
        /* chaos: every screen on its own clock, each one changing under 3 times a second */
        for (const s of cells) if (t >= s.at) { s.at = t + gap(); s.changed = t; s.show(pic(Math.floor(rand(0, kinds() * 4)))); }
        if (at >= T.TAKE.FLICKER_S) { for (const s of cells) s.at = Math.max((s.changed || 0) + 0.36, t + rand(0.1, T.TAKE.RESOLVE_S - 0.4)); go('resolve'); }
      } else if (phase === 'resolve') {
        /* each screen's next change is its last: the letters keep one picture, the rest go dark */
        for (const s of cells) if (s.at && t >= s.at) {
          s.at = 0;
          if (s.lit) s.show(pic(chosen)); else { s.dark(true); s.el.style.opacity = '.3'; }
        }
        if (at >= T.TAKE.RESOLVE_S + 0.1) go('drop');
      } else if (phase === 'gentle') {
        /* no flicker: one picture fades in on every screen, then the screens outside the letters fade dark */
        if (!root.dataset.one) {
          root.dataset.one = '1';
          for (const s of cells) { s.show(pic(chosen)); ctx.anim(s.scr, [{ opacity: 0 }, { opacity: 1 }], { duration: 800 }); }
        }
        if (at >= 2.8 && !root.dataset.word) {
          root.dataset.word = '1';
          for (const s of cells) if (!s.lit) { s.dark(true); ctx.anim(s.el, [{ opacity: 1 }, { opacity: 0.3 }], { duration: 1200, fill: 'both' }); }
        }
        if (at >= 4) go('drop');
      } else if (phase === 'drop') {
        if (at >= T.TAKE.DROP_S) { crash(); go('error'); }
      } else if (phase === 'error') {
        if (!calm) { const n = Math.min(big.lines.length, 1 + Math.floor(at / 0.22)); while (typed < n) big.set(big.lines[typed++]); }
        if (blinkEnd && t >= blinkEnd) { blinkEnd = 0; big.setFace('0_0'); }
        if (t >= blinkAt) { blinkAt = t + rand(2.5, 4); blinkEnd = t + (calm ? 0.4 : 0.13); big.setFace('-_-'); }
        if (at >= T.TAKE.IDLE_S) {
          /* the face: between two frames, no line */
          if (calm) ctx.anim(big.scr, [{ opacity: 0.2 }, { opacity: 1 }], { duration: 300 });
          big.setFace('uncanny');
          go('face');
        }
      } else if (phase === 'face') {
        if (at >= T.TAKE.FACE_S) { close(); go('close'); }
      } else if (phase === 'close') {
        if (at >= (calm ? T.TAKE.GENTLE_CLOSE_S : T.TAKE.CLOSE_S) + T.TAKE.DARK_S) finish();
      }
    });
  });
}

/* The rows live in emi-swarm.js; goon.js spreads this default. */
export default swarmRows;
