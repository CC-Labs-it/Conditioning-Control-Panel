/* ui/unlocks/themes-b/emi-swarm.js - EMI Takeover run-up (v3 brief handoff-1109/TC-EMI-TAKEOVER-V3-BRIEF.md): the goon
 * chain rows before go_end and the swarm they share. Numbers and rules: sim/takeover.js. Shared sprite parts:
 * emi-kit.js. The ending reads `liveSwarm()`.
 *
 * go_tx      one EMI stays put; her screen shows a picture, then her face, then another picture (2-3 s each)
 * go_will    she walks in short steps, stops, comments, hops now and then; never follows the cursor
 * go_corp    her own budget grows on the game clock; she walks to her guest tile in the shop and buys it
 *            (Amplifier, Overclock, Louder Opinions, Self Replication, then Override)
 * Self Rep.  each of the player's own manual clicks splits an EMI off a nearby one
 * Override   about half of those clicks turn a page object (and its bottom tab) into an EMI: a bzzz and a shake first
 * go_hive    the screens run in patterns while everyone keeps wandering; bought by EMI herself if the player waits
 *            (150 s after Override) */

import { EMI_BARKS } from '../../../data/content/emi-takeover.js';
import { LITE, frameRect } from '../../perf.js';
import { shopDoor } from '../../shop.js';
import * as T from '../../../sim/takeover.js';
import { art, SWAY, IDLE_FACES, rand, any, CSS, pose, sprites, bakeFaces, sprite, controlsRect, morePictures, tilesOf, bubble, placeBubble, deskHandoff } from './emi-kit.js';
import { liveEmi } from '../extras/emi.js';
import { iconFor } from '../../icons.js';
import { BY_ID } from '../../../data/items.js';

const SWARM_CSS = `
[data-et-gone]{visibility:hidden !important;}
.et-box .et-credit{z-index:4;}
[data-et-buzz]{pointer-events:none;}
.et-emi .et-h{position:absolute;left:0;right:0;bottom:79%;text-align:center;line-height:1;font-family:"Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji",sans-serif;pointer-events:none;}
`;
const STEP_HZ = 2.4;      // step bob: bumps a second while she walks
const BOB_PX = 3;
const JUMP_S = 0.55;

/* ---- the run-up swarm ---- */

let live = null;   // the swarm on the page (go_tx's scope owns it); the sibling rows and the ending ask it

function mountSwarm(ctx) {
  const doc = ctx.button.el.ownerDocument;
  ctx.css(CSS);
  ctx.css(SWARM_CSS);
  bakeFaces(doc);
  for (const n of [...SWAY, 'body-idle']) pose(n);
  const on = (id) => ctx.has(id) && ctx.switches.isOn(id);
  const stage = () => T.stageOf(on);
  const calm = () => ctx.gentle || ctx.reduced;
  let bag = T.cleanBag(ctx.store.get('bag'));
  const save = () => ctx.store.set('bag', bag);
  const phase = () => T.phaseOf(stage(), bag.bought);
  const fx = () => T.effectsOf(bag.bought);
  let barks = T.newBarks();

  const box = ctx.el('div', 'z-index:3;', null, 'over');
  box.className = 'et-box';
  const base = ctx.bounds().w < 760 ? 44 : 64;
  /* The EMI the player already has (the desk EMI, ui/unlocks/extras/emi.js) IS the main EMI: the main sprite takes her
   * spot, her size and her hat, and she is borrowed (hidden, quiet) until this scope ends. No desk EMI: a new one. */
  let desk = liveEmi();
  const hand = deskHandoff(desk);
  if (!hand) desk = null;
  let deskSize = hand?.size || 0;
  const sizeOf = (main) => Math.round((main && deskSize ? deskSize : base * (main ? 1.3 : 1)) * fx().size);
  /* measured now and then, never in the frame loop */
  const stripOf = () => doc.getElementById('dock-strip')?.offsetHeight || 0;
  let walls = controlsRect(doc), strip = stripOf();
  /* the button and the counter stay readable: EMIs walk through, never stop on them */
  const keepOf = () => [ctx.parts.buttonWrap, ctx.parts.counter].map((el) => el?.getBoundingClientRect()).filter((r) => r && r.width).map((r) => ({ x: r.left - 8, y: r.top - 8, w: r.width + 16, h: r.height + 16 }));
  let keep = keepOf();
  ctx.every(2500, () => { walls = controlsRect(doc); strip = stripOf(); keep = keepOf(); });
  const over_ = (k, x, y, size) => !!k && x < k.x + k.w && x + size > k.x && y < k.y + k.h && y + size > k.y;
  const inWalls = (x, y, size) => over_(walls, x, y, size);
  const blocked = (x, y, size) => inWalls(x, y, size) || keep.some((k) => over_(k, x, y, size));
  const clampX = (x, size) => Math.max(0, Math.min(ctx.bounds().w - size, x));
  const clampY = (y, size) => Math.max(0, Math.min(ctx.bounds().h - strip - size, y));

  /** A spot clear of Wake Up's corner and, for the first EMI, of the button and the shop. */
  function freeSpot(size, avoidParts = false) {
    const b = ctx.bounds();
    const keep = avoidParts ? [ctx.parts.buttonWrap, ctx.parts.shop, ctx.parts.counter].map((el) => el?.getBoundingClientRect()).filter((r) => r && r.width) : [];
    let best = null;
    for (let i = 0; i < 24; i++) {
      const x = rand(0, Math.max(1, b.w - size)), y = rand(40, Math.max(41, b.h - strip - size - 10));
      if (blocked(x, y, size)) continue;
      best = { x, y };
      if (!keep.some((r) => x < r.right + 12 && x + size > r.left - 12 && y < r.bottom + 12 && y + size > r.top - 12)) return best;
    }
    return best || { x: 8, y: Math.max(40, b.h - strip - size - 10) };
  }

  const emis = [];
  let t = 0;
  function add(main, at = null) {
    const size = sizeOf(main);
    const s = sprite(doc, size, size >= 80 || (main && !!desk));
    box.appendChild(s.el);
    const spot = at ? { x: clampX(at.x, size), y: clampY(at.y, size) } : freeSpot(size, main);
    const e = Object.assign(s, {
      main, size,
      speed: Math.max(40, Math.min(80, Math.min(ctx.bounds().w, ctx.bounds().h) * 0.08)) * rand(0.85, 1.15),
      x: spot.x, y: spot.y, dir: Math.random() < 0.5 ? -1 : 1,
      state: 'stand', until: t + rand(0.5, 3), tx: spot.x, ty: spot.y, walkT: 0, jumpAt: t + rand(6, 14), jumpT: -1,
      faceAt: t + rand(1, 4), poseAt: 0, sway: emis.length, coolUntil: 0,
      mode: 'face', modeUntil: t + rand(0.6, 2.5), lastScreen: -Infinity,
    });
    emis.push(e);
    return e;
  }
  const popIn = (e) => ctx.anim(e.el, calm() || LITE ? [{ opacity: 0 }, { opacity: 1 }] : [{ opacity: 0, scale: 0.7 }, { opacity: 1, scale: 1 }], { duration: calm() ? 400 : 260 });
  const main = add(true, hand);
  /* her hat (em_hat) rides on the main sprite, sized as on the desk */
  const hat = doc.createElement('div');
  hat.className = 'et-h';
  main.el.appendChild(hat);
  const fitHat = () => { hat.style.fontSize = `${Math.round(main.size * 0.42)}px`; };
  fitHat();
  const resize = (e, s) => {
    const d = (s - e.size) / 2;
    e.size = s; e.x = clampX(e.x - d, s); e.y = clampY(e.y - d, s); e.tx = e.x; e.ty = e.y;
    e.el.style.width = e.el.style.height = `${s}px`;
    if (e === main) fitHat();
  };
  /** Keep the desk EMI borrowed while this scope lives. She can turn up after the swarm (rows mount in catalog order on
   *  a reload): in the first second the main sprite moves to her spot and size; later she is only hidden. */
  function borrowDesk() {
    const d = liveEmi();
    if (d && d !== desk) {
      const h = deskHandoff(d);
      if (!h) return;
      desk = d;
      if (t <= 1) {
        deskSize = h.size;
        resize(main, sizeOf(true));
        main.x = main.tx = clampX(h.x, main.size); main.y = main.ty = clampY(h.y, main.size);
        main.state = 'stand'; main.until = t + rand(0.5, 3);
      }
    }
    if (!desk?.el.isConnected) return;
    if (!desk.borrowed) desk.borrow(true);
    const want = desk.hat?.textContent || '';
    if (hat.textContent !== want) hat.textContent = want;
  }
  borrowDesk();
  if (!ctx.restored && !desk) popIn(main);
  /* the one credit line, under the first EMI, outside her (she flips; the words do not) */
  const credit = doc.createElement('span');
  credit.className = 'et-credit'; credit.textContent = 'via Scrolller'; credit.hidden = true;
  credit.style.top = '0'; credit.style.left = '0';
  box.appendChild(credit);
  for (let i = 0; i < bag.copies; i++) add(false);

  /* -- pictures on the screens -- */
  const pics = [];
  const picMax = LITE ? 4 : 8;
  morePictures(ctx, pics, LITE ? 3 : 6);
  ctx.every(40000, () => { if (pics.length < picMax) morePictures(ctx, pics, picMax); });
  ctx.on('pictures', () => { pics.length = 0; for (const e of emis) { e.show(null); e.mode = 'face'; e.modeUntil = t + rand(1, 3); } morePictures(ctx, pics, LITE ? 3 : 6); });
  const picFor = (i) => (pics.length ? pics[((i % pics.length) + pics.length) % pics.length].url : tilesOf(ctx)[Math.abs(i) % tilesOf(ctx).length]);
  let pattern = null, patternUntil = 0, hiveT0 = null;
  function screens() {
    const ph = phase();
    if (ph === 'none') return;
    if (ph === 'hive') {
      if (hiveT0 == null) hiveT0 = t;
      if (t >= patternUntil) { pattern = T.nextPattern(pattern); patternUntil = t + rand(...T.HIVE.ROTATE_S); }
      const b = ctx.bounds(), period = bag.bought.includes('h_oc') ? T.HIVE.PERIOD_S_FAST : T.HIVE.PERIOD_S;
      const cx = main.x + main.size / 2, cy = main.y + main.size / 2, diag = Math.hypot(b.w, b.h) || 1;
      const tt = t - hiveT0;
      emis.forEach((e, i) => {
        const ex = e.x + e.size / 2, ey = e.y + e.size / 2;
        const lit = T.patternLit(pattern, { t: tt, period, x01: ex / (b.w || 1), d01: Math.hypot(ex - cx, ey - cy) / diag, i, n: emis.length, side: ex < b.w / 2 ? 0 : 1 });
        if (lit === !!e.pic || t - e.lastScreen < T.HIVE.MIN_GAP_S) return;
        e.show(lit ? picFor(T.patternPic(tt, period)) : null);
        e.lastScreen = t;
      });
    } else {
      hiveT0 = null;
      for (const e of emis) {
        if (t < e.modeUntil || t - e.lastScreen < T.HIVE.MIN_GAP_S) continue;
        e.mode = e.mode === 'face' ? 'pic' : 'face';
        e.modeUntil = t + rand(...fx().picS);
        e.show(e.mode === 'pic' ? picFor(Math.floor(rand(0, 64))) : null);
        e.lastScreen = t;
      }
    }
    credit.hidden = !emis.some((e) => e.pic && pics.some((p) => p.url === e.pic && p.credit));
  }

  /* -- barks -- */
  const bubs = [];
  let barkAt = Infinity;
  const quiet = () => !!doc.querySelector('#cards .backdrop, #cards .tc');   // a card is up: no bubbles, no copies, no shopping
  function say(e, text, { react = false } = {}) {
    if (!text || quiet()) return false;
    if (!T.barkRoom(barks, { now: t, visible: bubs.length, lite: LITE }) || (!react && t < e.coolUntil)) return false;
    const b = bubble(ctx, box, text);
    b.e = e; b.until = t + T.barkLife(text) / 1000;
    bubs.push(b);
    barks = T.barkSaid(barks, text, { now: t });
    e.coolUntil = t + (react ? 8 : fx().coolS);
    if (!e.pic) { e.setFace('^_^'); e.faceAt = t + 1.4; }
    return true;
  }
  function bark() {
    const ph = phase();
    if (ph === 'none' || ph === 'tx' || quiet()) return;   // Transmission only speaks once, on arrival
    const pool = T.poolOf(ph, bag.bought);
    const free = emis.filter((e) => t >= e.coolUntil && !(e === main && shopping));
    if (!free.length) return;
    const speaker = any(free);
    if (emis.length > 1 && Math.random() < 0.35 && T.barkRoom(barks, { now: t, visible: bubs.length, lite: LITE, cost: 2 })) {
      const pair = T.pickBark(barks, EMI_BARKS.pairs[pool], { now: t });
      const other = emis.filter((e) => e !== speaker).reduce((best, e) => (Math.hypot(e.x - speaker.x, e.y - speaker.y) < Math.hypot(best.x - speaker.x, best.y - speaker.y) ? e : best));
      if (pair && say(speaker, pair[0])) {
        barks = T.barkSaid(barks, pair, { now: t, cost: 0 });
        if (!other.pic) { other.setFace('o_o'); other.faceAt = t + 1.2; }
        ctx.after(rand(...T.BARKS.ANSWER_MS), () => say(other, pair[1], { react: true }));
        return;
      }
    }
    const line = T.pickBark(barks, EMI_BARKS.solo[pool], { now: t });
    if (line) say(speaker, line);
  }
  if (!ctx.restored) ctx.after(1600, () => say(main, any(EMI_BARKS.solo.transmission), { react: true }));
  const nextBark = () => { const [lo, hi] = fx().barkS; barkAt = t + rand(lo, hi); };

  /* -- her tiles: one guest tile in the shop, filled by her budget. She walks to it and it is hers. -- */
  let guest = null, shopping = null, lastBuy = -Infinity, stamped = 0;
  function drawGuest() {
    const tile = T.isShopping(phase(), bag.bought) ? T.nextTile(bag.bought) : null;
    if (stamped && t < stamped) return;
    stamped = 0;
    if (!tile) { if (guest) { shopDoor?.guest(null); guest = null; } return; }
    if (guest?.tile !== tile) {
      const el = doc.createElement('button');
      el.type = 'button';
      el.className = 'row et-guest short';
      el.setAttribute('aria-label', `${tile.name}. ${tile.what} Hers, not for sale.`);
      el.title = `${tile.name}: ${tile.what} Hers, not for sale.`;
      el.innerHTML = '<span class="bar" aria-hidden="true"></span><img class="ico" alt="" draggable="false"><span class="et-who"></span><span class="cost"></span>';
      el.querySelector('img').src = art('body-idle');
      el.querySelector('.et-who').textContent = tile.name;
      el.addEventListener('click', () => { ctx.toast('Hers, not for sale.'); if (!main.pic) { main.setFace('o_o'); main.faceAt = t + 1; } });
      guest = { el, tile, bar: el.querySelector('.bar'), cost: el.querySelector('.cost'), text: '' };
      shopDoor?.guest(el);
    }
    const have = Math.floor(Math.min(bag.budget, tile.price));
    const text = `${have}/${tile.price} hers`;
    if (text !== guest.text) { guest.text = text; guest.cost.textContent = text; guest.bar.style.transform = `scaleY(${Math.min(1, bag.budget / tile.price).toFixed(3)})`; }
  }
  function finishBuy() {
    const tile = shopping.tile;
    shopping = null;
    bag = { ...bag, ...T.buyTile(bag, tile) };
    lastBuy = t;
    main.state = 'stand'; main.until = t + 1.6;
    save();
    if (guest?.tile === tile) {
      guest.el.classList.add('et-own');
      guest.el.classList.remove('short');
      guest.cost.textContent = 'hers';
      guest.bar.style.transform = 'scaleY(1)';
      stamped = t + 3;
    }
    ctx.audio.play('assets/audio/sfx/ui_unlock.mp3', { bus: 'sfx', gain: 0.3 });
    if (!main.pic) { main.setFace('^_^'); main.faceAt = t + 2; }
    main.coolUntil = 0;
    say(main, any(EMI_BARKS.reactions[tile.id === 'h_over' ? 'override_bought' : 'emi_buys_tile']), { react: true });
    if (tile.id === 'h_amp') {
      for (const e of emis) resize(e, sizeOf(e.main));
    }
    if (tile.id === 'h_loud') barkAt = Math.min(barkAt, t + 4);
  }

  /* -- Self Replication (she splits) and Override (things on the page become her): the player's own clicks -- */
  const gone = new Set();
  const tabs = new Set();       // dock bodies whose bottom tab reads EMI while their object is hers
  const buzzing = new Set();    // objects shaking right now, about to be hers
  let lastRep = -Infinity;
  /** Page objects that may become an EMI: dock objects and picture screens, floater toys. Never the button, the
   *  counter, the shop, the dock strip or Wake Up's controls, and nothing that fills the page. */
  function objects() {
    const b = ctx.bounds();
    const btn = ctx.button.el, counter = ctx.parts.counter;
    return [...doc.querySelectorAll('section.dk:not(.door), #l-floaters > *, #l-screens > *')].filter((el) => {
      if (el.classList.contains('et-box') || el.hasAttribute('data-et-gone') || el.hasAttribute('data-et-buzz') || el.closest('#shop, #game-controls, #dock-strip, #cards, #page')) return false;
      if (el.contains(btn) || (counter && el.contains(counter))) return false;
      const r = el.getBoundingClientRect();
      if (r.width < 24 || r.height < 24 || r.right < 0 || r.bottom < 0 || r.left > b.w || r.top > b.h) return false;
      if (r.width * r.height > b.w * b.h * 0.4) return false;
      const cs = doc.defaultView.getComputedStyle(el);
      return cs.visibility !== 'hidden' && cs.display !== 'none' && Number(cs.opacity) > 0.05;
    });
  }
  /* the bzzz: a short, quiet, low electric buzz, synthesised (no sfx in the box fits); never two at once */
  let io = null, buzzAt = -Infinity;
  function bzzz() {
    if (!T.buzzRoom({ now: t, lastAt: buzzAt })) return;
    io ||= ctx.audio.input?.({ bus: 'sfx', gain: 0.55 }) || null;
    if (!io) return;
    buzzAt = t;
    const ac = io.ac, t0 = ac.currentTime, dur = 0.25;
    const env = ac.createGain();
    env.gain.setValueAtTime(0.0001, t0);
    env.gain.exponentialRampToValueAtTime(0.32, t0 + 0.015);
    env.gain.setValueAtTime(0.32, t0 + dur * 0.55);
    env.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
    const lp = ac.createBiquadFilter();
    lp.type = 'lowpass'; lp.frequency.value = 1100; lp.Q.value = 2;
    const hum = ac.createOscillator(), edge = ac.createOscillator();
    const f0 = rand(92, 104);
    hum.type = 'sawtooth'; hum.frequency.setValueAtTime(f0, t0); hum.frequency.linearRampToValueAtTime(f0 * 0.84, t0 + dur);
    edge.type = 'square'; edge.frequency.setValueAtTime(f0 * 2.01, t0); edge.frequency.linearRampToValueAtTime(f0 * 1.69, t0 + dur);
    const edgeGain = ac.createGain(); edgeGain.gain.value = 0.25;
    /* the crackle: a fast tremolo on the whole buzz */
    const trem = ac.createGain(), lfo = ac.createOscillator(), depth = ac.createGain();
    trem.gain.value = 0.6; lfo.type = 'square'; lfo.frequency.value = 34; depth.gain.value = 0.4;
    lfo.connect(depth).connect(trem.gain);
    hum.connect(lp); edge.connect(edgeGain).connect(lp);
    lp.connect(trem).connect(env).connect(io.node);
    for (const o of [hum, edge, lfo]) { o.start(t0); o.stop(t0 + dur + 0.02); }
    hum.onended = () => { for (const n of [hum, edge, edgeGain, lfo, depth, trem, lp, env]) n.disconnect(); };
  }
  /** A small fast jitter (a few px, a touch of rotation, never a stretch), added on top of the element's own motion. */
  function shake(el, ms) {
    if (calm()) return null;
    const frames = [];
    for (let i = 0; i <= 12; i++) {
      const k = i === 0 || i === 12 ? 0 : 1 - i / 16;
      frames.push({ translate: `${(rand(-3, 3) * k).toFixed(1)}px ${(rand(-2, 2) * k).toFixed(1)}px`, rotate: `${(rand(-1.6, 1.6) * k).toFixed(2)}deg` });
    }
    return ctx.anim(el, frames, { duration: ms, composite: 'add', easing: 'linear' });
  }
  /** The dock body behind a page object, if it is a dock object. */
  const dockOf = (el) => (el.matches('section.dk') ? el : el.closest('section.dk'))?.querySelector(':scope > .dk-b')?.dock || null;
  let emiIcon = null;
  const iconNode = () => {
    try { return (emiIcon ||= iconFor(doc, BY_ID.em_head)).cloneNode(true); } catch {
      const i = doc.createElement('img'); i.src = art('body-idle'); i.alt = ''; i.style.cssText = 'width:20px;height:20px;image-rendering:pixelated;';
      return i;
    }
  };
  /** Override: the nearest EMI looks, the object (and its tab) buzz and shake, then an EMI stands where it was. */
  function override(el) {
    const r0 = el.getBoundingClientRect();
    const cx = r0.left + r0.width / 2, cy = r0.top + r0.height / 2;
    const dist = (e) => Math.hypot(e.x + e.size / 2 - cx, e.y + e.size / 2 - cy);
    const near = emis.reduce((best, e) => (dist(e) < dist(best) ? e : best), main);
    if (!near.pic) { near.setFace('o_o'); near.faceAt = t + 1.2; }
    if (Math.abs(cx - (near.x + near.size / 2)) > 4) near.dir = cx > near.x + near.size / 2 ? 1 : -1;
    el.setAttribute('data-et-buzz', '');
    buzzing.add(el);
    const dock = dockOf(el), chip = dock?.chip;
    if (dock && chip) { tabs.add(dock); dock.disguise({ title: 'EMI', icon: iconNode() }); }
    bzzz();
    const ms = calm() ? 200 : T.REP.BUZZ_MS;
    const a = shake(el, ms), b = chip ? shake(chip, ms) : null;
    const fade = calm() ? ctx.anim(el, [{ opacity: 1 }, { opacity: 0 }], { duration: ms, fill: 'forwards' }) : null;
    ctx.after(ms, () => {
      buzzing.delete(el);
      el.removeAttribute('data-et-buzz');
      if (el.isConnected) convert(el);
      for (const x of [a, b, fade]) x?.cancel();   // after it is hidden, so it never flashes back
    });
  }
  function convert(el) {
    const r = el.getBoundingClientRect();
    el.setAttribute('data-et-gone', '');
    gone.add(el);
    const size = sizeOf(false);
    const e = add(false, { x: r.left + r.width / 2 - size / 2, y: r.top + r.height / 2 - size / 2 });
    e.until = t + rand(1, 2.5);
    popIn(e);
    bag.took += 1;
    bag.copies = emis.length - 1;
    save();
    if (Math.random() < 0.5) say(e, any(EMI_BARKS.reactions.object_taken), { react: true });
  }
  function split() {
    const src = any(emis.filter((e) => !(e === main && shopping))) || main;
    const size = sizeOf(false);
    const side = Math.random() < 0.5 ? -1 : 1;
    const e = add(false, { x: src.x + side * src.size * 0.7, y: src.y + rand(-0.2, 0.2) * src.size });
    e.dir = side;
    popIn(e);
    if (!src.pic) { src.setFace('o_o'); src.faceAt = t + 0.8; }
    bag.clicked += 1;
    bag.copies = emis.length - 1;
    save();
    if (Math.random() < 0.4) say(e, any(EMI_BARKS.reactions.copy_made), { react: true });
  }
  ctx.on('tap', (ev) => {
    if (ev?.auto || (ev?.via !== 'hand' && ev?.via !== 'right')) return;   // only the player's own presses
    const ph = phase();
    if ((ph !== 'rep' && ph !== 'hive') || quiet()) return;
    const count = emis.length + buzzing.size;
    if (t - lastRep < T.REP.COOL_S || count >= T.repCap(LITE)) return;
    const over = fx().override;
    const objs = over ? objects() : [];
    const act = T.repPress({ now: t, lastAt: lastRep, count, lite: LITE, objects: objs.length, override: over });
    if (!act) return;
    lastRep = t;
    if (act === 'convert') override(any(objs)); else split();
  });

  /* -- HIVE MIND: she buys it herself when the player waits too long -- */
  let forcedAt = -Infinity;
  function force() {
    if (t - forcedAt < 10 || quiet()) return;
    forcedAt = t;
    doc.dispatchEvent(new CustomEvent('fuzz:emi-buys-hive'));
    if (ctx.has('go_hive')) {
      ctx.toast('EMI bought HIVE MIND herself. It was free.');
      say(any(emis), any(EMI_BARKS.reactions.hive_forced), { react: true });
    }
  }

  /* -- walking: short steps, stops, a hop now and then; never toward the cursor -- */
  function walk(e, dt) {
    if (e === main && shopping) {
      const r = guest?.el.isConnected ? frameRect(guest.el) : null;
      if (!r || !r.width || t - shopping.since > 12) { finishBuy(); return; }
      e.tx = clampX((r.left + r.right) / 2 - e.size / 2, e.size);
      e.ty = clampY(r.top - e.size * 0.92, e.size);   // she stands on top of her tile, so it still reads
      e.state = 'walk';
    } else if (e.state === 'stand') {
      if (!calm() && e.jumpT < 0 && t >= e.jumpAt) { e.jumpT = t; e.jumpAt = t + rand(8, 18); }
      if (blocked(e.x, e.y, e.size)) e.until = Math.min(e.until, t);   // standing on the button or the counter: move on
      if (t >= e.until && e.jumpT < 0) {
        let tx = e.x, ty = e.y;
        for (let i = 0; i < 8; i++) {
          const reach = e.size * rand(0.9, 2.6) * (1 + i * 0.4), a = rand(0, Math.PI * 2);
          tx = clampX(e.x + Math.cos(a) * reach, e.size); ty = clampY(e.y + Math.sin(a) * reach * 0.6, e.size);
          if (!blocked(tx, ty, e.size)) break;
        }
        e.tx = tx; e.ty = ty; e.state = 'walk';
      }
    }
    if (e.state !== 'walk') return;
    const d = Math.hypot(e.tx - e.x, e.ty - e.y);
    const v = e.speed * fx().speed * (calm() ? 0.5 : 1) * (e === main && shopping ? 1.6 : 1);
    if (Math.abs(e.tx - e.x) > 2) e.dir = e.tx > e.x ? 1 : -1;
    if (d <= v * dt || d < 1) {
      e.x = e.tx; e.y = e.ty;
      if (e === main && shopping) { finishBuy(); return; }
      e.state = 'stand'; e.until = t + rand(...(calm() ? [2, 5] : [1.2, 3.6])); e.walkT = 0;
      return;
    }
    e.x += ((e.tx - e.x) / d) * v * dt;
    e.y += ((e.ty - e.y) / d) * v * dt;
    e.walkT += dt;
  }

  let slow = 0;
  ctx.frame((dt) => {
    t += dt;
    const ph = phase(), moving = ph !== 'none' && ph !== 'tx';
    const hurry = stage() === 'hive' && T.isShopping(ph, bag.bought);
    bag.budget = T.budgetStep(bag.budget, dt, { phase: ph, bought: bag.bought, hurry });
    if (T.hiveClockRuns({ phase: ph, bought: bag.bought, ownsHive: ctx.has('go_hive') })) bag.sinceOver = Math.min(T.HIVE.FORCE_S, bag.sinceOver + dt);

    slow += dt;
    if (slow >= 0.25) {
      slow = 0;
      borrowDesk();
      drawGuest();
      screens();
      if (T.isShopping(ph, bag.bought) && !shopping && !stamped && !quiet()) {
        const tile = T.canBuy({ phase: ph, budget: bag.budget, bought: bag.bought, sinceBuyS: t - lastBuy, hurry });
        if (tile && guest?.tile === tile) { shopping = { tile, since: t }; main.jumpT = -1; }
      }
      if (T.mustForceHive({ phase: ph, bought: bag.bought, ownsHive: ctx.has('go_hive'), sinceOverS: bag.sinceOver })) force();
      if (moving && barkAt === Infinity) nextBark();
      if (t >= barkAt) { nextBark(); bark(); }
    }

    const b = ctx.bounds();
    for (const e of emis) {
      if (moving) walk(e, dt);
      let lift = 0;
      if (e.jumpT >= 0) {
        const p = (t - e.jumpT) / JUMP_S;
        if (p >= 1 || calm()) e.jumpT = -1; else lift = 4 * e.size * 0.32 * p * (1 - p);
      } else if (e.state === 'walk' && !calm()) lift = Math.abs(Math.sin(e.walkT * Math.PI * STEP_HZ)) * BOB_PX;
      /* Wake Up's corner is a wall: she goes round it, never over it */
      if (inWalls(e.x, e.y, e.size)) { e.y = walls.y + walls.h; e.ty = Math.max(e.ty, e.y); }
      e.x = Math.max(0, Math.min(Math.max(0, b.w - e.size), e.x));
      e.y = Math.max(0, Math.min(Math.max(0, b.h - strip - e.size), e.y));
      if (t >= e.poseAt) {
        e.poseAt = t + 0.42;
        e.sway += 1;
        e.setPose(e.state === 'walk' && !calm() ? SWAY[e.sway % SWAY.length] : 'body-idle');
      }
      if (t >= e.faceAt && !e.pic) {
        const blink = Math.random() < 0.3;
        e.setFace(blink ? '-_-' : any(IDLE_FACES));
        e.faceAt = t + (blink ? 0.16 : rand(2.5, 6));
      }
      const flip = e.dir < 0;
      e.el.style.transform = `translate(${e.x.toFixed(1)}px,${(e.y - lift).toFixed(1)}px)${flip ? ' scaleX(-1)' : ''}`;
      const want = flip ? 'scaleX(-1)' : '';
      if (e.scr.style.transform !== want) e.scr.style.transform = want;   // the screen reads the right way round
      if (e === main && hat.style.transform !== want) hat.style.transform = want;   // and so does her hat
    }
    if (!credit.hidden) credit.style.transform = `translate(${(main.x + main.size / 2).toFixed(1)}px,${(main.y + main.size).toFixed(1)}px) translateX(-50%)`;
    for (let i = bubs.length - 1; i >= 0; i--) {
      const bu = bubs[i];
      if (t >= bu.until) { bubs.splice(i, 1); const el = bu.el; ctx.anim(el, [{ opacity: 1 }, { opacity: 0 }], { duration: 300, fill: 'both' }); ctx.after(320, () => el.remove()); continue; }
      placeBubble(bu, bu.e.x, bu.e.y, bu.e.size, b.w);
    }
  });
  ctx.every(10000, save);

  const api = {
    /** Where every EMI stands, in page pixels (the ending starts its fill there). */
    points: () => emis.map((e) => ({ x: e.x + e.size / 2, y: e.y + e.size / 2 })),
    /** Each EMI's size in px, in the same order as points(). */
    sizes: () => emis.map((e) => e.size),
    hide() { box.hidden = true; box.style.display = 'none'; shopDoor?.guest(null); guest = null; borrowDesk(); },
    count: () => emis.length,
    /** A sibling row just arrived: one line from its pool, soon. */
    arrive() { barkAt = Math.min(barkAt, t + 1.2); },
    state: () => ({
      stage: stage(), phase: phase(), budget: Math.floor(bag.budget), bought: [...bag.bought], count: emis.length, copies: emis.length - 1,
      bubbles: bubs.length, shopping: !!shopping, pattern: phase() === 'hive' ? pattern : null, gone: gone.size, tabs: tabs.size, buzzing: buzzing.size, sinceOver: Math.floor(bag.sinceOver),
      main: { x: main.x, y: main.y, size: main.size, state: main.state, jumping: main.jumpT >= 0, dir: main.dir, desk: !!desk?.borrowed, hat: hat.textContent }, pics: emis.filter((e) => e.pic).length,
    }),
    /** Dev drives and the play door only: hand her budget, her first `n` tiles, or time since Override. */
    dev: {
      budget(n) { bag.budget += Math.max(0, Number(n) || 0); },
      tiles(n) { bag.bought = T.HER_TILES.slice(0, Math.max(0, Math.min(T.HER_TILES.length, n))).map((x) => x.id); for (const e of emis) resize(e, sizeOf(e.main)); save(); },
      sinceOver(s) { bag.sinceOver = Math.min(T.HIVE.FORCE_S, Math.max(0, Number(s) || 0)); },
      crowd(n) { for (let i = 0; i < n && emis.length < T.repCap(LITE); i++) add(false); bag.copies = emis.length - 1; save(); },
    },
  };
  live = api;
  doc.defaultView.__emiSwarm = api;
  ctx.cleanup(() => {
    if (live === api) live = null;
    if (doc.defaultView.__emiSwarm === api) delete doc.defaultView.__emiSwarm;
    shopDoor?.guest(null);
    for (const el of gone) el.removeAttribute('data-et-gone');
    gone.clear();
    for (const el of buzzing) el.removeAttribute('data-et-buzz');
    buzzing.clear();
    for (const d of tabs) d.undisguise?.();   // every tab reads its own name again
    tabs.clear();
    io?.stop();
    for (const e of emis) sprites.delete(e);
    if (desk?.el.isConnected) desk.borrow(false);   // back where she stood, as she was
  });
  return {};
}

/** The swarm on the page, or null. The ending starts its fill from it and hides it. */
export const liveSwarm = () => live;

const sibling = { mount(ctx) { if (!ctx.restored) live?.arrive(); return {}; } };

export default {
  /* Transmission owns the swarm; the three rows after it only change what the swarm reads. */
  go_tx: { mount(ctx) { return mountSwarm(ctx); } },
  go_will: sibling,
  go_corp: sibling,
  go_hive: sibling,
};
