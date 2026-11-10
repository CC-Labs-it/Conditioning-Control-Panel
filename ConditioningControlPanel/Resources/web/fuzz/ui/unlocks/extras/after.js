/* ui/unlocks/extras/after.js - things kept across runs, drawn on the end-of-run card (ui/recap/recap.js). Each unlock
 * keeps its own logic and bookkeeping here and hands the card its data through recapFor(layer).fill.<piece>; the card
 * draws it inside the panel it belongs to (owner 2026-11-09: no tab bar). No network. The only thing that leaves the page
 * is what the player presses Share or Copy on. Numbers about people are this player's own runs; nothing here pretends to
 * be a crowd. */

import { AFTER_RUN } from '../../../data/content/combos.js';
import { ENDINGS } from '../../../data/content/endings.js';
import { FORKS, ITEMS, BY_ID } from '../../../data/items.js';
import { parse, serialize, SAVE_KEY } from '../../../sim/save.js';
import { AFTER_COPY as C } from './copy.js';
import { pick } from './kit.js';
import { recapFor } from '../../recap/recap.js';

const LINK = 'https://cclabs.app';

/** The end card's fills, or null when no card is up (a dev mount, a test). */
const card = (ctx) => recapFor(ctx.layer('credits'))?.fill || null;

const fill = (s, map) => Object.entries(map).reduce((acc, [k, v]) => acc.replaceAll(`{${k}}`, v), s);
const routeNames = (ctx) => Object.keys(FORKS).filter((f) => ctx.state.picks[f]).map((f) => FORKS[f].opts[ctx.state.picks[f]].name);
const endedId = (ctx) => ctx.state.ended?.id || ctx.life.routes[0]?.ending || null;
const endTitle = (id) => ENDINGS[id]?.title || BY_ID[id]?.name || '';
const today = () => new Date().toISOString().slice(0, 10);
function hash(s) { let x = 2166136261; for (const c of s) { x ^= c.charCodeAt(0); x = Math.imul(x, 16777619); } return x >>> 0; }

/** A real line on the player's own press: share sheet if there is one, otherwise the clipboard. */
async function sendOut(text) {
  if (navigator.share) { try { await navigator.share({ text, url: LINK }); return true; } catch { /* cancelled */ return false; } }
  try { await navigator.clipboard.writeText(`${text} ${LINK}`); return true; } catch { return false; }
}

export const AFTER_UNLOCKS = {
  /* Your path lit up; what you have never picked stays a question mark (peel the road's corner). */
  z_map: {
    mount(ctx) {
      card(ctx)?.map({ old: new Set(ctx.life.routes.flatMap((r) => r.route)) });
      return {};
    },
  },

  /* A card per ending, hidden until reached. Press one you have seen to read it again. */
  z_ends: {
    mount(ctx) {
      const ids = Object.keys(ENDINGS);
      const got = new Set(ids.filter((id) => ctx.life.endings[id]));
      const pool = got.size >= ids.length ? AFTER_RUN.ends.nudge.slice(3) : got.size <= 1 ? AFTER_RUN.ends.nudge.slice(0, 1) : AFTER_RUN.ends.nudge.slice(1, 3);
      card(ctx)?.ends({ got, nudge: (n) => ctx.brand.text(pool[n % pool.length]) });
      return {};
    },
  },

  /* What you tend to pick, from this player's own runs: tally marks under the road. */
  z_pct: {
    mount(ctx) {
      const counts = {};
      for (const f of Object.keys(FORKS)) {
        counts[f] = {};
        for (const lane of Object.keys(FORKS[f].opts)) counts[f][lane] = ctx.life.routes.filter((r) => r.route.includes(lane)).length;
      }
      card(ctx)?.pct({ counts });
      return {};
    },
  },

  /* Your route and ending as a line. The only thing that leaves is what you press Share on. */
  z_share: {
    mount(ctx) {
      const S = AFTER_RUN.share;
      const route = routeNames(ctx).join(', ') || BY_ID.start.name;
      const min = Math.max(1, Math.round(ctx.state.clock / 60000));
      const lines = [fill(S.diagnosis, { route }), fill(S.ending, { ending: endTitle(endedId(ctx)) }), fill(S.minutes, { min }).replace(/^1 minutes/, '1 minute')].map((l) => ctx.brand.text(l));
      card(ctx)?.share({ lines, send: sendOut, done: S.copied });
      return {};
    },
  },

  /* How many unlocks you have ever seen. */
  z_seen: {
    mount(ctx) {
      /* Everything on the list: bought rows, this card's own rows, and the moods met so far. */
      const total = ITEMS.length;
      const moods = { ...(ctx.store.get('moods') || {}) };
      if (ctx.state.mood) { moods[ctx.state.mood] = 1; moods.m_mood = 1; }
      ctx.store.set('moods', moods);
      const onCard = ITEMS.filter((i) => i.group === 'after').length;
      const now = Math.min(total, Object.keys(ctx.life.seen).length + onCard + Object.keys(moods).length);
      ctx.store.set('prev', now);
      card(ctx)?.seen({ now, total, line: fill(pick(ctx, AFTER_RUN.seen.lines), { total }) });
      return {};
    },
  },

  /* Keep one Look you have owned; the next run starts with it. */
  z_perk: {
    mount(ctx) {
      const looks = ITEMS.filter((i) => i.lane === 'look' && ctx.life.seen[i.id]);
      card(ctx)?.perk({ looks, current: ctx.life.perk || null, choose: (id) => ctx.setPerk(id) });
      return {};
    },
  },

  /* One route for the date, the same on every copy of the game. A target: the engine has no way to force picks. */
  z_daily: {
    mount(ctx) {
      const D = AFTER_RUN.daily;
      const day = today();
      const route = Object.entries(FORKS).map(([f, fork]) => {
        const lanes = Object.keys(fork.opts);
        const lane = lanes[hash(`${day}:${f}`) % lanes.length];
        return { f, lane, name: ctx.brand.text(fork.opts[lane].name), match: ctx.state.picks[f] === lane };
      });
      const theme = route[route.length - 1].name;
      const matched = route.filter((r) => r.match).length;
      /* The board is this player's runs today, nobody else's. */
      let log = ctx.store.get('log');
      if (!log || log.day !== day) log = { day, runs: [] };
      const key = `${ctx.state.seed}:${Math.round(ctx.state.clock)}`;
      const minutes = Math.max(1, Math.round(ctx.state.clock / 60000));
      if (ctx.state.ended && !log.runs.some((r) => r.key === key) && matched === route.length) log.runs.push({ key, minutes });
      ctx.store.set('log', log);
      const best = [...log.runs].sort((a, b) => a.minutes - b.minutes).slice(0, 4).map((r) => ({ who: D.you, label: `${r.minutes} min` }));
      card(ctx)?.daily({ route, best, title: fill(D.title, { theme }).replace(/ a ([AEIOU])/, ' an $1'), board: D.board, note: C.dailyNote, empty: C.dailyEmpty });
      return {};
    },
  },

  /* The side unlocks come in a different order each run after the first. The sim does it; this shows it happening. */
  z_shuffle: {
    mount(ctx) {
      card(ctx)?.shuffle({ items: ITEMS.filter((i) => i.lane === 'side').slice(0, 3), runNo: ctx.life.runs + 1 });
      return {};
    },
  },

  /* Your run as text. Copy on press; paste a save and load it. */
  z_save: {
    mount(ctx) {
      const S = AFTER_RUN.save;
      card(ctx)?.save({
        copy: () => serialize(ctx.state, ctx.life, {}),
        load: (text) => {
          if (!parse(text)) return S.bad;
          try { localStorage.setItem(SAVE_KEY, text); } catch { return S.bad; }
          location.reload();
          return true;
        },
        copied: AFTER_RUN.share.copied, bad: S.bad,
      });
      return {};
    },
  },
};
