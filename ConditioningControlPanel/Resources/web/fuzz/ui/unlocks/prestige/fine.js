/* ui/unlocks/prestige/fine.js - Renewal Sky lane entries (brief: handoff-1109/TC-PRESTIGE-WIRE-BRIEF.md).
 *
 * Fine Print: the contract joke made playable. Before a run the player strikes clauses (the Sky lane writes the picks
 * into ctx.prestige.next.rules, ids from FINE_RULES); this entry applies them for the run and shows a small paper chip,
 * "Contract terms", with the rules and the Stardust factor. The factor itself is paid by sim/prestige.js fineMult;
 * contractFactor() here only mirrors it for the chip and must stay equal to it.
 *
 *   silent     every game sound off for the run (audio kit), the speaker wears a strike.
 *   no_hands   a press on #btn itself pays nothing; bouncing copies, auto taps and every other income still pay. Until
 *              the first copy or per-second pay, the rule taps once a second for the player (no stalled start).
 *   blank      the picture feed answers nothing; picture screens show a paper "Blank" card.
 *   no_toys    toy rows cost nothing and stay shut (Control Panel switch off). Never refused: a Journey anchor or a
 *              trunk row must always go through, so a rule can never make a run impossible.
 *   speedrun   the run ends at half its planned time, through the engine's end path with the plain ending.
 *   one_theme  Second Theme is a trunk row (Spiral Overlay and the rest need it), so it is free and struck the same way.
 *
 * Nothing here touches Wake Up, pause, panic or the age gate: no listener prevents a default or stops propagation.
 * Game clock only. Lane files must not touch the DOM at import time.
 * ==========================================================================*/
import * as PD from '../../../data/prestige.js';
import { TOY_IDS as KEEPSAKE_TOYS } from '../../../sim/prestige.js';

const { FINE_RULES, FULL_CONTRACT, T_RUN } = PD;
const RULE_BY_ID = Object.freeze(Object.fromEntries(FINE_RULES.map((r) => [r.id, r])));
const CURRENCY = () => PD.PRESTIGE_CURRENCY || 'Stardust';

/** Toy rows No Toys strikes: every toy a Keepsake can carry (sim/prestige.js TOY_IDS) plus the dispenser refills.
 *  Keepsakes and the Lifetime Plan wheel are struck too: the rule wins. */
export const TOY_IDS = Object.freeze([...new Set([...KEEPSAKE_TOYS, 'dispenser_fast', 'dispenser_faster'])]);

/** The rows a rule set strikes: free in the shop and shut when owned. */
export function struckRows(rules) {
  const on = new Set(rules);
  const out = new Set();
  if (on.has('no_toys')) for (const id of TOY_IDS) out.add(id);
  if (on.has('one_theme')) out.add('taste2');
  return out;
}

/** The rules this run really carries: the run's stamp (ctx.prestige.run.rules, already filtered and capped by
 *  sim/prestige.js) when there is one, else next.rules; known ids, once each, star owned (when `has` is given). */
export function activeRules(prestige) {
  const stamped = prestige?.run?.rules;
  const raw = Array.isArray(stamped) ? stamped : Array.isArray(prestige?.next?.rules) ? prestige.next.rules : [];
  const has = typeof prestige?.has === 'function' ? (id) => !!prestige.has(id) : () => true;
  const seen = new Set();
  for (const id of raw) if (RULE_BY_ID[id] && has(RULE_BY_ID[id].star)) seen.add(id);
  return FINE_RULES.map((r) => r.id).filter((id) => seen.has(id));
}

/** The Stardust factor for a rule set. Mirrors sim/prestige.js fineMult: product of the rule mults, Hush Money (fine_6)
 *  lifts Silent, every rule with Full Contract (fine_10) is a flat x5. */
export function ruleMult(id, has = () => false) {
  const r = RULE_BY_ID[id];
  if (!r) return 1;
  return r.better && has(r.better.star) ? r.better.mult : r.mult;
}
export function contractFactor(rules, has = () => false) {
  const list = [...new Set(rules)].filter((id) => RULE_BY_ID[id]);
  if (!list.length) return 1;
  if (list.length === FINE_RULES.length && has(FULL_CONTRACT.star)) return FULL_CONTRACT.mult;
  return list.reduce((m, id) => m * ruleMult(id, has), 1);
}
/** x1.2, x2.3, x5: one decimal, no trailing .0. */
export const factorText = (f) => 'x' + (Math.round(f * 10) / 10).toString();

/** Speedrun: half the run's planned minutes, in game ms. The stamp's `minutes` wins (it knows the core's 45); else
 *  T_RUN by Depth at the start of the run, Depth past the table (Depth 10) keeping the last row. */
export function speedrunLimitMs(depth, minutes) {
  if (Number.isFinite(minutes) && minutes > 0) return minutes * 60000 / 2;
  const d = Math.max(0, Math.min(T_RUN.length - 1, Math.floor(Number(depth) || 0)));
  return T_RUN[d] * 60000 / 2;
}

const mmss = (ms) => {
  const s = Math.max(0, Math.ceil(ms / 1000));
  return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
};

/* Copy. Dry and short; the chip and toasts wink at the contract. */
export const COPY = Object.freeze({
  chip: 'Contract terms',
  head: (f) => `Your clauses this run. ${CURRENCY()} at the end ${factorText(f)}.`,
  silent: 'Silent is in the contract. The speaker stays struck.',
  noHands: 'Clause struck: that press paid nothing.',
  toyStruck: 'Clause struck. No charge, and it stays shut.',
  themeStruck: 'Signed for. The second theme stays struck.',
  stillStruck: 'The contract says no.',
  oneMinute: 'One minute left on the clause.',
  timeUp: 'Time. The clause is up.',
  left: (ms) => `${mmss(ms)} left`,
});

const CSS = `
.fp-chip{position:absolute;left:10px;top:64px;z-index:5;pointer-events:auto;font:600 12px/1.2 var(--font,system-ui);color:#2a2320;
  max-width:min(260px,calc(100vw - 32px));}
.fp-chip>button{all:unset;cursor:pointer;display:inline-flex;gap:6px;align-items:baseline;padding:5px 9px 5px 8px;background:#fffaf0;
  border:1.5px solid #2a2320;border-radius:3px 6px 4px 7px;box-shadow:2px 3px 0 rgba(42,35,32,.28);}
.fp-chip>button:focus-visible{outline:2px solid var(--accent,#ff6fb5);outline-offset:2px;}
.fp-chip>button b{color:#c0392b;font-weight:800;}
.fp-chip .fp-left{font-weight:500;opacity:.75;}
.fp-card{margin-top:6px;padding:8px 10px;background:#fffaf0;border:1.5px solid #2a2320;border-radius:5px 3px 7px 4px;
  box-shadow:2px 3px 0 rgba(42,35,32,.28);font-weight:500;}
.fp-card p{margin:0 0 6px;}
.fp-card li{margin:3px 0;list-style:none;}
.fp-card li s{color:#c0392b;font-weight:700;text-decoration-thickness:2px;}
.fp-card li i{display:block;opacity:.8;}
#audio-mute[data-fine-rule]{background-image:linear-gradient(to top right,transparent 45%,#c0392b 46% 54%,transparent 55%);}
.fp-blank .scr-card>*{visibility:hidden;}
.fp-blank .scr-card{position:relative;}
.fp-blank .scr-card::after{content:'Blank';position:absolute;inset:18% 14%;display:flex;align-items:center;justify-content:center;
  background:#fffaf0;color:#2a2320;border:1.5px solid #2a2320;border-radius:4px 7px 3px 6px;box-shadow:2px 3px 0 rgba(42,35,32,.28);
  font:700 15px/1 var(--font,system-ui);letter-spacing:.08em;text-transform:uppercase;}
`;

export default {
  fine: {
    mount(ctx) {
      const P = ctx.prestige;
      if (!P || ctx.state?.ended) return {};
      const rules = activeRules(P);
      if (!rules.length) return {};
      const on = new Set(rules);
      const has = (id) => !!P.has?.(id);
      const factor = contractFactor(rules, has);
      const btn = ctx.button?.el;
      const doc = btn?.ownerDocument || null;
      const win = doc?.defaultView || null;
      const listen = (target, type, fn, capture) => {
        if (!target?.addEventListener) return;
        target.addEventListener(type, fn, capture);
        ctx.cleanup(() => target.removeEventListener(type, fn, capture));
      };
      ctx.css(CSS);

      /* ---- Silent ---- */
      if (on.has('silent')) {
        if (typeof ctx.audio?.silence === 'function') ctx.audio.silence(true);   // Core hook: a run-long kit flag
        else {
          /* Fallback until the kit has a run-long flag: muteFor is capped at 15 s on the wall clock, so keep renewing it. */
          const hush = () => ctx.audio?.muteFor?.(15000);
          hush();
          ctx.every(4000, hush);
          ctx.on('resume', hush);
          ctx.on('suspend', ({ on: s } = {}) => { if (!s) hush(); });
        }
        const spk = doc?.getElementById?.('audio-mute');
        if (spk) {
          spk.dataset.fineRule = 'silent';
          ctx.cleanup(() => { delete spk.dataset.fineRule; });
          listen(spk, 'click', () => ctx.toast(COPY.silent), false);
        }
      }

      /* ---- No Hands ---- */
      if (on.has('no_hands')) {
        /* A press on #btn is marked in the capture phase, before ui/app.js taps the engine, and only for that one event:
         * the click mod reads it while that event is still dispatching. Copies are other elements, auto taps no event. */
        let manual = null;
        const mark = (ev) => { manual = ev.isTrusted && ev.target?.closest?.('#btn') ? ev : null; };
        const clear = (ev) => { if (manual === ev) manual = null; };
        for (const type of ['pointerdown', 'pointermove', 'click']) { listen(win, type, mark, true); listen(win, type, clear, false); }
        const pressing = () => !!manual && (win?.event === undefined || win.event === manual);
        ctx.mod({ timed: true, click: (v) => (pressing() ? 0 : v) });
        let lastToast = -Infinity, lastFloat = -Infinity;
        ctx.on('tap', ({ auto, x, y } = {}) => {
          if (auto || !pressing()) return;
          const t = ctx.state.clock;
          if (t - lastToast > 20000) { lastToast = t; ctx.toast(COPY.noHands); }
          if (Number.isFinite(x) && t - lastFloat > 1500) { lastFloat = t; ctx.float(x, y, 'struck', 'opacity:.7;font-size:12px;'); }
        });
        /* A run starts with nothing but the button: until the first bouncing copy or any per-second pay, the fine print
         * taps once a second for the player (an auto tap, which the rule lets through), so the run can never stall. */
        ctx.every(1000, () => { if (!ctx.has('stray') && !(ctx.rates().fps > 0)) ctx.button.press(); });
      }

      /* ---- Blank ---- */
      if (on.has('blank') && ctx.pictures) {
        const pics = ctx.pictures;
        const saved = { next: pics.next, rewardBatch: pics.rewardBatch, stillsForDisplay: pics.stillsForDisplay };
        try {
          pics.next = async () => null;
          pics.rewardBatch = () => [];
          pics.stillsForDisplay = () => [];
          ctx.cleanup(() => Object.assign(pics, saved));
        } catch { /* a frozen feed (tests): the rule then only paints */ }
        doc?.documentElement?.classList?.add('fp-blank');
        ctx.cleanup(() => doc?.documentElement?.classList?.remove('fp-blank'));
        ctx.emit('pictures', { source: pics.source });   // frames on screen refill, now with nothing
      }

      /* ---- No Toys, One Theme: free and shut ---- */
      const struck = struckRows(rules);
      if (struck.size) {
        ctx.mod({ price: (v, _s, _now, item) => (item && struck.has(item.id) ? 0 : v) });
        const shut = (id) => { if (ctx.has(id) && ctx.switches?.isOn?.(id)) ctx.switches.set(id, false); };
        const pending = new Set();   // a buy also emits 'switch' on: one shut, one line
        const shutLater = (id, line) => {
          if (pending.has(id)) return;
          pending.add(id);
          ctx.after(0, () => { pending.delete(id); shut(id); if (line) ctx.toast(line); });
        };
        for (const id of struck) if (ctx.has(id)) shutLater(id);   // keepsakes, Lifetime Plan, a reload
        const lineFor = (id) => (id === 'taste2' ? COPY.themeStruck : COPY.toyStruck);
        ctx.on('buy', ({ item, also } = {}) => {
          for (const it of [item, ...(also || [])]) if (it && struck.has(it.id)) shutLater(it.id, lineFor(it.id));
        });
        ctx.on('switch', ({ id, on: back } = {}) => {
          if (back && struck.has(id)) shutLater(id, COPY.stillStruck);
        });
      }

      /* ---- the chip ---- */
      const chip = ctx.el('div', '', null, 'over');
      chip.className = 'fp-chip';
      const face = ctx.el('button', '', null, chip);
      face.type = 'button';
      face.setAttribute?.('aria-expanded', 'false');
      ctx.el('span', '', COPY.chip, face);
      ctx.el('b', '', factorText(factor), face);
      const left = on.has('speedrun') ? ctx.el('span', '', '', face) : null;
      if (left) left.className = 'fp-left';
      let card = null, pinned = false;
      const open = (v) => {
        if (!!card === v) return;
        face.setAttribute?.('aria-expanded', String(v));
        if (!v) { card.remove(); card = null; return; }
        card = ctx.el('div', '', null, chip);
        card.className = 'fp-card';
        ctx.el('p', '', COPY.head(factor), card);
        const ul = ctx.el('ul', 'margin:0;padding:0;', null, card);
        for (const id of rules) {
          const r = RULE_BY_ID[id];
          const li = ctx.el('li', '', null, ul);
          ctx.el('s', '', r.name, li);
          ctx.el('span', '', ' ' + factorText(ruleMult(id, has)), li);
          ctx.el('i', '', r.plain, li);
        }
        if (ctx.gentle || ctx.reduced) ctx.anim(card, [{ opacity: 0 }, { opacity: 1 }], { duration: 250 });
        else ctx.anim(card, [{ opacity: 0, transform: 'translateY(-4px)' }, { opacity: 1, transform: 'none' }], { duration: 180, easing: 'ease-out' });
      };
      listen(face, 'click', () => { pinned = !pinned || !card; open(pinned); }, false);
      listen(chip, 'pointerenter', (ev) => { if (ev.pointerType === 'mouse') open(true); }, false);
      listen(chip, 'pointerleave', (ev) => { if (ev.pointerType === 'mouse' && !pinned) open(false); }, false);

      /* ---- Speedrun ---- */
      if (on.has('speedrun')) {
        const limit = speedrunLimitMs(P.run ? P.run.depth : P.depth, P.run?.minutes);
        let warned = false, done = false;
        const check = () => {
          if (done) return;
          const rest = limit - ctx.state.clock;
          if (left) left.textContent = COPY.left(rest);
          if (!warned && rest <= 60000 && rest > 0) { warned = true; ctx.toast(COPY.oneMinute); }
          if (rest > 0 || ctx.state.ended) return;
          done = true;
          ctx.toast(COPY.timeUp);
          if (typeof ctx.endRun === 'function') ctx.endRun('drop');   // Core hook: the engine's end path, plain ending
        };
        check();
        ctx.every(1000, check);
      }

      return {};
    },
  },
};
