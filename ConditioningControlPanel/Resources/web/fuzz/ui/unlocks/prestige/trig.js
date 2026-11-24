/* ui/unlocks/prestige/trig.js - Renewal Sky lane `trig`, Trigger Words (brief: handoff-1109/TC-PRESTIGE-WIRE-BRIEF.md).
 *
 * The player's word (ctx.prestige.next.words) floats across the page now and then as a paper cutout. Tap it: a burst of
 * income, the word flares and echoes, a quiet pluck that climbs with each catch of the run and Circe whispering the word
 * when a clip exists. The little tag on the cutout says how conditioned the word is. Numbers: sim/prestige-words.js.
 * Everywhere (trig_7): about half the appearances turn up written over an open picture screen instead, the second word
 * reel of Mini Slots shows a word (display only, arcade/slots.js) and the podcast caption tape carries one now and then
 * (podcast-player.js), which a tap catches for the same pay as the floating word. Both read wordsForEverywhere(ctx).
 * Gentle: no particles, no ring, slow fades. Reduced motion: the word holds still. One lamp: no rotation, so the paper
 * shadow always falls down-right. */

import {
  FADE_IN_GENTLE_MS, FADE_IN_MS, LIFE_GENTLE_MS, LIFE_MS, catchSeconds, conditionMult, isInstant, nextGapMs,
  parseWordStore, recordCatch, runWords, wordSlots,
} from '../../../sim/prestige-words.js';
import { setRich } from '../../rich-text.js';

const CSS = `
.tw-word { position: absolute; left: 0; top: 0; pointer-events: auto; touch-action: manipulation; user-select: none; -webkit-user-select: none;
  cursor: pointer; min-width: 64px; min-height: 44px; box-sizing: border-box; padding: 7px 18px 9px; display: flex; align-items: center; justify-content: center;
  font: 700 26px/1 var(--font, system-ui); letter-spacing: .02em; white-space: nowrap; color: color-mix(in srgb, var(--accent, #b89cff) 45%, #1d1328);
  background: #fffdf5 repeating-linear-gradient(-28deg, rgba(120,90,160,.05) 0 2px, transparent 2px 5px);
  border: 2.5px solid #2b2233; border-radius: 15px 9px 17px 8px / 9px 16px 8px 14px; box-shadow: 3px 4px 0 rgba(30,20,40,.26); }
.tw-word .tw-tag { position: absolute; right: -8px; bottom: -11px; padding: 1px 6px 2px; font: 700 11px/1.2 var(--font, system-ui);
  color: #2b2233; background: #ffe36e; border: 1.5px solid #2b2233; border-radius: 7px 4px 8px 3px; box-shadow: 1px 2px 0 rgba(30,20,40,.25); }
.tw-word .tw-ring { position: absolute; inset: -9px; border: 2.5px dashed var(--accent, #b89cff); border-radius: 18px 12px 20px 11px; pointer-events: none; }
.tw-word.tw-pic { background: none; border: none; box-shadow: none; padding: 6px 12px; color: #fff; font-size: 32px;
  text-shadow: 0 0 2px #1b1424, 1px 1px 0 #1b1424, -1px -1px 0 #1b1424, 3px 4px 0 rgba(20,10,30,.45); }
.tw-word.tw-pic .tw-tag { right: -2px; bottom: -6px; }
.tw-echo { position: absolute; left: 0; top: 0; pointer-events: none; font: 700 26px/1 var(--font, system-ui); color: var(--accent, #b89cff);
  text-shadow: 2px 3px 0 rgba(30,20,40,.18); white-space: nowrap; }
.tw-dot { position: absolute; left: 0; top: 0; width: 8px; height: 8px; border-radius: 50%; pointer-events: none; background: var(--accent, #b89cff); border: 1.5px solid #2b2233; }
`;

/* A major pentatonic on the wooden plucks (assets/audio/pluck/wood_00..17), one step up per catch in the run. */
const PLUCK_STEPS = [0, 2, 4, 7, 9, 12, 13];
const SOURCE = { source: 'trig_words', sourceLabel: 'Trigger words' };

/** Trigger Words Everywhere (trig_7): the run's words for other features to show (slot reels, podcast captions), or []
 *  without the star. Any feature's ctx works: it only reads ctx.prestige. */
export function wordsForEverywhere(ctx) {
  const P = ctx?.prestige;
  const has = (id) => !!P?.has?.(id);
  if (!has('trig_7')) return [];
  return runWords(P?.next?.words, wordSlots(has));
}

/* The live lane's catch, set while the trig entry is mounted: a word spotted elsewhere pays like the floating one. */
let elsewhere = null;
/** True when a word shown by another feature can be caught (the lane is mounted this run). */
export const canCatchEverywhere = () => !!elsewhere;
/** Catch `word` shown by another feature at page point `at` ({ x, y }). Answers false when nothing could pay. */
export function catchEverywhere(word, at = null) { return elsewhere ? elsewhere(word, at) : false; }

export default {
  trig: {
    mount(ctx) {
      const P = ctx.prestige;
      const has = (id) => !!P?.has?.(id);
      const words = runWords(P?.next?.words, wordSlots(has));
      if (!words.length) return {};
      const doc = ctx.button.el.ownerDocument;
      ctx.css(CSS);

      // Life store (survives New run). Until the Core lane hands out ctx.pstore, fall back to the run store.
      const life = ctx.pstore && typeof ctx.pstore === 'object' ? ctx.pstore : null;
      const book = parseWordStore(life || ctx.store.get('words'));
      const save = () => { if (life) Object.assign(life, book); else ctx.store.set('words', book); };

      let now = 0, live = null, runCatches = 0;
      let wait = nextGapMs(ctx.rng(), { often: has('trig_2') }) * 0.5;
      const soft = () => ctx.gentle || ctx.reduced;
      const worth = (s) => { const r = ctx.rates(); return Math.max(r.fps, r.click * 2, 2) * s; };
      const tagText = (w) => {
        const n = book.catches[w] || 0;
        if (has('trig_4')) {
          const pct = Math.round((conditionMult(n, { conditioned: true, deep: has('trig_9') }) - 1) * 100);
          return pct > 0 ? `+${pct}%` : 'new';
        }
        return n ? `${ctx.fmt(n)} caught` : 'new';
      };
      const soundOn = () => ctx.has('sound') && ctx.switches?.isOn?.('sound') !== false;

      const node = (cls, text, parent = 'over') => {
        const e = doc.createElement('div');
        e.className = cls;
        if (text != null) setRich(e, text);
        (typeof parent === 'string' ? ctx.layer(parent) : parent).appendChild(e);
        return e;
      };
      ctx.cleanup(() => { live?.el.remove(); live = null; elsewhere = null; });

      /* A picture screen on the page, for Everywhere: the body of an open screen with room for a word. */
      const pictureSpot = () => {
        const { w: W, h: H } = ctx.bounds();
        const found = [];
        for (const name of ['side', 'screens']) {
          for (const b of ctx.layer(name)?.querySelectorAll?.('.scr-body') || []) {
            const r = b.getBoundingClientRect();
            if (r.width >= 120 && r.height >= 80 && r.left >= 0 && r.top >= 0 && r.right <= W && r.bottom <= H) found.push(r);
          }
        }
        return found.length ? found[Math.floor(ctx.rng() * found.length)] : null;
      };

      const appear = (forcePic = false) => {
        const word = words[Math.floor(ctx.rng() * words.length)];
        const { w: W, h: H } = ctx.bounds();
        const spot = forcePic || (has('trig_7') && ctx.rng() < 0.5) ? pictureSpot() : null;
        const el = node(spot ? 'tw-word tw-pic' : 'tw-word', word);
        el.setAttribute('role', 'button');
        el.setAttribute('aria-label', `Catch the word ${word}`);
        node('tw-tag', tagText(word), el);
        const fin = soft() ? FADE_IN_GENTLE_MS : FADE_IN_MS;
        const lifeMs = (soft() ? LIFE_GENTLE_MS : LIFE_MS) * (spot ? 0.6 : 1);
        let sx, sy, ex, ey;
        if (spot) {
          sx = ex = spot.left + spot.width * (0.25 + ctx.rng() * 0.5) - 50;
          sy = ey = spot.top + spot.height * (0.3 + ctx.rng() * 0.4) - 22;
        } else {
          const fromLeft = ctx.rng() < 0.5;
          sy = 90 + ctx.rng() * Math.max(40, H * 0.5);
          ey = sy + (ctx.rng() - 0.5) * 80;
          sx = fromLeft ? W * (0.08 + ctx.rng() * 0.2) : W * (0.62 + ctx.rng() * 0.2);
          ex = sx + (fromLeft ? 1 : -1) * Math.min(W * 0.3, 260);
          if (ctx.reduced) { ex = sx; ey = sy; }
        }
        sx = Math.max(8, Math.min(W - 140, sx)); ex = Math.max(8, Math.min(W - 140, ex));
        el.style.transform = `translate(${sx}px,${sy}px)`;
        const me = { el, word, at: now, done: false, move: null, ring: null, expire: null };
        live = me;
        // In: a quick stamp onto the page (Gentle: a slow fade). The drift runs the whole life, linear and slow.
        ctx.anim(el, soft()
          ? [{ opacity: 0 }, { opacity: spot ? 0.85 : 1 }]
          : [{ opacity: 0, scale: 1.5 }, { opacity: spot ? 0.85 : 1, scale: 1 }], { duration: fin, fill: 'forwards', easing: 'ease-out' });
        if (ex !== sx || ey !== sy) {
          me.move = ctx.anim(el, [
            { transform: `translate(${sx}px,${sy}px)` },
            { transform: `translate(${(sx + ex) / 2}px,${(sy + ey) / 2 - 18}px)` },
            { transform: `translate(${ex}px,${ey}px)` }], { duration: lifeMs, fill: 'forwards', easing: 'linear' });
        }
        if (has('trig_10') && !ctx.gentle) {
          // Instant Response: a dashed ring closes on the word while the triple window is open.
          me.ring = node('tw-ring', null, el);
          const a = ctx.anim(me.ring, [{ opacity: 0.9, scale: 1.35 }, { opacity: 0, scale: 1 }], { duration: fin + 500, fill: 'forwards', easing: 'linear' });
          a.onfinish = () => me.ring?.remove();
        }
        el.addEventListener('pointerdown', (e) => {
          e.preventDefault();
          e.stopPropagation();
          if (me.done || ctx.paused) return;
          caught(me);
        });
        me.expire = ctx.after(lifeMs, () => {
          if (me.done) return;
          me.done = true;
          if (live === me) live = null;
          const a = ctx.anim(el, [{ opacity: spot ? 0.85 : 1 }, { opacity: 0 }], { duration: soft() ? 900 : 380, fill: 'forwards' });
          a.onfinish = a.oncancel = () => el.remove();
        });
      };

      const caught = (me) => {
        me.done = true;
        me.expire?.();
        if (live === me) live = null;
        const r = me.el.getBoundingClientRect();
        const x = r.left + r.width / 2, y = r.top + r.height / 2;
        const instant = has('trig_10') && isInstant(me.at, now, ctx.gentle);
        reward(me.word, x, y, instant);
        me.move?.pause();
        me.ring?.remove();

        // The flare: the cutout swells and lets go; its tag already counts the new catch.
        const tag = me.el.querySelector('.tw-tag');
        if (tag) tag.textContent = tagText(me.word);
        const out = ctx.anim(me.el, soft()
          ? [{ opacity: 1 }, { opacity: 0 }]
          : [{ scale: 1, opacity: 1 }, { scale: 1.32, opacity: 1, offset: 0.35 }, { scale: 1.5, opacity: 0 }],
        { duration: soft() ? 700 : 420, fill: 'forwards', easing: 'ease-out' });
        out.onfinish = out.oncancel = () => me.el.remove();

        // The echo: the word again, rising and fading. Gentle: one slow echo.
        const echoes = soft() ? 1 : 2;
        for (let i = 0; i < echoes; i += 1) {
          const e = node('tw-echo', me.word, 'fx');
          const a = ctx.anim(e, [
            { opacity: 0.75 - i * 0.25, transform: `translate(${x - 30}px,${y - 14}px)` },
            { opacity: 0, transform: `translate(${x - 30 + (i ? 18 : -14)}px,${y - 70 - i * 34}px)` }],
          { duration: soft() ? 1400 : 900 + i * 250, delay: i * 140, fill: 'both', easing: 'ease-out' });
          a.onfinish = a.oncancel = () => e.remove();
        }
        dots(x, y);
      };

      /* A few paper dots (never under Gentle or reduced motion). */
      const dots = (x, y) => {
        if (!soft()) {
          for (let i = 0; i < 7; i += 1) {
            const d = node('tw-dot', null, 'fx');
            const ang = (i / 7) * Math.PI * 2 + ctx.rng() * 0.4, reach = 34 + ctx.rng() * 26;
            const a = ctx.anim(d, [
              { opacity: 1, transform: `translate(${x - 4}px,${y - 4}px)` },
              { opacity: 0, transform: `translate(${x - 4 + Math.cos(ang) * reach}px,${y - 4 + Math.sin(ang) * reach}px)` }],
            { duration: 520, fill: 'both', easing: 'ease-out' });
            a.onfinish = a.oncancel = () => d.remove();
          }
        }
      };

      /* Book, pay and sound of one catch, for the floating word and for words spotted elsewhere (Everywhere). */
      const reward = (word, x, y, instant = false) => {
        const before = recordCatch(book, word);
        save();
        ctx.pay(worth(catchSeconds(has, before, instant)), 'trig', { x, y });
        runCatches += 1;
        // ctx.pay already flies the number to the counter; only Instant Response says why it was bigger.
        if (instant) ctx.float(x, y - 52, "Instant");
        if (soundOn()) {
          const step = PLUCK_STEPS[(runCatches - 1) % PLUCK_STEPS.length];
          const pan = Math.max(-0.7, Math.min(0.7, (x / Math.max(1, ctx.bounds().w) - 0.5) * 1.2));
          ctx.audio.play(`assets/audio/pluck/wood_${String(4 + step).padStart(2, '0')}.wav`, { bus: 'sfx', gain: 0.22, pan, ...SOURCE });
          ctx.voice(word.replace(/\s+/g, '-'), { gain: 0.32, pan, ...SOURCE });
        }
        const n = book.catches[word];
        if (n === 1) ctx.say(`${word}. You will hear it again.`);
        else if (n % 25 === 0) ctx.say(`${word}. Easier every time.`);
      };

      /* Everywhere: a word another feature showed (podcast caption) is caught here, same pay, with one quiet echo. */
      if (has('trig_7')) {
        elsewhere = (word, at) => {
          const w = typeof word === 'string' ? word.trim().toLowerCase() : '';
          if (!words.includes(w) || ctx.paused) return false;
          const { w: W, h: H } = ctx.bounds();
          const x = Number.isFinite(at?.x) ? at.x : W / 2, y = Number.isFinite(at?.y) ? at.y : H / 2;
          reward(w, x, y, false);
          const e = node('tw-echo', w, 'fx');
          const a = ctx.anim(e, [
            { opacity: 0.75, transform: `translate(${x - 30}px,${y - 14}px)` },
            { opacity: 0, transform: `translate(${x - 30}px,${y - 64}px)` }],
          { duration: soft() ? 1400 : 900, fill: 'both', easing: 'ease-out' });
          a.onfinish = a.oncancel = () => e.remove();
          dots(x, y);
          return true;
        };
      }

      /* Dev door: show a word now, read the book. */
      const view = doc.defaultView;
      if (view?.fuzz) {
        view.fuzz.trig = { show: (pic) => { if (!live) appear(!!pic); }, book: () => JSON.parse(JSON.stringify(book)) };
        ctx.cleanup(() => { if (view.fuzz) delete view.fuzz.trig; });
      }

      return {
        tick(dt, t) {
          now = t;
          if (live) return;
          wait -= dt * 1000;
          if (wait <= 0) { wait = nextGapMs(ctx.rng(), { often: has('trig_2') }); appear(); }
        },
      };
    },
  },
};
