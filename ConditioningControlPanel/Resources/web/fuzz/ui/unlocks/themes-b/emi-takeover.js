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

import { createFace } from '../../../../arcademy/emi/face.js';
import { FACE_CSS } from '../extras/emi-face.js';
import { EMI_BARKS } from '../../../data/content/emi-takeover.js';
import { LITE, frameRect } from '../../perf.js';
import { shopDoor } from '../../shop.js';
import * as T from '../../../sim/takeover.js';

const art = (name) => `assets/img/emi/${name}.png`;
const SWAY = ['body-sway1', 'body-sway2', 'body-sway3', 'body-sway4'];
const IDLE_FACES = ['0_0', '0_0', '^_^', 'o_o', '^_~'];
const BAKED = ['0_0', '^_^', 'o_o', '-_-', '^_~', '>_<'];
const PINK = '#FF69B4';
const rand = (lo, hi) => lo + Math.random() * (hi - lo);
const any = (list) => list[Math.floor(Math.random() * list.length)];

const CSS = `
${FACE_CSS}
.et-box{position:absolute;inset:0;pointer-events:none;}
.et-emi{position:absolute;left:0;top:0;pointer-events:none;will-change:transform;}
.et-emi img{position:absolute;display:block;pointer-events:none;-webkit-user-drag:none;user-select:none;}
.et-b{inset:0;width:100%;height:100%;}
.et-s{position:absolute;left:34.46%;top:29.46%;width:41.68%;height:37.63%;overflow:hidden;border-radius:2px;}
.et-s>img{inset:0;width:100%;height:100%;}
.et-f{image-rendering:pixelated;}
.et-p{object-fit:cover;visibility:hidden;}
.et-s[data-pic] .et-p{visibility:visible;}
.et-s[data-pic] .et-f{visibility:hidden;}
.et-s[data-off] .et-f{visibility:hidden;}
.et-credit{position:absolute;left:50%;top:100%;transform:translateX(-50%);font:10px var(--font);color:#fff;text-shadow:0 0 2px #000,0 0 2px #000;white-space:nowrap;}
.et-bub{position:absolute;left:0;top:0;z-index:4;width:max-content;max-width:min(220px,64vw);padding:5px 9px;background:#fff;color:#111;border:1px solid #333;border-radius:9px;font:600 14px/1.2 var(--font);box-shadow:1px 2px 0 rgba(0,0,0,.25);pointer-events:none;}
.row.et-guest{cursor:default;}
.row.et-guest .et-who{font-size:11px;line-height:1;color:var(--ink-soft);}
.row.et-guest .cost{font-size:12px;}
.row.et-guest.et-own .et-who{font-weight:700;color:var(--stamp);}
.et-err{position:absolute;left:50%;top:0;transform:translateX(-50%);width:min(560px,calc(100% - 24px));color:#fff4df;font:14px/1.45 Consolas,"Cascadia Mono",Menlo,"DejaVu Sans Mono","Courier New",monospace;}
.et-err .eh{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:6px;}
.et-err .eh img{position:static;height:40px;width:auto;}
.et-err .eh b{padding:2px 10px;background:#16110d;color:#ff8a8f;letter-spacing:2px;font-size:18px;}
.et-err .ln{min-height:1.45em;white-space:pre-wrap;}
.et-err .ln span{background:#16110d;padding:1px 6px;box-decoration-break:clone;-webkit-box-decoration-break:clone;}
@media (max-width:520px){.et-err{font-size:12.5px;}.et-err .eh img{height:32px;}}
`;

/* ---- the art: pose pictures kept as blob urls (a pose swap never refetches), faces painted once and reused ---- */

const poses = new Map();
const pose = (name) => {
  if (!poses.has(name)) {
    poses.set(name, art(name));
    fetch(art(name)).then((r) => (r.ok ? r.blob() : null)).then((b) => { if (b) poses.set(name, URL.createObjectURL(b)); }).catch(() => {});
  }
  return poses.get(name);
};

/** The face she ends on: (● ◡ ●), drawn as shapes in her own pink so it never depends on a font. */
function uncannyUrl(doc) {
  const c = doc.createElement('canvas');
  c.width = 152; c.height = 137;
  const g = c.getContext('2d');
  g.strokeStyle = g.fillStyle = PINK;
  g.lineWidth = 7; g.lineCap = 'round';
  for (const x of [50, 102]) { g.beginPath(); g.arc(x, 62, 15, 0, Math.PI * 2); g.fill(); }
  g.beginPath(); g.arc(76, 64, 16, Math.PI * 0.2, Math.PI * 0.8); g.stroke();
  g.beginPath(); g.arc(77, 68, 66, Math.PI - 0.42, Math.PI + 0.42); g.stroke();
  g.beginPath(); g.arc(75, 68, 66, -0.42, 0.42); g.stroke();
  return c.toDataURL('image/png');
}

const faces = {};
const sprites = new Set();
let facesAsked = false;
function bakeFaces(doc) {
  if (facesAsked) return;
  facesAsked = true;
  const canvas = doc.createElement('canvas');
  const painter = createFace(canvas);
  const paint = () => {
    for (const f of BAKED) { painter.draw(f); faces[f] = canvas.toDataURL('image/png'); }
    faces.uncanny ||= uncannyUrl(doc);
    for (const s of sprites) { if (!s.el.isConnected) { sprites.delete(s); continue; } const f = s.face; s.face = ''; s.setFace(f); }
  };
  paint();
  painter.ready.then(paint);   // the bundled face font may land after the first paint
}

/** One EMI: the body picture, her screen with a face on it, and a picture slot over the face. */
function sprite(doc, size, crisp = false) {
  const el = doc.createElement('div');
  el.className = 'et-emi';
  el.style.width = el.style.height = `${size}px`;
  const body = doc.createElement('img');
  body.className = 'et-b'; body.alt = ''; body.draggable = false;
  if (crisp) body.style.imageRendering = 'pixelated';
  body.src = pose('body-idle');
  const scr = doc.createElement('div');
  scr.className = 'et-s';
  const faceImg = doc.createElement('img');
  faceImg.className = 'et-f'; faceImg.alt = '';
  const picImg = doc.createElement('img');
  picImg.className = 'et-p'; picImg.alt = ''; picImg.referrerPolicy = 'strict-origin'; picImg.decoding = 'async';
  scr.append(faceImg, picImg);
  el.append(body, scr);
  const s = {
    el, body, scr, face: '', poseName: 'body-idle', pic: null,
    setFace(name) { if (name === s.face || !faces[name]) return; s.face = name; faceImg.src = faces[name]; },
    setPose(name) { if (name === s.poseName) return; s.poseName = name; body.src = pose(name); },
    /** A picture url, a plain colour ('#...'), or null for the face. */
    show(what) {
      if (what === s.pic) return;
      s.pic = what;
      if (!what) { delete scr.dataset.pic; scr.style.background = ''; return; }
      scr.dataset.pic = '1';
      if (what[0] === '#') { scr.style.background = what; picImg.style.display = 'none'; return; }
      scr.style.background = '';
      picImg.style.display = '';
      if (picImg.getAttribute('src') !== what) picImg.src = what;
    },
    /** The screen switched off: no face, no picture. */
    dark(on) { if (on) { s.show(null); scr.dataset.off = '1'; } else delete scr.dataset.off; },
  };
  sprites.add(s);
  s.setFace('0_0');
  return s;
}

function controlsRect(doc, pad = 10) {
  const r = doc.getElementById('game-controls')?.getBoundingClientRect();
  return r && r.width ? { x: r.left - pad, y: r.top - pad, w: r.width + pad * 2, h: r.height + pad * 2 } : null;
}

/** The player's pictures for EMI screens: a small pool of stills. Empty means plain colour tiles. */
async function morePictures(ctx, pool, max) {
  if (ctx.pick('f3') === 'blind') return;
  for (let i = pool.length; i < max; i++) {
    let p = null;
    try { p = await ctx.pictures.next({ niche: 'goon', still: true, allowScrolller: true }); } catch { p = null; }
    if (!p?.url || pool.some((q) => q.url === p.url)) break;
    pool.push({ url: p.url, credit: p.credit || '' });
  }
}
const tilesOf = (ctx) => { const p = ctx.palette(); return [...new Set([p.a, p.b, '#f2a33c', '#fff4df'].filter((c) => /^#/.test(c || '')))]; };

/** A bubble over an EMI. Text only: nothing here is ever spoken. */
function bubble(ctx, parent, text) {
  const el = parent.ownerDocument.createElement('div');
  el.className = 'et-bub';
  el.textContent = text;
  parent.appendChild(el);
  ctx.anim(el, [{ opacity: 0 }, { opacity: 1 }], { duration: 250, fill: 'both' });
  return { el, w: el.offsetWidth, h: el.offsetHeight, until: 0 };
}
function placeBubble(b, x, y, size, W) {
  const bx = Math.max(4, Math.min(W - b.w - 4, x + size / 2 - b.w / 2));
  const by = y - b.h - 6 < 4 ? y + size + 6 : y - b.h - 6;
  b.el.style.transform = `translate(${bx.toFixed(1)}px,${by.toFixed(1)}px)`;
}

/* ---- the run-up swarm ---- */

let live = null;   // the swarm on the page (go_auto's scope owns it); the sibling rows and the ending ask it

function mountSwarm(ctx) {
  const doc = ctx.button.el.ownerDocument;
  ctx.css(CSS);
  bakeFaces(doc);
  for (const n of [...SWAY, 'body-idle', 'body-pet', 'body-shock']) pose(n);
  const on = (id) => ctx.has(id) && ctx.switches.isOn(id);
  const stage = () => T.stageOf(on);
  const calm = () => ctx.gentle || ctx.reduced;
  let bag = T.cleanBag(ctx.store.get('bag'));
  const save = () => ctx.store.set('bag', bag);
  let pop = T.newPop(bag.copies, bag.meter);
  let barks = T.newBarks();

  const under = ctx.el('div', '', null, 'floaters');
  const over = ctx.el('div', 'z-index:3;', null, 'over');
  under.className = over.className = 'et-box';
  const base = ctx.bounds().w < 760 ? 44 : 64;
  const amp = () => bag.bought.includes('h_amp');
  const sizeOf = (main) => Math.round(base * (main ? 1.3 : 1) * (amp() ? (main ? 1.3 : 1.5) : 1));
  /* measured now and then, never in the frame loop */
  const stripOf = () => doc.getElementById('dock-strip')?.offsetHeight || 0;
  let walls = controlsRect(doc), strip = stripOf();
  ctx.every(2500, () => { walls = controlsRect(doc); strip = stripOf(); });

  const emis = [];
  let t = 0;
  function add(main, from = null) {
    const size = sizeOf(main), b = ctx.bounds();
    const s = sprite(doc, size, size >= 80);
    (main ? over : under).appendChild(s.el);
    const speed = Math.max(46, Math.min(104, Math.min(b.w, b.h) * 0.11)) * (main ? 0.7 : rand(0.8, 1.15));
    const a = rand(0, Math.PI * 2);
    const e = Object.assign(s, {
      main, size, speed,
      x: from ? from.x + from.size * 0.6 : rand(0, Math.max(1, b.w - size)),
      y: from ? from.y : rand(40, Math.max(41, b.h - size - 60)),
      dx: Math.cos(a), dy: Math.sin(a) || 0.3,
      faceAt: t + rand(1, 4), poseAt: 0, sway: emis.length, flinchUntil: 0, holdUntil: 0, coolUntil: 0,
      mode: 'face', modeUntil: t + rand(2, 8),
    });
    emis.push(e);
    return e;
  }
  const main = add(true);
  const credit = doc.createElement('span');
  credit.className = 'et-credit'; credit.textContent = 'via Scrolller'; credit.hidden = true;
  main.el.appendChild(credit);
  for (let i = 0; i < bag.copies; i++) add(false);
  /** A new copy pops in beside its source, the way a new bouncing copy arrives. */
  function split(self) {
    const b = frameRect(ctx.button.el), bx = (b.left + b.right) / 2, by = (b.top + b.bottom) / 2;
    const src = emis.reduce((best, e) => (Math.hypot(e.x - bx, e.y - by) < Math.hypot(best.x - bx, best.y - by) ? e : best), emis[0]);
    const e = add(false, src);
    ctx.anim(e.el, calm() || LITE ? [{ opacity: 0 }, { opacity: 1 }] : [{ scale: 0.4 }, { scale: 1.12, offset: 0.7 }, { scale: 1 }], { duration: calm() ? 400 : 220 });
    src.setFace('o_o'); src.faceAt = t + 0.8;
    if (self) bag.self += 1; else bag.clicked += 1;
    bag.copies = pop.copies; bag.meter = pop.meter;
    save();
    if (Math.random() < 0.6) say(e, any(EMI_BARKS.reactions.copy_made), { react: true });
  }

  /* -- barks -- */
  const bubs = [];
  let barkAt = 6 + rand(0, 8) + (ctx.restored ? 20 : 0);
  const quiet = () => !!doc.querySelector('#cards .backdrop, #cards .tc');   // a card is up: no bubbles, no copies, no shopping
  function say(e, text, { react = false, cost = 1 } = {}) {
    if (!text || quiet()) return false;
    const room = { now: t, visible: bubs.length, lite: LITE, cost };
    if (!T.barkRoom(barks, room) || (!react && t < e.coolUntil)) return false;
    const b = bubble(ctx, over, text);
    b.e = e; b.until = t + T.barkLife(text) / 1000;
    bubs.push(b);
    e.coolUntil = t + (react ? 8 : T.BARKS.COOL_S);
    e.setFace('^_^'); e.faceAt = t + 1.4;
    return true;
  }
  function bark() {
    const pool = T.poolOf(stage(), bag.bought);
    if (!pool || quiet()) return;
    const free = emis.filter((e) => t >= e.coolUntil);
    if (!free.length) return;
    const speaker = any(free);
    const pairs = EMI_BARKS.pairs[pool];
    if (emis.length > 1 && Math.random() < 0.35 && T.barkRoom(barks, { now: t, visible: bubs.length, lite: LITE, cost: 2 })) {
      const pair = T.pickBark(barks, pairs, { now: t });
      const other = emis.filter((e) => e !== speaker).reduce((best, e) => (Math.hypot(e.x - speaker.x, e.y - speaker.y) < Math.hypot(best.x - speaker.x, best.y - speaker.y) ? e : best));
      if (pair && say(speaker, pair[0])) {
        barks = T.barkSaid(barks, pair, { now: t, cost: 2 });
        other.setFace('o_o'); other.faceAt = t + 1.2;
        ctx.after(rand(...T.BARKS.ANSWER_MS), () => say(other, pair[1], { react: true }));
        return;
      }
    }
    const line = T.pickBark(barks, EMI_BARKS.solo[pool], { now: t });
    if (line && say(speaker, line)) barks = T.barkSaid(barks, line, { now: t });
  }

  /* -- her tiles: one guest tile in the shop, filled by her tips. She walks to it and it is hers. -- */
  let guest = null, shopping = null, lastBuy = -Infinity, hiveSince = null, stamped = 0;
  function drawGuest() {
    const tile = stage() === 'hive' ? T.nextTile(bag.bought) : null;
    if (stamped && t < stamped) return;
    stamped = 0;
    if (!tile) { if (guest) { shopDoor?.guest(null); guest = null; } return; }
    if (guest?.tile !== tile) {
      const el = doc.createElement('button');
      el.type = 'button';
      el.className = 'row et-guest short';
      el.setAttribute('aria-label', `${tile.name}. ${tile.what} Hers, not for sale.`);
      el.title = `${tile.name}: ${tile.what}`;
      el.innerHTML = '<span class="bar" aria-hidden="true"></span><img class="ico" alt="" draggable="false"><span class="et-who"></span><span class="cost"></span>';
      el.querySelector('img').src = art('body-idle');
      el.querySelector('.et-who').textContent = tile.name;
      el.addEventListener('click', () => { ctx.toast('Not yours. Hers.'); main.setFace('o_o'); main.faceAt = t + 1; });
      guest = { el, tile, bar: el.querySelector('.bar'), cost: el.querySelector('.cost'), text: '' };
      shopDoor?.guest(el);
    }
    const text = `${Math.min(bag.tips, tile.price)}/${tile.price} tips`;
    if (text !== guest.text) { guest.text = text; guest.cost.textContent = text; guest.bar.style.transform = `scaleY(${Math.min(1, bag.tips / tile.price).toFixed(3)})`; }
  }
  function finishBuy() {
    const tile = shopping.tile;
    shopping = null;
    bag = { ...bag, ...T.buyTile(bag, tile) };
    lastBuy = t;
    save();
    if (guest?.tile === tile) {
      guest.el.classList.add('et-own');
      guest.el.classList.remove('short');
      guest.cost.textContent = 'hers';
      guest.bar.style.transform = 'scaleY(1)';
      stamped = t + 3;
    }
    ctx.audio.play('assets/audio/sfx/ui_unlock.mp3', { bus: 'sfx', gain: 0.3 });
    main.setFace('^_^'); main.faceAt = t + 2; main.holdUntil = t + 1.2;
    main.coolUntil = 0;
    say(main, any(EMI_BARKS.reactions.emi_buys_tile), { react: true });
    if (tile.id === 'h_amp') for (const e of emis) { e.size = sizeOf(e.main); e.el.style.width = e.el.style.height = `${e.size}px`; }
    if (tile.id === 'h_tx' || tile.id === 'h_uni') { morePictures(ctx, pics, LITE ? 3 : 6); for (const e of emis) e.modeUntil = t + rand(0.5, 3); }
  }

  /* -- pictures on screens (Transmission), all the same (Unified) -- */
  const pics = [];
  let shared = { i: 0, until: 0, on: false };
  ctx.on('pictures', () => { pics.length = 0; for (const e of emis) { e.show(null); e.mode = 'face'; e.modeUntil = t + rand(1, 4); } morePictures(ctx, pics, LITE ? 3 : 6); });
  if (bag.bought.includes('h_tx')) morePictures(ctx, pics, LITE ? 3 : 6);
  const picFor = (i) => (pics.length ? pics[i % pics.length].url : tilesOf(ctx)[i % tilesOf(ctx).length]);
  function screens() {
    if (!bag.bought.includes('h_tx')) return;
    if (bag.bought.includes('h_uni')) {
      if (t >= shared.until) { shared = shared.on ? { i: shared.i + 1, until: t + 3, on: false } : { i: shared.i, until: t + 12, on: true }; }
      for (const e of emis) { if (t < e.flinchUntil) continue; e.show(shared.on ? picFor(shared.i) : null); if (!shared.on) { e.setFace('0_0'); e.faceAt = t + 1; } }
    } else {
      for (const e of emis) {
        if (t < e.modeUntil) continue;
        e.mode = e.mode === 'face' ? 'pic' : 'face';
        e.modeUntil = t + (e.mode === 'pic' ? rand(8, 12) : rand(6, 14));
        e.show(e.mode === 'pic' ? picFor(Math.floor(rand(0, 12))) : null);
      }
    }
    credit.hidden = !emis.some((e) => e.pic && pics.some((p) => p.url === e.pic && p.credit));
  }

  ctx.on('tap', (ev) => {
    if (ev?.auto || (ev?.via !== 'hand' && ev?.via !== 'right')) return;   // only the player's own presses make copies
    pop = T.popPress(pop, { stage: stage() });
  });

  let tapAt = 3, tipFrac = 0, slow = 0;
  ctx.frame((dt) => {
    t += dt;
    const st = stage(), b = ctx.bounds(), cap = T.copyCap(st, bag.bought, LITE);

    /* she taps the button herself; every tap is also a tip for her once she wants things */
    if (t >= tapAt) {
      tapAt = t + 3.2;
      ctx.button.press();
      if (!shopping && t >= main.flinchUntil) { main.setFace('^_^'); main.faceAt = t + 0.6; }
      if (st !== 'auto') bag.tips += T.TIPS.PER_TAP;
    }
    if (st !== 'auto' && st !== 'none') { tipFrac += dt * T.TIPS.PER_S; if (tipFrac >= 1) { const n = Math.floor(tipFrac); tipFrac -= n; bag.tips += n; } }
    if (st === 'hive' && hiveSince == null) hiveSince = t;

    /* copies: the first arrives with Self Copying, the rest from the player's taps, and on their own after HIVE MIND */
    if ((st === 'copy' || st === 'hive') && !quiet()) {
      if (pop.copies === 0) { pop = { ...pop, copies: 1, lastSplit: t }; split(false); }
      else { const step = T.popStep(pop, { now: t, stage: st, cap, lite: LITE }); pop = step.pop; if (step.made) split(step.self); }
    }

    /* her shopping */
    slow += dt;
    if (slow >= 0.5) {
      slow = 0;
      drawGuest();
      screens();
      if (!shopping && !stamped) {
        const tile = T.canBuy({ stage: st, tips: bag.tips, bought: bag.bought, sinceBuyS: t - lastBuy, sinceHiveS: t - (hiveSince ?? t) });
        if (tile && guest?.tile === tile && !quiet()) shopping = { tile, since: t };
      }
      if (t >= barkAt) { barkAt = t + rand(9, 20); bark(); }
    }

    for (const e of emis) {
      let v = e.speed * (calm() ? 0.5 : 1);
      if (e === main && shopping) {
        const r = guest?.el.isConnected ? frameRect(guest.el) : null;
        if (!r || !r.width || t - shopping.since > 9) { finishBuy(); }
        else {
          const tx = (r.left + r.right) / 2 - e.size / 2, ty = r.top + r.height * 0.45 - e.size / 2;
          const d = Math.hypot(tx - e.x, ty - e.y);
          if (d < 10) finishBuy();
          else { e.dx = (tx - e.x) / d; e.dy = (ty - e.y) / d; v = calm() ? 60 : 130; }
        }
      }
      if (t >= e.holdUntil) { e.x += e.dx * v * dt; e.y += e.dy * v * dt; }
      let hit = false;
      const maxX = Math.max(0, b.w - e.size), maxY = Math.max(0, b.h - strip - e.size);
      if (e.x < 0) { e.x = 0; e.dx = Math.abs(e.dx); hit = true; } else if (e.x > maxX) { e.x = maxX; e.dx = -Math.abs(e.dx); hit = true; }
      if (e.y < 0) { e.y = 0; e.dy = Math.abs(e.dy); hit = true; } else if (e.y > maxY) { e.y = maxY; e.dy = -Math.abs(e.dy); hit = true; }
      /* Wake Up's corner is a wall: she goes round it, never over it */
      if (walls && e.x < walls.x + walls.w && e.x + e.size > walls.x && e.y < walls.y + walls.h && e.y + e.size > walls.y) {
        const left = e.x + e.size - walls.x, down = walls.y + walls.h - e.y;
        if (left < down) { e.x = walls.x - e.size; e.dx = -Math.abs(e.dx); } else { e.y = walls.y + walls.h; e.dy = Math.abs(e.dy); }
        hit = true;
      }
      if (hit && !calm() && !(e === main && shopping)) { e.flinchUntil = t + 0.35; e.setPose('body-shock'); if (!e.pic) e.setFace('>_<'); e.faceAt = t + 0.35; }
      if (t >= e.flinchUntil && t >= e.poseAt) {
        e.poseAt = t + 0.42;
        e.sway += 1;
        e.setPose(calm() ? 'body-idle' : SWAY[e.sway % SWAY.length]);
      }
      if (t >= e.faceAt && !e.pic) {
        const blink = !calm() && Math.random() < 0.25;
        e.setFace(blink ? '-_-' : any(IDLE_FACES));
        e.faceAt = t + (blink ? 0.14 : rand(2.5, 6));
      }
      e.el.style.transform = `translate(${e.x.toFixed(1)}px,${e.y.toFixed(1)}px)`;
    }
    for (let i = bubs.length - 1; i >= 0; i--) {
      const bu = bubs[i];
      if (t >= bu.until) { bubs.splice(i, 1); const el = bu.el; ctx.anim(el, [{ opacity: 1 }, { opacity: 0 }], { duration: 300, fill: 'both' }); ctx.after(320, () => el.remove()); continue; }
      placeBubble(bu, bu.e.x, bu.e.y, bu.e.size, b.w);
    }
  });
  ctx.every(10000, () => { bag.copies = pop.copies; bag.meter = pop.meter; save(); });

  const api = {
    /** Where every EMI stands, in page pixels (the ending starts its fill there). */
    points: () => emis.map((e) => ({ x: e.x + e.size / 2, y: e.y + e.size / 2 })),
    hide() { under.hidden = over.hidden = true; under.style.display = over.style.display = 'none'; shopDoor?.guest(null); },
    count: () => emis.length,
    /** A sibling row just arrived: one line from its pool, soon. */
    arrive() { barkAt = Math.min(barkAt, t + 1.2); },
    /** Dev drive only: hand her tips (her own count, nothing the player owns). */
    tip(n) { bag.tips += Math.max(0, Math.floor(n) || 0); },
    /** Dev drive only: hand her tips (her own count, nothing the player owns). */
    tip(n) { bag.tips += Math.max(0, Math.floor(n) || 0); },
    state: () => ({ stage: stage(), tips: bag.tips, bought: [...bag.bought], copies: emis.length - 1, meter: pop.meter, bubbles: bubs.length, shopping: !!shopping, main: { x: main.x, y: main.y, size: main.size }, pics: emis.filter((e) => e.pic).length }),
  };
  live = api;
  doc.defaultView.__emiSwarm = api;
  ctx.cleanup(() => {
    if (live === api) live = null;
    if (doc.defaultView.__emiSwarm === api) delete doc.defaultView.__emiSwarm;
    shopDoor?.guest(null);
    for (const e of emis) sprites.delete(e);
  });
  return {};
}

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

/* ---- the rows ---- */

const sibling = { mount(ctx) { if (!ctx.restored) live?.arrive(); return {}; } };

export default {
  /* Give EMI Autonomy owns the swarm; the three rows after it only change what the swarm reads. */
  go_auto: { mount(ctx) { return mountSwarm(ctx); } },
  go_will: sibling,
  go_copy: sibling,
  go_hive: sibling,
};
