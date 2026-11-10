/* Bimbodoll (bi_end): the button becomes a pink bubble, pink fog creeps in from the edges, and the bubble grows until it
 * has swallowed the whole page, then pops. Runs as the ending's take phase (ENGINE.md), while the page is still mounted
 * and frozen. The prepared bubble interior follows the pop.
 *
 * The real nodes are only moved and shrunk (individual translate/scale, opacity), never removed: unmountAll after the
 * fade puts every object away as in every other ending. A taken node's own translate/scale is kept and composed with the
 * swallow (the floaters are placed with `translate`). Wake Up's group (#game-controls) goes in LAST, right before the pop,
 * never below WAKE_MIN_SCALE and still clickable; it flies back out at the pop. All cosmetic randomness is Math.random;
 * ctx.rng is not touched. */

import { frameRect, LITE } from '../../perf.js';
import { openClause } from '../../clause.js';
import { BY_ID } from '../../../data/items.js';
import { ENDINGS } from '../../../data/content/index.js';
import { setRich } from '../../rich-text.js';

/* ---- the pure part (tests/bimbodoll.test.mjs) ---- */

export const POP_FLOOR_S = 90;     // an untouched player pops about here
export const POP_CAP_S = 100;      // the pop comes by now whatever the radius
export const SWELL_SHARE = 0.025;  // one well-spaced click: 2.5% of the screen diagonal
export const SWELL_GAP_S = 2.6;    // clicks closer than this swell less, so hammering pops in 30-40 s, not 5
export const SWELL_EASE_S = 0.35;
export const CARD_AT = 0.7;        // the last terms card opens at 70% of the way
export const CARD_HOLD_S = 1.5;    // and is swallowed this long after

export function bubblePlan({ w, h, cx, cy, r0 }) {
  const rPop = Math.max(Math.hypot(cx, cy), Math.hypot(w - cx, cy), Math.hypot(cx, h - cy), Math.hypot(w - cx, h - cy));
  return { w, h, cx, cy, r0, rPop, diag: Math.hypot(w, h), floor: Math.max(0, rPop - r0) / POP_FLOOR_S };
}
export const newBubble = (plan) => ({ r: plan.r0, pending: 0, t: 0, lastClick: -Infinity, popped: false });
export function swell(b, plan) {
  const amount = plan.diag * SWELL_SHARE * Math.min(1, (b.t - b.lastClick) / SWELL_GAP_S);
  b.pending += amount;
  b.lastClick = b.t;
  return amount;
}
export function stepBubble(b, plan, dt) {
  b.t += dt;
  const got = b.pending * (1 - Math.exp(-dt * 3 / SWELL_EASE_S));   // ease-out: most of a swell lands in the first third
  b.pending -= got;
  b.r += plan.floor * dt + got;
  if (b.r >= plan.rPop || b.t >= POP_CAP_S) b.popped = true;
  return b;
}
/** Seconds to the pop for a player clicking `perS` times a second (0 = untouched), at `dt` steps. */
export function popTime(plan, perS = 0, dt = 1 / 60) {
  const b = newBubble(plan);
  let next = perS > 0 ? 0 : Infinity;
  while (!b.popped) {
    if (b.t >= next) { swell(b, plan); next += 1 / perS; }
    stepBubble(b, plan, dt);
  }
  return b.t;
}

/** Distance from (cx, cy) to the nearest point of a rect. */
export function nearestDist(r, cx, cy) {
  const dx = Math.max(r.left - cx, 0, cx - r.right), dy = Math.max(r.top - cy, 0, cy - r.bottom);
  return Math.hypot(dx, dy);
}

/** Which candidates the bubble takes now. cands: [{ rect, kind?, wake?, force?, taken? }]. Nearest first; Wake Up never;
 *  `force` (the last terms card) goes whatever the distance; the counter waits and goes second to last. */
export function nextTakes(cands, cx, cy, r) {
  const live = cands.filter((c) => !c.taken && !c.wake);
  const d = (c) => nearestDist(c.rect, cx, cy);
  const forced = live.filter((c) => c.force);
  const plain = live.filter((c) => !c.force);
  const counter = plain.find((c) => c.kind === 'counter');
  const others = plain.filter((c) => c !== counter);
  const now = others.filter((c) => d(c) <= r).sort((a, b) => d(a) - d(b));
  if (counter) {
    const left = others.length - now.length;
    if (others.length <= 1) return [...forced, counter];                  // one other left (or none): the counter goes now
    if (left === 0) return [...forced, ...now.slice(0, -1), counter];    // all would go at once: keep the farthest for last
  }
  return [...forced, ...now];
}

/** At the pop: what is still out goes in all at once, then Wake Up's group, always last. */
export function popOrder(cands) {
  const live = cands.filter((c) => !c.taken);
  return [...live.filter((c) => !c.wake), ...live.filter((c) => c.wake)];
}

export const WAKE_MIN_SCALE = 0.55;   // Wake Up's miniature inside the bubble: never smaller than this
export const WAKE_SETTLE_S = 1.7;     // the pop waits this long after Wake Up goes in
export const SWALLOWED_OPACITY = 0.85;
export const INSIDE_SIDE = 0.36;      // a swallowed object's longer side, as a share of the bubble radius (18% of the diameter)
/** The scale a swallowed object settles at inside a bubble of radius r: bubble-relative, between the floor and 0.9. */
export function insideScale(c, r, minScale = swallowStyle(c).minScale) {
  const side = Math.max(1, c.rect?.width || 0, c.rect?.height || 0);
  return Math.min(0.9, Math.max(minScale, (INSIDE_SIDE * r) / side));
}
/** How a swallowed candidate is styled: Wake Up keeps its pointer events, its full opacity and a scale floor. */
export function swallowStyle(c) {
  return c.wake ? { minScale: WAKE_MIN_SCALE, pointerEvents: null, opacity: 1 } : { minScale: 0.28, pointerEvents: 'none', opacity: SWALLOWED_OPACITY };
}

/** A computed `translate` ('none', '12px', '12px -4px', '50% 10px') as px, against the element's own box. */
export function parseTranslate(s, w = 0, h = 0) {
  if (!s || s === 'none') return [0, 0];
  const p = String(s).trim().split(/\s+/);
  const one = (v, size) => { const n = parseFloat(v); if (!Number.isFinite(n)) return 0; return v.endsWith('%') ? n / 100 * size : n; };
  return [one(p[0], w), p[1] ? one(p[1], h) : 0];
}
/** A computed `scale` ('none', '1.2', '1 0.8') as a pair. */
export function parseScale(s) {
  if (!s || s === 'none') return [1, 1];
  const p = String(s).trim().split(/\s+/).map((v) => (v.endsWith('%') ? parseFloat(v) / 100 : parseFloat(v)));
  const x = Number.isFinite(p[0]) ? p[0] : 1;
  return [x, Number.isFinite(p[1]) ? p[1] : x];
}

/* ---- the show ---- */

const POPS = ['assets/audio/pop/Pop.mp3', 'assets/audio/sfx/golden_pop.mp3'];
const GIGGLES = ['assets/audio/themes/bimbo/giggle1.mp3', 'assets/audio/themes/bimbo/giggle2.mp3', 'assets/audio/themes/bimbo/giggle3.mp3'];
const GIGGLE_GAP_S = 6;
const TAU = Math.PI * 2;
let aborted = false;
/** True when the last take ended on panic: no pop, no card. */
export const bimbodollAborted = () => aborted;

/** Every element on the page that can be swallowed, as candidates. Skips Wake Up's whole control group, the cards,
 *  the ending layer and the button itself, and full-page backdrops (they go with the pop). */
function collect(doc, known, w, h) {
  const out = [];
  const add = (el, kind = 'obj') => {
    if (!el || known.has(el) || !el.isConnected || el.hidden) return;
    if (el.closest('#game-controls, #pause, #l-ending, #btn-zone, .tc') || el.querySelector?.('#wake, #btn')) return;
    const rect = frameRect(el);
    if (rect.width < 2 || rect.height < 2) return;
    if (rect.width * rect.height > w * h * 0.6) return;
    if (getComputedStyle(el).visibility === 'hidden') return;
    known.add(el);
    out.push({ el, kind, rect, taken: false });
  };
  add(doc.getElementById('counter'), 'counter');
  for (const id of ['l-ticker', 'phrase', 'shop', 'fz-tools', 'dock-strip']) add(doc.getElementById(id));
  for (const id of ['l-meters', 'l-banner', 'l-screens', 'l-floaters', 'l-over', 'l-fx', 'l-toast', 'l-back', 'l-wash']) {
    const host = doc.getElementById(id);
    if (host) for (const c of host.children) add(c);
  }
  for (const rail of doc.querySelectorAll('#l-side .rail')) for (const c of rail.children) add(c);
  for (const c of doc.querySelectorAll('#l-side > :not(.rail)')) add(c);
  /* Any other chrome straight under body (the Control Panel door, a dock volume card, host chrome): fixed or absolute,
   * visible, not the world, the cards, the pause card or Wake Up's group. */
  for (const c of doc.body?.children || []) {
    if (/^(SCRIPT|STYLE|LINK|TEMPLATE|NOSCRIPT)$/.test(c.tagName) || /^(world|cards|pause|game-controls)$/.test(c.id) || c.classList.contains('backdrop')) continue;
    const pos = getComputedStyle(c).position;
    if (pos === 'fixed' || pos === 'absolute') add(c);
  }
  return out;
}

/** Wake Up's whole group as its one candidate, or null when it is not on the page. */
function wakeCand(doc) {
  const el = doc.getElementById('game-controls');
  if (!el || !el.isConnected) return null;
  return { el, kind: 'wake', wake: true, rect: frameRect(el), taken: false };
}

function fogSprite(doc, rgb = '255,140,205', rgb2 = '255,120,200') {
  const spr = doc.createElement('canvas');
  spr.width = spr.height = 128;
  const g = spr.getContext('2d'), q = g.createRadialGradient(64, 64, 0, 64, 64, 64);
  q.addColorStop(0, `rgba(${rgb},.55)`); q.addColorStop(0.5, `rgba(${rgb2},.22)`); q.addColorStop(1, `rgba(${rgb2},0)`);
  g.fillStyle = q; g.fillRect(0, 0, 128, 128);
  return spr;
}

/** The bubble's body: a canvas in the world under every swallowed layer (z 4: over the backdrops, under the screens,
 *  the stage and everything above), clipped to the circle, with up to three layers of pink fog blobs on slow circular
 *  paths, each layer swirling round the centre at its own speed and direction. Drawn at a fraction of the screen
 *  resolution: it is all soft. lite = one layer, slower; still = a static mist. */
function bubbleMist(doc, world, { lite, still }) {
  const cv = doc.createElement('canvas');
  cv.style.cssText = 'position:absolute;left:0;top:0;width:100%;height:100%;z-index:4;pointer-events:none;';
  world.appendChild(cv);
  const g = cv.getContext('2d');
  const k = lite ? 0.33 : 0.5;
  const sprites = [fogSprite(doc, '255,246,251', '255,214,236'), fogSprite(doc, '255,120,190', '240,96,170'), fogSprite(doc, '255,176,220', '255,150,210')];
  const layers = Array.from({ length: lite ? 1 : 3 }, (_, li) => ({
    spr: sprites[lite ? 0 : li], spin: (0.05 + 0.04 * li) * (li % 2 ? -1 : 1) * (lite ? 0.5 : 1),
    blobs: Array.from({ length: 8 + Math.floor(Math.random() * 7) }, () => ({
      orb: 0.1 + Math.random() * 0.6, a: Math.random() * TAU, w: (0.08 + Math.random() * 0.22) * (Math.random() < 0.5 ? -1 : 1) * (lite ? 0.5 : 1),
      wob: 0.05 + Math.random() * 0.12, ph: Math.random() * TAU, sz: 0.45 + Math.random() * 0.6,
    })),
  }));
  let W = 0, H = 0;
  return {
    el: cv,
    draw(cx, cy, R, t, w, h, fade = 1) {
      if (w !== W || h !== H) { W = w; H = h; cv.width = Math.max(1, Math.round(w * k)); cv.height = Math.max(1, Math.round(h * k)); }
      const tt = still ? 0 : t;
      g.setTransform(k, 0, 0, k, 0, 0);
      g.clearRect(0, 0, W, H);
      if (R <= 1 || fade <= 0) return;
      g.save();
      g.globalAlpha = fade;
      g.beginPath(); g.arc(cx, cy, R, 0, TAU); g.clip();
      const body = g.createRadialGradient(cx - R * 0.18, cy - R * 0.22, 0, cx, cy, R);
      body.addColorStop(0, 'rgba(255,222,240,.70)'); body.addColorStop(0.55, 'rgba(255,168,213,.76)');
      body.addColorStop(0.88, 'rgba(244,122,186,.84)'); body.addColorStop(1, 'rgba(226,90,160,.9)');
      g.fillStyle = body; g.fillRect(cx - R, cy - R, 2 * R, 2 * R);
      for (const L of layers) {
        const rot = tt * L.spin;
        g.globalAlpha = fade * (lite ? 0.75 : 0.6);
        for (const q of L.blobs) {
          const a = q.a + tt * q.w + rot, o = R * (q.orb + q.wob * Math.sin(tt * 0.5 + q.ph));
          const s = R * q.sz * (1 + 0.12 * Math.sin(tt * 0.7 + q.ph));
          g.drawImage(L.spr, cx + Math.cos(a) * o - s / 2, cy + Math.sin(a) * o - s / 2, s, s);
        }
      }
      g.restore();
    },
  };
}

/** A reverb tail made of noise, no file: two channels of exponentially decaying noise. */
function impulse(ac, seconds = 2.8, decay = 3) {
  const n = Math.floor(ac.sampleRate * seconds), buf = ac.createBuffer(2, n, ac.sampleRate);
  for (let ch = 0; ch < 2; ch++) {
    const d = buf.getChannelData(ch);
    for (let i = 0; i < n; i++) d[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / n, decay);
  }
  return buf;
}

/** A balloon being blown up, more and more. Synth only. set(p) follows progress (0..1), bend() is one breath into it
 *  (a click), stop(ms) lets go. Layers, all into one master at most 1 (= the sfx bus):
 *  - latex: a sawtooth with a slow random pitch creep through a resonant bandpass (Q 11) rising 180 -> 900 Hz with p;
 *    past p 0.85 a strained 6-9 Hz tremolo on it;
 *  - skin hiss: looped noise through a lowpass sweeping 600 Hz -> 3 kHz;
 *  - stick-slip squeaks: 60-140 ms falling sawtooth chirps through the same kind of bandpass, a soft 10-20 ms attack,
 *    every 0.6-2 s (denser with p, every 0.25 s past p 0.8). */
/** Off for now (owner 2026-11-09: "skip this sfx for now, keep the pop"): the synth never passed the ear test and the recordings
 *  wait on the owner. Flip to run the synth again; the pop, giggles and muffling stay. */
export const BALLOON_SFX = false;

function balloonTone(ctx) {
  const io = BALLOON_SFX ? ctx.audio.input({ bus: 'sfx', gain: 1 }) : null;
  if (!io) return { set() {}, bend() {}, stop() {} };
  const ac = io.ac, t0 = ac.currentTime;
  const master = ac.createGain(), LEVEL = 0.82; master.gain.value = LEVEL; master.connect(io.node);   // every layer at once stays under 1 = the bus
  /* noise, shared */
  const nb = ac.createBuffer(1, ac.sampleRate * 2, ac.sampleRate), nd = nb.getChannelData(0);
  for (let i = 0; i < nd.length; i++) nd[i] = Math.random() * 2 - 1;
  /* latex */
  const saw = ac.createOscillator(), bp = ac.createBiquadFilter(), trem = ac.createGain(), lat = ac.createGain();
  const tlfo = ac.createOscillator(), tdepth = ac.createGain();
  saw.type = 'sawtooth'; saw.frequency.value = 95;
  bp.type = 'bandpass'; bp.frequency.value = 180; bp.Q.value = 11;
  trem.gain.value = 1; tlfo.frequency.value = 6 + Math.random() * 3; tdepth.gain.value = 0;
  tlfo.connect(tdepth); tdepth.connect(trem.gain);
  lat.gain.value = 0;
  saw.connect(bp); bp.connect(trem); trem.connect(lat); lat.connect(master);
  /* skin hiss */
  const hiss = ac.createBufferSource(), hlp = ac.createBiquadFilter(), hg = ac.createGain();
  hiss.buffer = nb; hiss.loop = true; hlp.type = 'lowpass'; hlp.frequency.value = 600; hg.gain.value = 0;
  hiss.connect(hlp); hlp.connect(hg); hg.connect(master);
  for (const o of [saw, tlfo, hiss]) o.start(t0);
  let dead = false, p = 0, nextSqueak = ac.currentTime + 0.8, nextCreep = 0, creep = 0;
  const squeak = (loud = 1) => {
    const now = ac.currentTime, dur = 0.06 + Math.random() * 0.08, att = 0.01 + Math.random() * 0.01;
    const f = (1400 + Math.random() * 800) * (1 + p);
    const o = ac.createOscillator(), f2 = ac.createBiquadFilter(), sg = ac.createGain();
    o.type = 'sawtooth';
    o.frequency.setValueAtTime(f, now); o.frequency.exponentialRampToValueAtTime(f / 2, now + dur);
    f2.type = 'bandpass'; f2.Q.value = 12;
    f2.frequency.setValueAtTime(f * 0.9, now); f2.frequency.exponentialRampToValueAtTime(f * 0.45, now + dur);
    const peak = Math.min(0.42, (0.06 + 0.3 * p) * loud);
    sg.gain.setValueAtTime(0.0001, now); sg.gain.linearRampToValueAtTime(peak, now + att);
    sg.gain.setTargetAtTime(0.0001, now + att, (dur - att) / 3);
    o.connect(f2); f2.connect(sg); sg.connect(master);
    o.start(now); o.stop(now + dur + 0.08);
  };
  return {
    set(np) {
      if (dead) return;
      p = Math.max(0, Math.min(1, np));
      const now = ac.currentTime;
      if (now >= nextCreep) { nextCreep = now + 0.4 + Math.random() * 0.8; creep = (Math.random() - 0.5) * 0.08; }
      saw.frequency.setTargetAtTime((95 + 120 * p) * (1 + creep), now, 0.5);
      bp.frequency.setTargetAtTime(180 + 720 * p, now, 0.15);
      lat.gain.setTargetAtTime(0.012 + 0.4 * p * p, now, 0.25);   // barely there at p 0, the loudest thing on the page at 1
      hlp.frequency.setTargetAtTime(600 + 2400 * p, now, 0.2);
      hg.gain.setTargetAtTime(0.006 + 0.1 * p, now, 0.3);
      tdepth.gain.setTargetAtTime(p > 0.85 ? 0.35 * (p - 0.85) / 0.15 : 0, now, 0.2);
      if (now >= nextSqueak) {
        squeak();
        nextSqueak = now + (p > 0.8 ? 0.25 : (2 - 1.4 * (p / 0.8)) * (0.75 + Math.random() * 0.5));
      }
    },
    bend() {   // one breath in: a puff of air and a louder squeak
      if (dead) return;
      const now = ac.currentTime;
      const s = ac.createBufferSource(), lp = ac.createBiquadFilter(), g = ac.createGain();
      s.buffer = nb;
      lp.type = 'lowpass'; lp.frequency.setValueAtTime(300, now); lp.frequency.exponentialRampToValueAtTime(3000, now + 0.12);
      g.gain.setValueAtTime(0.0001, now); g.gain.linearRampToValueAtTime(0.16 + 0.12 * p, now + 0.02);
      g.gain.setTargetAtTime(0.0001, now + 0.06, 0.05);
      s.connect(lp); lp.connect(g); g.connect(master);
      s.start(now, Math.random()); s.stop(now + 0.18);
      setTimeout(() => { if (!dead) squeak(1.6); }, 70);
      nextSqueak = Math.max(nextSqueak, now + 0.3);
    },
    stop(ms = 60) {
      if (dead) return;
      dead = true;
      const now = ac.currentTime;
      master.gain.cancelScheduledValues(now); master.gain.setTargetAtTime(0, now, Math.max(0.005, ms / 3000));
      setTimeout(() => { for (const o of [saw, tlfo, hiss]) { try { o.stop(); } catch { /* gone */ } } io.stop(); }, ms + 200);
    },
  };
}

/** The pop: Pop + golden_pop, dry and through a long synthesised reverb. Falls back to a plain play. */
function popSound(ctx, decoded) {
  const io = ctx.audio.input({ bus: 'sfx', gain: 1.6 });
  if (!io || !decoded.length) { for (const p of POPS) ctx.audio.play(p, { bus: 'sfx', gain: 1 }); return; }
  const ac = io.ac, conv = ac.createConvolver(), wet = ac.createGain();
  conv.buffer = impulse(ac); wet.gain.value = 0.7;
  conv.connect(wet); wet.connect(io.node);
  for (const [i, buf] of decoded.entries()) {
    const src = ac.createBufferSource(), dry = ac.createGain();
    src.buffer = buf; dry.gain.value = i === 0 ? 1 : 0.6;
    src.connect(dry); dry.connect(io.node); src.connect(conv);
    src.start(ac.currentTime + i * 0.015);
  }
  setTimeout(() => io.stop(), 4500);
}

export function bimbodollTake(ctx, readyForPop = () => true) {
  aborted = false;
  const doc = ctx.world?.ownerDocument || document;
  const gentle = () => ctx.gentle || ctx.reduced;
  const btn = doc.getElementById('btn');
  const box = ctx.bounds();
  let W = box.w || innerWidth, H = box.h || innerHeight;
  const br = btn ? frameRect(btn) : { left: W / 2 - 40, right: W / 2 + 40, top: H / 2 - 20, bottom: H / 2 + 20, width: 80, height: 40 };
  const cx = (br.left + br.right) / 2, cy = (br.top + br.bottom) / 2;
  const plan = bubblePlan({ w: W, h: H, cx, cy, r0: Math.max(br.width, br.height) * 0.55 + 6 });
  const b = newBubble(plan);

  return new Promise((resolve) => {
    let done = false;
    const finish = () => { if (done) return; done = true; resolve(); };
    ctx.cleanup(finish);

    /* Layers on the ending layer: fog, a click catcher, the bubble, and the black at the end. */
    const fog = ctx.el('canvas', 'position:absolute;left:0;top:0;width:100%;height:100%;pointer-events:none;', null, 'ending');
    const catcher = ctx.el('div', 'position:absolute;inset:0;pointer-events:auto;cursor:pointer;', null, 'ending');
    /* The rim on top of everything it swallowed: a highlight and a thin bright edge; the body is the mist underneath. */
    const bubble = ctx.el('div', 'position:absolute;left:0;top:0;border-radius:50%;pointer-events:none;will-change:transform;'
      + 'background:radial-gradient(circle at 34% 28%,rgba(255,255,255,.7) 0 3%,rgba(255,255,255,0) 11%),'
      + 'radial-gradient(circle,rgba(255,168,213,0) 0 80%,rgba(255,140,205,.16) 92%,rgba(255,236,248,.6) 98%,rgba(255,255,255,0) 100%);', null, 'ending');
    const mist = ctx.world?.appendChild ? bubbleMist(doc, ctx.world, { lite: LITE || !!ctx.gentle, still: !!ctx.reduced }) : null;
    if (mist) ctx.cleanup(() => mist.el.remove());
    let mistFade = 1;
    /* The hued bubble art as the rim only: screen-blended, so its dark body drops out and the iridescent edge stays. */
    ctx.el('div', 'position:absolute;inset:0;border-radius:50%;background:url("assets/img/bubble.png") center/100% 100% no-repeat;mix-blend-mode:screen;opacity:.85;', null, bubble);
    const black = ctx.el('div', 'position:absolute;inset:0;background:#000;opacity:0;pointer-events:none;', null, 'ending');
    const dpr = Math.min(1.5, globalThis.devicePixelRatio || 1);
    const sizeFog = () => { fog.width = Math.round(W * dpr); fog.height = Math.round(H * dpr); };
    sizeFog();
    const g = fog.getContext('2d');
    const spr = fogSprite(doc);

    /* The button's face fades into the bubble. */
    if (btn) ctx.anim(btn, [{ opacity: 1 }, { opacity: 0 }], { duration: gentle() ? 1200 : 800, fill: 'forwards' });

    /* Fog: 46 soft blobs around the four edges (super-effects mockup mkFog), here pushed back by a click and coming back thicker. */
    const blobs = Array.from({ length: 46 }, (_, i) => ({ side: i % 4, u: Math.random(), d: 0.15 + Math.random() * 0.85, sz: 0.22 + Math.random() * 0.3, ph: Math.random() * TAU, ox: 0, oy: 0, vx: 0, vy: 0, px: 0, py: 0 }));
    let C = 0.04, Ct = 0.04, cap = gentle() ? 0.35 : 0.55;
    const motes = [];

    /* Audio. */
    const tone = balloonTone(ctx);
    const decoded = [];
    const io0 = ctx.audio.input({ bus: 'sfx', gain: 0 });
    if (io0) {
      for (const p of POPS) fetch(p).then((r) => (r.ok ? r.arrayBuffer() : null)).then((a) => (a ? io0.ac.decodeAudioData(a) : null)).then((buf) => { if (buf) decoded[POPS.indexOf(p)] = buf; }).catch(() => {});
      io0.stop();
    }
    const muffled = new Set();
    let underwater = false;
    ctx.cleanup(() => { tone.stop(0); for (const o of muffled) ctx.audio.muffle(o, false); if (underwater) ctx.audio.muffleOthers?.(false); });
    let lastGiggle = -Infinity;

    /* Candidates. */
    const known = new Set();
    const cands = [];
    const recollect = () => { for (const c of collect(doc, known, W, H)) cands.push(c); };
    recollect();
    const wake = wakeCand(doc);
    if (wake) cands.push(wake);
    let sinceCollect = 0, popAt = Infinity;

    function take(c, delay = 0) {
      c.taken = true;
      c.t0 = b.t + delay;
      c.rect = frameRect(c.el);
      const mx = (c.rect.left + c.rect.right) / 2, my = (c.rect.top + c.rect.bottom) / 2;
      c.mx = mx; c.my = my;
      c.ang = Math.random() * TAU; c.frac = 0.12 + Math.random() * 0.4; c.ph = Math.random() * TAU;
      c.style = swallowStyle(c);
      /* Compose, never replace: the floaters and copies are placed with their own translate/scale. */
      const cs = getComputedStyle(c.el);
      c.bt = parseTranslate(cs.translate, c.el.offsetWidth || c.rect.width, c.el.offsetHeight || c.rect.height);
      c.bs = parseScale(cs.scale);
      c.base = { translate: c.el.style.translate, scale: c.el.style.scale, opacity: c.el.style.opacity, transition: c.el.style.transition, pe: c.el.style.pointerEvents };
      c.el.style.transition = 'none';
      if (c.style.pointerEvents) c.el.style.pointerEvents = c.style.pointerEvents;
      const el = c.el;
      ctx.cleanup(() => { el.style.translate = c.base.translate; el.style.scale = c.base.scale; el.style.opacity = c.base.opacity; el.style.transition = c.base.transition; el.style.pointerEvents = c.base.pe; });
      for (const o of new Set([el.dataset.fzOwner, ...[...el.querySelectorAll('[data-fz-owner]')].slice(0, 8).map((n) => n.dataset.fzOwner)])) {
        if (o && !muffled.has(o)) { muffled.add(o); ctx.audio.muffle(o, true); }
      }
      if (c.kind === 'counter' && !underwater) { underwater = true; ctx.audio.muffleOthers?.(true); }
      if (b.t - lastGiggle > GIGGLE_GAP_S) {
        lastGiggle = b.t;
        ctx.audio.play(GIGGLES[Math.floor(Math.random() * GIGGLES.length)], { bus: 'voice', gain: 0.35 });
      }
    }

    /** Wake Up's group flies back out to where it was (the pop): its own styles back, one short tween. */
    function release(c) {
      c.released = true;
      const el = c.el;
      el.style.transition = gentle() ? 'opacity .5s' : 'translate .45s cubic-bezier(.3,1.5,.5,1), scale .45s cubic-bezier(.3,1.5,.5,1), opacity .3s';
      el.style.translate = c.base.translate; el.style.scale = c.base.scale; el.style.opacity = c.base.opacity; el.style.pointerEvents = c.base.pe;
      ctx.after(600, () => { el.style.transition = c.base.transition; });
    }

    function place(c) {
      if (c.released) return;
      const age = b.t - c.t0;
      if (age < 0) return;
      /* Where its owner has it now (some particles keep moving on their own transform): the measured centre minus what
       * we added last frame. c.now is read for every held node before any write (frame loop), so one layout a frame. */
      const r = c.now;
      if (r && (r.width || r.height)) { c.mx = (r.left + r.right) / 2 - (c.ax || 0); c.my = (r.top + r.bottom) / 2 - (c.ay || 0); }
      const st = c.style, settle = c.wake ? st.minScale : insideScale(c, b.r, st.minScale), keep = 1 - settle;
      // bigger objects sit nearer the middle so they stay under the skin
      if (!c.wake) { const half = Math.max(c.rect.width, c.rect.height) * settle / 2; c.frac = Math.min(c.frac0 ?? (c.frac0 = c.frac), Math.max(0.05, (0.9 * b.r - half) / b.r)); }
      let gx = cx + Math.cos(c.ang) * c.frac * b.r, gy = cy + Math.sin(c.ang) * c.frac * b.r;
      if (c.wake) {   // Wake Up goes under the skin but only partway to the middle (away from the clicks), whole, on screen
        gx = c.mx + (cx - c.mx) * 0.45; gy = c.my + (cy - c.my) * 0.45;
        const hw = c.rect.width * st.minScale / 2 + 8, hh = c.rect.height * st.minScale / 2 + 8;
        gx = Math.min(W - hw, Math.max(hw, gx)); gy = Math.min(H - hh, Math.max(hh, gy));
      }
      const tx = gx - c.mx, ty = gy - c.my;
      const ease = (u) => 1 - Math.pow(1 - u, 3);
      let x, y, sx, sy, op = 1;
      if (gentle()) {
        const u = ease(Math.min(1, age / 1.4));
        x = tx * u; y = ty * u; sx = sy = 1 - keep * u; op = 1 - (1 - st.opacity) * u;
      } else if (age < 0.4) {
        const u = age / 0.4, s = Math.sin(u * Math.PI / 2);
        const horiz = Math.abs(tx) > Math.abs(ty);
        x = tx * 0.08 * s; y = ty * 0.08 * s;
        sx = horiz ? 1 + 0.4 * s : 1 - 0.25 * s; sy = horiz ? 1 - 0.25 * s : 1 + 0.4 * s;
      } else {
        const u = ease(Math.min(1, (age - 0.4) / 1.2)), horiz = Math.abs(tx) > Math.abs(ty);
        const stretch = 1 - u;
        x = tx * (0.08 + 0.92 * u); y = ty * (0.08 + 0.92 * u);
        const sc = 1 - keep * u;
        sx = sc * (horiz ? 1 + 0.4 * stretch : 1 - 0.25 * stretch); sy = sc * (horiz ? 1 - 0.25 * stretch : 1 + 0.4 * stretch);
        op = 1 - (1 - st.opacity) * u;
        if (u >= 1) { const bob = Math.sin(b.t * 1.3 + c.ph); y += bob * 4; x += Math.cos(b.t * 0.9 + c.ph) * 3; sx *= 1 + 0.04 * bob; sy *= 1 - 0.04 * bob; }
      }
      if (c.wake) { sx = Math.max(st.minScale, sx); sy = Math.max(st.minScale, sy); }
      c.ax = x; c.ay = y;
      c.el.style.translate = `${(c.bt[0] + x).toFixed(1)}px ${(c.bt[1] + y).toFixed(1)}px`;
      c.el.style.scale = `${Math.max(0.01, c.bs[0] * sx).toFixed(3)} ${Math.max(0.01, c.bs[1] * sy).toFixed(3)}`;
      c.el.style.opacity = String(op);
    }

    /* The last terms card: opens at 70%, swallowed before it can be ticked. */
    let card = null;
    function openCard() {
      try {
        const it = BY_ID.bi_end;
        const h = openClause({ doc, item: it, purchase: 1, gentle, onAccept() {}, onDecline() {} });
        const wins = doc.querySelectorAll('.tc.clause .win');
        const win = wins[wins.length - 1];
        const span = win?.querySelector('.checks label span');
        if (span) setRich(span, ENDINGS.bi_end.card || 'I accept that there is nothing left to accept.');
        const veil = win?.closest('.tc');
        card = { h, win, veil, at: b.t };
        ctx.cleanup(() => h.close());
      } catch (e) { console.error(e); card = { h: null, at: b.t, done: true }; }
    }

    let popped = false;
    /* A probe for dev/bimbodoll-drive.mjs (local hosts only): where the bubble is and what it holds. */
    const view = doc.defaultView;
    if (view && /^(127\.0\.0\.1|localhost)$/.test(view.location?.hostname || '')) {
      view.__bimbodollProbe = () => ({ cx, cy, r: b.r, popped, t: b.t, held: cands.filter((c) => c.taken && !c.released).map((c) => ({ el: c.el, wake: !!c.wake, age: b.t - c.t0, id: c.el.id || `${c.el.parentElement?.id || c.el.parentElement?.className || "?"}>${c.el.tagName}.${String(c.el.className).slice(0, 30)}` })) });
      ctx.cleanup(() => { delete view.__bimbodollProbe; });
    }

    /** The bubble is full: what is still out goes in at once, Wake Up's group last, and the pop waits for it to settle. */
    function swallowLast() {
      popOrder(cands).forEach((c, i) => take(c, c.wake ? 0.25 : i * 0.02));
      popAt = b.t + (wake ? WAKE_SETTLE_S : 0.3);
    }

    function pop() {
      popped = true;
      for (const c of popOrder(cands)) take(c);
      tone.stop(50);
      ctx.after(60, () => popSound(ctx, decoded.filter(Boolean)));   // the stretch lets go 60 ms before the pop
      ctx.audio.play(GIGGLES[Math.floor(Math.random() * GIGGLES.length)], { bus: 'voice', gain: 0.4 });
      const soft = gentle();
      for (const c of cands) if (c.taken && !c.wake) c.el.style.opacity = '0';
      if (wake) release(wake);
      mistFade = 0.999;
      if (soft) ctx.anim(bubble, [{ opacity: 1 }, { opacity: 0 }], { duration: 900, fill: 'forwards' });
      else ctx.anim(bubble, [{ opacity: 1, scale: '1' }, { opacity: 0, scale: '1.18' }], { duration: 260, easing: 'ease-out', fill: 'forwards' });
      for (const q of blobs) { const dx = q.px - cx, dy = q.py - cy, d = Math.hypot(dx, dy) || 1; q.vx += dx / d * 2600; q.vy += dy / d * 2600; }
      Ct = 0;
      ctx.anim(fog, [{ opacity: 1 }, { opacity: 0 }], { duration: soft ? 1400 : 900, fill: 'forwards' });
      card?.h?.close();
      ctx.after(1100, () => {
        ctx.anim(black, [{ opacity: 0 }, { opacity: 1 }], { duration: soft ? 900 : 600, fill: 'forwards' });
        ctx.after(soft ? 950 : 650, finish);
      });
    }

    catcher.addEventListener('pointerdown', (e) => {
      if (!e.isTrusted || popped) return;
      swell(b, plan);
      tone.bend();
      Ct = Math.max(0.02, Ct - 0.25);
      cap = Math.min(0.9, cap + 0.02);
      const m = Math.min(W, H);
      for (const q of blobs) { const dx = q.px - cx, dy = q.py - cy, d = Math.hypot(dx, dy) || 1, f = 420 / (1 + d / (m * 0.3)); q.vx += dx / d * f; q.vy += dy / d * f; }
    });

    ctx.on('suspend', (s) => {
      if (s?.on && s.reason === 'panic') { aborted = true; tone.stop(0); finish(); }
    });

    ctx.frame((dt) => {
      if (done) return;
      const nb = ctx.bounds();
      if (nb.w && nb.h && (nb.w !== W || nb.h !== H)) {
        W = nb.w; H = nb.h; sizeFog();
        const np = bubblePlan({ w: W, h: H, cx, cy, r0: plan.r0 });
        plan.rPop = np.rPop; plan.diag = np.diag;
      }
      if (!popped) {
        stepBubble(b, plan, dt);
        const p = Math.min(1, (b.r - plan.r0) / Math.max(1, plan.rPop - plan.r0));
        tone.set(p);
        sinceCollect += dt;
        if (sinceCollect > 0.5) { sinceCollect = 0; recollect(); }
        if (!card && p >= CARD_AT) openCard();
        if (card && !card.done && b.t - card.at >= CARD_HOLD_S && card.win) {
          card.done = true;
          if (card.veil) card.veil.style.background = 'transparent';
          const c = { el: card.win, kind: 'card', rect: frameRect(card.win), force: true, taken: false };
          cands.push(c);
          ctx.after(1800, () => card.h?.close());
        }
        const now = nextTakes(cands, cx, cy, b.r);
        now.forEach((c, i) => take(c, i * 0.06));
        if (b.popped && popAt === Infinity) swallowLast();
        // If a selected clip is still decoding, keep the live bubble on screen for a short final hold.
        if (b.t >= popAt && (readyForPop() || b.t >= popAt + 4)) pop();
      } else b.t += dt;
      if (!popped) {
        for (const c of cands) c.now = c.taken && !c.released && b.t >= c.t0 ? frameRect(c.el) : null;   // reads first,
        for (const c of cands) if (c.taken) place(c);                                                    // then writes
      }

      /* the bubble */
      const wob = gentle() || popped ? 0 : Math.sin(b.t * 2.1);
      const R = b.r;
      bubble.style.width = bubble.style.height = `${(2 * R).toFixed(1)}px`;
      bubble.style.transform = `translate(${(cx - R).toFixed(1)}px,${(cy - R).toFixed(1)}px) scale(${(1 + 0.018 * wob).toFixed(4)},${(1 - 0.018 * wob).toFixed(4)})`;
      if (mist) {
        if (popped) mistFade = Math.max(0, mistFade - dt / (gentle() ? 0.9 : 0.26));
        mist.draw(cx, cy, R * (popped && !gentle() ? 1 + 0.18 * (1 - mistFade) : 1), b.t * (ctx.gentle ? 0.5 : 1), W, H, mistFade);
      }

      /* the fog, with a soft hole where the bubble is */
      const soft = gentle();
      Ct = popped ? 0 : Math.min(cap, Ct + dt * (soft ? 0.05 : 0.11));
      C += (Ct - C) * (1 - Math.exp(-dt * (Ct < C ? 6 : 2)));
      const m = Math.min(W, H), t = b.t;
      g.setTransform(dpr, 0, 0, dpr, 0, 0);
      g.clearRect(0, 0, W, H);
      g.globalCompositeOperation = 'source-over';
      for (const q of blobs) {
        q.vx *= Math.exp(-dt * 2); q.vy *= Math.exp(-dt * 2); q.ox += q.vx * dt; q.oy += q.vy * dt;
        if (!popped) { q.ox *= Math.exp(-dt * 0.9); q.oy *= Math.exp(-dt * 0.9); }
        const dep = q.d * C * m * 0.75, e = -m * 0.05, sway = soft ? 0.5 : 1;
        let x, y;
        if (q.side === 0) { x = q.u * W; y = e + dep; } else if (q.side === 1) { x = W - e - dep; y = q.u * H; } else if (q.side === 2) { x = q.u * W; y = H - e - dep; } else { x = e + dep; y = q.u * H; }
        x += Math.sin(t * 0.4 * sway + q.ph) * m * 0.05 * sway + q.ox; y += Math.cos(t * 0.33 * sway + q.ph) * m * 0.05 * sway + q.oy;
        q.px = x; q.py = y;
        const sz = q.sz * m * 2.6 * (1 + 0.15 * Math.sin(t * 0.7 + q.ph));
        g.globalAlpha = Math.min(1, Math.max(0, (0.3 + C * 0.75) * (soft ? 0.6 : 1)));
        g.drawImage(spr, x - sz / 2, y - sz / 2, sz, sz);
      }
      g.globalAlpha = 1;
      if (!soft && Math.random() < dt * 10 * C) motes.push({ x: Math.random() * W, y: Math.random() * H, vx: (Math.random() - 0.5) * 8, vy: -6 - Math.random() * 8, l: 0 });
      for (let i = motes.length - 1; i >= 0; i--) {
        const q = motes[i];
        q.l += dt; q.x += q.vx * dt; q.y += q.vy * dt;
        if (q.l >= 3) { motes.splice(i, 1); continue; }
        g.fillStyle = `rgba(255,200,235,${(0.5 * Math.sin(Math.min(1, q.l / 3) * Math.PI)).toFixed(3)})`;
        g.beginPath(); g.arc(q.x, q.y, 1.5, 0, TAU); g.fill();
      }
      if (!popped) {
        g.globalCompositeOperation = 'destination-out';
        const hole = g.createRadialGradient(cx, cy, Math.max(0, R * 0.85), cx, cy, R * 1.08);
        hole.addColorStop(0, 'rgba(0,0,0,1)'); hole.addColorStop(1, 'rgba(0,0,0,0)');
        g.fillStyle = hole;
        g.beginPath(); g.arc(cx, cy, R * 1.08, 0, TAU); g.fill();
        g.fillStyle = '#000';
        g.beginPath(); g.arc(cx, cy, R * 0.85, 0, TAU); g.fill();
        g.globalCompositeOperation = 'source-over';
      }
    });
  });
}
