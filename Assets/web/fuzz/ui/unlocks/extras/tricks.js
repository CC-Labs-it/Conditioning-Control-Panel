/* ui/unlocks/extras/tricks.js - how each Button Trouble trick draws itself. Model: Steer in
 * Resources/web/intake/core/contracts.js. Friction, never lockout: on every trick the easy answer takes one press and the
 * hard one always gets there in the end. A trick gets `t` from quiz.js; whatever it starts through t stops with the card. */

import { QUIZ_COPY as C } from './copy.js';

const fill = (s, n) => s.replace('{n}', n);
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

const GAP = 6, BH = 46;

/** The four answers in a shuffled two by two. top leaves room above each row, for a note. Returns the wanted button, the wrong
 *  ones and where each cell is. */
function quad(t, { top = 0, under = 0 } = {}) {
  const bw = Math.floor((t.zw - GAP) / 2);
  const rowH = top + BH + GAP;
  t.zone.style.height = `${rowH * 2 - GAP + under}px`;
  const order = shuffle(t.ctx, t.q.answers);
  const cells = order.map((ans, i) => {
    const x = i % 2 ? bw + GAP : 0, y = (i >> 1) * rowH + top;
    return { ans, x, y, b: t.btn(ans, x, y, bw) };
  });
  return { cells, y: cells.find((c) => c.ans === t.yes), ns: cells.filter((c) => c.ans !== t.yes), bw, rowH };
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

/** All four under a puzzle. The wanted one is left to its trick; the others answer on press. */
function underRow(t) {
  const r = t.el('div', '', null, t.zone);
  r.className = 'xq-row';
  let wanted = null;
  for (const ans of shuffle(t.ctx, t.q.answers)) {
    const b = t.el('button', 'min-height:44px;', t.ctx.brand.text(t.label(ans)), r);
    b.type = 'button';
    b.className = 'xq-a';
    t.mark(ans, b);
    if (ans === t.yes) wanted = b; else b.addEventListener('click', () => t.answer(ans, b));
  }
  return { A: wanted, r };
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
  none: { draw(t) { quad(t); } },
  mono: { draw(t) { quad(t); } },

  /* Press a wrong answer and it melts off the card. It pays nothing and takes nothing, but a right answer after it pays a little less. */
  'melt-away': {
    draw(t) {
      const p = quad(t, { under: 20 });
      for (const c of p.ns) {
        c.b.blocked = true;
        c.b.addEventListener('click', () => {
          if (c.b.melting) return;
          c.b.melting = true;
          t.melted();
          t.pop();
          t.ctx.toast(C.melted);
          c.b.style.pointerEvents = 'none';
          const frames = t.gentle ? [{ opacity: 1 }, { opacity: 0 }] : [{ transform: 'none', opacity: 1, borderRadius: '0' }, { transform: 'translateY(8px) scale(.92,1.4)', opacity: 1, borderRadius: '0 0 14px 14px', offset: 0.5 }, { transform: 'translateY(26px) scale(.45,2)', opacity: 0, borderRadius: '0 0 24px 24px' }];
          t.anim(c.b, frames, { duration: t.gentle ? 500 : 1600, easing: 'ease-in', fill: 'forwards' });
        });
      }
    },
  },

  /* Reach for a wrong answer and the wanted one takes its place. Three swaps, then they are tired. */
  'hover-swap': {
    draw(t) {
      const p = quad(t);
      let swaps = 0;
      const swap = (c) => {
        if (swaps >= 3) return false;
        swaps += 1;
        [c.x, p.y.x] = [p.y.x, c.x];
        [c.y, p.y.y] = [p.y.y, c.y];
        for (const k of [c, p.y]) { k.b.style.left = `${k.x}px`; k.b.style.top = `${k.y}px`; }
        return true;
      };
      t.card.addEventListener('pointermove', (ev) => {
        if (ev.pointerType !== 'mouse') return;
        for (const c of p.ns) {
          const r = c.b.getBoundingClientRect();
          if (ev.clientX > r.left - 6 && ev.clientX < r.right + 6 && ev.clientY > r.top - 6 && ev.clientY < r.bottom + 6) { swap(c); return; }
        }
      });
      for (const c of p.ns) c.b.addEventListener('pointerdown', (ev) => { if (ev.pointerType !== 'mouse' && swap(c)) { ev.preventDefault(); c.b.blocked = true; t.after(260, () => { c.b.blocked = false; }); } });
    },
  },

  /* The wrong answers slide away from your finger. Six runs in all, then you can catch them. */
  flee: {
    draw(t) {
      const bw = Math.floor((t.zw - GAP) / 2), rowH = BH + GAP;
      t.zone.style.height = `${rowH * 3 - GAP}px`;
      const spots = [0, 1, 2, 3, 4, 5].map((i) => [i % 2 ? bw + GAP : 0, (i >> 1) * rowH]);
      const taken = new Set([0, 1, 2, 3]);
      const start = shuffle(t.ctx, t.q.answers).map((ans, i) => ({ ans, at: i, b: t.btn(ans, spots[i][0], spots[i][1], bw) }));
      const ns = start.filter((c) => c.ans !== t.yes);
      let runs = 6;
      const run = (c) => {
        if (runs <= 0) return false;
        const free = [0, 1, 2, 3, 4, 5].filter((i) => !taken.has(i));
        const to = free[Math.floor(t.ctx.rng() * free.length)];
        runs -= 1;
        taken.delete(c.at);
        taken.add(to);
        c.at = to;
        c.b.style.left = `${spots[to][0]}px`;
        c.b.style.top = `${spots[to][1]}px`;
        return true;
      };
      t.card.addEventListener('pointermove', (ev) => {
        if (ev.pointerType !== 'mouse') return;
        for (const c of ns) {
          const r = c.b.getBoundingClientRect();
          if (Math.hypot(ev.clientX - (r.left + r.width / 2), ev.clientY - (r.top + r.height / 2)) < r.width / 2 + 10) { run(c); return; }
        }
      });
      for (const c of ns) c.b.addEventListener('pointerdown', (ev) => { if (ev.pointerType !== 'mouse' && run(c)) { ev.preventDefault(); c.b.blocked = true; t.after(260, () => { c.b.blocked = false; }); } });
    },
  },

  /* A wrong answer asks if you are sure, up to three times. */
  'nested-nag': {
    draw(t) {
      const p = quad(t, { under: 26 });
      let up = false;
      for (const c of p.ns) {
        c.b.blocked = true;
        c.b.addEventListener('click', () => {
          if (up) return;
          up = true;
          let n = 0;
          const nag = () => {
            const dw = Math.min(170, t.zw - 30);
            const box = t.el('div', `position:absolute;z-index:3;left:${t.zw - dw - (2 - n) * 10}px;top:${n * 24}px;width:${dw}px;height:82px;background:#f4f1e8;border:2px outset #fff;box-shadow:2px 2px 0 rgba(0,0,0,.35);`, null, t.zone);
            t.el('div', 'padding:6px 4px 0;text-align:center;font-size:14px;', C.nag[n], box);
            const b = t.el('button', 'left:50%;bottom:4px;margin-left:-60px;width:120px;height:40px;min-height:40px;', t.ctx.brand.text(t.label(c.ans)), box);
            b.type = 'button';
            b.className = 'xq-a';
            t.mark(c.ans, b);
            t.anim(box, [{ transform: 'scale(.7)', opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 140, easing: 'ease-out' });
            n += 1;
            b.addEventListener('click', () => { if (n >= C.nag.length) t.answer(c.ans, b); else nag(); });
          };
          nag();
        });
      }
    },
  },

  /* Fake buttons multiply. A fake just fades when pressed; the real four are in there. */
  crowd: {
    draw(t) {
      const cols = 3, rows = 4, gap = 4, bh = 38;
      const zh = rows * (bh + gap);
      t.zone.style.height = `${zh}px`;
      const bw = Math.floor((t.zw - (cols - 1) * gap) / cols);
      const slots = [];
      for (let r = 0; r < rows; r++) for (let c = 0; c < cols; c++) slots.push([c * (bw + gap), r * (bh + gap)]);
      const order = shuffle(t.ctx, slots);
      const answers = shuffle(t.ctx, t.q.answers);
      /* The real ones turn up among the fakes, so the first few never give it away. */
      const real = new Map(shuffle(t.ctx, [3, 4, 5, 6, 7, 8, 9, 10, 11]).slice(0, 4).map((idx, k) => [idx, answers[k]]));
      let i = 0;
      const add = () => {
        if (i >= order.length) return;
        const [x, y] = order[i];
        const idx = i++;
        let b;
        if (real.has(idx)) b = t.btn(real.get(idx), x, y, bw, bh);
        else {
          b = t.el('button', `left:${x}px;top:${y}px;width:${bw}px;height:${bh}px;min-height:0;`, t.ctx.brand.text(t.label(answers[idx % 4])), t.zone);
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
        b.style.padding = '1px 2px';
        if (!t.gentle) t.anim(b, [{ transform: 'scale(.3)' }, { transform: 'none' }], { duration: 130 });
      };
      t.every(t.gentle ? 400 : 110, add);
      for (let k = 0; k < 4; k++) add();
    },
  },

  /* Answers float up as bubbles and come round again. Pop one to choose. Gentle holds them still. */
  'bubble-pop': {
    draw(t) {
      const zh = 176, d = Math.min(74, Math.floor(t.zw / 3));
      t.zone.style.height = `${zh}px`;
      t.zone.style.overflow = 'hidden';
      const step = (t.zw - d) / 3;
      shuffle(t.ctx, t.q.answers).forEach((ans, i) => {
        const b = t.el('button', `left:${i * step}px;top:${zh - d}px;width:${d}px;height:${d}px;`, t.ctx.brand.text(t.label(ans)), t.zone);
        b.type = 'button';
        b.className = 'xq-bub';
        t.mark(ans, b);
        if (t.gentle) {
          b.style.left = `${i % 2 ? t.zw - d - 6 : 6}px`;
          b.style.top = `${(i >> 1) * (d + 6)}px`;
        } else {
          t.anim(b, [{ transform: `translateY(${d}px)` }, { transform: `translateY(${-(zh - d) - 4}px)` }], { duration: 4400 + i * 500, delay: -i * 1300, iterations: Infinity, easing: 'linear' });
        }
        b.addEventListener('click', () => { t.pop(); t.answer(ans, b); });
      });
    },
  },

  /* A slider with the four answers along it, leaning to the last. Your answer is wherever it is when you press. */
  'lean-slider': {
    draw(t) {
      const zh = 122, list = t.q.answers;
      t.zone.style.height = `${zh}px`;
      const num = t.el('div', `position:absolute;left:0;right:0;top:0;text-align:center;font:800 22px sans-serif;color:#333;font-variant-numeric:tabular-nums;white-space:nowrap;`, '', t.zone);
      const track = t.el('div', 'top:40px;', null, t.zone);
      track.className = 'xq-slide';
      const knob = t.el('div', '', null, track);
      knob.className = 'xq-knob';
      const nw = Math.floor(t.zw / 4);
      list.forEach((ans, i) => {
        const n = t.note(t.label(ans), i * nw, 54, nw);
        n.style.fontSize = '11px';
      });
      let v = 0.2, held = false;
      const at = () => list[Math.min(list.length - 1, Math.floor(v * list.length))];
      const draw = () => {
        knob.style.left = `${v * 100}%`;
        num.textContent = t.ctx.brand.text(t.label(at()));
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
      t.frame((dt) => { if (!held && v < 1) { v = Math.min(1, v + dt * (0.06 + v * 0.2)); draw(); } });
      draw();
      const dw = Math.min(170, t.zw);
      const done = t.el('button', `left:${(t.zw - dw) / 2}px;top:${zh - 46}px;width:${dw}px;height:44px;`, C.sliderDone, t.zone);
      done.type = 'button';
      done.className = 'xq-a';
      t.mark(t.yes, done);
      done.addEventListener('click', () => t.answer(at(), done));
    },
  },

  /* The right one grows, the others shrink. All stay pressable. */
  'size-skew': {
    draw(t) {
      const p = quad(t);
      p.y.b.style.zIndex = '2';
      if (t.gentle) { p.y.b.style.transform = 'scale(1.18)'; for (const c of p.ns) c.b.style.transform = 'scale(.7)'; return; }
      const opts = { duration: 3200, easing: 'ease-in-out', fill: 'forwards' };
      t.anim(p.y.b, [{ transform: 'scale(1)' }, { transform: 'scale(1.22)' }], opts);
      for (const c of p.ns) t.anim(c.b, [{ transform: 'scale(1)' }, { transform: 'scale(.62)' }], opts);
    },
  },

  /* The wrong ones fade out but can still be tapped. */
  'opacity-skew': {
    draw(t) {
      const p = quad(t);
      for (const c of p.ns) {
        t.anim(c.b, [{ opacity: 1 }, { opacity: 0.06 }], { duration: 2400, easing: 'ease-in', fill: 'forwards' });
        c.b.addEventListener('pointerdown', () => { const r = c.b.getBoundingClientRect(); t.float(r.left + r.width / 2, r.top, C.stillThere, 'color:#a3232b;font-size:14px;'); });
      }
    },
  },

  /* The right answer drifts toward your finger. The others stay on top of it, so each can always be pressed. */
  magnet: {
    draw(t) {
      const p = quad(t, { under: 34 });
      const zh = parseFloat(t.zone.style.height), bw = p.bw;
      const y = p.y.b;
      y.style.transition = 'none';
      for (const c of p.ns) c.b.style.zIndex = '2';
      let x0 = p.y.x, y0 = p.y.y, tx = x0, ty = y0;
      const aim = (ev) => {
        const r = t.zone.getBoundingClientRect();
        tx = clamp(ev.clientX - r.left - bw / 2, 0, t.zw - bw);
        ty = clamp(ev.clientY - r.top - BH / 2, 0, zh - BH);
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

  /* The other answers arrive after a wait, one by one. */
  'late-bloom': {
    extraMs: 9000,
    draw(t) {
      const bw = Math.floor((t.zw - GAP) / 2), rowH = BH + GAP;
      t.zone.style.height = `${rowH * 2 - GAP}px`;
      const WAIT = 5000;
      shuffle(t.ctx, t.q.answers).forEach((ans, i) => {
        const x = i % 2 ? bw + GAP : 0, y = (i >> 1) * rowH;
        if (ans === t.yes) { t.btn(ans, x, y, bw); return; }
        const wait = WAIT + (i * 2000);
        const slot = t.el('div', `position:absolute;left:${x}px;top:${y}px;width:${bw}px;height:${BH}px;box-sizing:border-box;border:1.5px dashed #888;overflow:hidden;`, null, t.zone);
        const f = t.el('div', 'position:absolute;inset:0;background:#d4d0c8;transform-origin:0 50%;', null, slot);
        t.anim(f, [{ transform: 'scaleX(0)' }, { transform: 'scaleX(1)' }], { duration: wait, easing: 'linear', fill: 'forwards' });
        let left = Math.ceil(wait / 1000);
        const lab = t.el('div', 'position:absolute;inset:0;display:grid;place-items:center;font-size:13px;color:#333;', fill(C.lateIn, left), slot);
        t.every(1000, () => { left -= 1; if (left > 0) lab.textContent = fill(C.lateIn, left); });
        t.after(wait, () => {
          slot.remove();
          const n = t.btn(ans, x, y, bw);
          t.anim(n, [{ transform: 'scale(.8)', opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 180 });
        });
      });
    },
  },

  /* The right one takes one tap. Any other takes a three-second hold. */
  'hold-refuse': {
    draw(t) {
      const p = quad(t, { top: 18, under: 8 });
      t.note(C.oneTap, p.y.x, p.y.y - 17, p.bw);
      for (const c of p.ns) {
        const lab = t.note(fill(C.holdFor, 3), c.x, c.y - 17, p.bw);
        const bar = t.el('div', `left:${c.x}px;top:${c.y + BH + 1}px;width:${p.bw}px;height:4px;`, null, t.zone);
        bar.className = 'xq-bar';
        const inner = t.el('i', '', null, bar);
        c.b.blocked = true;
        c.b.addEventListener('click', (ev) => { if (ev.detail === 0) t.answer(c.ans, c.b); });
        holdable(t, c.b, 3000, {
          onTick(f) { inner.style.transform = `scaleX(${f})`; lab.textContent = fill(C.holdFor, Math.max(1, Math.ceil(3 * (1 - f)))); },
          onCancel() { inner.style.transform = 'scaleX(0)'; lab.textContent = fill(C.holdFor, 3); },
          done() { lab.textContent = C.phew; t.after(220, () => t.answer(c.ans, c.b)); },
        });
      }
    },
  },

  /* The other answers are under piles of notes. Drag them off, or flick one with a tap. */
  'drag-reveal': {
    draw(t) {
      const p = quad(t, { top: 2, under: 22 });
      t.note(C.whereIsNo, 0, parseFloat(t.zone.style.height) - 18, t.zw);
      const tints = ['#d8f5c9', '#ffd0e8', '#c7e8ff', '#fff2a8'];
      p.ns.forEach((cell, k) => [0, 1].forEach((i) => {
        const tint = tints[(k + i) % tints.length];
        const c = t.el('div', `left:${cell.x + i * 2 - 1}px;top:${cell.y + i - 2}px;width:${p.bw}px;height:50px;background:${tint};z-index:${2 + i};`, C.stickies[(k + i) % C.stickies.length], t.zone);
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
      }));
    },
  },

  /* Each wrong answer peels off to show itself again. Six in all, each. */
  'bottomless-no': {
    draw(t) {
      const p = quad(t, { top: 18 });
      for (const cell of p.ns) {
        let left = 6;
        const lab = t.note(fill(C.more, left), cell.x, cell.y - 17, p.bw);
        lab.style.color = '#a3232b';
        cell.b.style.boxShadow = '2px 2px 0 #aaa,4px 4px 0 #999';
        cell.b.blocked = true;
        cell.b.addEventListener('click', () => {
          left -= 1;
          if (left <= 0) { t.answer(cell.ans, cell.b); return; }
          lab.textContent = fill(C.more, left);
          const c = t.el('div', `position:absolute;left:${cell.x}px;top:${cell.y}px;width:${p.bw}px;height:${BH}px;box-sizing:border-box;background:#d4d0c8;border:2px outset #fff;display:grid;place-items:center;pointer-events:none;`, t.ctx.brand.text(t.label(cell.ans)), t.zone);
          const a = t.anim(c, [{ transform: 'none', opacity: 1 }, { transform: 'translate(46px,-46px) rotate(24deg)', opacity: 0 }], { duration: 420, easing: 'ease-out' });
          a.onfinish = () => c.remove();
          t.pop();
        });
      }
    },
  },

  /* Not a robot: hold the box steady for two seconds. It knows it is a joke, and there is nothing to fill in. */
  'verify-checkbox': {
    draw(t) {
      const wrongs = t.q.answers.filter((a) => a !== t.yes);
      t.zone.style.height = '134px';
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
      t.mark(t.yes, box);
      const bw = Math.floor((t.zw - 2 * 4) / 3);
      shuffle(t.ctx, wrongs).forEach((ans, i) => { t.btn(ans, i * (bw + 4), 84, bw).style.fontSize = '12px'; });
    },
  },

  /* Select the spirals. The Done button points out what you missed once, then takes it anyway. */
  'verify-grid': {
    draw(t) {
      const n = 4 + Math.floor(t.ctx.rng() * 3);
      const decoy = DECOY_EMOJI[t.q.decoy] || FILLER[0];
      const set = shuffle(t.ctx, [...Array(n).fill(SPIRAL), ...shuffle(t.ctx, [decoy, decoy, ...FILLER]).slice(0, 9 - n)]);
      const { list } = tiles(t, set, (b) => { b.classList.toggle('on'); t.pop(); });
      const { A } = underRow(t);
      let warned = false;
      A.addEventListener('click', () => {
        const missed = list.some((b, i) => set[i] === SPIRAL && !b.classList.contains('on')) || list.some((b, i) => set[i] !== SPIRAL && b.classList.contains('on'));
        if (missed && !warned) { warned = true; t.deny(); t.nudge(A); t.ctx.toast(C.gridMissed); return; }
        t.answer(t.yes, A);
      });
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
      const { A } = underRow(t);
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
      const { A } = underRow(t);
      A.addEventListener('click', () => t.answer(t.yes, A));
    },
    extraMs: 15000,
  },

  /* A pink cursor comes to get the answer for you. Knock it away three times and it gives up. Any other answer always works. */
  'mouse-hijack': {
    draw(t) {
      const p = quad(t, { top: 30 });
      const cur = t.el('div', '', null, t.zone);
      cur.className = 'xq-cur';
      t.el('i', '', null, cur);
      const yr = () => ({ x: p.y.x + p.bw / 2, y: p.y.y + 14 });
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
        if (d < 6) { gone = true; t.ctx.toast(C.hijackDone); t.after(200, () => t.answer(t.yes, p.y.b)); return; }
        const step = Math.min(d, 62 * dt);
        x += (dx / d) * step; y += (dy / d) * step;
        cur.style.left = `${x}px`; cur.style.top = `${y}px`;
      });
    },
  },
};
