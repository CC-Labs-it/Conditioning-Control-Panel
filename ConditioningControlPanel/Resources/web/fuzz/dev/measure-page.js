/* dev/measure-page.js - injected by dev/measure.mjs before the page loads, never shipped to a player page. A stepped clock
 * (timers, rAF, Date, performance) and a scripted person who only presses what a person can press. */
(() => {
  const real = { setTimeout: window.setTimeout.bind(window), now: performance.now.bind(performance) };
  const base = Date.now();
  let vnow = 0, nextId = 1e6, seed = 7;
  const timers = new Map();
  let raf = [];
  window.__seed = (n) => { seed = n >>> 0 || 1; };
  /* Same run every time, whatever the machine's speed. */
  Math.random = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed / 2 ** 32; };
  performance.now = () => vnow;
  Date.now = () => base + vnow;
  window.setTimeout = (fn, ms = 0, ...a) => { const id = nextId++; timers.set(id, { at: vnow + Math.max(0, ms), fn, a }); return id; };
  window.clearTimeout = (id) => { timers.delete(id); };
  window.setInterval = (fn, ms = 0, ...a) => { const id = nextId++; timers.set(id, { at: vnow + Math.max(4, ms), every: Math.max(4, ms), fn, a }); return id; };
  window.clearInterval = window.clearTimeout;
  window.requestAnimationFrame = (fn) => { const id = nextId++; raf.push({ id, fn }); return id; };
  window.cancelAnimationFrame = (id) => { raf = raf.filter((r) => r.id !== id); };
  /* Web Animations run in wall time, so they run fast enough that nothing an unlock waits on lags the stepped clock. */
  const SPEEDUP = 25;
  const rateDesc = Object.getOwnPropertyDescriptor(Animation.prototype, 'playbackRate');
  Object.defineProperty(Animation.prototype, 'playbackRate', { get() { return rateDesc.get.call(this) / SPEEDUP; }, set(v) { rateDesc.set.call(this, v * SPEEDUP); } });
  const animate = Element.prototype.animate;
  Element.prototype.animate = function patched(k, o) { const a = animate.call(this, k, o); rateDesc.set.call(a, SPEEDUP); return a; };

  window.__vt = {
    advance(ms) {
      vnow += ms;
      for (const [id, t] of [...timers]) {
        if (t.at > vnow) continue;
        if (t.every) t.at = vnow + t.every; else timers.delete(id);
        try { t.fn(...t.a); } catch (e) { console.error(e); }
      }
      const q = raf; raf = [];
      for (const r of q) { try { r.fn(vnow); } catch (e) { console.error(e); } }
    },
  };

  const chan = new MessageChannel();
  let wake = null;
  chan.port1.onmessage = () => { const w = wake; wake = null; w?.(); };
  const breathe = () => new Promise((res) => { wake = res; chan.port2.postMessage(0); });

  const LAYER_SEL = ['l-over', 'l-floaters', 'l-side', 'l-banner', 'l-meters', 'l-ticker', 'l-wash', 'l-back'].map((id) => `#${id}`);
  const TAPPABLE = LAYER_SEL.flatMap((l) => [`${l} button`, `${l} [role=button]`, `${l} [style*="pointer-events: auto"]`]).join(',');

  function press(el) {
    const r = el.getBoundingClientRect();
    const o = { bubbles: true, cancelable: true, composed: true, clientX: r.left + r.width / 2, clientY: r.top + r.height / 2, button: 0, pointerId: 1, pointerType: 'mouse' };
    el.dispatchEvent(new PointerEvent('pointerdown', o));
    el.dispatchEvent(new MouseEvent('mousedown', o));
    el.dispatchEvent(new PointerEvent('pointerup', o));
    el.dispatchEvent(new MouseEvent('mouseup', o));
    el.dispatchEvent(new MouseEvent('click', o));
  }

  /* HUD controls persist after use. Work through their actual buttons repeatedly,
   * including opening a folded dock; a synthetic pointer cannot capture a pointer
   * that the browser never pressed, so use the supported keyboard click path. */
  function hudPress(el, t) {
    if (!el || el.disabled) return false;
    const owner = el.closest('[data-arcade]')?.dataset.arcade;
    const blocked = (reason) => { P.hudBlocked[owner] = { reason, at: Math.round(t), n: (P.hudBlocked[owner]?.n || 0) + 1 }; return false; };
    const dock = el.closest('.dk');
    if (dock?.classList.contains('auto') || dock?.classList.contains('min')) {
      dock.querySelector('.dk-t')?.click();
    }
    el.scrollIntoView?.({ block: 'nearest', inline: 'nearest' });
    const r = el.getBoundingClientRect();
    if (!r.width || !r.height || r.bottom <= 0 || r.top >= innerHeight) return blocked(`hidden: ${dock?.className}`);
    const x = Math.max(0, Math.min(innerWidth - 1, r.left + r.width / 2));
    const y = Math.max(0, Math.min(innerHeight - 1, r.top + r.height / 2));
    const hit = document.elementFromPoint(x, y);
    if (!hit || !el.contains(hit)) return blocked(`covered: ${hit?.id || hit?.className || hit?.tagName}`);
    el.click();
    P.pressed += 1;
    P.hudBusyUntil = t + .45;
    return true;
  }

  function playHud(t) {
    if (t < P.nextHud) return;
    P.nextHud = t + .5;
    const dispenser = document.querySelector('[data-arcade=dispenser]');
    const phase = dispenser?.dataset.phase;
    if (phase === 'dissolve' && P.lastDispenserPhase !== 'dissolve') P.hud.dispenserMisses++;
    if (phase === 'catch' && P.lastDispenserPhase !== 'catch') P.catchAt = t + .45;
    P.lastDispenserPhase = phase;
    if (phase === 'catch' && !P.skipCatch && t >= P.catchAt && hudPress(dispenser.querySelector('.prize-bubble'), t)) {
      P.hud.dispenserCatches++; return;
    }
    const slots = document.querySelector('[data-arcade=mini_slots] .mini-slot-spin');
    if (slots && !slots.disabled && hudPress(slots, t)) { P.hud.slotSpins++; return; }
    const race = document.querySelector('[data-arcade=mini_race] .mini-race-collect');
    if (race && !race.disabled && hudPress(race, t)) { P.hud.raceCollections++; return; }

    const toy = document.querySelector('[data-arcade=toy_box]');
    if (toy && t >= P.nextToy && !toy.querySelector('.tb-lid')) {
      const selected = toy.querySelector('.tb-piece.selected:not(:disabled)');
      if (selected) {
        const id = selected.dataset.toyPiece;
        const slot = [...toy.querySelectorAll('[data-toy-slot]')].find((e) => e.dataset.toySlot === id);
        if (hudPress(slot, t)) {
          P.hud.toyPieces++;
          if (!toy.querySelector('.tb-piece:not(:disabled)')) {
            P.hud.toyBoxes++; P.nextToy = Math.max(t, (P.toyStartedAt ?? t) + 30); P.toyStartedAt = null;
          }
          else P.nextHud = t + 1;
          return;
        }
      } else {
        const piece = toy.querySelector('.tb-piece:not(:disabled)');
        if (hudPress(piece, t)) { P.toyStartedAt ??= t; P.nextHud = t + 1; return; }
      }
    }
    if (phase === 'ready' && hudPress(dispenser.querySelector('.prize-crank'), t)) {
      P.hud.dispenserCranks++;
      P.skipCatch = P.hud.dispenserCranks % 5 === 0;
    }
  }

  let P = null;
  window.__play = {
    start({ picks, taps, frame, collect = 30, right = 0.6, panelOff = false }) {
      const f = window.fuzz;
      P = {
        picks, taps, frame, collect, right, panelOff, nextCollect: collect, lastSrc: {}, lumps: {}, sideLog: [], nextSide: 0, t: 0, tapDebt: 0, lastBuy: 0, nextBuy: 0, nextScan: 0, nextCard: 0, cardSeen: 0, cardEl: null, cardPress: 0,
        seen: new WeakMap(), gaps: [], forks: {}, endings: {}, pressed: 0, resting: 0,
        buys: [], fpsBase: 0, fpsReal: 0, clickBase: 0, clickReal: 0, nextSample: 0,
        nextHud: 0, nextToy: 0, toyStartedAt: null, hudBusyUntil: 0, catchAt: Infinity, lastDispenserPhase: null, skipCatch: false, hudBlocked: {},
        hud: { toyPieces: 0, toyBoxes: 0, slotSpins: 0, raceCollections: 0, dispenserCranks: 0, dispenserCatches: 0, dispenserMisses: 0 },
        ends: f.items.filter((i) => i.kind === 'end' && i.group === 'run'),
      };
      for (const e of P.ends) P.endings[e.id] = null;
    },

    async run(seconds) {
      const f = window.fuzz, e = f.engine, s = f.state, stop = P.t + seconds, dt = P.frame;
      const btn = document.getElementById('btn');
      let n = 0, rect = null;
      while (P.t < stop && !s.ended) {
        window.__vt.advance(dt);
        P.t += dt / 1000;
        const t = P.t;
        if (document.getElementById('pause') && !document.getElementById('pause').hidden) f.ui.resume();

        /* The picture-source addendum makes the world inert. Make its ordinary
         * no-files choice before attempting any controls behind it. */
        const sourceCard = document.querySelector('.tc [data-pick="none"]');
        if (sourceCard && sourceCard !== P.sourceCard) { P.sourceCard = sourceCard; P.sourceSeen = t; }
        /* A card blocks the page: read it for a moment, then answer. */
        const card = document.querySelector('#cards .backdrop .card');
        if (sourceCard) {
          if (t - P.sourceSeen > 2) { sourceCard.click(); P.pictureChoices = (P.pictureChoices || 0) + 1; }
        } else if (card) {
          if (P.cardEl !== card) { P.cardEl = card; P.cardSeen = t; P.cardPress = 0; }
          if (t - P.cardSeen > 1.2 + P.cardPress * 0.8) {
            const b = [...card.querySelectorAll('button')].filter((x) => !x.disabled);
            if (b.length) {
              const prim = P.cardPress > 2 ? b[b.length - 1] : b[Math.floor(Math.random() * b.length)];
              press(prim);
              P.cardPress += 1; P.pressed += 1;
            }
          }
        } else {
          /* A person on the Rest route rests: that is what the lane pays for. */
          const rest = P.picks.f2 === 'sweet' && s.owned.f2 && (t % 50) > 35;
          P.tapDebt += P.taps * (dt / 1000);
          while (P.tapDebt >= 1) {
            P.tapDebt -= 1;
            if (rest || t < P.hudBusyUntil) continue;
            if (n % 20 === 0 || !rect) rect = btn.getBoundingClientRect();
            btn.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true, cancelable: true, clientX: rect.left + rect.width / 2, clientY: rect.top + rect.height / 2, button: 0, pointerId: 1 }));
          }

          /* Levels: the one button that stays and keeps asking. Everything else below is pressed once per element. */
          if (P.collect > 0 && t >= P.nextCollect) {
            P.nextCollect = t + P.collect;
            const lv = document.querySelector('#l-meters .fz-lvl-ready');
            if (lv) { press(lv); P.pressed += 1; }
          }

          /* A question: a person reads it for two seconds, and gets P.right of them right. */
          const qc = document.querySelector('.xq:not(.xq-done)');
          if (qc && qc !== P.qCard) { P.qCard = qc; P.qAt = t; P.qGood = Math.random() < P.right; }
          if (qc && t - P.qAt > 2) {
            const as = [...qc.querySelectorAll('.xq-a')].filter((b) => !b.blocked && !b.disabled);
            const wanted = as.filter((b) => window.__yes?.has(b.textContent.trim()));
            const other = as.filter((b) => !wanted.includes(b));
            const b = (P.qGood ? wanted[0] : other[0]) || wanted[0] || other[0];
            if (b) { P.qAsked = (P.qAsked || 0) + 1; if (wanted.includes(b)) P.qRight = (P.qRight || 0) + 1; press(b); P.pressed += 1; P.qAt = t + 0.5; }
          }

          playHud(t);

          /* Anything tappable that drifts by: press it after a human moment. */
          if (t >= P.nextScan) {
            P.nextScan = t + 0.5;
            for (const el of document.querySelectorAll(TAPPABLE)) {
              // Guide and settings are navigation, not repeatable prize controls.
              if (el.closest('.dk.door')) continue;
              if (el.closest('[data-arcade]') || (el.matches('.dk-t') && el.closest('.dk')?.querySelector('[data-arcade]'))) continue;
              const first = P.seen.get(el);
              if (first === undefined) { P.seen.set(el, t); continue; }
              if (first < 0 || t - first < 0.9 || el.classList.contains('xq-a')) continue;
              const r = el.getBoundingClientRect();
              if (r.width < 4 || r.height < 4 || r.right < 0 || r.bottom < 0 || r.left > innerWidth || r.top > innerHeight) continue;
              P.seen.set(el, -1);
              press(el);
              P.pressed += 1;
              break;
            }
          }

          /* The next thing it can afford, cheapest first; a fork when its price is reached. */
          if (t >= P.nextBuy) {
            P.nextBuy = t + 0.25;
            const win = e.shop(8);
            const rows = [...win.rows, win.goal].filter(Boolean).map((it) => ({ it, c: e.price(it.id) })).sort((a, b) => a.c - b.c);
            for (const { it, c } of rows) {
              if (it.kind === 'end' && it.id !== 'drop') continue;
              if (c > s.fuzz) break;
              const res = it.kind === 'fork' ? f.pick(it.id, P.picks[it.id]) : f.buy(it.id);
              if (res.ok) {
                P.gaps.push({ gap: t - P.lastBuy, at: t, next: it.id });
                P.lastBuy = t;
                if (it.kind === 'fork') P.forks[it.id] = t;
                break;
              }
            }
          }
        }

        /* Every lump as it lands, in seconds of what the player earns right now: is a press still worth it? */
        {
          const r = e.rates(), rate = Math.max(1, r.fps + r.click * P.taps);
          for (const [k, v] of Object.entries(s.sources)) {
            const was = P.lastSrc[k] || 0;
            if (v === was) continue;
            P.lastSrc[k] = v;
            if (k === 'tap' || k === 'wall' || k === 'fps') continue;
            const l = P.lumps[k] ||= { n: 0, got: 0, secs: 0, lateN: 0, lateSecs: 0, best: 0 };
            const secs = (v - was) / rate;
            l.n += 1; l.got += v - was; l.secs += secs; l.best = Math.max(l.best, secs);
            if (s.total > 2e5) { l.lateN += 1; l.lateSecs += secs; }
          }
          if (t >= P.nextSide) {
            P.nextSide = t + 120;
            P.sideLog.push({ min: Math.round(t / 60), budget: Math.round(s.side.budget), asked: Math.round(s.side.asked), paid: Math.round(s.side.paid), fps: Math.round(r.fps), click: Math.round(r.click) });
          }
        }

        /* Control Panel: the worst tidy-up there is. The moment it is owned, everything goes off. */
        if (P.panelOff && !P.panelDone && s.owned.panel && f.engine.switches) {
          P.panelDone = t;
          for (const g of f.engine.switches.groups()) for (const row of g.rows) f.engine.switches.set(row.id, false);
        }

        /* When each ending could first have been bought. */
        for (const it of P.ends) if (P.endings[it.id] == null && s.owned[it.req[0]] && s.fuzz >= e.price(it.id)) P.endings[it.id] = t;

        if (t >= P.nextSample) {
          P.nextSample = t + 1;
          const r = e.rates();
          P.fpsBase += s.base.fps; P.fpsReal += r.fps;
          P.clickBase += s.base.click * P.taps; P.clickReal += r.click * P.taps;
        }
        if (++n % 8 === 0) await breathe();
      }
      return { t: P.t, done: !!s.ended };
    },

    report() {
      const f = window.fuzz, s = f.state, e = f.engine, r = e.rates();
      const total = s.total || 1;
      const share = {};
      for (const [k, v] of Object.entries(s.sources)) share[k] = Math.round((v / total) * 1000) / 10;
      const planned = ['tap', 'wall', 'fps'];
      const lumps = Object.entries(s.sources).filter(([k]) => !planned.includes(k)).reduce((a, [, v]) => a + v, 0);
      const longest = P.gaps.reduce((a, g) => (g.gap > a.gap ? g : a), { gap: 0, at: 0, next: '' });
      const fpsExtra = Math.max(0, P.fpsReal - P.fpsBase), clickExtra = Math.max(0, P.clickReal - P.clickBase);
      return {
        t: P.t, ended: s.ended?.id || null, buys: s.buys, total: s.total, spent: s.spent, fps: r.fps, click: r.click,
        fpsBoost: Math.round((r.fps / Math.max(1, s.base.fps)) * 100) / 100, clickBoost: Math.round((r.click / Math.max(1, s.base.click)) * 100) / 100,
        share, lumpShare: Math.round((lumps / total) * 1000) / 10,
        modShare: Math.round(((fpsExtra + clickExtra) / Math.max(1, P.fpsReal + P.clickReal + (s.sources.wall || 0) / Math.max(1, P.t))) * 1000) / 10,
        longest: { gap: Math.round(longest.gap), at: longest.at, next: longest.next },
        over60: P.gaps.filter((g) => g.gap > 60).length,
        meanGap: Math.round((P.t / Math.max(1, P.gaps.length)) * 10) / 10,
        forks: P.forks, endings: P.endings, pressed: P.pressed, hud: { ...P.hud }, hudBlocked: { ...P.hudBlocked }, pictureChoices: P.pictureChoices || 0,
        pod: P.gaps.find((g) => g.next.endsWith('_pod'))?.at ?? null,
        at: Object.fromEntries(P.gaps.map((g) => [g.next, Math.round(g.at)])),
        qAsked: P.qAsked || 0, qRight: P.qRight || 0, lumps: P.lumps, sideLog: P.sideLog, side: { ...s.side }, panelDone: P.panelDone || null,
      };
    },
  };
})();
