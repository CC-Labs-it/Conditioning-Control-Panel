/* ui/adcard/card.js - the Living Ad share card on the page: DOM, living portrait, spiral background, one timeline.
 * Port of the approved mockup (artifact U1zhXxRr7NEoWdgc6qr8PD, handoff-1109/tc-adcard/).
 *
 * The timeline is WAAPI only, so an export can freeze it at any time t and rasterise (ui/adcard/export.js). Transform
 * keyframes ADD onto an element's own resting transform (a tilt); the idle loops (turning spirals, the doodle orbit,
 * the drip, llama hops, the slow Ken Burns push) join the same timeline. Reduced motion: the card is drawn finished and
 * still. Living portrait rule (owner): every spiral is punched out of the portrait and a larger copy turns BEHIND the
 * hole; nothing turning is ever laid on top. */

import { PORTRAITS, SPRITES } from './portraits.js';
import { QR_PATH, QR_SIZE } from './qr.js';
import { llamaLayout } from './model.js';
import { ADCARD_COPY } from '../../data/content/adcard.js';
import { whole } from '../../sim/format.js';
import { canvasDpr, paintEvery } from '../perf.js';

const ASSETS = new URL('../../assets/adcard/', import.meta.url).href;
const CSS_URL = new URL('./card.css', import.meta.url).href;
const ORBIT_SET = ['heart', 'star', 'heart2', 'starline', 'dot', 'dot2'];
const EASE = 'cubic-bezier(.2,.8,.2,1)', BACK = 'cubic-bezier(.34,1.56,.64,1)';
const IN = {
  fade: [{ opacity: 0 }, { opacity: 1 }],
  rise: [{ opacity: 0, transform: 'translateY(8%) scale(.96)' }, { opacity: 1, transform: 'none' }],
  slideL: [{ opacity: 0, transform: 'translateX(-30%)' }, { opacity: 1, transform: 'none' }],
  slap: [{ opacity: 0, transform: 'scale(1.35) rotate(-10deg)' }, { opacity: 1, transform: 'scale(.95) rotate(1deg)', offset: .65 }, { transform: 'none' }],
  stamp: [{ opacity: 0, transform: 'scale(2.4) rotate(14deg)' }, { opacity: 1, transform: 'scale(.9)', offset: .6 }, { transform: 'none' }],
};
export const JITTER = ['translate(.18cqw,-.12cqw) rotate(-.4deg)', 'translate(-.2cqw,.14cqw) rotate(.35deg)', 'translate(.14cqw,.18cqw) rotate(-.2deg)', 'translate(-.14cqw,-.18cqw) rotate(.45deg)'];
const pct = (v, of) => (v / of * 100) + '%';
const esc = (t) => String(t).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

/** The stylesheet, once per document; resolves when it (and the card fonts) are ready. */
export function loadCardCss(doc) {
  let link = doc.querySelector('link[data-adcard]');
  if (!link) {
    link = doc.createElement('link');
    link.rel = 'stylesheet'; link.href = CSS_URL; link.dataset.adcard = '1';
    doc.head.append(link);
  }
  const sheet = link.sheet ? Promise.resolve() : new Promise((r) => { link.addEventListener('load', r, { once: true }); link.addEventListener('error', r, { once: true }); });
  return sheet.then(() => Promise.all(['700 10px AdcRound', '400 10px AdcBlock', '700 10px AdcMono'].map((f) => doc.fonts?.load(f).catch(() => null)))).then(() => {});
}

/* ---------- spiral background ---------- */
function drawSpiral(cv, spec, t, size) {
  const [W0, H0] = size;
  if (!W0) return;
  const dpr = canvasDpr(2), W = Math.round(W0 * dpr), H = Math.round(H0 * dpr);
  if (cv.width !== W || cv.height !== H) { cv.width = W; cv.height = H; }
  const g = cv.getContext('2d');
  g.setTransform(1, 0, 0, 1, 0, 0); g.clearRect(0, 0, W, H);
  g.fillStyle = spec.base; g.fillRect(0, 0, W, H);
  const cx = W * 0.5, cy = H * 0.36, R = Math.hypot(W, H), tw = 0.022 * 400 / W, n = 8, step = (Math.PI * 2) / n, inc = 6 * dpr;
  g.globalAlpha = 0.55;
  for (let i = 0; i < n; i++) {
    g.fillStyle = spec.cols[i % spec.cols.length];
    g.beginPath(); g.moveTo(cx, cy);
    for (let d = 0; d <= R; d += inc) { const a = i * step + d * tw - t; g.lineTo(cx + Math.cos(a) * d, cy + Math.sin(a) * d); }
    for (let d = R; d >= 0; d -= inc) { const a = (i + 1) * step + d * tw - t; g.lineTo(cx + Math.cos(a) * d, cy + Math.sin(a) * d); }
    g.closePath(); g.fill();
  }
  g.globalAlpha = 1;
  const v = g.createRadialGradient(cx, cy, 0, cx, cy, R * 0.7);
  v.addColorStop(0, 'rgba(255,255,255,.55)'); v.addColorStop(0.35, 'rgba(255,255,255,.1)'); v.addColorStop(1, 'rgba(255,255,255,0)');
  g.fillStyle = v; g.fillRect(0, 0, W, H);
}

/* ---------- living portrait ---------- */
function livePortrait(doc, pic, theme) {
  const P = PORTRAITS[theme], [W, H] = P.size;
  pic.style.aspectRatio = W + ' / ' + H;
  const img = doc.createElement('img');
  img.className = 'base'; img.alt = P.alt; img.decoding = 'async';
  img.src = ASSETS + theme + '.webp';
  for (const L of P.layers) {
    /* a frame on the spiral's centre squashes a round copy into the drawing's oval; it never turns */
    const frame = doc.createElement('span'); frame.className = 'layerframe';
    let x0 = 0, host = null;
    if (L.clip) {
      /* a disc that reaches past the drawing gets a still clip box: overflow (the export keeps it) plus the exact outline */
      x0 = Math.min(...L.clip.map((p) => p[0])) + 1.8;
      host = doc.createElement('span'); host.className = 'layerclip';
      Object.assign(host.style, { left: pct(x0, W), width: pct(W - x0, W),
        clipPath: 'polygon(' + L.clip.map(([x, y]) => pct(x - x0, W - x0) + ' ' + pct(y, H)).join(',') + ')' });
    }
    const WW = W - x0;
    Object.assign(frame.style, { left: pct(L.cx - L.R - x0, WW), top: pct(L.cy - L.R, H), width: pct(2 * L.R, WW), height: pct(2 * L.R, H), transform: 'scaleY(' + (L.sy || 1) + ')' });
    const el = doc.createElement('img');
    el.className = 'ac-layer'; el.alt = ''; el.dataset.sec = L.sec; el.decoding = 'async';
    el.src = ASSETS + theme + '-' + L.name + '.webp';
    frame.append(el);
    if (host) { host.append(frame); pic.append(host); } else pic.append(frame);
  }
  pic.append(img);
  const [ox, oy, rx, k, sec] = P.orbit;
  const orbit = doc.createElement('span'); orbit.className = 'orbit'; orbit.dataset.sec = sec;
  Object.assign(orbit.style, { left: pct(ox, W), top: pct(oy, H), width: pct(2 * rx, W), height: pct(2 * rx, H), transform: 'translate(-50%,-50%) scaleY(' + k + ')' });
  const ring = doc.createElement('span'); ring.className = 'ring'; orbit.append(ring);
  ORBIT_SET.forEach((name, i) => {
    const a = i / ORBIT_SET.length * Math.PI * 2;
    const slot = doc.createElement('span'); slot.className = 'slot';
    Object.assign(slot.style, { left: (50 + 50 * Math.cos(a)) + '%', top: (50 + 50 * Math.sin(a)) + '%', width: (SPRITES[name] / (2 * rx) * 100 * 1.15) + '%' });
    const turn = doc.createElement('b'), flat = doc.createElement('i'), im = doc.createElement('img');
    flat.style.transform = 'scaleY(' + (1 / k) + ')';
    im.src = ASSETS + 'sprite-' + name + '.webp'; im.alt = '';
    flat.append(im); turn.append(flat); slot.append(turn); ring.append(slot);
  });
  pic.append(orbit);
  if (P.drip.length) {
    const d = doc.createElement('i'); d.className = 'drip';
    Object.assign(d.style, { left: pct(P.drip[0] - 4, W), top: pct(P.drip[1] - 3, H), width: pct(8, W), height: pct(11, H) });
    pic.append(d);
  }
}

/* ---------- DOM ---------- */
function cardHtml(m) {
  const pair = m.labels.length > 1 ? `<span>${esc(m.labels[0])}</span><i></i><span>${esc(m.labels[1])}</span>` : `<span>${esc(m.labels[0])}</span>`;
  const stats = m.stats.map((s) => `<div class="stat"><div class="ac-row"><span class="lab">${esc(s.label)}</span><span class="num" data-n="${s.n}">${whole(s.n)}</span></div>`
    + `<div class="barwrap"><div class="ac-bar"><i style="--w:${s.w}%"><s class="shine"></s></i></div></div></div>`).join('');
  const L = llamaLayout(m.llamas);
  const llamas = `<div class="stat"><div class="ac-row"><span class="lab">${esc(ADCARD_COPY.stats.llamas)}</span><span class="num" data-n="${m.llamas}">${m.llamas}</span></div>`
    + `<div class="llamas${L.herd ? ' herd' : ''}" style="--ls:${L.font.toFixed(2)}cqw;--lg:${L.gap.toFixed(2)}cqw">${'<span>\u{1F999}</span>'.repeat(m.llamas)}</div></div>`;
  const qr = `<svg viewBox="-1 -1 ${QR_SIZE + 2} ${QR_SIZE + 2}" aria-hidden="true"><path d="${QR_PATH}" fill="${esc(m.colours.ink)}"/></svg>`;
  return `<canvas class="sp" aria-hidden="true"></canvas><div class="inner">`
    + `<div class="ac-top"><img class="logo" data-in="stamp" data-t="60" src="${ASSETS}logo.webp" alt="${esc(ADCARD_COPY.logoAlt)}"></div>`
    + `<div class="hero"><div class="hmeta"><span class="pair" data-in="slideL" data-t="300">${pair}</span><span class="serial" data-in="fade" data-t="400">${esc(m.serial)}</span></div>`
    + `<span class="pic" data-in="slap" data-t="350"></span>`
    + `<div class="terms" data-in="stamp" data-t="1500"><span class="n"><span data-n="${m.terms}">${whole(m.terms)}</span><small>${esc(ADCARD_COPY.terms)}</small></span><span class="vib" data-vib="1650">${esc(ADCARD_COPY.accepted)}</span></div></div>`
    + `<div class="label" data-in="rise" data-t="800"><h3>${m.head}</h3><p class="ac-sub">${m.sub}</p></div>`
    + `<div class="stats" data-in="rise" data-t="2200">${stats}${llamas}</div>`
    + `<div class="ac-foot" data-in="fade" data-t="4200"><div class="links"><span><b>${esc(ADCARD_COPY.play)}</b> ${esc(ADCARD_COPY.playUrl)}</span><span>${esc(ADCARD_COPY.more)} ${esc(ADCARD_COPY.moreUrl)}</span></div><div class="qr">${qr}</div></div>`
    + `</div>`;
}

/** The headline shrinks a little, then the boxes tighten, until the footer sits on the card (the approved card's
 *  footer already runs 2.8% of the width past the edge: that is the slack). */
function fitCard(card) {
  const h3 = card.querySelector('h3'), inner = card.querySelector('.inner'), foot = card.querySelector('.ac-foot');
  card.classList.remove('tight');
  let size = 5.6;
  h3.style.fontSize = size + 'cqw';
  const over = () => foot.offsetTop + foot.offsetHeight > inner.clientHeight + card.offsetWidth * 0.028;
  while (over() && size > 4.5) { size -= 0.2; h3.style.fontSize = size.toFixed(1) + 'cqw'; }
  if (over()) card.classList.add('tight');
}

/** One timeline for the live reveal and the exported poster. build(true) lets an export scrub it. */
export function createAdCard({ doc, box, model, label = ADCARD_COPY.cardLabel, reduced = false, onBeat = null }) {
  const win = doc.defaultView;
  const card = doc.createElement('article');
  card.className = 'adc';
  card.setAttribute('aria-label', label);
  const C = model.colours;
  for (const [k, v] of Object.entries({ A: C.A, B: C.B, M: C.M, hot: C.hot, ink: C.ink, paper: C.paper, hlc: C.hlc })) card.style.setProperty('--' + k, v);
  card.innerHTML = cardHtml(model);
  box.replaceChildren(card);
  livePortrait(doc, card.querySelector('.pic'), model.first);
  const canvas = card.querySelector('canvas.sp'), spec = { cols: C.spiral, base: C.paper };
  let tl = null, raf = 0, dead = false, lastSpiral = 0, phase = 0, heldAt = null;
  const heldAnimations = new Set();
  let spiralSize = [0, 0];
  function drawSpiralAt(t) {
    phase = reduced ? 0 : (t + 20000) / 1000 * 0.25;
    drawSpiral(canvas, spec, phase, spiralSize);
  }
  const refit = () => { fitCard(card); spiralSize = [canvas.offsetWidth, canvas.offsetHeight]; drawSpiral(canvas, spec, phase, spiralSize); };
  refit();

  // A late font or a narrower column can wrap the headline and push the footer out.
  let lastWidth = card.offsetWidth;
  const ro = win.ResizeObserver ? new win.ResizeObserver(() => {
    if (card.offsetWidth === lastWidth) return;
    lastWidth = card.offsetWidth;
    refit();
  }) : null;
  ro?.observe(card);
  if (!ro) win.addEventListener('resize', refit);
  doc.fonts?.addEventListener?.('loadingdone', refit);
  doc.fonts?.ready?.then(() => { if (!dead) refit(); });

  function A(s, el, frames, o) {
    const opts = { fill: 'backwards', easing: EASE, ...o };
    const hasT = frames.some((f) => 'transform' in f);
    const rest = frames.map((f) => { const k = { ...f }; delete k.transform; return k; });
    const hasR = rest.some((f) => Object.keys(f).some((x) => x !== 'offset' && x !== 'easing'));
    if (hasT) s.anims.push(el.animate(frames.map((f) => { const k = { transform: f.transform || 'none' }; if ('offset' in f) k.offset = f.offset; if ('easing' in f) k.easing = f.easing; return k; }), { ...opts, composite: 'add' }));
    if (hasR) s.anims.push(el.animate(rest, opts));
  }

  function idle(s) {
    const loop = (el, frames, ms, o) => A(s, el, frames, { duration: ms, iterations: Infinity, easing: 'linear', fill: 'none', ...o });
    const E = s.end;
    s.tail = 2600;   // an export runs on past the build-in: one bar sweep and the llama hops are in the loop
    card.querySelectorAll('.ac-layer').forEach((el) => { const sec = +el.dataset.sec; loop(el, [{ transform: 'rotate(0deg)' }, { transform: 'rotate(' + (sec > 0 ? 360 : -360) + 'deg)' }], Math.abs(sec) * 1000); });
    card.querySelectorAll('.orbit').forEach((orbit) => {
      const ms = +orbit.dataset.sec * 1000;
      loop(orbit.querySelector('.ring'), [{ transform: 'rotate(0deg)' }, { transform: 'rotate(360deg)' }], ms);
      orbit.querySelectorAll('.slot > b').forEach((b, i) => {
        loop(b, [{ transform: 'rotate(0deg)' }, { transform: 'rotate(-360deg)' }], ms);
        loop(b.querySelector('img'), [{ transform: 'scale(1) rotate(0deg)' }, { transform: 'scale(1.3) rotate(' + (i % 2 ? 18 : -18) + 'deg)', offset: 0.5 }, { transform: 'scale(1) rotate(0deg)' }], 900 + i * 170, { easing: 'ease-in-out', delay: -i * 230 });
      });
    });
    card.querySelectorAll('.drip').forEach((d) => loop(d, [
      { opacity: 0, transform: 'translateY(0) scale(.2)' }, { opacity: 0, transform: 'translateY(0) scale(.2)', offset: 0.3 },
      { opacity: 1, transform: 'translateY(0) scale(1)', offset: 0.55 }, { opacity: 1, transform: 'translateY(40%) scale(1,1.15)', offset: 0.7 },
      { opacity: 0, transform: 'translateY(360%) scale(.8,1.2)', offset: 0.9 }, { opacity: 0, transform: 'translateY(0) scale(.2)' },
    ], 3400, { easing: 'ease-in' }));
    /* llamas hop now and then, each on its own beat */
    card.querySelectorAll('.llamas span').forEach((sp, j) => loop(sp, [
      { transform: 'none' }, { transform: 'translateY(10%) scale(1.14,.84)', offset: 0.07, easing: 'ease-out' },
      { transform: 'translateY(-62%) rotate(' + (j % 2 ? 9 : -9) + 'deg) scale(.92,1.1)', offset: 0.17, easing: 'ease-in' },
      { transform: 'translateY(6%) scale(1.12,.88)', offset: 0.27, easing: 'ease-out' }, { transform: 'none', offset: 0.34 }, { transform: 'none' },
    ], 2300 + ((j * 7) % 5) * 380, { delay: E - 300 + j * 140 }));
    /* Ken Burns: background, portrait and the stats panel push in slowly, out of step */
    loop(canvas, [{ transform: 'scale(1) translate(0,0)' }, { transform: 'scale(1.09) translate(-1.6%,1%)', offset: 0.5 }, { transform: 'scale(1) translate(0,0)' }], 18000, { easing: 'ease-in-out', delay: E - 2500 });
    loop(card.querySelector('.pic'), [{ transform: 'scale(1) translate(0,0)' }, { transform: 'scale(1.045) translate(-1.2%,-.8%)', offset: 0.5 }, { transform: 'scale(1) translate(0,0)' }], 13000, { easing: 'ease-in-out', delay: E - 1200 });
    loop(card.querySelector('.stats'), [{ transform: 'scale(1) translate(0,0)' }, { transform: 'scale(1.03) translate(.7%,-.5%)', offset: 0.5 }, { transform: 'scale(1) translate(0,0)' }], 9000, { easing: 'ease-in-out', delay: E + 200 });
    card.querySelectorAll('.ac-bar i').forEach((fill, i) => {
      const P = 5600, d = E + 300 + i * 320;
      loop(fill, [{ transform: 'scaleX(1)' }, { transform: 'scaleX(1.03)', offset: 0.07, easing: 'ease-out' }, { transform: 'scaleX(.995)', offset: 0.13 }, { transform: 'scaleX(1)', offset: 0.18 }, { transform: 'scaleX(1)' }], P, { delay: d, easing: 'ease-in-out' });
      loop(fill.querySelector('.shine'), [
        { opacity: 1, transform: 'translateX(-110%)' }, { opacity: 1, transform: 'translateX(320%)', offset: 0.17, easing: 'linear' },
        { opacity: 0, transform: 'translateX(320%)', offset: 0.171 }, { opacity: 0, transform: 'translateX(320%)' },
      ], P, { delay: d, easing: 'ease-in-out' });
      const num = fill.closest('.stat')?.querySelector('.num');
      if (num) loop(num, [{ transform: 'scale(1)' }, { transform: 'scale(1.12)', offset: 0.08, easing: 'ease-out' }, { transform: 'scale(1)', offset: 0.2 }, { transform: 'scale(1)' }], P, { delay: d + 120 });
    });
  }

  function build(paused, quiet = false) {
    if (tl) tl.anims.forEach((a) => a.cancel());
    heldAnimations.clear();
    card.querySelectorAll('.bob').forEach((el) => el.classList.remove('bob'));
    card.querySelectorAll('.vib').forEach((el) => { el.classList.remove('buzz'); el.style.transform = ''; });
    const s = { start: heldAt ?? win.performance.now(), anims: [], counts: [], vibs: [], bob: [], beats: [], end: 0, tail: 0, paused, hover: 0 };
    tl = s;
    const beat = (n, t, i = 0) => s.beats.push({ n, t, i });
    for (const [n, sel] of [['logo', '.logo'], ['pic', '.pic'], ['label', '.label'], ['terms', '.terms'], ['foot', '.ac-foot']]) {
      const el = card.querySelector(sel);
      if (el?.dataset.t) beat(n, +el.dataset.t + (n === 'terms' ? 200 : 0));   // the terms stamp hits at 60% of its slam
    }
    card.querySelectorAll('[data-vib]').forEach((el) => beat('accepted', +el.dataset.vib + 250));   // inside the buzz, after the stamp
    A(s, card, [{ opacity: 0, transform: 'translateY(2%) scale(.97)' }, { opacity: 1, transform: 'none' }], { duration: 400 });
    card.querySelectorAll('[data-in]').forEach((el) => {
      const kind = el.dataset.in, t = +el.dataset.t || 0;
      A(s, el, IN[kind] || IN.fade, { duration: kind === 'fade' ? 420 : 560, delay: t, easing: ['slap', 'stamp'].includes(kind) ? 'ease-out' : EASE });
    });
    card.querySelectorAll('.hl').forEach((el) => {
      el.style.backgroundRepeat = 'no-repeat';
      const t = +(el.closest('[data-t]')?.dataset.t || 0) + 600;
      A(s, el, [{ backgroundSize: '0% 100%' }, { backgroundSize: '100% 100%' }], { duration: 520, delay: t });
    });
    card.querySelectorAll('[data-vib]').forEach((el) => s.vibs.push({ el, start: +el.dataset.vib, dur: 1000 }));
    card.querySelectorAll('.terms [data-n]').forEach((el) => s.counts.push({ el, to: +el.dataset.n, start: 1600, dur: 700 }));
    /* the bars: slow fill, overshoot and settle, squash, shine, number pop */
    const FILL = 1800, base = +(card.querySelector('.stats')?.dataset.t || 2000);
    card.querySelectorAll('.stat').forEach((row, i) => {
      const d = base + 300 + i * 650, f0 = d + 150;
      A(s, row, [{ opacity: 0, transform: 'translateX(-6%)' }, { opacity: 1, transform: 'none' }], { duration: 360, delay: d });
      const bar = row.querySelector('.ac-bar'), fill = row.querySelector('.ac-bar i'), num = row.querySelector('.num[data-n]');
      if (fill) {
        const KO = [0, 0.7, 0.85, 0.93, 1], KS = [0, 1.07, 0.97, 1.012, 1], KE = ['cubic-bezier(.45,.05,.3,1)', 'ease-in-out', 'ease-in-out', 'ease-out', 'linear'];
        A(s, fill, KO.map((o, k) => ({ offset: o, transform: `scaleX(${KS[k]})`, easing: KE[k] })), { duration: FILL, delay: f0, easing: 'linear' });
        A(s, bar, [{ transform: 'scaleY(1)' }, { transform: 'scaleY(1.35)', offset: 0.3 }, { transform: 'scaleY(.88)', offset: 0.6 }, { transform: 'scaleY(1.06)', offset: 0.8 }, { transform: 'scaleY(1)' }], { duration: 520, delay: f0 + 1240, fill: 'none', easing: 'ease-out' });
        A(s, fill.querySelector('.shine'), [{ opacity: 1, transform: 'translateX(-110%)' }, { opacity: 1, transform: 'translateX(320%)' }], { duration: 800, delay: f0 + FILL, fill: 'none', easing: 'ease-in-out' });
        s.end = Math.max(s.end, f0 + FILL + 800);
      }
      if (num) {
        s.counts.push({ el: num, to: +num.dataset.n, start: f0, dur: 1500 });
        if (fill) beat('stat', f0 + 1520, i);
        const c = win.getComputedStyle(num).color, accent = C.A;
        A(s, num, [{ transform: 'scale(1)', color: c }, { transform: 'scale(1.4)', color: accent, offset: 0.35 }, { transform: 'scale(1)', color: c }], { duration: 460, delay: f0 + 1520, fill: 'none', easing: 'ease-out' });
      }
      const herd = row.querySelectorAll('.llamas span'), nl = herd.length;
      const st = Math.min(240, 1500 / Math.max(1, nl)), st2 = Math.min(110, 700 / Math.max(1, nl)), w0 = 200 + nl * st + 450;
      herd.forEach((sp, j) => {
        A(s, sp, [{ opacity: 0, transform: 'translateY(80%) scale(.3) rotate(-30deg)' }, { opacity: 1, transform: 'translateY(-30%) scale(1.3) rotate(10deg)', offset: 0.6 }, { transform: 'none' }], { duration: 440, delay: d + 200 + j * st, easing: 'ease-out' });
        A(s, sp, [{ transform: 'none' }, { transform: 'translateY(-35%) rotate(-8deg)', offset: 0.4 }, { transform: 'none' }], { duration: 380, delay: d + Math.max(1200, w0) + j * st2, fill: 'none', easing: 'ease-in-out' });
      });
      if (nl) { s.end = Math.max(s.end, d + Math.max(1700, w0 + nl * st2 + 400)); beat('llamas', d + 200 + Math.floor(nl / 2) * st); }
    });
    s.bob.push(card.querySelector('.pic'));
    s.end = Math.max(s.end, 4800) + 300;
    if (!reduced) idle(s);
    if (paused) s.anims.forEach((a) => { a.pause(); a.currentTime = 0; });
    /* the footer's chime is the finale: after the last bar has popped, never on top of it */
    const foot = s.beats.find((b) => b.n === 'foot'), last = Math.max(0, ...s.beats.filter((b) => b.n === 'stat' || b.n === 'llamas').map((b) => b.t));
    if (foot) foot.t = Math.max(foot.t, last + 650);
    if (paused || quiet || reduced || !onBeat) s.beats.length = 0;
    s.beats.sort((a, b) => a.t - b.t);
    if (!paused) {
      sideAt(s, 0, true);
      if (heldAt !== null) hold(true);
      scheduleFrame();
    }
    return s;
  }

  /** What the timeline shows at t for the parts WAAPI does not own: the counting numbers and the ACCEPTED buzz. */
  function sideAt(s, t, live) {
    const fmt = (n) => whole(n);
    s.counts.forEach((c) => { const k = Math.max(0, Math.min(1, (t - c.start) / c.dur)); const text = fmt(c.to * (1 - Math.pow(1 - k, 3))); if (c.el.textContent !== text) c.el.textContent = text; });
    s.vibs.forEach((v) => {
      const on = !reduced && ((t >= v.start && t < v.start + v.dur) || (live && t < s.hover));
      if (live) { v.el.classList.toggle('buzz', on); v.el.style.transform = ''; } else v.el.style.transform = on ? JITTER[Math.floor((t - v.start) / 70) % 4] : '';
    });
    if (live) s.bob.forEach((el) => el?.classList.toggle('bob', !reduced && t > s.end));
  }

  function scheduleFrame() {
    if (!raf && !dead && !reduced && !tl?.paused && heldAt === null) raf = win.requestAnimationFrame(frame);
  }
  function frame(now) {
    raf = 0;
    if (dead || !tl || tl.paused || reduced || heldAt !== null) return;
    const t = Math.max(0, now - tl.start);
    sideAt(tl, t, true);
    while (tl.beats.length && tl.beats[0].t <= t) {
      const b = tl.beats.shift();
      if (t - b.t < 400) { try { onBeat(b.n, b.i); } catch (e) { console.error(e); } }
    }
    if (now - lastSpiral >= paintEvery(30)) { lastSpiral = now; drawSpiralAt(t); }
    scheduleFrame();
  }

  // The canvas, counters and cues use active time. Freeze the browser's tracks at the same point.
  function hold(on) {
    const now = win.performance.now();
    if (on) {
      heldAt ??= now;
      win.cancelAnimationFrame(raf); raf = 0;
      for (const a of card.getAnimations({ subtree: true })) {
        if (a.playState !== 'running') continue;
        heldAnimations.add(a); a.pause();
      }
    } else if (heldAt !== null) {
      if (tl) tl.start += now - heldAt;
      heldAt = null;
      for (const a of heldAnimations) if (a.playState === 'paused') a.play();
      heldAnimations.clear();
      scheduleFrame();
    }
  }
  function play({ quiet = false } = {}) {
    if (reduced) {
      const s = build(true);
      s.anims.forEach((a) => a.finish());
      sideAt(s, s.end + 1, true);
      drawSpiralAt(0);
      return s;
    }
    return build(false, quiet);
  }

  card.addEventListener('pointerenter', () => { if (tl && !reduced && heldAt === null) tl.hover = win.performance.now() - tl.start + 600; });

  return {
    card,
    play,
    hold,
    setReduced(on) { if (reduced !== on) { reduced = on; play(); } },
    build,
    sideAt,
    drawSpiralAt,
    get timeline() { return tl; },
    destroy() {
      dead = true;
      win.cancelAnimationFrame(raf);
      ro?.disconnect();
      win.removeEventListener('resize', refit);
      doc.fonts?.removeEventListener?.('loadingdone', refit);
      heldAnimations.clear();
      tl?.anims.forEach((a) => a.cancel());
    },
  };
}
