/* ui/unlocks/themes-b/emi-takeover.js - EMI Takeover, the goon lane's ending take (owner notes 2026-11-09, v3 brief
 * handoff-1109/TC-EMI-TAKEOVER-V3-BRIEF.md). The run-up rows live in emi-swarm.js.
 *
 * go_end take, one timeline on the game clock (numbers in sim/takeover-end.js):
 *   fill   the front grid spreads out from the walking EMIs, each screen flashing pictures (under 3 Hz); behind it
 *          EMIs of different sizes pile in from where the swarm stood until no page shows through
 *   shut   every screen dark at once, about 1 s
 *   drop   pictures only on the grid pixels of DROP (DR over OP on a portrait phone)
 *   error  a silent, small system dialog with the game logo, a short shake (none under Gentle)
 *   waiting every animation and all audio stop. The dialog's OK
 *          (or its x, or Enter/Space) is live and focused, and the take waits for it (on its own after 45 s)
 *   giant  on OK a hard shake and a sting accompany a giant EMI (the shipped idle sprite scaled whole) is
 *          there in the same frame, slightly too big then settled; she blinks, then ^_~ on body-smug
 *          (owner 2026-11-10; Gentle: no shakes, a 300 ms fade, the sting at half volume)
 *   close  the eyelid shuts to black (Gentle: a plain fade). Then the engine goes straight to the recap.
 *
 * No bubbles, nothing follows or gathers round the pointer. Pictures are DOM <img> from ctx.pictures, never a canvas.
 * Wake Up's group stays clear: no EMI is made under it. */

import { bindCopyPets } from './emi-pets.js';
import { LITE } from '../../perf.js';
import * as T from '../../../sim/takeover.js';
import swarmRows, { liveSwarm } from './emi-swarm.js';
import { IDLE_FACES, rand, any, CSS, bakeFaces, sprite, controlsRect, morePictures, tilesOf } from './emi-kit.js';

const TAKE_CSS = `
.et-front,.et-back{position:absolute;inset:0;pointer-events:none;}
.et-dlg{position:absolute;left:50%;top:50%;transform:translate(-50%,-50%);width:min(340px,calc(100% - 32px));background:#ece9e2;color:#111;border:1px solid #2a2a2a;box-shadow:3px 4px 0 rgba(0,0,0,.45);font:13px/1.35 "Segoe UI",system-ui,sans-serif;z-index:6;}
.et-dlg .tb{display:flex;align-items:center;gap:8px;padding:4px 8px;background:#2a1f3d;color:#fff;font-weight:700;}
.et-dlg .tb span{flex:1;}
.et-dlg button{font:inherit;color:inherit;cursor:pointer;pointer-events:auto;}
.et-dlg .x{background:none;border:0;padding:0 4px;color:#fff;opacity:.8;}
.et-dlg .bd{display:flex;gap:12px;align-items:center;padding:12px 14px 6px;}
.et-dlg .bd img{position:static;width:72px;height:auto;flex:none;}
.et-dlg .bd p{margin:0 0 4px;}
.et-dlg .ft{display:flex;justify-content:flex-end;padding:4px 12px 10px;}
.et-dlg .ok{min-width:72px;padding:4px 12px;border:1px solid #555;background:#f7f6f2;}
.et-dlg .ok:focus-visible,.et-dlg .ok:focus{outline:2px dotted #2a1f3d;outline-offset:-5px;}
`;

let aborted = false;
/** True when the last take ended on panic. */
export const emiTakeoverAborted = () => aborted;

export function emiTakeoverTake(ctx, releaseWorld) {
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
  const spread = T.spreadOrder(grid, pts);
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
    const putFront = (arrival) => {
      const i = spread[arrival];
      const cell = grid.cells[i];
      const s = sprite(doc, size);
      s.el.style.transform = `translate(${cell.x.toFixed(1)}px,${cell.y.toFixed(1)}px)`;
      s.setFace(any(IDLE_FACES));
      s.el.dataset.etPet = '';
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
      s.el.dataset.etPet = '';
      if (calm) { if (Math.random() < 0.4) s.show(pic(k)); if (!p.seed) ctx.anim(s.el, [{ opacity: 0 }, { opacity: 1 }], { duration: 500 }); }
      else { flash(s, true); if (!p.seed && !LITE) ctx.anim(s.el, [{ transform: `${s.el.style.transform} scale(.6)` }, { transform: s.el.style.transform }], { duration: 180, easing: 'ease-out' }); }
    };
    const every = (fn) => { for (const s of cells) if (s) fn(s); for (const s of backs) if (s) fn(s); };

    let t = 0, phase = 'fill', big = null, dlg = null, blinks = [], frozen = false, shook = false, jolted = false;
    /* dev door: the drive can shorten the wait to prove the take goes on alone (always inside the take's own cap) */
    const waitMaxS = T.clampWait(Number(view.__emiTakeoverWaitS) || T.TAKE.WAIT_MAX_S);
    const pets = bindCopyPets(ctx, () => !done && phase === 'fill');
    const seedN = pile.filter((p) => p.seed).length;

    let worldReleased = false;
    function cutAudio() {
      if (worldReleased) return;
      worldReleased = true;
      releaseWorld();
      ctx.audio.solo(true);
      ctx.audio.silence(true);
      root.style.clipPath = '';
    }
    function shut() {
      cutAudio();
      pets.clear();
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
    let okAt = null, okBy = '';
    /** The player pressed OK (or the x, or Enter/Space on it): the wait ends on the next frame. */
    const pressOk = (by) => {
      if (okAt != null || phase !== 'waiting' || ctx.paused || !T.okTakes(t - at.waiting)) return;
      okAt = t; okBy = by;
    };
    function error() {
      const d = T.errorDialog();
      dlg = doc.createElement('div');
      dlg.className = 'et-dlg';
      dlg.setAttribute('role', 'alertdialog');
      dlg.setAttribute('aria-label', d.title);
      dlg.innerHTML = '<div class="tb"><span></span><button type="button" class="x">x</button></div><div class="bd"><img alt=""><div></div></div><div class="ft"><button type="button" class="ok"></button></div>';
      dlg.querySelector('.tb span').textContent = d.title;
      const logo = dlg.querySelector('.bd img');
      logo.src = 'assets/adcard/logo.webp';
      logo.alt = ctx.brand.game;
      const text = dlg.querySelector('.bd div');
      for (const line of d.lines) { const p = doc.createElement('p'); p.textContent = line; text.appendChild(p); }
      const ok = dlg.querySelector('.ok'), x = dlg.querySelector('.x');
      ok.textContent = d.button;
      x.setAttribute('aria-label', d.close);
      /* a native button: Enter and Space on it click it */
      ok.addEventListener('click', (ev) => { if (ev.isTrusted) pressOk('ok'); });
      x.addEventListener('click', (ev) => { if (ev.isTrusted) pressOk('x'); });
      root.appendChild(dlg);
      if (calm) ctx.anim(dlg, [{ opacity: 0 }, { opacity: 1 }], { duration: 300 });
      else {
        ctx.anim(dlg, [{ transform: 'translate(-50%,-50%) scale(.85)' }, { transform: 'translate(-50%,-50%) scale(1)' }], { duration: 120, easing: 'ease-out' });
        /* a small shake, about 300 ms */
        ctx.anim(root, [{ transform: 'none' }, { transform: 'translate(5px,-2px)' }, { transform: 'translate(-6px,2px)' }, { transform: 'translate(4px,1px)' }, { transform: 'translate(-3px,-1px)' }, { transform: 'none' }], { duration: T.TAKE.SHAKE_S * 1000 });
        shook = true;
      }
    }

    /* the sting: a short sharp hit, a thud under a noise crack and a dissonant stab, on the normal sfx bus */
    function sting() {
      const g = T.TAKE.STING_GAIN * (calm ? 0.5 : 1);
      ctx.audio.play('assets/audio/sfx/thud.mp3', { bus: 'sfx', gain: g });
      const io = ctx.audio.input?.({ bus: 'sfx', gain: g });
      if (!io) return;
      const ac = io.ac, n = ac.currentTime;
      const out = ac.createGain();
      out.gain.setValueAtTime(0.0001, n); out.gain.linearRampToValueAtTime(0.55, n + 0.006); out.gain.setTargetAtTime(0.0001, n + 0.02, 0.16);
      out.connect(io.node);
      const nb = ac.createBuffer(1, Math.floor(ac.sampleRate * 0.3), ac.sampleRate), nd = nb.getChannelData(0);
      for (let i = 0; i < nd.length; i++) nd[i] = (Math.random() * 2 - 1) * (1 - i / nd.length);
      const noise = ac.createBufferSource(), hp = ac.createBiquadFilter(), ng = ac.createGain();
      noise.buffer = nb; hp.type = 'highpass'; hp.frequency.value = 1400; ng.gain.value = 0.5;
      noise.connect(hp); hp.connect(ng); ng.connect(out);
      const stab = [[220, 'sawtooth'], [233.1, 'sawtooth'], [311.1, 'square']].map(([f, type]) => {
        const o = ac.createOscillator(), lp = ac.createBiquadFilter(), og = ac.createGain();
        o.type = type; o.frequency.setValueAtTime(f, n); o.frequency.exponentialRampToValueAtTime(f * 0.94, n + 0.5);
        lp.type = 'lowpass'; lp.frequency.value = 2400; og.gain.value = 0.22;
        o.connect(lp); lp.connect(og); og.connect(out);
        return o;
      });
      for (const s of [noise, ...stab]) { s.start(n); s.stop(n + 0.7); }
      ctx.after(1200, () => io.stop());
    }

    function freeze() {
      /* no motion anywhere and the page silent; the dialog's OK is live and takes the focus */
      frozen = true;
      for (const a of root.getAnimations({ subtree: true })) { if (a.effect?.target === root && a.effect.getKeyframes?.().some((k) => k.backgroundColor)) { a.finish(); continue; } a.pause(); }
      dlg?.querySelector('.ok')?.focus({ preventScroll: true });
    }
    function giant() {
      /* The jump and its single impact share the first frame after acknowledging the dialog. */
      for (const b of dlg?.querySelectorAll('button') || []) b.disabled = true;
      if (okBy !== 'timeout') {
        ctx.audio.silence(false);
        sting();
      }
      const bh = Math.min(Hfull * 0.96, W * 1.05), bw = bh * 859 / 869;
      const top = Math.max(0, (Hfull - bh) * 0.5);
      big = sprite(doc, bh, true);
      big.el.style.width = `${bw}px`;
      big.el.style.transform = `translate(${((W - bw) / 2).toFixed(1)}px,${top.toFixed(1)}px)`;
      big.el.style.zIndex = '8';
      big.setFace('0_0');
      root.appendChild(big.el);
      if (calm) ctx.anim(big.el, [{ opacity: 0 }, { opacity: 1 }], { duration: 300 });
      else {
        /* no fade, no flash: one frame slightly too big, then she settles */
        ctx.anim(big.el, [{ scale: 1.12 }, { scale: 0.98, offset: 0.55 }, { scale: 1 }], { duration: 220, easing: 'ease-out' });
        ctx.anim(root, [{ transform: 'none' }, { transform: 'translate(-16px,7px)' }, { transform: 'translate(14px,-9px)' }, { transform: 'translate(-11px,-5px)' }, { transform: 'translate(9px,6px)' }, { transform: 'translate(-5px,2px)' }, { transform: 'translate(3px,-1px)' }, { transform: 'none' }], { duration: T.TAKE.JUMP_SHAKE_S * 1000 });
        jolted = true;
      }
      blinks = T.blinkTimes().map((b) => ({ on: beats.giant + b, off: beats.giant + b + (calm ? 0.3 : T.TAKE.BLINK_LEN_S) }));
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
      dark: [...cells, ...backs].filter((c) => c && c.scr.dataset.off).length, frozen, shook, jolted, dialog: !!dlg?.isConnected,
      waiting: phase === 'waiting', waitedS: okAt != null ? okAt - at.waiting : phase === 'waiting' ? t - at.waiting : null, okBy,
      waitMaxS, focused: doc.activeElement === dlg?.querySelector('.ok'),
      big: !!big?.el.isConnected, face: big?.face || '', pose: big?.poseName || '', credit: !credit.hidden,
    });

    /* the beats after OK, set when it is pressed (or the wait runs out) */
    let beats = null;
    const enter = (name, fn) => { if (phase !== name) { phase = name; fn?.(); } };
    let uiClock = 0;
    ctx.frame((dt) => {
      if (done) return;
      if (!worldReleased && (uiClock += dt) >= 0.25) {
        live?.advanceEndingUi?.(uiClock * 2);
        uiClock = 0;
        // Keep her counter place visible through the filling grid until every little screen shuts.
        const digit = [...doc.querySelectorAll('#counter [data-et-digit]')].find(el => el.getClientRects().length);
        if (digit) {
          const d = digit.getBoundingClientRect(), r = root.getBoundingClientRect();
          const x = d.left-r.left, y = d.top-r.top, right=x+d.width, bottom=y+d.height;
          root.style.clipPath = `polygon(evenodd,0 0,100% 0,100% 100%,0 100%,0 0,${x}px ${y}px,${x}px ${bottom}px,${right}px ${bottom}px,${right}px ${y}px,${x}px ${y}px)`;
        }
      }
      t += dt;
      if (t < at.shut) {
        /* the fill: new screens spread outward from the walking copies, the back pile behind, every lit screen on its own flash clock */
        let budget = LITE ? 10 : 24;   // never build the whole screen in one frame
        while (backIn < B && T.backAt(backIn, B, seedN) <= t && budget > 0) { putBack(backIn++); budget -= 1; }
        const want = T.gridCount(t, N);
        while (gridIn < want && budget > 0) { putFront(gridIn++); budget -= 1; }
        if (!calm) every((s) => { if (s.at && t >= s.at) flash(s, !!s.isBack); });
        if (!calm && t > 0.5) showCredit(true);
        if (calm && t > T.TAKE.GRID_START_S) showCredit(true);
        if (backIn === B && gridIn === N) cutAudio();
        return;
      }
      // Run every crossed beat, including on a slow frame with the game clock sped up.
      if (phase === 'fill') {
        while (backIn < B) putBack(backIn++);
        while (gridIn < N) putFront(gridIn++);
        enter('shut', shut);
      }
      if (t < at.drop) return;
      if (phase === 'shut') enter('drop', drop);
      if (t < at.error) return;
      if (!dlg) enter('error', error);
      if (t < at.waiting) return;
      if (!beats) {
        enter('waiting', freeze);
        if (okAt == null && t - at.waiting >= waitMaxS) { okAt = t; okBy = 'timeout'; }
        if (okAt != null) beats = T.phaseStarts({ calm, waitS: okAt - at.waiting });
        if (beats) beats = { ...beats, giant: okAt, cheeky: okAt + (beats.cheeky - beats.giant), close: okAt + (beats.close - beats.giant), end: okAt + (beats.end - beats.giant) };
      } else if (t < beats.cheeky) {
        enter('giant', giant);
        const b = blinks.find((x) => t >= x.on && t < x.off);
        big.setFace(b ? '-_-' : '0_0');
      } else if (t < beats.close) { if (!big) giant(); enter('cheeky', cheeky); }
      else if (t < beats.end) enter('close', close);
      else finish();
    });
  });
}

/* The rows live in emi-swarm.js; goon.js spreads this default. */
export default swarmRows;
