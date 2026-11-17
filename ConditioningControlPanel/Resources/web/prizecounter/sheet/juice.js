/* sheet/juice.js - the sheet's movement, feedback and sound on top of what sheet/view.js paints (Lane MOTION). It decides
 * nothing and never touches the buy/wear logic, the wire, the press guard or the Esc ladder: it wraps view.paint, looks at
 * what changed between two paints (tab, pick, the Yes step, the OWNED stamp, the wallet, the gift, the stamp card, the
 * saving-for bar, what is worn) and plays a short animation on the nodes that are on screen, through Web Animations.
 * Cartoon rules: anticipation, squash and stretch, overshoot, follow-through; every effect has an IN and an OUT.
 * Motion levels (one rule for all of it): off = nothing moves and nothing sounds, less = half the travel, Gentle = fades
 * only (no transform at all, fades never rise faster than 300 ms), never a flicker. Only transform-family properties
 * (translate, rotate, scale, transform) and opacity animate. A page without Web Animations (the tests' fake DOM) gets the
 * same outcome with no movement. Static looks live in sheet.css (Lane LOOK); the juice's own CSS is sheet-juice.css. */

/** Every timing in one place (ms unless named otherwise). Tune here. */
export const TIMING = {
  slide: 280,          // the sheet's own slide in from the wallet (sheet.css --slide); arrivals wait for most of it
  arriveMs: 460,       // one part's arrival
  arriveStep: 60,      // between parts: head, then tabs, then the spotlight, then the shelves, then the fitting room
  arriveSpan: 640,     // the whole stagger never takes longer than this before the last part starts
  arriveLift: 18,      // px the parts rise from
  tabPopMs: 340,       // the picked tab pops
  swapMs: 300,         // the panel slides in from the side you travelled to
  swapShift: 34,       // px
  swapStep: 45,        // between sections of the new panel
  pickMs: 460,         // the picked card bounces once
  roomMs: 320,         // the fitting room's lines settle in after a pick
  roomStep: 35,
  swingMs: 1100,       // a price tag swings on its hole when its shelf comes into view (once per view)
  swingDeg: 9,
  swingStep: 40,
  swingCap: 10,        // tags past this many swing together
  askMs: 420,          // the Yes step: the button gathers itself (anticipation) and the question drops in
  soldMs: 1700,        // the OWNED stamp's whole life (matches SOLD_MS in sheet/main.js)
  soldHit: 0.1,        // where in that life it hits the paper (fraction)
  confetti: 18,        // bits at the stamp's hit (less: half, Gentle: none)
  rollMs: 900,         // the wallet rolls down digit by digit while the stars leave
  rollSteps: 12,
  lidMs: 700,          // the gift's lid pops
  stampMs: 520,        // a stamp press thumps the card
  couponMs: 760,       // the coupon tears off
  meterMs: 820,        // the saving-for bar grows with an overshoot
  shineMs: 900,
  nopeMs: 480,         // not enough Sparkles: a friendly head-shake on the sticker
  nopePx: 6,
  wipeMs: 420,         // wear / take off: the screen wipes to the new look
  lessTime: 0.8,       // less: durations a little shorter as well as half the travel
  gentleFloor: 300,    // Gentle: no fade rises faster than this
};
const EASE = {
  out: 'cubic-bezier(.2,.8,.25,1)',
  back: 'cubic-bezier(.3,1.45,.5,1)',      // overshoot, settles
  inOut: 'cubic-bezier(.45,0,.3,1)',
  soft: 'ease-out',
};

/** The motion level as numbers: amp scales every distance and angle, time scales durations, fade = Gentle. */
export function levelOf({ motion = 'full', gentle = false } = {}) {
  if (motion === 'off') return { mode: 'off', on: false, amp: 0, time: 0, fade: false };
  if (gentle) return { mode: 'gentle', on: true, amp: 0, time: 1, fade: true };
  if (motion === 'less') return { mode: 'less', on: true, amp: 0.5, time: TIMING.lessTime, fade: false };
  return { mode: 'full', on: true, amp: 1, time: 1, fade: false };
}
/** A duration at a level: 0 when off; Gentle never below its floor. */
export function durOf(ms, L) {
  if (!L.on) return 0;
  const d = Math.round(ms * L.time);
  return L.fade ? Math.max(TIMING.gentleFloor, d) : d;
}
/** Gentle: a keyframe keeps only its opacity (and offset / easing); no movement survives. */
export function fadeOnly(frames) {
  const out = frames.map((f) => { const g = {}; for (const k of ['offset', 'easing', 'opacity']) if (k in f) g[k] = f[k]; return g; });
  return out.some((f) => 'opacity' in f) ? out : null;
}

/** The open stagger: head, tabs (close together), the spotlight, the shelves, then the fitting room; within a kind, page
 * order. Answers one delay per node (same order as `kinds`), capped so the last part starts by `span`. */
export const ARRIVE_ORDER = ['head', 'tab', 'spot', 'sec', 'room'];
const WEIGHT = { tab: 0.35 };
export function staggerDelays(kinds, { step = TIMING.arriveStep, span = TIMING.arriveSpan } = {}) {
  const order = kinds.map((k, i) => [ARRIVE_ORDER.indexOf(k) < 0 ? ARRIVE_ORDER.length : ARRIVE_ORDER.indexOf(k), i, k]).sort((a, b) => a[0] - b[0] || a[1] - b[1]);
  const out = new Array(kinds.length);
  let at = 0, prev = null;
  for (const [, i, k] of order) {
    if (prev !== null) at += step * (k === prev && WEIGHT[k] ? WEIGHT[k] : 1);
    out[i] = Math.round(Math.min(at, span));
    prev = k;
  }
  return out;
}

/** The wallet's roll: whole numbers from `from` to `to`, fast first and slowing into the end, no repeats, ending on `to`. */
export function rollSteps(from, to, n = TIMING.rollSteps) {
  from = Math.round(Number(from) || 0); to = Math.round(Number(to) || 0);
  if (from === to) return [to];
  const out = [];
  for (let i = 1; i <= n; i++) {
    const k = 1 - Math.pow(1 - i / n, 2), v = Math.round(from + (to - from) * k);
    if (v !== from && v !== out[out.length - 1]) out.push(v);
  }
  if (out[out.length - 1] !== to) out.push(to);
  return out;
}
/** Which digits of `b` (left to right) differ from the same place in `a`, right aligned (a new leading digit counts). */
export function changedDigits(a, b) {
  const s = String(a), t = String(b), out = [];
  for (let i = 0; i < t.length; i++) { const j = s.length - t.length + i; if (j < 0 || s[j] !== t[i]) out.push(i); }
  return out;
}
/** The saving-for bar's growth as a scale from the old fill: starts at old/new, overshoots, settles at 1. */
export function meterFrames(from, to, amp = 1) {
  const f = Math.max(0, Math.min(1, to > 0 ? from / to : 1));
  return [{ scale: `${f} 1` }, { scale: `${1 + 0.05 * amp} 1`, offset: 0.62 }, { scale: `${1 - 0.015 * amp} 1`, offset: 0.82 }, { scale: '1 1' }];
}
/** A hung tag's swing: big first, each swing smaller and the other way, rest at 0. */
export function swingAngles(deg = TIMING.swingDeg, amp = 1, swings = 4) {
  const out = [0];
  for (let i = 0; i < swings; i++) out.push(Math.round(deg * amp * Math.pow(-0.55, i) * 100) / 100);
  out.push(0);
  return out;
}
/** Percent from a meter fill's inline style ("width:55%"), or null. */
export function pctOf(style) { const m = /width:\s*([\d.]+)%/.exec(String(style || '')); return m ? Number(m[1]) : null; }

export function installJuice(view, { motion = 'full', gentle = false, doc, win = globalThis.window, sound = null, fx = null, sheet = false,
  now = () => (globalThis.performance ? globalThis.performance.now() : Date.now()), later = (f, ms) => setTimeout(f, ms) } = {}) {
  const L = levelOf({ motion, gentle }), A = L.amp, root = view.root;
  const live = L.on && typeof root.animate === 'function' && typeof root.querySelector === 'function';
  const st = { arrived: false, arriveAt: 0, t0: now(), tab: null, pick: null, sold: null, soldAt: 0, soldNode: null, ask: false, sp: null,
    eq: null, gift: null, fresh: 0, coupon: null, meter: null, roll: 0, swung: new Set(), io: null };
  let offVis = null;   // dispose() (a reused page mounts again: sheet/reuse.js)
  const sfx = (name, ...a) => { try { if (sound && typeof sound[name] === 'function') sound[name](...a); } catch (e) { /* sound is a bonus */ } };
  const q = (n, sel) => (n && typeof n.querySelector === 'function' ? n.querySelector(sel) : null);
  const qa = (n, sel) => (n && typeof n.querySelector === 'function' ? Array.from(n.querySelectorAll(sel)) : []);

  /** One animation: frames at this level (Gentle keeps fades only), filled only backwards so nothing is held after it
   * ends (the static look and the hover states take over again). at = ms already played (a repaint mid-way). */
  function play(n, frames, ms, { delay = 0, easing = EASE.out, at = 0, composite, fill = 'backwards' } = {}) {
    if (!live || !n || typeof n.animate !== 'function') return null;
    const fr = L.fade ? fadeOnly(frames) : frames;
    if (!fr) return null;
    try {
      const a = n.animate(fr, { duration: durOf(ms, L), delay: L.fade ? 0 : Math.round(delay * L.time), easing, fill, ...(composite ? { composite } : {}) });
      if (at > 0) a.currentTime = at;
      return a;
    } catch (e) { return null; }
  }

  /* ---------- open: the parts arrive in a short stagger after the sheet slides ---------- */
  function arrive(t) {
    const nodes = [], kinds = [];
    const add = (n, k) => { if (n && !n.hidden) { nodes.push(n); kinds.push(k); } };
    add(view.head, 'head');
    for (const tb of qa(root, '.s-tab')) add(tb, 'tab');
    for (const c of Array.from(view.list.children)) add(c, c.classList.contains('spot') ? 'spot' : 'sec');
    add(view.room, 'room');
    const delays = staggerDelays(kinds), wait = Math.max(0, st.arriveAt - t), at = Math.max(0, t - st.arriveAt);
    nodes.forEach((n, i) => {
      const k = kinds[i], lift = (k === 'head' ? -10 : k === 'room' ? 0 : TIMING.arriveLift) * A, side = k === 'room' ? 22 * A : 0;
      const frames = k === 'tab'
        ? [{ translate: `0 ${12 * A}px`, scale: `${1 - 0.12 * A}`, opacity: 0 }, { translate: '0 0', scale: `${1 + 0.06 * A}`, opacity: 1, offset: 0.6 }, { translate: '0 0', scale: '1', opacity: 1 }]
        : [{ translate: `${side}px ${lift}px`, scale: `${1 - 0.03 * A}`, opacity: 0 }, { translate: `0 ${-2 * A}px`, scale: '1', opacity: 1, offset: 0.7 }, { translate: '0 0', scale: '1', opacity: 1 }];
      play(n, frames, TIMING.arriveMs, { delay: delays[i] + wait, easing: EASE.out, at });
    });
    return Math.max(0, ...delays) + TIMING.arriveMs;
  }

  /* ---------- tabs: the picked tab pops, the panel slides in from the side you travelled to ---------- */
  function swap(dir) {
    const tb = q(root, '.s-tab[aria-selected="true"]');
    play(tb, [{ scale: `${1 - 0.1 * A} ${1 - 0.06 * A}` }, { scale: `${1 + 0.08 * A} ${1 + 0.1 * A}`, offset: 0.45 }, { scale: `${1 - 0.02 * A}`, offset: 0.75 }, { scale: '1' }], TIMING.tabPopMs, { easing: EASE.inOut });
    Array.from(view.list.children).slice(0, 6).forEach((c, i) => play(c,
      [{ translate: `${dir * TIMING.swapShift * A}px 0`, opacity: 0 }, { translate: `${-dir * 3 * A}px 0`, opacity: 1, offset: 0.75 }, { translate: '0 0', opacity: 1 }],
      TIMING.swapMs, { delay: i * TIMING.swapStep }));
    sfx('blip');
  }

  /* ---------- cards: the picked card bounces once, the fitting room settles, a short sticker shakes its head ---------- */
  function picked(id) {
    const card = qa(view.list, '.it[aria-pressed="true"]').find((n) => n.dataset.key === 'it:' + id);
    const stand = q(card, '.stand');
    play(stand, [{ scale: '1' }, { scale: `${1 + 0.07 * A} ${1 - 0.1 * A}`, offset: 0.16 }, { scale: `${1 - 0.05 * A} ${1 + 0.08 * A}`, offset: 0.42 }, { scale: `${1 + 0.02 * A} ${1 - 0.02 * A}`, offset: 0.7 }, { scale: '1' }], TIMING.pickMs, { easing: 'linear' });
    room();
    const short = q(view.room, '.d-price .tg.short');
    if (short) { nope(short, 140); later(() => sfx('nope'), 120); } else sfx('pick');
  }
  function room() {
    Array.from(view.room.children).slice(0, 6).forEach((c, i) => play(c, [{ translate: `0 ${8 * A}px`, opacity: 0 }, { translate: '0 0', opacity: 1 }], TIMING.roomMs, { delay: i * TIMING.roomStep }));
  }
  function nope(n, delay = 0) {
    const p = TIMING.nopePx * A;
    play(n, [{ translate: '0 0', rotate: '0deg' }, { translate: `${-p}px 0`, rotate: `${-3 * A}deg`, offset: 0.18 }, { translate: `${p * 0.8}px 0`, rotate: `${2.4 * A}deg`, offset: 0.4 },
      { translate: `${-p * 0.45}px 0`, rotate: `${-1.2 * A}deg`, offset: 0.62 }, { translate: `${p * 0.2}px 0`, rotate: '0deg', offset: 0.82 }, { translate: '0 0', rotate: '0deg' }], TIMING.nopeMs, { delay, easing: 'linear' });
  }

  /* ---------- the price stickers swing like hung tags when a shelf comes into view, once per view ---------- */
  function tags(t) {
    const IO = win && win.IntersectionObserver;
    if (!live || typeof IO !== 'function') return;
    if (!st.io) {
      st.io = new IO((entries) => {
        const hits = entries.filter((e) => e.isIntersecting);
        const wait = Math.max(0, st.arriveAt + 220 - now());
        hits.forEach((e, i) => {
          st.io.unobserve(e.target);
          if (st.swung.has(e.target.dataset.jkey)) return;
          st.swung.add(e.target.dataset.jkey);
          const ang = swingAngles(TIMING.swingDeg, A);
          play(e.target, ang.map((d, j) => ({ rotate: `${d}deg`, offset: j / (ang.length - 1), easing: 'ease-in-out' })), TIMING.swingMs, { delay: wait + Math.min(i, TIMING.swingCap) * TIMING.swingStep, easing: 'linear' });
        });
      }, { threshold: 0.6 });
    }
    st.io.disconnect();
    for (const tg of qa(view.list, '.tg')) {
      const it = tg.closest('.it') || tg.closest('.spot');
      tg.dataset.jkey = (st.tab || '') + '|' + (it ? it.dataset.key || 'spot' : 'tag');
      if (!st.swung.has(tg.dataset.jkey)) st.io.observe(tg);
    }
  }

  /* ---------- the buy moment: anticipation on Yes, the stamp's hit, ink and confetti, the wallet rolls down ---------- */
  function ask() {
    const yes = qa(view.room, '.d-act .btn').find((b) => b.dataset.main === '1' && !b.disabled) || q(view.room, '.d-act .btn');
    play(q(view.room, '.d-ask'), [{ translate: `0 ${-8 * A}px`, opacity: 0 }, { translate: '0 0', opacity: 1 }], TIMING.askMs * 0.7, { easing: EASE.back });
    play(yes, [{ scale: '1' }, { scale: `${1 + 0.05 * A} ${1 - 0.1 * A}`, offset: 0.3 }, { scale: `${1 - 0.03 * A} ${1 + 0.07 * A}`, offset: 0.6 }, { scale: '1' }], TIMING.askMs, { easing: EASE.inOut, delay: 60 });
    sfx('knock');
  }
  function stamp(node, t, fresh) {
    if (L.fade) return;   // Gentle: sheet.css's own fade stays
    const ms = TIMING.soldMs, hit = TIMING.soldHit;
    // linear overall, eased per segment, so the hit lands exactly at soldHit: the slam accelerates in, then squash and settle
    // composite add: the squash rides on the stamp's own centring transform (sheet.css), in the stamp's own frame
    // (opacity plays apart, replacing: an added opacity would never fade)
    const io = 'ease-in-out', o = 'ease-out', sc = (x, y = x, r = 0) => `rotate(${r}deg) scale(${x}, ${y})`, from = fresh ? 0 : t - st.soldAt;
    play(node, [{ transform: sc(1 + 1.1 * A, 1 + 1.1 * A, -10 * A), easing: 'cubic-bezier(.6,0,.9,.5)' },
      { transform: sc(1 + 0.1 * A, 1 - 0.16 * A), offset: hit, easing: o },
      { transform: sc(1 - 0.06 * A, 1 + 0.08 * A), offset: hit + 0.06, easing: io }, { transform: sc(1 + 0.02 * A), offset: hit + 0.12, easing: io }, { transform: sc(1), offset: hit + 0.18 },
      { transform: sc(1), offset: 0.84, easing: 'ease-in' }, { transform: sc(1 + 0.04 * A) }], ms / L.time, { easing: 'linear', at: from, composite: 'add' });
    play(node, [{ opacity: 0, easing: 'ease-in' }, { opacity: 1, offset: hit }, { opacity: 1, offset: 0.84, easing: 'ease-in' }, { opacity: 0 }], ms / L.time, { easing: 'linear', at: from, fill: 'both' });   // held out: paint removes it
    if (!fresh) return;
    later(() => {
      const mon = q(view.room, '.mon'), c = colourOf(mon);
      if (fx && typeof fx.at === 'function' && node.isConnected) { const p = fx.at(node); fx.ink(p.x, p.y); fx.confetti(p.x, p.y, c, Math.round(TIMING.confetti * A)); }
      play(mon, [{ translate: '0 0' }, { translate: `0 ${4 * A}px`, offset: 0.25 }, { translate: `0 ${-1.5 * A}px`, offset: 0.6 }, { translate: '0 0' }], 300);
      sfx('fanfare');
    }, Math.round(ms * hit));
  }
  const colourOf = (n) => { try { return (win.getComputedStyle(n).getPropertyValue('--fam') || '').trim() || null; } catch (e) { return null; } };
  function roll(from, to) {
    const n = q(view.wallet, '.n');
    if (!n || view.wallet.hidden || !live) return;
    const steps = rollSteps(from, to), token = ++st.roll, gap = TIMING.rollMs * L.time / steps.length;
    let prev = from;
    const show = (v) => {
      const ch = changedDigits(prev, v), s = String(v), spans = [];
      for (let i = 0; i < s.length; i++) { const d = doc.createElement('span'); d.className = 'j-dg'; d.textContent = s[i]; spans.push(d); }
      n.replaceChildren(...spans);
      if (!L.fade) for (const i of ch) play(spans[i], [{ translate: `0 ${-0.45 * A}em`, opacity: 0.2 }, { translate: '0 0', opacity: 1 }], gap * 1.6, { easing: EASE.back });
      prev = v;
    };
    show(from);
    steps.forEach((v, i) => later(() => { if (st.roll === token) { if (i === steps.length - 1) { n.textContent = String(v); } else show(v); } }, Math.round((i + 1) * gap)));
    play(view.wallet, [{ scale: '1' }, { scale: `${1 - 0.06 * A}`, offset: 0.2 }, { scale: `${1 + 0.03 * A}`, offset: 0.55 }, { scale: '1' }], 360, { easing: EASE.inOut });
  }

  /* ---------- gift and stamps: the lid pops, a stamp thumps, the coupon tears off, the bar fills ---------- */
  function lid() {
    const box = q(view.list, '.gift .gbox'), gl = q(box, '.gl');
    play(gl, [{ transform: 'none', offset: 0 }, { transform: `translate(${-8 * A}px, ${-40 * A}px) rotate(${-48 * A}deg)`, offset: 0.32 }, { transform: `translate(${-3 * A}px, ${-6 * A}px) rotate(${-20 * A}deg)`, offset: 0.7 }], TIMING.lidMs, { easing: EASE.out });
    play(box, [{ scale: '1' }, { scale: `${1 + 0.1 * A} ${1 - 0.14 * A}`, offset: 0.14 }, { scale: `${1 - 0.06 * A} ${1 + 0.1 * A}`, offset: 0.36 }, { scale: `${1 + 0.02 * A} ${1 - 0.02 * A}`, offset: 0.62 }, { scale: '1' }], TIMING.lidMs, { easing: 'linear' });
    sfx('pick');
  }
  function stampPress(dot) {
    const card = q(view.list, '.scard');
    play(card, [{ translate: '0 0', scale: '1' }, { translate: `0 ${3 * A}px`, scale: `${1 + 0.008 * A} ${1 - 0.02 * A}`, offset: 0.22 }, { translate: `0 ${-1 * A}px`, scale: '1', offset: 0.55 }, { translate: '0 0', scale: '1' }], TIMING.stampMs, { easing: 'linear' });
    if (fx && typeof fx.at === 'function' && dot) { const p = fx.at(dot); later(() => fx.ink(p.x, p.y, true), 90); }
  }
  function tear(cp) {
    play(cp, [{ translate: '0 0', rotate: '0deg' }, { translate: `${1 * A}px ${-1 * A}px`, rotate: `${-2 * A}deg`, offset: 0.22 }, { translate: `${10 * A}px ${-12 * A}px`, rotate: `${8 * A}deg`, offset: 0.5 },
      { translate: `${3 * A}px ${-2 * A}px`, rotate: `${2 * A}deg`, offset: 0.78 }, { translate: '0 0', rotate: '0deg' }], TIMING.couponMs, { delay: 260, easing: EASE.inOut });
    if (fx && typeof fx.at === 'function') later(() => { if (cp.isConnected) { const p = fx.at(cp); fx.confetti(p.x, p.y, null, Math.round(TIMING.confetti * 0.6 * A)); } }, 520);
    later(() => sfx('air', 0.3, 0.02, true), 260);
  }
  function grow(fill, from, to) {
    play(fill, meterFrames(from, to, A), TIMING.meterMs, { easing: EASE.out });
    if (L.fade) return;
    fill.classList.add('j-shine');
    later(() => fill.classList.remove('j-shine'), TIMING.shineMs + TIMING.meterMs);
  }

  /* ---------- wear / take off: the fitting room's screen wipes to the new look ---------- */
  function wipe() {
    const prev = q(view.room, '.prev');
    if (!prev || !live) return;
    const w = doc.createElement('i');
    w.className = 'j-wipe';
    prev.append(w);
    const a = L.fade
      ? play(w, [{ opacity: 0 }, { opacity: 0.55, offset: 0.4 }, { opacity: 0 }], TIMING.wipeMs, { easing: 'linear' })
      : play(w, [{ transform: 'scaleX(0)', transformOrigin: '0 50%' }, { transform: 'scaleX(1)', transformOrigin: '0 50%', offset: 0.45 }, { transform: 'scaleX(1)', transformOrigin: '100% 50%', offset: 0.55 }, { transform: 'scaleX(0)', transformOrigin: '100% 50%' }], TIMING.wipeMs, { easing: EASE.inOut });
    if (a) a.onfinish = () => w.remove(); else w.remove();
  }

  /* ---------- idle: a few slow bubbles drift up behind the content (sheet-juice.css), paused while hidden ---------- */
  if (live && !L.fade && typeof root.prepend === 'function') {
    const layer = doc.createElement('div');
    layer.className = 'j-idle';
    layer.setAttribute('aria-hidden', 'true');
    for (const [x, s, d, w] of [[8, 34, 26, 0], [23, 18, 21, 9], [47, 26, 31, 4], [68, 14, 19, 14], [86, 40, 34, 20], [57, 12, 23, 27]]) {
      const b = doc.createElement('i');
      b.className = 'j-bub';
      b.setAttribute('style', `--x:${x}%;--s:${s}px;--d:${d}s;--w:${-w}s`);
      layer.append(b);
    }
    root.prepend(layer);
    const vis = () => { layer.dataset.paused = doc.hidden ? '1' : ''; };
    if (typeof doc.addEventListener === 'function') { doc.addEventListener('visibilitychange', vis); offVis = () => doc.removeEventListener('visibilitychange', vis); }
  }

  /* ---------- the paint hook: compare, then animate what changed ---------- */
  function after(v) {
    const t = now();
    if (v.status !== 'ready') { st.sp = v.sp; return; }
    let opened = false;
    if (!st.arrived) { st.arrived = true; opened = true; st.arriveAt = t + Math.max(0, (sheet ? TIMING.slide * 0.7 : 0) - (t - st.t0)); }
    const inArrival = t - st.arriveAt < TIMING.arriveSpan + TIMING.arriveMs;
    if (v.tab !== st.tab) st.swung.clear();
    if (inArrival && (opened || v.tab === st.tab)) arrive(t);
    else if (st.tab !== null && v.tab !== st.tab && !v.query) { const i0 = v.tabs.indexOf(st.tab), i1 = v.tabs.indexOf(v.tab); swap(i1 >= i0 ? 1 : -1); }
    st.tab = v.tab;
    tags(t);
    if (v.pick && v.pick !== st.pick && !opened) picked(v.pick);
    st.pick = v.pick;
    const asking = !!(v.room && v.room.ask);
    if (asking && !st.ask) ask();
    st.ask = asking;
    if (v.sold) {
      const node = q(view.room, '.sold'), fresh = v.sold !== st.sold;
      if (fresh) st.soldAt = t;
      if (node && node !== st.soldNode) stamp(node, t, fresh);
      st.soldNode = node;
    }
    st.sold = v.sold;
    const spent = st.sp !== null && v.sp < st.sp;
    if (spent) roll(st.sp, v.sp); else if (v.sp !== st.sp) st.roll++;
    st.sp = v.sp;
    const gift = q(view.list, '.gift');
    if (gift) { const ready = !gift.classList.contains('opened'); if (st.gift === true && !ready) lid(); st.gift = ready; }
    const dot = q(view.list, '.dots span.fresh'), day = dot ? Array.from(dot.parentNode.children).indexOf(dot) + 1 : 0;
    if (day && day !== st.fresh) stampPress(dot);
    st.fresh = day;
    const cp = q(view.list, '.coupon');
    if (cp) { const ready = cp.classList.contains('ready'); if (st.coupon === false && ready) tear(cp); st.coupon = ready; }
    const fill = q(view.list, '.meter i'), pct = fill ? pctOf(fill.getAttribute('style')) : null;
    if (pct !== null) { if (st.meter !== null && pct > st.meter) grow(fill, st.meter, pct); st.meter = pct; }
    const eq = JSON.stringify((v.pv && v.pv.equip) || {}) + '|' + Array.from((v.wearing && v.wearing.entries()) || []).join(',');
    if (st.eq !== null && eq !== st.eq && !spent && !v.sold) wipe();   // a buy that wears itself has its own beat
    st.eq = eq;
  }
  const inner = view.paint;
  view.paint = (v, on) => {
    inner(v, on);
    if (live) { try { after(v); } catch (e) { /* the juice is a bonus: the page works without it */ } }
  };
  return {
    level: L, state: st,
    /** Let go of what outlives the nodes: the shelf observer and the visibility listener. */
    dispose() { try { if (st.io) st.io.disconnect(); if (offVis) offVis(); } catch (e) { /* the juice is a bonus */ } st.io = null; offVis = null; },
  };
}
