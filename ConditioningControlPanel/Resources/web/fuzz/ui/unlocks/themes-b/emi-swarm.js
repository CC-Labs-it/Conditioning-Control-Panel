/* ui/unlocks/themes-b/emi-swarm.js - EMI Takeover run-up: the goon chain rows before go_end and the swarm they share.
 * Numbers and rules: sim/takeover.js. Shared sprite parts: emi-kit.js. The ending reads `liveSwarm()`. */

import { EMI_BARKS } from '../../../data/content/emi-takeover.js';
import { LITE, frameRect } from '../../perf.js';
import { shopDoor } from '../../shop.js';
import * as T from '../../../sim/takeover.js';
import { art, SWAY, IDLE_FACES, PINK, rand, any, CSS, pose, faces, sprites, bakeFaces, sprite, controlsRect, morePictures, tilesOf, bubble, placeBubble } from './emi-kit.js';

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

/** The swarm on the page, or null. The ending starts its fill from it and hides it. */
export const liveSwarm = () => live;

const sibling = { mount(ctx) { if (!ctx.restored) live?.arrive(); return {}; } };

export default {
  /* Give EMI Autonomy owns the swarm; the three rows after it only change what the swarm reads. */
  go_auto: { mount(ctx) { return mountSwarm(ctx); } },
  go_will: sibling,
  go_copy: sibling,
  go_hive: sibling,
};
