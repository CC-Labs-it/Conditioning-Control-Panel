/* ui/unlocks/extras/after.js - things kept across runs, drawn on the credits card: a row of tabs under the run's numbers.
 * No network. The only thing that leaves the page is what the player presses Share or Copy on. Numbers about people are
 * this player's own runs; nothing here pretends to be a crowd. */

import { AFTER_RUN } from '../../../data/content/combos.js';
import { ENDINGS } from '../../../data/content/endings.js';
import { FORKS, ITEMS, BY_ID } from '../../../data/items.js';
import { parse, serialize, SAVE_KEY } from '../../../sim/save.js';
import { AFTER_COPY as C } from './copy.js';
import { h, pick } from './kit.js';

const LINK = 'https://cclabs.app';
const EMOJI_FONT = '"Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji",sans-serif';
const PINK = '#ff69b4';

const CSS = `
.xa-top{margin-bottom:8px;}
.xa-tabs{display:flex;flex-wrap:wrap;gap:4px;margin-bottom:8px;}
.card .xa-tabs button{min-height:36px;padding:2px 9px;font-size:14px;}
.card .xa-tabs button[aria-selected=true]{border-style:inset;background:#ffdcee;}
.xa-h{font-weight:700;margin-bottom:6px;}
.xa-note{font-size:14px;color:#444;margin-top:6px;}
.xa-chips{display:flex;flex-wrap:wrap;gap:4px;margin-bottom:12px;position:relative;z-index:1;}
.xa-chip{padding:1px 7px;min-height:24px;line-height:22px;border:1px solid #8a867d;background:#eeeae0;color:#6e6a62;font-size:14px;}
.xa-chip.old{color:#222;}
.xa-chip.lit{background:${PINK};border-color:#c23c84;color:#fff;transition:background .3s;}
.xa-grid{display:grid;grid-template-columns:repeat(5,1fr);gap:4px;}
.card button.xa-end{min-height:44px;padding:2px;background:#3a3550;color:#fff;border:1px solid #222;font-size:12px;line-height:1.05;}
.card button.xa-end.got{background:${PINK};border-color:#c23c84;}
.xa-bar{position:relative;height:24px;background:#eeeae0;border:1px solid #8a867d;margin:2px 0 6px;}
.xa-bar i{position:absolute;left:0;top:0;bottom:0;background:#8d94b5;transform-origin:0 50%;}
.xa-bar.you i{background:${PINK};}
.xa-bar span{position:absolute;left:6px;right:6px;top:0;display:flex;justify-content:space-between;line-height:22px;font-size:14px;}
.xa-share{position:relative;padding:10px;background:#fff;border:1.5px solid #333;box-shadow:3px 3px 0 ${PINK};transform:rotate(-1deg);text-align:center;margin:4px 3px 12px;}
.xa-share b{display:block;font:700 18px Georgia,serif;margin-bottom:4px;}
.xa-dots{display:grid;grid-template-columns:repeat(32,1fr);gap:2px;max-width:320px;margin-top:3px;}
.xa-dots i{display:block;height:6px;background:#b9b4a6;}
.xa-dots i.on{background:${PINK};}
.card button.xa-look{display:block;width:100%;min-height:40px;margin-bottom:4px;text-align:left;}
.card button.xa-look.on{background:#ffdcee;border-style:inset;}
.xa-tile{position:absolute;width:30%;height:48px;box-sizing:border-box;display:grid;place-items:center;text-align:center;background:#fffdf5;border:1px solid #777;font-size:12px;line-height:1.1;transition:left .6s ease-in-out,top .6s ease-in-out;}
`;

let board = null;

/** One tab bar per credits card. The first tab that asks gets to be open. */
function tabs(ctx) {
  const slot = ctx.layer('credits');
  if (board?.slot === slot && board.root.isConnected) return board;
  ctx.css(CSS);
  const root = h('div', '', null, slot);
  const top = h('div', '', null, root);
  top.className = 'xa-top';
  const bar = h('div', '', null, root);
  bar.className = 'xa-tabs';
  bar.setAttribute('role', 'tablist');
  const panel = h('div', '', null, root);
  const all = [];
  const show = (t) => {
    for (const x of all) { x.btn.setAttribute('aria-selected', String(x === t)); x.node.hidden = x !== t; }
    t.onShow?.();
  };
  board = {
    slot, root, top,
    add(label, onShow) {
      const btn = h('button', '', label, bar);
      btn.type = 'button';
      btn.setAttribute('role', 'tab');
      const node = h('div', '', null, panel);
      const t = { btn, node, onShow };
      all.push(t);
      btn.addEventListener('click', () => show(t));
      node.hidden = all.length > 1;
      btn.setAttribute('aria-selected', String(all.length === 1));
      if (all.length === 1) onShow?.();
      return node;
    },
  };
  return board;
}

const fill = (s, map) => Object.entries(map).reduce((acc, [k, v]) => acc.replaceAll(`{${k}}`, v), s);
const routeNames = (ctx) => Object.keys(FORKS).filter((f) => ctx.state.picks[f]).map((f) => FORKS[f].opts[ctx.state.picks[f]].name);
const endedId = (ctx) => ctx.state.ended?.id || ctx.life.routes[0]?.ending || null;
const endTitle = (id) => ENDINGS[id]?.title || BY_ID[id]?.name || '';
const today = () => new Date().toISOString().slice(0, 10);
function hash(s) { let x = 2166136261; for (const c of s) { x ^= c.charCodeAt(0); x = Math.imul(x, 16777619); } return x >>> 0; }

const button = (label, parent, primary = false) => {
  const b = h('button', '', label, parent);
  b.type = 'button';
  if (primary) b.className = 'primary';
  return b;
};

/** A real line on the player's own press: share sheet if there is one, otherwise the clipboard. */
async function sendOut(text) {
  if (navigator.share) { try { await navigator.share({ text, url: LINK }); return true; } catch { /* cancelled */ return false; } }
  try { await navigator.clipboard.writeText(`${text} ${LINK}`); return true; } catch { return false; }
}

export const AFTER_UNLOCKS = {
  /* Your path lit up; what you have never picked stays a question mark. */
  z_map: {
    mount(ctx) {
      const t = tabs(ctx);
      /* Go Outside closes politely, and only because you pressed. */
      if (endedId(ctx) === 'outside') {
        const out = button(C.closeTab, t.top);
        out.addEventListener('click', () => {
          if (ctx.hosted) { ctx.host('exit', { reason: 'back' }); return; }
          window.close();
          ctx.after(250, () => { out.textContent = C.closeTabHard; out.disabled = true; });
        });
      }
      const node = t.add(C.tabs.map);
      h('div', '', AFTER_RUN.map.title, node).className = 'xa-h';
      const old = new Set(ctx.life.routes.flatMap((r) => r.route));
      const lit = [];
      for (const [f, fork] of Object.entries(FORKS)) {
        h('div', 'font-size:13px;color:#444;', fork.name, node);
        const row = h('div', '', null, node);
        row.className = 'xa-chips';
        for (const [lane, opt] of Object.entries(fork.opts)) {
          const mine = ctx.state.picks[f] === lane;
          const known = mine || old.has(lane);
          const c = h('div', '', known ? opt.name : AFTER_RUN.map.unseen, row);
          c.className = `xa-chip${known ? ' old' : ''}`;
          if (mine) lit.push(c);
        }
      }
      const id = endedId(ctx);
      if (id) {
        const c = h('div', 'display:inline-block;', endTitle(id), node);
        c.className = 'xa-chip old';
        lit.push(c);
      }
      lit.forEach((c, i) => { const go = () => c.classList.add('lit'); if (ctx.gentle) go(); else ctx.after(250 + i * 300, go); });
      return {};
    },
  },

  /* A card per ending, hidden until reached. Press one you have seen to read it again. */
  z_ends: {
    mount(ctx) {
      const node = tabs(ctx).add(C.tabs.ends);
      const ids = Object.keys(ENDINGS);
      const got = ids.filter((id) => ctx.life.endings[id]);
      h('div', '', fill(AFTER_RUN.ends.title, { n: got.length }), node).className = 'xa-h';
      const grid = h('div', '', null, node);
      grid.className = 'xa-grid';
      const read = h('div', '', pick(ctx, got.length >= ids.length ? AFTER_RUN.ends.nudge.slice(3) : got.length <= 1 ? AFTER_RUN.ends.nudge.slice(0, 1) : AFTER_RUN.ends.nudge.slice(1, 3)), node);
      read.className = 'xa-note';
      for (const id of ids) {
        const seen = !!ctx.life.endings[id];
        const b = button(seen ? ENDINGS[id].title : AFTER_RUN.ends.locked, grid);
        b.className = `xa-end${seen ? ' got' : ''}`;
        b.addEventListener('click', () => {
          if (seen) read.textContent = ctx.brand.text(ENDINGS[id].result);
          else if (!ctx.gentle) ctx.anim(b, [{ transform: 'translateX(-3px)' }, { transform: 'translateX(3px)' }, { transform: 'none' }], { duration: 160 });
        });
      }
      return {};
    },
  },

  /* What you tend to pick, from this player's own runs. Real percentages need a server; nothing here is made up. */
  z_pct: {
    mount(ctx) {
      const node = tabs(ctx).add(C.tabs.pct);
      const runs = ctx.life.routes;
      for (const [f, fork] of Object.entries(FORKS)) {
        const mine = ctx.state.picks[f];
        if (!mine) continue;
        h('div', 'font-size:14px;margin-top:4px;', fork.name, node);
        const counts = Object.keys(fork.opts).map((lane) => ({ lane, n: runs.filter((r) => r.route.includes(lane)).length }));
        const total = Math.max(1, counts.reduce((a, c) => a + c.n, 0));
        for (const c of counts) {
          if (!c.n && c.lane !== mine) continue;
          const pct = Math.round((c.n / total) * 100);
          const bar = h('div', '', null, node);
          bar.className = `xa-bar${c.lane === mine ? ' you' : ''}`;
          const fillBar = h('i', `width:${pct}%;`, null, bar);
          const lab = h('span', '', null, bar);
          h('b', 'font-weight:600;', `${fork.opts[c.lane].name}${c.lane === mine ? ` (${AFTER_RUN.pct.you})` : ''}`, lab);
          h('b', 'font-weight:600;', `${pct}%`, lab);
          void fillBar;
        }
      }
      h('div', '', runs.length < 2 ? C.pctNone : C.pctNote, node).className = 'xa-note';
      return {};
    },
  },

  /* A card with your route and ending. The only thing that leaves is what you press Share on. */
  z_share: {
    mount(ctx) {
      const node = tabs(ctx).add(C.tabs.share);
      const S = AFTER_RUN.share;
      const route = routeNames(ctx).join(', ') || BY_ID.start.name;
      const id = endedId(ctx);
      const min = Math.max(1, Math.round(ctx.state.clock / 60000));
      const lines = [fill(S.diagnosis, { route }), fill(S.ending, { ending: endTitle(id) }), fill(S.minutes, { min }).replace(/^1 minutes/, '1 minute')];
      const card = h('div', '', null, node);
      card.className = 'xa-share';
      h('b', '', ctx.brand.game, card);
      for (const l of lines) h('div', '', ctx.brand.text(l), card);
      const a = button(ctx.brand.text(S.play), card);
      a.style.cssText = 'display:block;margin:6px auto 0;min-height:36px;background:none;border:0;color:#c23c84;text-decoration:underline;';
      a.addEventListener('click', () => ctx.openLink(LINK));
      const b = button(S.button, node, true);
      b.addEventListener('click', async () => {
        const ok = await sendOut(lines.join('. '));
        if (ok) { b.textContent = S.copied; ctx.anim(card, [{ transform: 'rotate(-1deg)' }, { transform: 'rotate(1deg) scale(1.03)' }, { transform: 'rotate(-1deg)' }], { duration: 400 }); }
      });
      return {};
    },
  },

  /* How many unlocks you have ever seen. Always in view, above the tabs. */
  z_seen: {
    mount(ctx) {
      const top = tabs(ctx).top;
      /* Everything on the list: bought rows, this credits card's own rows, and the moods met so far. */
      const total = ITEMS.length;
      const moods = { ...(ctx.store.get('moods') || {}) };
      if (ctx.state.mood) { moods[ctx.state.mood] = 1; moods.m_mood = 1; }
      ctx.store.set('moods', moods);
      const onCard = ITEMS.filter((i) => i.group === 'after').length;
      const now = Object.keys(ctx.life.seen).length + onCard + Object.keys(moods).length;
      const prev = Math.min(now, ctx.store.get('prev') ?? 0);
      ctx.store.set('prev', now);
      const seenLine = pick(ctx, AFTER_RUN.seen.lines);
      const label = h('div', 'font-weight:700;font-variant-numeric:tabular-nums;', '', top);
      const dots = h('div', '', null, top);
      dots.className = 'xa-dots';
      const list = Array.from({ length: 96 }, () => h('i', '', null, dots));
      const paint = (n) => {
        label.textContent = `${fill(AFTER_RUN.seen.label, { n: ctx.fmt(n) }).replace('283', String(total))}. ${seenLine}`;
        const on = Math.round((n / total) * list.length);
        list.forEach((d, i) => d.classList.toggle('on', i < on));
      };
      if (ctx.gentle || prev === now) { paint(now); return {}; }
      let n = prev;
      paint(n);
      const step = Math.max(1, Math.ceil((now - prev) / 30));
      const stop = ctx.every(40, () => { n = Math.min(now, n + step); paint(n); if (n >= now) stop(); });
      return {};
    },
  },

  /* Keep one Look you have owned; the next run starts with it. */
  z_perk: {
    mount(ctx) {
      const node = tabs(ctx).add(C.tabs.perk);
      h('div', '', AFTER_RUN.perk.title, node).className = 'xa-h';
      const looks = ITEMS.filter((i) => i.lane === 'look' && ctx.life.seen[i.id]);
      if (!looks.length) { h('div', '', C.perkNone, node).className = 'xa-note'; return {}; }
      const note = h('div', '', ctx.life.perk ? C.perkKept : AFTER_RUN.perk.line, node);
      note.className = 'xa-note';
      const list = h('div', 'margin-top:6px;max-height:220px;overflow-y:auto;', null, node);
      const buttons = new Map();
      const paint = () => { for (const [id, b] of buttons) b.classList.toggle('on', ctx.life.perk === id); };
      const choose = (id) => { ctx.setPerk(id); note.textContent = id ? C.perkKept : AFTER_RUN.perk.line; paint(); };
      for (const it of looks) {
        const b = button('', list);
        b.className = 'xa-look';
        h('span', `font-family:${EMOJI_FONT};margin-right:6px;`, it.emoji, b);
        b.append(ctx.brand.text(it.name));
        b.addEventListener('click', () => choose(it.id));
        buttons.set(it.id, b);
      }
      const drop = button(C.perkDrop, node);
      drop.style.marginTop = '4px';
      drop.addEventListener('click', () => choose(null));
      paint();
      return {};
    },
  },

  /* One route for the date, the same on every copy of the game. A target: the engine has no way to force picks. */
  z_daily: {
    mount(ctx) {
      const node = tabs(ctx).add(C.tabs.daily);
      const D = AFTER_RUN.daily;
      const day = today();
      const route = Object.entries(FORKS).map(([f, fork]) => {
        const lanes = Object.keys(fork.opts);
        const lane = lanes[hash(`${day}:${f}`) % lanes.length];
        return { f, lane, name: fork.opts[lane].name };
      });
      const theme = route[route.length - 1].name;
      h('div', '', fill(D.title, { theme }).replace(/ a ([AEIOU])/, ' an $1'), node).className = 'xa-h';
      const chips = h('div', '', null, node);
      chips.className = 'xa-chips';
      let matched = 0;
      for (const r of route) {
        const same = ctx.state.picks[r.f] === r.lane;
        if (same) matched += 1;
        const c = h('div', '', r.name, chips);
        c.className = `xa-chip old${same ? ' lit' : ''}`;
      }
      h('div', '', fill(C.dailyMatch, { n: matched }), node).className = 'xa-note';

      /* The board is this player's runs today, nobody else's. */
      let log = ctx.store.get('log');
      if (!log || log.day !== day) log = { day, runs: [] };
      const key = `${ctx.state.seed}:${Math.round(ctx.state.clock)}`;
      const minutes = Math.max(1, Math.round(ctx.state.clock / 60000));
      if (ctx.state.ended && !log.runs.some((r) => r.key === key) && matched === route.length) log.runs.push({ key, minutes });
      ctx.store.set('log', log);
      h('div', 'margin-top:8px;font-weight:700;', D.board, node);
      const best = [...log.runs].sort((a, b) => a.minutes - b.minutes).slice(0, 4);
      if (!best.length) h('div', '', C.dailyEmpty, node).className = 'xa-note';
      best.forEach((r, i) => h('div', `display:flex;justify-content:space-between;padding:1px 6px;background:${PINK};color:#fff;margin-top:2px;`, `${i + 1} ${D.you}  ${r.minutes} min`, node));
      h('div', '', C.dailyNote, node).className = 'xa-note';
      const go = button(D.start, node);
      go.style.marginTop = '6px';
      go.addEventListener('click', () => document.querySelector('.card.credits .card-buttons .primary')?.click());
      return {};
    },
  },

  /* The side unlocks come in a different order each run after the first. The sim does it; this shows it happening. */
  z_shuffle: {
    mount(ctx) {
      const node = tabs(ctx).add(C.tabs.shuffle, () => { paint?.(); });
      const S = AFTER_RUN.shuffle;
      h('div', '', fill(S.label, { n: ctx.life.runs + 1 }), node).className = 'xa-h';
      const side = ITEMS.filter((i) => i.lane === 'side').slice(0, 6);
      const yard = h('div', 'position:relative;height:108px;', null, node);
      const pos = (i) => [`${(i % 3) * 33.3}%`, `${Math.floor(i / 3) * 54}px`];
      const list = side.map((it) => { const t = h('div', '', null, yard); t.className = 'xa-tile'; h('div', `font:18px/1 ${EMOJI_FONT};`, it.emoji, t); h('div', '', ctx.brand.text(it.name), t); return t; });
      let order = list.map((_, i) => i);
      var paint = () => { list.forEach((t, i) => { const [x, y] = pos(order[i]); t.style.left = x; t.style.top = y; }); };
      paint();
      if (!ctx.gentle) ctx.every(1800, () => { order = order.map((v, i) => order[(i + 1 + (order[0] % 2)) % order.length]); paint(); });
      h('div', '', S.line, node).className = 'xa-note';
      h('div', '', C.shuffleNote, node).className = 'xa-note';
      return {};
    },
  },

  /* Your run as text. Copy on press; paste into the box and load it. */
  z_save: {
    mount(ctx) {
      const node = tabs(ctx).add(C.tabs.save);
      const S = AFTER_RUN.save;
      h('div', '', S.title, node).className = 'xa-h';
      const row = h('div', 'display:flex;flex-wrap:wrap;gap:6px;', null, node);
      const copy = button(S.copy, row);
      const paste = button(S.paste, row);
      const box = h('textarea', 'width:100%;margin-top:8px;font:12px/1.2 Consolas,"Courier New",monospace;', null, node);
      box.rows = 3;
      box.spellcheck = false;
      box.hidden = true;
      box.setAttribute('aria-label', S.title);
      const load = button(S.load, node);
      load.hidden = true;
      load.style.marginTop = '6px';
      const note = h('div', '', '', node);
      note.className = 'xa-note';
      copy.addEventListener('click', async () => {
        const text = serialize(ctx.state, ctx.life, {});
        box.hidden = false;
        box.readOnly = true;
        box.value = text;
        load.hidden = true;
        box.select?.();
        try { await navigator.clipboard.writeText(text); note.textContent = AFTER_RUN.share.copied; } catch { note.textContent = ''; }
      });
      paste.addEventListener('click', () => { box.hidden = false; box.readOnly = false; box.value = ''; load.hidden = false; box.focus(); });
      load.addEventListener('click', () => {
        if (!parse(box.value)) { note.textContent = S.bad; return; }
        try { localStorage.setItem(SAVE_KEY, box.value); } catch { note.textContent = S.bad; return; }
        note.textContent = S.done;
        location.reload();
      });
      return {};
    },
  },
};
