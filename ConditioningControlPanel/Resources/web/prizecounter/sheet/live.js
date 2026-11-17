/* sheet/live.js - every moving picture on the sheet: the effect thumbnails on the shelves (the bubble skins alive, the
 * card frames, fog and word styles moving), the spotlight stage, the "Also new" chips, and the fitting room's "Your
 * screen", a small copy of the desktop that wears the picked look (port of the mockup's frame()). One clock for all of
 * them, at most 30 frames a second, only for canvases on screen. Motion off: one still frame per paint. Less: half speed.
 * installLive(view, opts) fills view.thumb / mini / stage / monitor / idleRoom / roomExtra; canvases are kept per item so a
 * repaint never restarts an animation. A click on a bubble in "Your screen" pops it with its own sound (opts.onPop). */

import { makeBubble, stepBubble, drawBubble, popBubble, stepParts, drawParts, loadShells } from './skins.js';
import { drawBack, drawFront, drawFrame, drawCard, drawNoFrame, drawWord, drawSpiralBack, BACK_LIFE, FOGS, WORDS, hueOf, secondFog } from './looks.js';
import { labelOf, locate, nameOf } from './model.js';
import { itemArt } from './pictures.js';
import { makeFogMix } from './fogmix.js';
import { FX_SLOTS } from './wire.js';

const kindOf = (id) => String(id || '').split('.').pop();
const DEFAULTS = { bubble: 'soap', card: 'plain', behind: 'none', fog: 'pink', word: 'classic' };

export function installLive(view, { t, base = './', motion = 'full', gentle = false, raf = globalThis.requestAnimationFrame, doc, win = globalThis.window, onPop = () => {} }) {
  const { el } = view;
  const speed = motion === 'less' ? 0.5 : 1;
  const canDraw = (cv) => cv && typeof cv.getContext === 'function';
  const cache = new Map(); let used = new Set(), live = [], running = false, last = 0, clock = 0, v0 = null, popTurn = 0, popAt = 0;
  loadShells(`${base}sheet/art/soap.png`, doc, () => kick());

  /** A canvas kept per key across paints (so its bubbles and particles live on), with its own little world. */
  function keep(key, make) {
    let k = key, n = 2;
    while (used.has(k)) k = `${key}#${n++}`;
    used.add(k);
    if (!cache.has(k)) { const cv = el('canvas'); cv.setAttribute('aria-hidden', 'true'); cache.set(k, make(cv)); }
    const item = cache.get(k);
    live.push(item);
    return item;
  }
  function fit(item) {
    const cv = item.cv, r = cv.getBoundingClientRect ? cv.getBoundingClientRect() : null;
    if (!r || !r.width || !r.height) return false;
    if (win && (r.bottom < 0 || r.top > (win.innerHeight || 1e5))) return false;
    const d = Math.min(2, (win && win.devicePixelRatio) || 1), W = Math.round(r.width * d), H = Math.round(r.height * d);
    if (cv.width !== W || cv.height !== H) { cv.width = W; cv.height = H; }
    item.w = r.width; item.h = r.height;
    item.c.setTransform(d, 0, 0, d, 0, 0);
    return true;
  }

  /* ---------- what is worn, with the picked look tried on ---------- */
  function looks(v) {
    const eq = (v.pv && v.pv.equip) || {}, out = {};
    for (const s of FX_SLOTS) out[s] = kindOf(eq[s]) || DEFAULTS[s];
    out.mix = !!eq.mix; out.fog2 = kindOf(eq.fog2) || 'lilac';
    const e = v.pick && locate(v.state, v.pick, v.pv);
    if (e && e.fx) { if (e.x.special) out.mix = true; else out[e.fx] = kindOf(e.id); }
    if (v.fog2Try) out.fog2 = kindOf(v.fog2Try);
    // Colour one is the user's own filter colour (init.fogColour, handed to the view as v.fogColour by sheet/main.js);
    // a host that sends none gets pink. A second colour too close to it steps on to the next.
    out.own = /^#[0-9a-f]{6}$/i.test(v.fogColour || '') ? v.fogColour : FOGS.pink;
    const second = secondFog(out.fog2, out.own);
    out.fog2Picked = out.fog2; out.fog2 = second.kind; out.fog2Skipped = second.skipped;
    return out;
  }

  /** Two-tone on a shelf: the user's own colour and the second colour they would wear. */
  const fogPair = () => { const L = looks(v0 || {}); return [L.own, FOGS[L.fog2]]; };
  /** Two-tone as the desktop draws it (sheet/fogmix.js): the user's colour with the second as clean drifting regions.
   * Motion off holds one moment. Each canvas keeps its own small grid. */
  const fogMix = (world, g, tt, w, h, alpha) => { const [a, b] = fogPair(); (world.mix = world.mix || makeFogMix(doc))(g, a, b || FOGS.lilac, motion === 'off' ? 12 : tt, w, h, alpha); };

  /* ---------- one item's little live picture ---------- */
  function painter(e) {
    const slot = e.fx, kind = kindOf(e.id), todo = e.x.todo;
    if (slot === 'bubble') {
      return (item, tt, dt) => {
        const w = item.world, r = Math.min(item.w, item.h) * 0.31;
        if (!w.b) { w.wait -= dt; if (w.wait <= 0) { w.b = makeBubble(kind, item.w / 2, item.h / 2, r, { doc, colour: todo, gentle }); if (!w.seen) { w.seen = true; w.b.grow = 1; w.b.age = 3; } } }
        const g = item.c; g.clearRect(0, 0, item.w, item.h);
        if (w.b) { const b = w.b; b.r = r; b.x = item.w / 2; b.y = item.h / 2 + Math.sin(tt * 1.2 + b.seed) * 2; stepBubble(w, b, dt); if (b.state === 'dead') { w.b = null; w.wait = 0.9; } else drawBubble(w, b, tt); }
        stepParts(w, dt); drawParts(w);
      };
    }
    return (item, tt) => {
      const g = item.c, w = item.w, h = item.h; g.clearRect(0, 0, w, h);
      if (slot === 'card') { const cw = h * 0.68, ch = h * 0.88; g.save(); g.translate(w / 2, h / 2); g.rotate(motion === 'off' ? -0.05 : Math.sin(tt * 0.9 + hueOf(e.id)) * 0.08); drawCard(g, kind, -cw / 2, -ch / 2, cw, ch, 200); if (kind === 'plain') drawNoFrame(g, -cw / 2, -ch / 2, cw, ch); g.restore(); }   // the same picture on every thumb: only the frame differs
      else if (slot === 'behind') {
        const cw = h * 0.42, ch = h * 0.72, y = (h - ch) / 2;
        // Whisper: the card's spiral back with a word coming at you (one per 2.6 s); Nothing: the plain card
        if (kind === 'whisper') drawSpiralBack(g, WORDS[Math.floor(tt / 2.6) % WORDS.length], motion === 'off' ? 1.2 : tt % 2.6, (w - cw) / 2, y, cw, ch, motion === 'off' ? 0 : tt * 0.9, 'plain', motion === 'off');
        else drawCard(g, 'plain', (w - cw) / 2, y, cw, ch, 200);
      } else if (slot === 'fog') fogMix(item.world, g, tt, w, h, 0.7);
      else if (slot === 'word') drawWord(g, kind, 'sink', w / 2, h * 0.52, h * 0.33, motion === 'off' ? 2 : tt % 4, 1);
    };
  }
  const newThumb = (e) => (cv) => ({ cv, c: cv.getContext ? cv.getContext('2d') : null, draw: painter(e), world: { c: null, p: [], b: null, wait: 0, cap: 260 }, e });
  function thumbCanvas(prefix, e, cls) {
    const item = keep(`${prefix}:${e.id}`, newThumb(e));
    if (item.world) item.world.c = item.c;
    item.cv.className = cls || '';
    return item.cv;
  }

  /* ---------- the spotlight stage: the headline item in a beam of light ---------- */
  const still = motion === 'off' || gentle;   // the stage's own movement (bob, sweep); Gentle keeps the slow twinkle fades
  /** A few small four-point sparkles fading in and out slowly at fixed spots (a 7 s cycle each, never a flicker). */
  function twinkles(k, w, h, tt) {
    const spots = [[0.12, 0.18, 4.5, 0], [0.84, 0.14, 3.5, 2.3], [0.9, 0.62, 4, 4.1], [0.2, 0.7, 3, 5.6], [0.66, 0.08, 3, 1.2]];
    k.save(); k.fillStyle = '#fff6dc';
    for (const [x, y, r, ph] of spots) {
      const a = motion === 'off' ? (ph < 3 ? 0.6 : 0) : Math.pow(Math.max(0, Math.sin(tt * 0.9 + ph)), 3) * 0.85;
      if (a < 0.02) continue;
      const px = x * w, py = y * h, s = r * (0.7 + a * 0.4);
      k.globalAlpha = a; k.beginPath();
      k.moveTo(px, py - s * 2); k.quadraticCurveTo(px, py, px + s * 2, py); k.quadraticCurveTo(px, py, px, py + s * 2); k.quadraticCurveTo(px, py, px - s * 2, py); k.quadraticCurveTo(px, py, px, py - s * 2);
      k.fill();
    }
    k.restore();
  }
  function heroPainter(e) {
    const slot = e.fx, kind = kindOf(e.id);
    return (item, tt, dt) => {
      const k = item.c, w = item.w, h = item.h, cx = w / 2, floor = h * 0.87, bob = still ? 0 : Math.sin(tt * 1.6) * h * 0.028;
      k.clearRect(0, 0, w, h);
      // the lamp sweeps slowly across the stage and its pool follows (Gentle and off: it stands still)
      const sweep = still ? 0 : Math.sin(tt * 0.32) * w * 0.07, pool = cx + sweep * 0.8;
      k.fillStyle = 'rgba(255,240,214,.13)'; k.beginPath(); k.moveTo(cx - w * 0.05, 0); k.lineTo(cx + w * 0.05, 0); k.lineTo(pool + w * 0.34, floor); k.lineTo(pool - w * 0.34, floor); k.closePath(); k.fill();
      k.fillStyle = 'rgba(255,240,214,.22)'; k.beginPath(); k.ellipse(pool, floor, w * 0.34, h * 0.065, 0, 0, Math.PI * 2); k.fill();
      twinkles(k, w, h, tt);
      k.fillStyle = 'rgba(0,0,0,.5)'; k.beginPath(); k.ellipse(cx, floor, h * 0.2 * (1 - bob / h * 3), h * 0.028, 0, 0, Math.PI * 2); k.fill();
      if (slot === 'bubble') {
        const W = item.world, R = Math.min(w * 0.5, h) * 0.31, spots = [[cx - w * 0.27, h * 0.3, 0.36, 1.5], [cx + w * 0.28, h * 0.56, 0.5, -0.9], [cx, h * 0.44, 1, -1]];
        W.bs = W.bs || [null, null, null]; W.wait = W.wait || [0, 0, 0];
        spots.forEach(([x, y, s, bb], i) => {
          if (!W.bs[i]) { W.wait[i] -= dt; if (W.wait[i] <= 0) { W.bs[i] = makeBubble(kind, x, y, R * s, { doc, colour: e.x.todo, gentle }); if (!W.seen) W.bs[i].grow = 1; } }
          const b = W.bs[i]; if (!b) return;
          b.x = x; b.y = y + bob * bb; b.r = R * s; stepBubble(W, b, dt);
          if (b.state === 'dead') { W.bs[i] = null; W.wait[i] = 0.8; } else drawBubble(W, b, tt);
        });
        W.seen = true;
        W.next = (W.next || 3) - dt;
        if (motion !== 'off' && W.next <= 0) { W.next = 3.2; const b = W.bs[W.turn = ((W.turn || 0) + 1) % 3]; if (b) popBubble(b); }
        stepParts(W, dt); drawParts(W);
      } else if (slot === 'card') { k.save(); k.translate(cx, h * 0.44 + bob); k.rotate(motion === 'off' ? -0.05 : Math.sin(tt * 0.9) * 0.09); drawCard(k, kind, -h * 0.22, -h * 0.3, h * 0.44, h * 0.6, 200); if (kind === 'plain') drawNoFrame(k, -h * 0.22, -h * 0.3, h * 0.44, h * 0.6); k.restore(); }
      else if (slot === 'behind') {
        const cw = h * 0.38, ch = h * 0.56, x = cx - cw / 2, y = h * 0.16 + bob * 0.5, age = motion === 'off' ? 1.2 : tt % 2.6, word = WORDS[Math.floor(tt / 2.6) % WORDS.length];
        if (kind === 'whisper') drawSpiralBack(k, word, age, x, y, cw, ch, still ? 0 : tt * (gentle ? 0.35 : 0.9), 'plain', motion === 'off');
        else drawCard(k, 'plain', x, y, cw, ch, 200);
      } else if (slot === 'fog') fogMix(item.world, k, tt, w, h, 0.6);
      else if (slot === 'word') drawWord(k, kind, WORDS[Math.floor(tt / 4) % WORDS.length], cx, h * 0.45, h * 0.2, motion === 'off' ? 2 : tt % 4, 1);
    };
  }

  /* ---------- "Your screen": a small desktop wearing the looks ---------- */
  const screen = { cv: el('canvas'), fog: null, bubbles: [], kind: null, mouse: { x: -9, y: -9 }, cards: [{ x: 0.1, y: 0.2, hue: 320, off: 0, cyc: -1, w: null }, { x: 0.62, y: 0.38, hue: 190, off: 2.6, cyc: -1, w: null }], wi: 0 };
  screen.cv.className = 'scr';
  screen.cv.setAttribute('role', 'img');
  screen.cv.setAttribute('aria-label', t('shop_sheet_screen_label'));
  screen.item = { cv: screen.cv, c: screen.cv.getContext ? screen.cv.getContext('2d') : null, world: { c: null, p: [], cap: 500 }, draw: (item, tt, dt) => drawScreen(item, tt, dt) };
  screen.item.world.c = screen.item.c;
  function drawScreen(item, tt, dt) {
    const g = item.c, W = item.w, H = item.h, L = looks(v0 || {}), still = motion === 'off';
    if (screen.kind !== L.bubble) {   // a new look: the bubbles on screen take it at once
      screen.kind = L.bubble; screen.bubbles = [];
      for (let i = 0; i < 7; i++) { const b = makeBubble(L.bubble, Math.random() * W, Math.random() * H, (0.05 + Math.random() * 0.035) * W, { doc, gentle }); b.grow = 1; b.fx = b.x / W; b.fy = b.y / H; b.fr = b.r / W; screen.bubbles.push(b); }
    }
    const bg = g.createLinearGradient(0, 0, W, H); bg.addColorStop(0, '#1d1826'); bg.addColorStop(1, '#0f0c14'); g.fillStyle = bg; g.fillRect(0, 0, W, H);
    g.fillStyle = '#ffffff0d'; g.fillRect(W * 0.06, H * 0.14, W * 0.34, H * 0.5); g.fillRect(W * 0.52, H * 0.3, W * 0.4, H * 0.48);
    g.fillStyle = '#ffffff14'; g.fillRect(0, H - H * 0.07, W, H * 0.07);
    const cw = W * 0.2, ch = H * 0.36;
    for (const c of screen.cards) {
      const kk = (tt + c.off) % 5.2, cyc = Math.floor((tt + c.off) / 5.2);
      let sx = 1;
      if (kk < 0.5) sx = Math.cos(kk / 0.5 * Math.PI); else if (kk < 2.2) sx = -1; else if (kk < 2.7) sx = -Math.cos((kk - 2.2) / 0.5 * Math.PI);
      if (still) sx = c.off ? -1 : 1;
      if (cyc !== c.cyc) { c.cyc = cyc; c.born = tt; c.word = WORDS[screen.wi++ % WORDS.length]; }
      const whisper = L.behind === 'whisper', age = still ? 1.2 : (tt + c.off) % 5.2 - 0.25;
      const w = Math.max(2, Math.abs(sx) * cw), x = c.x * W + (cw - w) / 2;
      // The back: with words behind the card worn, a spiral with the word coming at you; else the next picture
      if (sx < 0) { if (whisper) drawSpiralBack(g, c.word || WORDS[0], age, x, c.y * H, w, ch, still ? 0 : tt * 0.9, L.card, still); else drawCard(g, L.card, x, c.y * H, w, ch, (c.hue + 140) % 360); }
      else drawCard(g, L.card, x, c.y * H, w, ch, c.hue);   // the frame stays on through the flip
    }
    if (!screen.fog) screen.fog = doc.createElement('canvas');
    const fog = screen.fog, d = item.cv.width / W;
    if (fog.width !== item.cv.width || fog.height !== item.cv.height) { fog.width = item.cv.width; fog.height = item.cv.height; }
    const fg = fog.getContext('2d');
    fg.setTransform(d, 0, 0, d, 0, 0); fg.globalCompositeOperation = 'source-over'; fg.clearRect(0, 0, W, H);
    // the flat tint in the user's colour; Two-tone moves the second colour through it at the same strength
    if (L.mix) fogMix(screen.world || (screen.world = {}), fg, still ? 3 : tt, W, H, 0.32);
    else { fg.globalAlpha = 0.32; fg.fillStyle = L.own; fg.fillRect(0, 0, W, H); fg.globalAlpha = 1; }
    const m = screen.mouse;
    if (m.x > -1) { fg.globalCompositeOperation = 'destination-out'; const rg = fg.createRadialGradient(m.x, m.y, 0, m.x, m.y, W * 0.2); rg.addColorStop(0, 'rgba(0,0,0,1)'); rg.addColorStop(1, 'rgba(0,0,0,0)'); fg.fillStyle = rg; fg.fillRect(0, 0, W, H); }
    g.drawImage(fog, 0, 0, W, H);
    const wd = item.world;
    for (const b of screen.bubbles) {
      if (!still && b.state === 'alive') { b.fy -= (0.02 + (b.seed % 7) * 0.004) * dt * b.skin.rise; if (b.fy < -0.16) { b.fy = 1.14; b.fx = Math.random(); } }
      b.x = (b.fx + Math.sin(tt * 0.7 + b.seed) * 0.012) * W; b.y = b.fy * H; b.r = b.fr * W;
      stepBubble(wd, b, dt);
      if (b.state === 'dead') { const n = makeBubble(L.bubble, 0, 0, b.r, { doc, gentle }); Object.assign(b, n, { fx: Math.random(), fy: 1.1 + Math.random() * 0.2, fr: b.fr }); }
      drawBubble(wd, b, tt);
    }
    stepParts(wd, dt); drawParts(wd);
    drawWord(g, L.word, WORDS[Math.floor(tt / 4) % WORDS.length], W * 0.5, H * 0.86, Math.max(13, W * 0.06), still ? 2 : tt % 4);
  }
  const at = (e) => { const r = screen.cv.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; };
  if (screen.cv.addEventListener) {
    screen.cv.addEventListener('pointermove', (e) => { Object.assign(screen.mouse, at(e)); kick(); });
    screen.cv.addEventListener('pointerleave', () => { screen.mouse.x = -9; screen.mouse.y = -9; kick(); });
    screen.cv.addEventListener('pointerdown', (e) => {
      const p = at(e);
      for (const b of screen.bubbles) if (Math.hypot(b.x - p.x, b.y - p.y) < b.r * 1.12 + 6 && popBubble(b, onPop)) { kick(); return; }
    });
  }
  function screenBox() {
    used.add('screen');
    live.push(screen.item);
    const p = el('div', 'prev screen');
    p.append(screen.cv);
    return p;
  }

  /* ---------- the hooks the view calls ---------- */
  const art = view.thumb;
  view.thumb = (e, v) => (e.fx ? thumbCanvas('t', e) : art(e, v));
  view.mini = (e, v) => { if (!e.fx) { const s = el('span', 'mini-art'); s.append(art(e, v)); return s; } return thumbCanvas('m', e, 'mini-cv'); };
  view.stage = (e, v) => {
    const s = el('div', 'stage'), item = keep(`s:${e.id}`, (cv) => ({ cv, c: cv.getContext ? cv.getContext('2d') : null, draw: heroPainter(e), world: { c: null, p: [], cap: 500 } }));
    item.world.c = item.c;
    item.cv.setAttribute('role', 'img'); item.cv.setAttribute('aria-label', t('shop_sheet_spotlit', labelOf(e, t)));
    s.append(item.cv);
    if (!e.fx) { const a = view.artNode(itemArt(e.id, v.index, e.tab, base), true); a.classList.add('lit'); s.append(a); }
    return s;
  };
  const monitor = view.monitor;
  view.monitor = (e, v) => (e && e.fx ? screenBox() : monitor(e, v));
  const idle = view.idleRoom;
  view.idleRoom = (v) => {
    if (!v.pv || !v.pv.equip || v.status !== 'ready') return idle(v);
    const mon = el('div', 'mon'), foot = el('div', 'mon-foot');
    foot.append(el('span', null, t(v.platform === 'desktop' ? 'shop_sheet_your_screen' : 'shop_sheet_your_desktop')));
    mon.append(screenBox(), foot);
    const name = el('h2', 'd-name', t('shop_sheet_wearing_now'));
    name.tabIndex = -1;
    const dl = el('dl', 'wearing');
    const row = (k, val) => { const r = el('div'); r.append(el('dt', null, k), el('i'), el('dd', null, val)); dl.append(r); };
    for (const s of FX_SLOTS) { const e = locate(v.state, v.pv.equip[s], v.pv); if (e && !e.id.endsWith('.none')) row(t('shop_sheet_sec_' + s), nameOf(e, t)); }
    if (v.pv.equip.mix) row(t('shop_sheet_fog_mix'), t('shop_item_fx_fog_twotone_name'));
    return [mon, name, el('p', 'd-where', t(v.platform === 'desktop' ? 'shop_sheet_wearing_pick' : 'shop_sheet_wearing_pick_away')), dl];
  };

  /** Two-tone fog: the second colour, as round swatches right under "What it does" (above the pinned actions, so the
   * room shows them at every size). Owned: "Second colour" and the pick named; before buying: "Try a second colour". */
  view.roomExtra = (e, v, on) => {
    const eq = (v.pv && v.pv.equip) || {}, tw = v.state && locate(v.state, 'fx.fog.twotone', v.pv);
    if (!e || e.fx !== 'fog' || !(e.x.special || (tw && tw.row.owned && eq.mix))) return [];
    // Colour one is the user's own filter colour; the second is free, from the palette. Every swatch is offered; one
    // too close to the user's own colour hands over to the next, and one short line says so.
    const L = looks(v), box = el('div', 'mix'), name = (k) => t('shop_item_fx_fog_' + k + '_name');
    const owned = !!(tw && tw.row.owned), head = el('p', 'mix-head');
    box.dataset.fog2 = owned ? 'owned' : 'try';
    if (owned) head.append(el('span', null, t('shop_sheet_second_colour')), el('b', null, name(L.fog2Picked)));
    else head.append(el('span', null, t('shop_sheet_fog2_try')));
    box.append(head);
    for (const k of Object.keys(FOGS)) {
      const b = view.button('', null, 'fog2:' + k, () => on.fog2('fx.fog.' + k));
      b.setAttribute('style', 'background:' + FOGS[k]); b.setAttribute('aria-label', name(k)); b.setAttribute('aria-pressed', String(L.fog2Picked === k));
      box.append(b);
    }
    return L.fog2Skipped ? [box, el('p', 'd-where fog2-skip', t('shop_sheet_fog2_close', name(L.fog2Picked), name(L.fog2)))] : [box];
  };

  /* ---------- the clock ---------- */
  function frame(now) {
    running = false;
    const dt = last ? Math.min(0.05, (now - last) / 1000) : 1 / 30;
    if (last && now - last < 30) { schedule(); return; }   // at most about 30 frames a second
    last = now; clock += dt * speed;
    let any = false;
    for (const item of live) {
      if (!item.c || !item.cv.isConnected || !fit(item)) continue;
      any = true;
      item.draw(item, clock, motion === 'off' ? 0 : dt * speed);
    }
    // the shelf plays itself: one bubble thumbnail pops in turn, silently, every 2.6 s
    if (motion !== 'off' && clock - popAt > 2.6) {
      popAt = clock;
      const shelf = live.filter((i) => i.world && i.world.b && i.e && i.e.fx === 'bubble' && i.cv.isConnected && i.w);
      if (shelf.length) popBubble(shelf[popTurn++ % shelf.length].world.b);
    }
    if (motion !== 'off' && any) schedule();
  }
  function schedule() { if (!running && typeof raf === 'function') { running = true; raf(frame); } }
  function kick() { if (motion === 'off') { last = 0; } schedule(); }

  const paint = view.paint;
  view.paint = (v, on) => {
    v0 = v; used = new Set(); live = [];
    paint(v, on);
    for (const k of [...cache.keys()]) if (!used.has(k)) cache.delete(k);
    kick();
  };
  view.live = { looks, screen, kick, count: () => live.length };
  return view.live;
}
export { DEFAULTS, kindOf };
