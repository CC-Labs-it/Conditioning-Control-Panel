/* ui/unlocks/prestige/ghost.js - Renewal Sky lane `ghost` (brief: handoff-1109/TC-PRESTIGE-WIRE-BRIEF.md). Ghost Run.
 *
 * Always mounted (`always: true`), the first run included: it records the run (sim/ghost.js) into the run store as it
 * plays and, when the run ends, keeps the packed run in the lane's life store (ctx.pstore: last + fastest, a few KB).
 * With ghost stars it races that run: a see-through paper chip under the counter ("1:20 ahead of your ghost"), a ghost
 * marker on the guide map (hook in ui/guide-map.js, event `guide-paint`), chapter pay for beating the ghost to a chapter,
 * Ghost Hands taps (auto, through ctx.button.press), Hints (what the ghost bought next), Best Self, and Replay (the
 * ghost's Golden Buttons come round again: event `ghost-gold`, which the Golden Button answers).
 * Game clock only; the race reads state.clock (play time, never ctx.speed). Gentle: nothing moves, the chip only fades. */

import * as G from '../../../sim/ghost.js';
import { journeyChapter } from '../../../sim/journey-effects.js';

const CSS = `
.gh-chip { order: 6; display: inline-flex; align-items: center; gap: 6px; margin-top: 4px; padding: 3px 10px 3px 6px;
  font: 700 13px/1.2 var(--bubble, "Trebuchet MS", system-ui, sans-serif); color: #2a2031; white-space: nowrap;
  background: rgba(251, 246, 238, .72); border: 2px solid rgba(42, 32, 49, .55); border-radius: 12px 14px 11px 15px;
  box-shadow: 2px 3px 0 rgba(42, 28, 36, .22); font-variant-numeric: tabular-nums; text-shadow: none; }
.gh-chip svg { width: 18px; height: 20px; flex: none; }
.gh-chip.ahead b { color: #1d7f78; } .gh-chip.behind b { color: #b4364c; }
.gh-mark { position: absolute; z-index: 5; width: 44px; height: 50px; margin: -78px 0 0 -58px; pointer-events: none; opacity: .62;
  filter: drop-shadow(2px 3px 0 rgba(42, 28, 36, .25)); }
.gh-mark svg { width: 100%; height: 100%; }
.gh-mark i { position: absolute; left: 50%; top: 100%; transform: translateX(-50%); font: 700 11px/1 var(--bubble, "Trebuchet MS", sans-serif);
  font-style: normal; color: #2a2031; background: rgba(251, 246, 238, .8); border: 1.5px solid rgba(42, 32, 49, .5); border-radius: 8px; padding: 2px 5px; white-space: nowrap; }
.gh-hint { position: absolute; z-index: 5; margin: 30px 0 0 -40px; pointer-events: none; padding: 3px 7px; white-space: nowrap;
  font: 700 12px/1 var(--bubble, "Trebuchet MS", sans-serif); color: #2a2031; background: rgba(251, 246, 238, .78);
  border: 2px dashed rgba(42, 32, 49, .55); border-radius: 9px; box-shadow: 2px 3px 0 rgba(42, 28, 36, .2); opacity: .85; }
`;

/* A crayon paper ghost: wobbly sheet, two eyes. One lamp top-left: the shadow sits on the still wrapper. */
const GHOST_SVG = '<svg viewBox="0 0 40 46" aria-hidden="true"><path d="M5 42 C4 26 4 9 20 4 C35 6 37 24 35 42 L30 37.5 L25.5 43 L20 37 L14.5 43 L10 37.8 Z" fill="rgba(255,255,255,.78)" stroke="#2a2031" stroke-width="2.4" stroke-linejoin="round" stroke-linecap="round"/><path d="M9 17 C11 11 15 8.5 19 8" fill="none" stroke="rgba(79,216,207,.7)" stroke-width="2" stroke-linecap="round"/><ellipse cx="15" cy="20" rx="2.6" ry="3.4" fill="#2a2031"/><ellipse cx="25.5" cy="20.5" rx="2.6" ry="3.4" fill="#2a2031"/></svg>';

export default {
  ghost: {
    always: true,
    mount(ctx) {
      const level = (() => { try { return +ctx.prestige?.level?.('ghost') || 0; } catch { return 0; } })();
      const P = G.perks(level);
      const pstore = ctx.pstore && typeof ctx.pstore === 'object' ? ctx.pstore : null;
      const store = ctx.store;
      const S = () => ctx.state;
      const tNow = () => (S().clock || 0) / 1000;

      /* ---- the recorder: every run ---- */
      const rec = G.parseRec(store?.get?.('rec'), S().seed);
      let chapter = journeyChapter(S());
      G.noteChapter(rec, S().clock, chapter);
      let dirty = false;
      const persist = () => { if (dirty) { store?.set?.('rec', rec); dirty = false; } };

      /* ---- the race: ghost stars ---- */
      const ghost = P.race ? G.chooseGhost(pstore, P.best) : null;
      let chip = null, gapEl = null, beat = Number.isInteger(store?.get?.('beat')) ? store.get('beat') : -1;
      let mark = null, hint = null, map = null, lastGold = tNow();

      function onChapter(k) {
        if (!ghost || !P.beat || !G.beatChapter(ghost, k, tNow())) return;
        beat = k;
        store?.set?.('beat', k);
        ctx.toast(`Ahead of your ghost: +${Math.round(P.beat * 100)}% this chapter`);
      }

      ctx.on('buy', ({ item } = {}) => {
        if (!item?.id) return;
        G.noteBuy(rec, S().clock, item.id);
        const k = journeyChapter(S());
        if (k !== chapter) { chapter = k; if (G.noteChapter(rec, S().clock, k)) onChapter(k); }
        dirty = true; persist();
        if (map) paintMap();
      });
      ctx.on('golden', () => { G.noteGold(rec, S().clock); dirty = true; persist(); });
      let kept = false;
      ctx.on('ending', ({ id } = {}) => {
        if (kept || !pstore) return;
        kept = true;
        G.sample(rec, S().clock, S().total);
        G.keep(pstore, G.finish(rec, S().clock, id || S().ended?.id || null));
      });

      if (ghost && P.beat) {
        const bonus = (v, s) => (beat > 0 && journeyChapter(s) === beat ? v * (1 + P.beat) : v);
        ctx.mod({ timed: true, fps: bonus, click: bonus });
      }

      if (ghost) {
        ctx.css(CSS);
        chip = ctx.el('div', '', null, 'meters');
        chip.className = 'gh-chip';
        chip.setAttribute('aria-label', 'Your ghost');
        chip.innerHTML = GHOST_SVG;
        gapEl = ctx.el('b', '', '', chip);
        if (ctx.gentle || ctx.reduced) ctx.anim(chip, [{ opacity: 0 }, { opacity: 1 }], { duration: 400 });
        else ctx.anim(chip, [{ opacity: 0, transform: 'translateY(-4px)' }, { opacity: 1, transform: 'none' }], { duration: 320, easing: 'ease-out' });
      }

      function paintChip() {
        if (!chip) return;
        const gap = G.gapSeconds(ghost, rec.b.length, tNow());
        gapEl.textContent = G.gapLine(gap);
        chip.classList.toggle('ahead', gap >= 1);
        chip.classList.toggle('behind', gap <= -1);
      }

      /* The guide map: the ghost stands on the last thing it had bought by now; Hints tags what it bought next. */
      function paintMap() {
        if (!ghost || !map || !map.world?.isConnected) return;
        const byId = new Map(map.nodes.map((n) => [n.id, n]));
        const done = G.ghostBuysAt(ghost, tNow());
        let at = byId.get('start') || null;
        for (let i = done.length - 1; i >= 0; i--) { const n = byId.get(done[i].id); if (n && !n.fork) { at = n; break; } }
        if (!mark || mark.parentNode !== map.world) {
          mark = ctx.el('div', '', null, map.world);
          mark.className = 'gh-mark';
          mark.innerHTML = GHOST_SVG;
          ctx.el('i', '', 'Ghost', mark);
        }
        if (at) { mark.style.left = at.x + 'px'; mark.style.top = at.y + 'px'; mark.style.display = ''; } else mark.style.display = 'none';
        if (!P.hints) return;
        const nid = G.ghostNext(ghost, S().owned);
        const nn = nid && byId.get(nid);
        if (!hint || hint.parentNode !== map.world) { hint = ctx.el('div', '', 'Your ghost bought this next', map.world); hint.className = 'gh-hint'; }
        if (nn) { hint.style.left = nn.x + 'px'; hint.style.top = nn.y + 'px'; hint.style.display = ''; } else hint.style.display = 'none';
      }
      if (ghost) ctx.on('guide-paint', (m) => { if (m?.world && Array.isArray(m.nodes)) { map = m; paintMap(); } });

      /* Ghost Hands: auto taps (the engine marks them auto, so tap juice stays quiet). At most 2 a second. */
      if (ghost && P.taps) ctx.every(Math.round(1000 / P.taps), () => { if (!S().ended) ctx.button.press(); });

      /* Once a second of game time: samples, the chip, the map spot, replayed Golden Buttons. */
      let acc = 0;
      ctx.frame((dt) => {
        acc += dt;
        if (acc < 1) return;
        acc = 0;
        if (S().ended) return;
        if (G.sample(rec, S().clock, S().total)) { dirty = true; persist(); }   // every sample: a reload loses nothing (the run save itself runs every 5 s)
        paintChip();
        if (map) paintMap();
        if (ghost && P.replay) {
          const t = tNow();
          if (G.goldsBetween(ghost, lastGold, t) > 0) ctx.emit('ghost-gold', { t });
          lastGold = t;
        }
      });
      paintChip();

      return { off() { persist(); } };
    },
  },
};
