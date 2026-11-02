/* ui/unlocks/extras/tricks.js - how each Button Trouble trick draws itself. Model: Steer in
 * Resources/web/intake/core/contracts.js. Friction, never lockout: on every trick the easy answer takes one press and the
 * hard one always gets there in the end. A trick gets `t` from quiz.js; whatever it starts through t stops with the card. */

import { QUIZ_COPY as C } from './copy.js';

const fill = (s, n) => s.replace('{n}', n);
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

/** The plain pair. Returns {y, n, slots}. */
function pair(t, zh = 52) {
  t.zone.style.height = `${zh}px`;
  const bw = Math.floor((t.zw - 12) / 2);
  const sy = Math.max(0, zh - 50);
  const [a, b] = t.q.answers;
  const A = t.btn(a, 0, sy, bw), B = t.btn(b, bw + 12, sy, bw);
  return { a: A, b: B, y: a === t.no ? B : A, n: a === t.no ? A : B, bw, sy };
}

/** A press-and-hold with a bar. done() on a full hold; a keyboard press counts too. */
function holdable(t, el, ms, { onStart, onTick, done, onCancel, moveLimit = 0 }) {
  let held = 0, down = false, startX = 0, startY = 0;
  el.classList.add('xq-hold');
  const stop = () => { if (!down) return; down = false; held = 0; onCancel?.(); };
  el.addEventListener('pointerdown', (ev) => {
    ev.preventDefault();
    down = true; held = 0; startX = ev.clientX; startY = ev.clientY;
    try { el.setPointerCapture(ev.pointerId); } catch { /* fine */ }
    onStart?.();
  });
  el.addEventListener('pointerup', stop);
  el.addEventListener('pointercancel', stop);
  el.addEventListener('contextmenu', (ev) => ev.preventDefault());
  if (moveLimit) el.addEventListener('pointermove', (ev) => { if (down && Math.hypot(ev.clientX - startX, ev.clientY - startY) > moveLimit) stop(); });
  t.frame((dt) => {
    if (!down) return;
    held += dt * 1000;
    onTick?.(Math.min(1, held / ms));
    if (held >= ms) { down = false; done(); }
  });
}

/** The 3x3 grid both picture tricks use. */
function tiles(t, emojis, onTile) {
  const g = t.el('div', '', null, t.zone);
  g.className = 'xq-grid';
  const list = emojis.map((e, i) => {
    const b = t.el('button', `height:${t.zw < 280 ? 44 : 50}px;`, e, g);
    b.type = 'button';
    b.className = 'xq-tile';
    b.addEventListener('click', () => onTile(b, i));
    return b;
  });
  return { g, list };
}

function underRow(t, first, second) {
  const r = t.el('div', '', null, t.zone);
  r.className = 'xq-row';
  const mk = (ans) => {
    const b = t.el('button', 'min-height:44px;', t.ctx.brand.text(t.label(ans)), r);
    b.type = 'button';
    b.className = `xq-a${t.tone(ans) !== 'no' ? ' yes' : ''}`;
    return b;
  };
  return { A: mk(first), B: mk(second), r };
}

const DECOY_EMOJI = { socks: '🧦', hat: '🎩', soap: '🧼' };
const FILLER = ['🌵', '🍋', '🐟', '🎈'];
const SPIRAL = '🌀';

const shuffle = (ctx, list) => {
  const a = [...list];
  for (let i = a.length - 1; i > 0; i--) { const j = Math.floor(ctx.rng() * (i + 1)); [a[i], a[j]] = [a[j], a[i]]; }
  return a;
};

export const TRICK_FNS = {
  none: { draw(t) { pair(t); } },
  mono: { draw(t) { pair(t); } },

  /* Press the refusal and it melts off the card. It pays nothing and takes nothing. */
  'melt-away': {
    draw(t) {
      const p = pair(t, 60);
      p.n.blocked = true;
      p.n.addEventListener('click', () => {
        if (p.n.melting) return;
        p.n.melting = true;
        t.pop();
        t.ctx.toast(C.melted);
        p.n.style.pointerEvents = 'none';
        const frames = t.gentle ? [{ opacity: 1 }, { opacity: 0 }] : [{ transform: 'none', opacity: 1, borderRadius: '0' }, { transform: 'translateY(8px) scale(.92,1.4)', opacity: 1, borderRadius: '0 0 14px 14px', offset: 0.5 }, { transform: 'translateY(26px) scale(.45,2)', opacity: 0, borderRadius: '0 0 24px 24px' }];
        t.anim(p.n, frames, { duration: t.gentle ? 500 : 1600, easing: 'ease-in', fill: 'forwards' });
      });
    },
  },

  /* The answers trade places as you close in. Three swaps, then they are tired. */
  'hover-swap': {
    draw(t) {
      const p = pair(t);
      let swaps = 0, flipped = false;
      const swap = () => {
        if (swaps >= 3) return false;
        swaps += 1; flipped = !flipped;
        const x = (b, left) => { b.style.left = `${left ? 0 : p.bw + 12}px`; };
        x(p.a, flipped ? false : true);
        x(p.b, flipped ? true : false);
        return true;
      };
      t.card.addEventListener('pointermove', (ev) => {
        if (ev.pointerType !== 'mouse') return;
        const r = p.n.getBoundingClientRect();
        if (ev.clientX > r.left - 6 && ev.clientX < r.right + 6 && ev.clientY > r.top - 10 && ev.clientY < r.bottom + 10) swap();
      });
      p.n.addEventListener('pointerdown', (ev) => { if (ev.pointerType !== 'mouse' && swap()) { ev.preventDefault(); p.n.blocked = true; t.after(260, () => { p.n.blocked = false; }); } });
    },
  },

  /* The refusal slides away from your finger. Five runs, then you can catch it. */
  flee: {
    draw(t) {
      const zh = 128;
      t.zone.style.height = `${zh}px`;
      const bw = Math.floor((t.zw - 12) / 2);
      const y = t.btn(t.yes, 0, 40, bw), n = t.btn(t.no, bw + 12, 40, bw);
      void y;
      const spots = [[bw + 12, 0], [bw + 12, 80], [Math.floor(bw / 2), 0], [0, 88], [bw + 12, 40]];
      let i = 0;
      const run = () => {
        if (i >= spots.length) return false;
        const [sx, sy] = spots[i++];
        n.style.left = `${sx}px`; n.style.top = `${sy}px`;
        return true;
      };
      t.card.addEventListener('pointermove', (ev) => {
        if (ev.pointerType !== 'mouse') return;
        const r = n.getBoundingClientRect();
        if (Math.hypot(ev.clientX - (r.left + r.width / 2), ev.clientY - (r.top + r.height / 2)) < r.width / 2 + 16) run();
      });
      n.addEventListener('pointerdown', (ev) => { if (ev.pointerType !== 'mouse' && run()) { ev.preventDefault(); n.blocked = true; t.after(260, () => { n.blocked = false; }); } });
    },
  },

  /* No asks if you are sure, up to three times. */
  'nested-nag': {
    draw(t) {
      const zh = 132;
      t.zone.style.height = `${zh}px`;
      const bw = Math.floor((t.zw - 12) / 2);
      t.btn(t.yes, 0, zh - 50, bw);
      const first = t.btn(t.no, bw + 12, zh - 50, bw);
      let n = 0;
      const nag = () => {
        const dw = Math.min(170, t.zw - 56);
        const box = t.el('div', `position:absolute;left:${t.zw - dw - (2 - n) * 12}px;top:${n * 34}px;width:${dw}px;height:82px;background:#f4f1e8;border:2px outset #fff;box-shadow:2px 2px 0 rgba(0,0,0,.35);`, null, t.zone);
        t.el('div', 'padding:6px 4px 0;text-align:center;font-size:14px;', C.nag[n], box);
        const b = t.el('button', 'left:50%;bottom:4px;margin-left:-44px;width:88px;height:40px;min-height:40px;', t.ctx.brand.text(t.label(t.no)), box);
        b.type = 'button';
        b.className = 'xq-a';
        t.anim(box, [{ transform: 'scale(.7)', opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 140, easing: 'ease-out' });
        n += 1;
        b.addEventListener('click', () => { if (n >= C.nag.length) t.answer(t.no, b); else nag(); });
      };
      first.blocked = true;
      first.addEventListener('click', nag);
    },
  },

  /* Fake buttons multiply. A fake just fades when pressed; the real two are in there. */
  crowd: {
    draw(t) {
      const cols = 4, rows = 4, gap = 4, bh = 36;
      const zh = rows * (bh + gap);
      t.zone.style.height = `${zh}px`;
      const bw = Math.floor((t.zw - (cols - 1) * gap) / cols);
      const slots = [];
      for (let r = 0; r < rows; r++) for (let c = 0; c < cols; c++) slots.push([c * (bw + gap), r * (bh + gap)]);
      const order = shuffle(t.ctx, slots);
      let i = 0;
      const realY = 3 + Math.floor(t.ctx.rng() * 6), realN = 9 + Math.floor(t.ctx.rng() * 6);
      const add = () => {
        if (i >= order.length) return;
        const [x, y] = order[i];
        const idx = i++;
        let b;
        if (idx === realY) b = t.btn(t.yes, x, y, bw, bh);
        else if (idx === realN) b = t.btn(t.no, x, y, bw, bh);
        else {
          b = t.el('button', `left:${x}px;top:${y}px;width:${bw}px;height:${bh}px;min-height:0;`, t.ctx.brand.text(idx % 2 ? t.label(t.no) : t.label(t.yes)), t.zone);
          b.type = 'button';
          b.className = 'xq-a';
          b.addEventListener('click', () => {
            b.style.opacity = '.15';
            b.style.pointerEvents = 'none';
            const r = b.getBoundingClientRect();
            t.float(r.left + r.width / 2, r.top, C.fake, 'color:#a3232b;font-size:14px;');
            t.deny();
          });
        }
        b.style.minHeight = '0';
        b.style.fontSize = '12px';
        if (!t.gentle) t.anim(b, [{ transform: 'scale(.3)' }, { transform: 'none' }], { duration: 130 });
      };
      t.every(110, add);
      for (let k = 0; k < 4; k++) add();
    },
  },

  /* Answers float up as bubbles and come round again. Pop one to choose. */
  'bubble-pop': {
    draw(t) {
      const zh = 176, d = 78;
      t.zone.style.height = `${zh}px`;
      t.zone.style.overflow = 'hidden';
      const mk = (ans, x, dur, delay) => {
        const b = t.el('button', `left:${x}px;top:${zh - d}px;width:${d}px;height:${d}px;`, t.ctx.brand.text(t.label(ans)), t.zone);
        b.type = 'button';
        b.className = 'xq-bub';
        if (t.gentle) { b.style.top = `${(zh - d) / 2}px`; } else {
          t.anim(b, [{ transform: `translateY(${d}px)` }, { transform: `translateY(${-(zh - d) - 4}px)` }], { duration: dur, delay, iterations: Infinity, easing: 'linear' });
        }
        b.addEventListener('click', () => { t.pop(); t.answer(ans, b); });
        return b;
      };
      mk(t.yes, t.zw * 0.16, 5200, 0);
      mk(t.no, t.zw * 0.56, 4600, -1800);
    },
  },

  /* A slider that leans to the top. Your answer is wherever it is when you press. */
  'lean-slider': {
    draw(t) {
      const zh = 122;
      t.zone.style.height = `${zh}px`;
      const num = t.el('div', `position:absolute;left:0;right:0;top:0;text-align:center;font:800 26px sans-serif;color:#c23c84;font-variant-numeric:tabular-nums;`, '', t.zone);
      const track = t.el('div', 'top:44px;', null, t.zone);
      track.className = 'xq-slide';
      const knob = t.el('div', '', null, track);
      knob.className = 'xq-knob';
      t.note(t.q.low, 0, 58, 90).style.textAlign = 'left';
      const hi = t.note(t.q.high, t.zw - 90, 58, 90);
      hi.style.textAlign = 'right';
      const numeric = /^\d+$/.test(t.q.low);
      let v = 0.92, held = false;
      const draw = () => {
        knob.style.left = `${v * 100}%`;
        num.textContent = numeric ? String(Math.round(Number(t.q.low) + v * (Number(t.q.high) - Number(t.q.low)))) : (v < 0.5 ? t.q.low : t.q.high);
      };
      const setFrom = (ev) => {
        const r = track.getBoundingClientRect();
        v = clamp((ev.clientX - r.left) / r.width, 0, 1);
        draw();
      };
      track.addEventListener('pointerdown', (ev) => { ev.preventDefault(); held = true; try { track.setPointerCapture(ev.pointerId); } catch { /* fine */ } setFrom(ev); });
      track.addEventListener('pointermove', (ev) => { if (held) setFrom(ev); });
      const up = () => { held = false; };
      track.addEventListener('pointerup', up);
      track.addEventListener('pointercancel', up);
      t.frame((dt) => { if (!held && v < 1) { v = Math.min(1, v + dt * (0.1 + v * 0.35)); draw(); } });
      draw();
      const done = t.el('button', `left:${(t.zw - 170) / 2}px;top:${zh - 44}px;width:170px;height:44px;`, C.sliderDone, t.zone);
      done.type = 'button';
      done.className = 'xq-a yes';
      done.addEventListener('click', () => t.answer(v >= 0.5 ? t.yes : t.no, done));
    },
  },

  /* The right one grows, the other shrinks. Both stay pressable. */
  'size-skew': {
    draw(t) {
      const p = pair(t, 62);
      p.y.style.transformOrigin = p.y === p.a ? 'left center' : 'right center';
      p.n.style.transformOrigin = p.n === p.a ? 'left center' : 'right center';
      const opts = { duration: 3200, easing: 'ease-in-out', fill: 'forwards' };
      if (t.gentle) { p.y.style.transform = 'scale(1.35)'; p.n.style.transform = 'scale(.55)'; return; }
      t.anim(p.y, [{ transform: 'scale(1)' }, { transform: 'scale(1.5)' }], opts);
      t.anim(p.n, [{ transform: 'scale(1)' }, { transform: 'scale(.42)' }], opts);
    },
  },

  /* The refusal fades out but can still be tapped. */
  'opacity-skew': {
    draw(t) {
      const p = pair(t);
      t.anim(p.n, [{ opacity: 1 }, { opacity: 0.06 }], { duration: 2400, easing: 'ease-in', fill: 'forwards' });
      p.n.addEventListener('pointerdown', () => { const r = p.n.getBoundingClientRect(); t.float(r.left + r.width / 2, r.top, C.stillThere, 'color:#a3232b;font-size:14px;'); });
    },
  },

  /* The right answer drifts toward your finger. Refusing stays on top of it, so it can always be pressed. */
  magnet: {
    draw(t) {
      const zh = 120;
      t.zone.style.height = `${zh}px`;
      const bw = Math.floor((t.zw - 12) / 2);
      const y = t.btn(t.yes, 0, 37, bw), n = t.btn(t.no, bw + 12, 37, bw);
      y.style.transition = 'none';
      n.style.zIndex = '2';
      let x0 = 0, y0 = 37, tx = 0, ty = 37;
      const aim = (ev) => {
        const r = t.zone.getBoundingClientRect();
        tx = clamp(ev.clientX - r.left - bw / 2, 0, t.zw - bw);
        ty = clamp(ev.clientY - r.top - 23, 0, zh - 46);
      };
      t.card.addEventListener('pointermove', aim);
      t.card.addEventListener('pointerdown', aim);
      t.frame((dt) => {
        const k = 1 - Math.exp(-dt * 2.4);
        x0 += (tx - x0) * k; y0 += (ty - y0) * k;
        y.style.left = `${x0}px`; y.style.top = `${y0}px`;
      });
    },
    extraMs: 0,
  },

  /* No arrives after a wait. */
  'late-bloom': {
    extraMs: 6000,
    draw(t) {
      t.zone.style.height = '52px';
      const bw = Math.floor((t.zw - 12) / 2);
      t.btn(t.yes, 0, 2, bw);
      const WAIT = 7000;
      const slot = t.el('div', `position:absolute;left:${bw + 12}px;top:2px;width:${bw}px;height:46px;box-sizing:border-box;border:1.5px dashed #888;overflow:hidden;`, null, t.zone);
      const f = t.el('div', 'position:absolute;inset:0;background:#d4d0c8;transform-origin:0 50%;', null, slot);
      t.anim(f, [{ transform: 'scaleX(0)' }, { transform: 'scaleX(1)' }], { duration: WAIT, easing: 'linear', fill: 'forwards' });
      const lab = t.el('div', 'position:absolute;inset:0;display:grid;place-items:center;font-size:13px;color:#333;', fill(C.lateIn, Math.ceil(WAIT / 1000)), slot);
      let left = Math.ceil(WAIT / 1000);
      t.every(1000, () => { left -= 1; if (left > 0) lab.textContent = fill(C.lateIn, left); });
      t.after(WAIT, () => {
        slot.remove();
        const n = t.btn(t.no, bw + 12, 2, bw);
        t.anim(n, [{ transform: 'scale(.8)', opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 180 });
      });
    },
  },

  /* Yes takes one tap. No takes a three-second hold. */
  'hold-refuse': {
    draw(t) {
      const p = pair(t, 76);
      p.y.style.top = p.n.style.top = '20px';
      const x = (b) => parseFloat(b.style.left);
      t.note(C.oneTap, x(p.y), 2, p.bw).style.color = '#c23c84';
      const lab = t.note(fill(C.holdFor, 3), x(p.n), 2, p.bw);
      const bar = t.el('div', `left:${x(p.n)}px;top:68px;width:${p.bw}px;`, null, t.zone);
      bar.className = 'xq-bar';
      const inner = t.el('i', '', null, bar);
      p.n.blocked = true;
      p.n.addEventListener('click', (ev) => { if (ev.detail === 0) t.answer(t.no, p.n); });
      holdable(t, p.n, 3000, {
        onTick(f) { inner.style.transform = `scaleX(${f})`; lab.textContent = fill(C.holdFor, Math.max(1, Math.ceil(3 * (1 - f)))); },
        onCancel() { inner.style.transform = 'scaleX(0)'; lab.textContent = fill(C.holdFor, 3); },
        done() { lab.textContent = C.phew; t.after(220, () => t.answer(t.no, p.n)); },
      });
    },
  },

  /* The refusal is under a pile of notes. Drag them off, or flick one with a tap. */
  'drag-reveal': {
    draw(t) {
      const zh = 96;
      t.zone.style.height = `${zh}px`;
      const bw = Math.floor((t.zw - 12) / 2);
      t.btn(t.yes, 0, 26, bw);
      const nx = bw + 12;
      t.btn(t.no, nx, 26, bw);
      t.note(C.whereIsNo, nx, 2, bw).style.color = '#a3232b';
      const tints = ['#d8f5c9', '#ffd0e8', '#c7e8ff', '#fff2a8'];
      tints.forEach((tint, i) => {
        const c = t.el('div', `left:${nx + i - 2}px;top:${18 + i}px;width:${bw}px;height:60px;background:${tint};z-index:${2 + i};`, C.stickies[i], t.zone);
        c.className = 'xq-stick';
        let down = false, sx = 0, sy = 0, dx = 0, dy = 0;
        c.addEventListener('pointerdown', (ev) => { ev.preventDefault(); down = true; sx = ev.clientX; sy = ev.clientY; try { c.setPointerCapture(ev.pointerId); } catch { /* fine */ } });
        c.addEventListener('pointermove', (ev) => { if (!down) return; dx = ev.clientX - sx; dy = ev.clientY - sy; c.style.transform = `translate(${dx}px,${dy}px) rotate(${dx / 20}deg)`; });
        const up = () => {
          if (!down) return;
          down = false;
          const dir = dx >= 0 ? 1 : -1;
          c.style.pointerEvents = 'none';
          const a = t.anim(c, [{ transform: `translate(${dx}px,${dy}px)`, opacity: 1 }, { transform: `translate(${dx + dir * 180}px,${dy - 40}px) rotate(${dir * 24}deg)`, opacity: 0 }], { duration: 240, easing: 'ease-out', fill: 'forwards' });
          a.onfinish = () => c.remove();
        };
        c.addEventListener('pointerup', up);
        c.addEventListener('pointercancel', up);
      });
    },
  },

  /* Each No peels off to show another No. Six in all. */
  'bottomless-no': {
    draw(t) {
      const p = pair(t, 72);
      p.y.style.top = p.n.style.top = '20px';
      let left = 6;
      const lab = t.note(fill(C.more, left), parseFloat(p.n.style.left), 2, p.bw);
      lab.style.color = '#a3232b';
      p.n.style.boxShadow = '2px 2px 0 #aaa,4px 4px 0 #999,6px 6px 0 #888';
      p.n.blocked = true;
      p.n.addEventListener('click', () => {
        left -= 1;
        if (left <= 0) { t.answer(t.no, p.n); return; }
        lab.textContent = fill(C.more, left);
        const c = t.el('div', `position:absolute;left:${p.n.style.left};top:20px;width:${p.bw}px;height:46px;box-sizing:border-box;background:#d4d0c8;border:2px outset #fff;display:grid;place-items:center;pointer-events:none;`, t.ctx.brand.text(t.label(t.no)), t.zone);
        const a = t.anim(c, [{ transform: 'none', opacity: 1 }, { transform: 'translate(46px,-46px) rotate(24deg)', opacity: 0 }], { duration: 420, easing: 'ease-out' });
        a.onfinish = () => c.remove();
        t.pop();
      });
    },
  },

  /* Not a robot: hold the box steady for two seconds. It knows it is a joke, and there is nothing to fill in. */
  'verify-checkbox': {
    draw(t) {
      t.zone.style.height = '132px';
      const panel = t.el('div', 'position:absolute;left:0;right:0;top:0;height:76px;background:#fafafa;border:1px solid #bbb;border-radius:3px;', null, t.zone);
      const box = t.el('div', 'position:absolute;left:12px;top:12px;width:44px;height:44px;box-sizing:border-box;background:#fff;border:2px solid #888;border-radius:3px;display:grid;place-items:center;font:700 32px/1 sans-serif;color:#1f9d55;cursor:pointer;', '', panel);
      t.el('div', 'position:absolute;left:68px;right:6px;top:12px;font-size:15px;line-height:1.15;', t.ctx.brand.text(t.label(t.yes)), panel);
      const hint = t.el('div', 'position:absolute;left:68px;top:36px;font-size:13px;color:#555;', C.robotHold, panel);
      const bar = t.el('div', 'left:12px;right:12px;top:64px;', null, panel);
      bar.className = 'xq-bar';
      const inner = t.el('i', '', null, bar);
      holdable(t, box, 2200, {
        moveLimit: 26,
        onTick(f) { inner.style.transform = `scaleX(${f})`; },
        onCancel() { inner.style.transform = 'scaleX(0)'; hint.textContent = C.robotTwitch; t.nudge(box); },
        done() { box.textContent = '✓'; hint.textContent = C.robotDone; t.after(420, () => t.answer(t.yes, box)); },
      });
      t.btn(t.no === t.yes ? t.q.answers[1] : t.no, (t.zw - 170) / 2, 84, 170);
    },
  },

  /* Select the spirals. The Done button points out what you missed once, then takes it anyway. */
  'verify-grid': {
    draw(t) {
      const n = 4 + Math.floor(t.ctx.rng() * 3);
      const decoy = DECOY_EMOJI[t.q.decoy] || FILLER[0];
      const set = shuffle(t.ctx, [...Array(n).fill(SPIRAL), ...shuffle(t.ctx, [decoy, decoy, ...FILLER]).slice(0, 9 - n)]);
      const { list } = tiles(t, set, (b) => { b.classList.toggle('on'); t.pop(); });
      const { A, B } = underRow(t, t.yes, t.no);
      let warned = false;
      A.addEventListener('click', () => {
        const missed = list.some((b, i) => set[i] === SPIRAL && !b.classList.contains('on')) || list.some((b, i) => set[i] !== SPIRAL && b.classList.contains('on'));
        if (missed && !warned) { warned = true; t.deny(); t.nudge(A); t.ctx.toast(C.gridMissed); return; }
        t.answer(t.yes, A);
      });
      B.addEventListener('click', () => t.answer(t.no, B));
    },
  },

  /* Copy the wobbly text. There is no text box: tap the letters. Wrong ones cost nothing. */
  'verify-transcribe': {
    draw(t) {
      const phrase = t.q.phrase;
      const wob = t.el('div', '', null, t.zone);
      wob.className = 'xq-wob';
      [...phrase].forEach((c, i) => {
        const s = t.el('span', `rotate:${((i * 37) % 30) - 15}deg;animation-delay:${-i * 170}ms;${c === ' ' ? 'width:10px;' : ''}`, c === ' ' ? ' ' : c, wob);
        void s;
      });
      const typed = t.el('div', '', '', t.zone);
      typed.className = 'xq-typed';
      const pad = t.el('div', '', null, t.zone);
      pad.className = 'xq-pad';
      const need = [...new Set(phrase.replace(/ /g, ''))];
      const extra = shuffle(t.ctx, [...'ABKQWZ'].filter((c) => !need.includes(c))).slice(0, 2);
      let at = 0;
      const { A, B } = underRow(t, t.yes, t.no);
      A.disabled = true;
      A.style.opacity = '.5';
      const advance = () => {
        while (phrase[at] === ' ') at += 1;
        typed.textContent = phrase.slice(0, at);
        if (at >= phrase.length) { A.disabled = false; A.style.opacity = ''; t.after(500, () => t.answer(t.yes, A)); }
      };
      for (const c of shuffle(t.ctx, [...need, ...extra])) {
        const b = t.el('button', '', c, pad);
        b.type = 'button';
        b.addEventListener('click', () => {
          if (phrase[at] === c) { at += 1; advance(); t.pop(); } else { t.deny(); t.nudge(typed); t.ctx.toast(C.wrongLetter); }
        });
      }
      A.addEventListener('click', () => t.answer(t.yes, A));
      B.addEventListener('click', () => t.answer(t.no, B));
    },
    extraMs: 30000,
  },

  /* Clear the grid while it refills. Clearing all nine at once pays double, and nobody does that. */
  'verify-regen': {
    draw(t) {
      const set = Array(9).fill(SPIRAL);
      const clear = new Set();
      const { list } = tiles(t, set, (b, i) => {
        if (clear.has(i)) return;
        clear.add(i);
        count += 1;
        b.style.opacity = '0';
        b.style.pointerEvents = 'none';
        lab.textContent = fill(C.regenCount, count);
        t.pop();
        if (clear.size === 9) { t.ctx.toast(C.regenAll); t.answer(t.yes, b, 2); return; }
        /* It tires: after a dozen the refill slows so that a patient player can win. */
        t.after(count < 12 ? 900 : 2600 + count * 160, () => { clear.delete(i); b.style.opacity = '1'; b.style.pointerEvents = ''; });
      });
      void list;
      let count = 0;
      const lab = t.el('div', 'text-align:center;margin-top:4px;font-size:13px;color:#555;', fill(C.regenCount, 0), t.zone);
      const { A, B } = underRow(t, t.yes, t.no);
      A.addEventListener('click', () => t.answer(t.yes, A));
      B.addEventListener('click', () => t.answer(t.no, B));
    },
    extraMs: 15000,
  },

  /* A pink cursor comes to get the answer for you. Knock it away three times and it gives up. Refusing always works. */
  'mouse-hijack': {
    draw(t) {
      const p = pair(t, 96);
      p.y.style.top = p.n.style.top = '48px';
      const cur = t.el('div', '', null, t.zone);
      cur.className = 'xq-cur';
      t.el('i', '', null, cur);
      const yr = () => ({ x: parseFloat(p.y.style.left) + p.bw / 2, y: 62 });
      let x = t.zw - 30, y = 0, knocks = 0, gone = false;
      const knock = () => {
        if (gone) return;
        knocks += 1;
        x = Math.min(t.zw - 30, x + 90); y = 0;
        t.pop();
        if (knocks >= 3) {
          gone = true;
          t.ctx.toast(C.hijackLost);
          t.anim(cur, [{ opacity: 1 }, { opacity: 0 }], { duration: 300, fill: 'forwards' });
          cur.style.pointerEvents = 'none';
        }
      };
      cur.addEventListener('pointerdown', (ev) => { ev.preventDefault(); ev.stopPropagation(); knock(); });
      let cool = 0;
      t.card.addEventListener('pointermove', (ev) => {
        if (ev.pointerType !== 'mouse' || gone || cool > 0) return;
        const r = cur.getBoundingClientRect();
        if (Math.hypot(ev.clientX - (r.left + 12), ev.clientY - (r.top + 16)) < 26) { cool = 0.5; knock(); }
      });
      t.frame((dt) => {
        cool -= dt;
        if (gone) return;
        const g = yr(), dx = g.x - x, dy = g.y - y, d = Math.hypot(dx, dy);
        if (d < 6) { gone = true; t.ctx.toast(C.hijackDone); t.after(200, () => t.answer(t.yes, p.y)); return; }
        const step = Math.min(d, 62 * dt);
        x += (dx / d) * step; y += (dy / d) * step;
        cur.style.left = `${x}px`; cur.style.top = `${y}px`;
      });
    },
  },
};
