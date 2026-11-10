/* ui/recap/recap.js - the end-of-run card for every ending (owner 2026-11-09, mockup artifact SzGXu9tb9BgViHRC5AfF2r:
 * "simplify ... nest options ... nudge ... cartoonish like the ad card ... show, dont tell").
 *
 * One comic page, no tab bar. At rest it asks one thing: Renew or New run. What used to be eight tabs lives inside the
 * panel it belongs to and opens in place:
 *   splash   the ending, its line (an "off" in it is a red stamp), run time + unlocks as rubber stamps
 *   road     your five picks as clip art; peel the corner for the roads not taken (Map) with tallies (Choices)
 *   coll     13 ending cards, tap one to read it (Endings); the seen number and meter (Seen Counter)
 *   next     Renew, Take one along (Keep One Look), Today's road ticket (Daily Route), New run, the shuffling tiles
 *   poster   every ending plays the shareable run poster, with Share and Save stickers beneath it
 * The pieces are filled by the after-run unlocks (ui/unlocks/extras/after.js) through recapFor(layer).fill.*; a piece
 * nothing fills stays out of sight. The intro (ui/recap/intro.js) drops every piece onto the card. */

import { ENDINGS } from '../../data/content/endings.js';
import { FORKS } from '../../data/items.js';
import { OFF_SHELF } from '../../sim/economy.js';
import { THEME_PALETTES, mixHex } from '../palette.js';
import { iconFor } from '../icons.js';
import { setRich } from '../rich-text.js';
import { brandText } from '../../data/brand.js';
import { ADCARD_COPY } from '../../data/content/adcard.js';
import { mountRunPoster } from '../adcard/recap.js';
import { beatCue, playCardCue } from '../adcard/stage.js';
import { crayonStarSvg, starVariant, LOOPY, RUSHED } from '../unlocks/basics/gold-fx.js';
import { RECAP_COPY as R } from '../unlocks/extras/copy.js';
import { createIntro, SOUNDS, SCALE, arrowPath } from './intro.js';
import { BOOK_EVENT, newInRun, stickerArt } from '../sticker-book.js';

const CLIP = 'assets/clipart/';
const SOURCE = 'recap';
/** A fork option's picture: the clip-art shape the shop already uses for that idea. */
const LANE_ART = { eyes: 'eye', ears: 'headphones', sweet: 'moon', strict: 'hand', slides: 'gallery', scroll: 'phone', blind: 'eraser', sell: 'billboard',
  premium: 'shield', trance: 'spiral', bimbo: 'bubbles', drone: 'robot', sissy: 'bow', goon: 'heart', locked: 'lock', censored: 'ink', infection: 'bug' };
/** Each theme's own ending: its pill reads "Drone ending"; any other ending reads "The end". */
/* The theme's own ending (fate cards, data/fate.js: Goon's is the Gooning Museum; EMI Takeover is open to every theme). */
const THEME_END = { trance: 'tr_end', bimbo: 'bi_end', drone: 'dr_end', sissy: 'si_end', goon: 'go_mus', locked: 'lo_end', censored: 'ce_end', infection: 'in_end' };
const REG = new WeakMap();
const CSS_URL = new URL('./recap.css', import.meta.url).href;

/** The recap API for an element inside the card (the after-run unlocks get it from ctx.layer('credits')). */
export function recapFor(el) {
  const root = el?.closest?.('.rr') || el;
  return root ? REG.get(root) || null : null;
}

export function loadRecapCss(doc) {
  let link = doc.querySelector('link[data-recap]');
  if (!link) {
    link = doc.createElement('link');
    link.rel = 'stylesheet'; link.href = CSS_URL; link.dataset.recap = '1';
    doc.head.append(link);
  }
  return link;
}

const fill = (s, map) => Object.entries(map).reduce((a, [k, v]) => a.replaceAll(`{${k}}`, v), String(s));
const lum = (hex) => { const n = parseInt(String(hex).slice(1), 16); return ((n >> 16) * .299 + ((n >> 8) & 255) * .587 + (n & 255) * .114) / 255; };

/** The card's colours from the run's theme (no theme yet: the house pink). */
export function recapColours(run) {
  const p = THEME_PALETTES[run?.picks?.f5] || THEME_PALETTES.bimbo;
  return { a: p.a, soft: p.soft, ink: p.ink, deep: mixHex(p.a, p.ink, .35), b: mixHex(p.soft, p.ink, .38), onA: lum(p.a) > .55 ? p.ink : '#fff' };
}

const STAR_SVGS = [() => crayonStarSvg(0), () => starVariant(LOOPY, 0), () => starVariant(RUSHED, 0)];
let starCache = null;
const starSvg = (i) => { starCache ||= STAR_SVGS.map((f) => { try { return f(); } catch { return ''; } }); return starCache[i % starCache.length]; };

/**
 * Opens the card. hooks: { renew, renewDust, renewCurrency, keepPlaying, canKeepPlaying, newRun, routeNames }.
 * openLink(url) follows the game's link rule (real press only). Returns the root element (id l-credits).
 */
export function openRunRecap({ cards, doc, id, run, life, hooks, audio, name = '', stats, openLink, gentle = () => false }) {
  const win = doc.defaultView;
  loadRecapCss(doc);
  const item = ENDINGS[id] || {};
  const title = brandText(item.title || id);
  const C = recapColours(run);
  const motion = win.matchMedia('(prefers-reduced-motion: reduce)');
  const calm = () => gentle() || motion.matches || doc.body.classList.contains('gentle');

  /* ---- tiny DOM kit ---- */
  const h = (tag, cls, text, parent) => { const e = doc.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; parent?.append(e); return e; };
  const clip = (shape, cls = 'rr-clip') => { const i = doc.createElement('img'); i.className = cls; i.src = CLIP + shape + '.webp'; i.alt = ''; i.decoding = 'async'; return i; };
  const icon = (itemId, cls = 'rr-ic') => { const box = h('span', cls); try { const s = iconFor(doc, { id: itemId }); s.classList.add('rr-icon'); box.append(s); } catch { box.append(clip('box')); } return box; };
  const clipBox = (shape, cls = 'rr-ic') => { const box = h('span', cls); box.append(clip(shape)); return box; };
  const button = (cls, parent, label) => { const b = h('button', cls, null, parent); b.type = 'button'; if (label) b.setAttribute('aria-label', label); return b; };

  /* ---- sound: the game's own files, one mixer channel while the card is up ---- */
  const controller = new AbortController();
  let wasQuiet;
  function openChannel() {
    if (!audio || wasQuiet !== undefined) return;
    wasQuiet = audio.quiet;
    audio.solo((sid) => sid === SOURCE);
    audio.setQuiet(false);
  }
  const sfx = (nameKey, gain = .25, rate = 1) => {
    if (!audio || controller.signal.aborted || doc.hidden || root.closest('[inert]')) return;
    const url = SOUNDS[nameKey];
    if (url) audio.play(url, { bus: 'sfx', gain, rate, source: SOURCE, sourceLabel: title, signal: controller.signal });
  };
  function warm() {
    // the kit drops a cue whose file takes over 250 ms to arrive; play each once at zero gain so the intro finds it loaded
    Object.values(SOUNDS).forEach((url, i) => win.setTimeout(() => {
      if (!controller.signal.aborted) audio?.play(url, { bus: 'sfx', gain: 0, source: SOURCE, sourceLabel: title, signal: controller.signal });
    }, i * 95));
  }
  let lastHover = 0;
  const hoverNote = (i) => { const now = performance.now(); if (now - lastHover < 70) return; lastHover = now; sfx(SCALE[Math.max(0, i) % SCALE.length], .1); };

  /* ---- the card ---- */
  const root = h('div', 'rr');
  root.id = 'l-credits';
  root.setAttribute('aria-label', R.label);
  for (const [k, v] of Object.entries({ a: C.a, soft: C.soft, ink: C.ink, deep: C.deep, b: C.b, 'on-a': C.onA })) root.style.setProperty('--rr-' + k, v);
  const fx = h('div', 'rr-fx', null, root);
  fx.setAttribute('aria-hidden', 'true');
  const col = h('div', 'rr-col', null, root);
  const posterCol = h('div', 'rr-poster', null, root);
  posterCol.dataset.rr = 'poster';

  // 1 splash
  const splash = h('section', 'rr-panel rr-splash', null, col);
  splash.dataset.rr = 'splash';
  const kind = h('span', 'rr-kind', null, splash);
  const theme = run.picks?.f5, themeName = theme && FORKS.f5?.opts?.[theme]?.name;
  kind.append(icon(id), themeName && THEME_END[theme] === id ? fill(R.ending, { name: brandText(themeName) }) : R.endingPlain);
  const heading = h('h2', 'rr-title', title, splash);
  heading.id = 'rr-title';
  const srow = h('div', 'rr-srow', null, splash);
  const line = h('p', 'rr-line', null, srow);
  setRich(line, item.result || '');
  stampOffs(line);
  const stamps = h('div', 'rr-stamps', null, srow);
  const st1 = h('div', 'rr-stamp', null, stamps); st1.title = R.time; st1.append(clipBox('clock'), stats.time);
  const st2 = h('div', 'rr-stamp', null, stamps); st2.title = R.unlocks; st2.append(clipBox('key'), String(stats.unlocks));
  /* Sticker Book: what this run put in the book, as little stickers; a tap opens the book (ui/sticker-book.js). */
  const fresh = newInRun(life);
  if (fresh.length) {
    const stars = [crayonStarSvg(0), starVariant(LOOPY, 0), starVariant(RUSHED, 0)].map((x) => `data:image/svg+xml;charset=utf-8,${encodeURIComponent(x)}`);
    const sb = button('rr-stickers', splash, `${R.stickers}: ${fresh.length}`);
    h('span', 'rr-stlabel', `${R.stickers} · ${fresh.length}`, sb);
    const row = h('span', 'rr-strow', null, sb);
    for (const s of fresh.slice(0, 7)) { const a = stickerArt(s, life, stars); const i = h('img', a.cls, null, row); i.src = a.src; i.alt = ''; i.decoding = 'async'; }
    sb.addEventListener('click', () => doc.dispatchEvent(new win.CustomEvent(BOOK_EVENT)));
  }

  // 2 road
  const road = h('section', 'rr-panel rr-road', null, col);
  road.dataset.rr = 'road';
  h('span', 'rr-cap', R.road, road);
  const peel = button('rr-peel', road, R.otherRoads);
  peel.append(clipBox('sign'));
  peel.setAttribute('aria-pressed', 'false');
  peel.hidden = true;
  const peelTag = h('span', 'rr-peeltag', R.otherRoads, road);
  peelTag.hidden = true;
  const strip = h('div', 'rr-strip', null, road);
  const picks = Object.entries(FORKS).filter(([f]) => run.picks?.[f]);
  const steps = {};
  picks.forEach(([f, fork], i) => {
    const lane = run.picks[f];
    const step = h('div', 'rr-step' + (f === 'f5' ? ' theme' : ''), null, strip);
    const fr = h('div', 'rr-fr', null, step);
    fr.append(clip(LANE_ART[lane] || 'sign'));
    h('div', 'rr-word', brandText(fork.opts[lane]?.name || lane), step);
    const tallyBox = h('span', 'rr-tallybox', null, step);
    const alts = h('div', 'rr-alts', null, step);
    steps[f] = { step, tallyBox, alts, lane };
    if (i < picks.length - 1) { const a = h('div', 'rr-arrow', null, strip); a.innerHTML = `<svg viewBox="0 0 22 18" aria-hidden="true"><path d="${arrowPath(i)}"/></svg>`; }
  });
  if (!picks.length) road.hidden = true;

  // 3 endings + seen
  const coll = h('section', 'rr-panel rr-coll', null, col);
  coll.dataset.rr = 'coll';
  coll.hidden = true;
  h('span', 'rr-cap', R.endings, coll);
  const endsBox = h('div', '', null, coll);
  const seenBox = h('div', 'rr-seen', null, coll);
  seenBox.hidden = true;

  // 4 next run
  const next = h('section', 'rr-panel rr-next', null, col);
  next.dataset.rr = 'next';
  h('span', 'rr-cap', R.next, next);
  const nextRow = h('div', 'rr-nextrow', null, next);
  const dust = hooks.renewDust?.();
  const renew = button('rr-renew', nextRow);
  if (hooks.renew) {
    renew.dataset.dust = Number.isFinite(dust) ? String(Math.round(dust)) : '0';
    const star = h('span', 'rr-star', null, renew); star.innerHTML = starSvg(0);
    h('span', 'rr-plus', Number.isFinite(dust) ? '+' + Math.round(dust) : '+', renew);
    const what = h('span', 'rr-what', null, renew);
    h('b', '', hooks.renewCurrency || 'Stardust', what); what.append(R.renew);
    for (const [cls, k] of [['rr-twinkle t1', 1], ['rr-twinkle t2', 2]]) { const t = h('span', cls, null, renew); t.innerHTML = starSvg(k); }
  } else renew.hidden = true;
  const side = h('div', 'rr-side', null, nextRow);
  const keepBtn = button('rr-tool', side); keepBtn.hidden = true;
  const dailyBtn = button('rr-tool rr-ticket', side); dailyBtn.hidden = true;
  const restart = h('div', 'rr-restart', null, side);
  const newRun = button('rr-newrun', restart);
  newRun.append(clipBox('button'), R.newRun);
  const keepPlay = button('rr-newrun inf', restart);
  keepPlay.append(clipBox('infinity'), R.keepPlaying);
  keepPlay.hidden = !(hooks.keepPlaying && hooks.canKeepPlaying?.());
  const deck = h('div', 'rr-deck', null, restart);
  deck.hidden = true;
  const drawer = h('div', 'rr-drawer', null, nextRow);
  drawer.hidden = true;

  // 5 poster or hero, stickers, credit
  h('div', 'rr-pshadow', null, posterCol).setAttribute('aria-hidden', 'true');
  const tilt = h('div', 'rr-tilt', null, posterCol);
  const frame = h('div', 'rr-frame', null, tilt);
  h('i', 'rr-glare', null, tilt).setAttribute('aria-hidden', 'true');
  const poster = mountRunPoster({ doc, host: frame, run, life, name, autoplay: false, loop: true, unit: id === 'dr_end', onBeat: (beat, i) => {
    if (controller.signal.aborted || root.closest('[inert]') || doc.hidden) return;
    openChannel();
    playCardCue(audio, doc, beatCue(beat, i), { source: SOURCE, sourceLabel: title, signal: controller.signal });
  } });
  const pocket = h('div', 'rr-pocket', null, posterCol);
  const shareBtn = button('rr-sticker', pocket, R.share);
  shareBtn.setAttribute('aria-expanded', 'false');
  shareBtn.append(clip('link')); h('span', 'rr-lbl', R.share, shareBtn);
  const saveBtn = button('rr-sticker', pocket, R.save); saveBtn.hidden = true;
  saveBtn.append(clip('scroll')); h('span', 'rr-lbl', R.save, saveBtn);
  saveBtn.setAttribute('aria-expanded', 'false');
  const savePop = h('div', 'rr-pop', null, pocket); savePop.hidden = true;
  const sharePop = h('div', 'rr-pop', null, pocket); sharePop.hidden = true;
  const formats = h('div', 'rr-row', null, sharePop);
  const shareStatus = h('div', 'rr-note', '', sharePop); shareStatus.setAttribute('role', 'status');
  const formatButtons = [];
  for (const format of ['png', 'gif', 'video']) {
    const option = button('rr-chip', formats);
    option.textContent = ADCARD_COPY.buttons[format];
    formatButtons.push(option);
    option.addEventListener('click', async () => {
      for (const b of formatButtons) b.disabled = true;
      shareStatus.textContent = R.preparingCard;
      try {
        const { status } = await poster.export(format, { filename: 'terms-and-conditioning-' + id, progress: (_i, _n, line) => { shareStatus.textContent = line; } });
        shareStatus.textContent = status === 'saved' ? ADCARD_COPY.saved : status === 'shared' ? R.sharedCard : '';
      } catch {
        shareStatus.textContent = R.exportFailed;
      } finally {
        for (const b of formatButtons) b.disabled = false;
      }
    });
  }
  shareBtn.addEventListener('click', () => {
    savePop.hidden = true; saveBtn.setAttribute('aria-expanded', 'false');
    sharePop.hidden = !sharePop.hidden;
    shareBtn.setAttribute('aria-expanded', String(!sharePop.hidden));
    sfx('pop2', .26);
  });
  const credit = button('rr-credit', posterCol);
  h('span', 'rr-by', R.by, credit); h('span', 'rr-lab', R.studio, credit); h('span', 'rr-url', R.site, credit);
  credit.addEventListener('click', () => openLink?.('https://cclabs.app/'));

  /* ---- toasts ---- */
  let toastEl = null, toastT = 0;
  function toast(text) {
    toastEl?.remove();
    toastEl = h('div', 'rr-toast', text, doc.body);
    sfx('chip', .14, 1.15);
    if (!calm()) toastEl.animate([{ transform: 'translateX(-50%) translateY(40px) rotate(-8deg) scale(.7)', opacity: 0 }, { transform: 'translateX(-50%) translateY(-4px) rotate(-1deg) scale(1.04)', opacity: 1, offset: .6 }, { transform: 'translateX(-50%) rotate(-1.5deg)', opacity: 1 }], { duration: 340, easing: 'ease-out' });
    win.clearTimeout(toastT);
    const t = toastEl;
    toastT = win.setTimeout(() => { if (calm()) { t.remove(); return; } t.animate([{ opacity: 1 }, { opacity: 0, transform: 'translateX(-50%) translateY(30px) rotate(4deg)' }], { duration: 180, easing: 'ease-in' }).onfinish = () => t.remove(); }, 2300);
  }
  const pop = (node) => { if (!calm()) node.animate([{ transform: 'scale(.6) rotate(-8deg)' }, { transform: 'scale(1.15) rotate(4deg)' }, { transform: 'none' }], { duration: 380, easing: 'cubic-bezier(.3,1.6,.5,1)' }); };

  /* ---- drawers: grow out of their tool, go back into it, one at a time ---- */
  let openTool = null, outTok = 0;
  function closeDrawer() {
    const was = openTool;
    if (!was) { drawer.hidden = true; return; }
    was.setAttribute('aria-expanded', 'false');
    openTool = null;
    const tok = ++outTok;
    if (calm() || drawer.hidden) { drawer.hidden = true; drawer.replaceChildren(); return; }
    sfx('tally', .3, .8);
    drawer.animate([{ opacity: 1, transform: 'none' }, { opacity: 0, transform: 'translateY(-8px) scale(.94)' }], { duration: 150, easing: 'ease-in' }).onfinish = () => {
      if (tok === outTok && !openTool) { drawer.hidden = true; drawer.replaceChildren(); }
    };
  }
  function openDrawer(tool, fillFn) {
    if (openTool === tool) { closeDrawer(); return; }
    openTool?.setAttribute('aria-expanded', 'false');
    outTok++;
    drawer.getAnimations().forEach((a) => a.cancel());
    drawer.replaceChildren();
    openTool = tool; tool.setAttribute('aria-expanded', 'true');
    fillFn(drawer); drawer.hidden = false;
    const r = tool.getBoundingClientRect(), p = drawer.getBoundingClientRect();
    const tail = Math.max(14, Math.min(p.width - 40, r.left - p.left + 22));
    drawer.style.setProperty('--tail', tail + 'px');
    sfx('pop2', .2, 1.1);
    if (calm()) return;
    drawer.style.transformOrigin = `${tail + 10}px 0`;
    drawer.animate([{ opacity: 0, transform: 'translateY(-12px) scale(.85)' }, { opacity: 1, transform: 'scale(1.02)', offset: .65 }, { opacity: 1, transform: 'none' }], { duration: 300, easing: 'ease-out' });
    [...drawer.querySelectorAll('.rr-look, .rr-r5, .rr-board, .rr-go')].forEach((el, i) => {
      el.animate([{ opacity: 0, transform: 'translateY(10px) scale(.6)' }, { opacity: 1, transform: 'scale(1.08)', offset: .6 }, { opacity: 1, transform: 'none' }], { duration: 280, delay: 60 + i * 28, fill: 'backwards', easing: 'ease-out' });
    });
  }
  function dhead(text, parent) {
    const d = h('div', 'rr-dhead', null, parent); h('span', '', text, d);
    const x = button('rr-x', d, R.close); x.textContent = 'X'; x.addEventListener('click', closeDrawer);
    return d;
  }

  /* ---- the fills the after-run unlocks call ---- */
  const api = {
    root,
    fill: {
      /** z_map: old = Set of lanes picked on earlier runs. */
      map({ old }) {
        for (const [f, s] of Object.entries(steps)) {
          for (const [lane, opt] of Object.entries(FORKS[f].opts)) {
            if (lane === s.lane) continue;
            const known = old.has(lane);
            const box = h('div', 'rr-alt', null, s.alts);
            if (known) box.append(clip(LANE_ART[lane] || 'sign')); else h('span', 'rr-q', '?', box);
            h('span', 'rr-nm', known ? brandText(opt.name) : R.notYet, box);
          }
        }
        peel.hidden = false; peelTag.hidden = false;
      },
      /** z_pct: counts[fork][lane] = runs that picked it, this player's own. */
      pct({ counts }) {
        for (const [f, s] of Object.entries(steps)) s.tallyBox.replaceChildren(tally(doc, counts[f]?.[s.lane] || 0));
      },
      /** z_ends: got = Set of ending ids seen, nudge(n) -> a line for a locked card. */
      ends({ got, nudge }) {
        coll.hidden = false;
        // The endings on offer, plus any retired one this player already has (sim/economy.js OFF_SHELF).
        const ids = Object.keys(ENDINGS).filter((eid) => !OFF_SHELF.has(eid) || got.has(eid));
        coll.dataset.got = String(got.size);
        const big = h('div', 'rr-big rr-ends-big', null, endsBox);
        big.append(String(got.size)); h('span', '', '/' + ids.length, big);
        const row = h('div', 'rr-ends', null, endsBox);
        const bub = h('div', 'rr-bubble', null, endsBox);
        const read = (eid) => { bub.replaceChildren(); h('b', '', brandText(ENDINGS[eid].title) + '. ', bub); const sp = h('span', '', null, bub); setRich(sp, ENDINGS[eid].result || ''); };
        read(got.has(id) ? id : [...got][0] || id);
        let n = 0;
        for (const eid of ids) {
          const seen = got.has(eid);
          const c = button('rr-endcard' + (seen ? ' got' : '') + (eid === id ? ' now' : ''), row, seen ? brandText(ENDINGS[eid].title) : R.notYet);
          if (seen) c.append(icon(eid, 'rr-ic')); else h('span', 'rr-q', '?', c);
          c.addEventListener('click', () => {
            const r = c.getBoundingClientRect(), p = row.getBoundingClientRect();
            bub.style.setProperty('--tail', Math.max(10, r.left - p.left + 8) + 'px');
            if (seen) {
              read(eid); sfx('pop3', .22);
              if (!calm()) {
                c.animate([{ transform: 'perspective(300px) rotateY(0)' }, { transform: 'perspective(300px) rotateY(180deg) scale(1.15)', offset: .5 }, { transform: 'perspective(300px) rotateY(360deg)' }], { duration: 420, easing: 'ease-in-out' });
                bub.animate([{ transform: 'scale(.6)', opacity: 0 }, { transform: 'scale(1.04)', opacity: 1, offset: .6 }, { transform: 'none', opacity: 1 }], { duration: 300, easing: 'ease-out' });
              }
            } else {
              bub.textContent = nudge(n++);
              sfx('denied', .3);
              if (!calm()) c.animate([{ transform: 'translateX(-3px)' }, { transform: 'translateX(3px)' }, { transform: 'none' }], { duration: 180 });
            }
          });
        }
      },
      /** z_seen: now of total ever seen, line under it. */
      seen({ now, total, line: tail }) {
        coll.hidden = false; seenBox.hidden = false;
        seenBox.dataset.n = String(now);
        const big = h('div', 'rr-big', null, seenBox); big.append(String(now)); h('span', '', '/' + total, big);
        const m = h('div', 'rr-meter', null, seenBox); const fillEl = h('i', '', null, m);
        fillEl.style.setProperty('--w', Math.min(100, (100 * now) / Math.max(1, total)).toFixed(1) + '%');
        m.setAttribute('role', 'img'); m.setAttribute('aria-label', `${now} / ${total}`);
        h('div', 'rr-tiny', R.seenTail + tail, seenBox);
      },
      /** z_perk: looks = owned Look items; current id or null; choose(id|null) -> bool. */
      perk({ looks, current, choose }) {
        if (!looks.length) return;
        keepBtn.hidden = false;
        keepBtn.setAttribute('aria-expanded', 'false');
        const slot = h('span', 'rr-slot', null, keepBtn);
        const words = h('span', '', null, keepBtn); h('span', 'rr-t', R.keep, words); const sub = h('span', 'rr-s', R.keepSub, words);
        const tag = h('span', 'rr-freetag', R.free, keepBtn);
        let kept = looks.find((l) => l.id === current) || null;
        const paint = () => {
          slot.replaceChildren();
          slot.classList.toggle('full', !!kept);
          if (kept) slot.append(icon(kept.id)); else h('span', 'rr-plusmark', '+', slot);
          sub.textContent = kept ? brandText(kept.name) : R.keepSub;
          tag.hidden = !!kept;
        };
        paint();
        keepBtn.addEventListener('click', () => openDrawer(keepBtn, (d) => {
          dhead(R.keepPick, d);
          const g = h('div', 'rr-looks', null, d);
          for (const it of [...looks, null]) {
            const b = button('rr-look', g);
            b.setAttribute('aria-pressed', String(it ? kept?.id === it.id : !kept));
            if (it) { b.append(icon(it.id)); h('span', '', brandText(it.name), b); } else { b.append(clipBox('bin')); h('span', '', R.keepNone, b); }
            b.addEventListener('click', () => {
              if (!choose(it ? it.id : null)) return;
              const from = b.querySelector('.rr-ic')?.getBoundingClientRect();
              kept = it; paint(); closeDrawer();
              if (!it) { sfx('denied', .25); return; }
              flyTo(it.id, from, slot);
            });
          }
        }));
      },
      /** z_daily: route [{name, lane, match}], best [{minutes}] (this player's runs today), title, note, empty. */
      daily({ route, best, title: dtitle, note, empty, board }) {
        dailyBtn.hidden = false;
        dailyBtn.setAttribute('aria-expanded', 'false');
        const matched = route.filter((r) => r.match).length;
        dailyBtn.append(clipBox('sign'));
        const words = h('span', '', null, dailyBtn);
        h('span', 'rr-t', fill(R.today, { n: matched, total: route.length }), words);
        const dots = h('span', 'rr-dots', null, words);
        for (const r of route) { const d = h('span', 'rr-dot' + (r.match ? ' lit' : ''), null, dots); d.append(clip(LANE_ART[r.lane] || 'sign')); }
        dailyBtn.addEventListener('click', () => openDrawer(dailyBtn, (d) => {
          dhead(dtitle || R.todayHead, d);
          const rr = h('div', 'rr-route5', null, d);
          for (const r of route) { const c = h('div', 'rr-r5' + (r.match ? ' match' : ''), null, rr); const f = h('div', 'rr-f', null, c); f.append(clip(LANE_ART[r.lane] || 'sign')); h('span', '', r.name, c); }
          const b = h('div', 'rr-board', null, d);
          if (board) h('div', 'rr-dnote', board, b);
          if (!best.length) h('div', 'rr-dnote', empty, d);
          best.forEach((r, i) => { const row = h('div', '', null, b); h('span', '', `${i + 1} ${r.who}`, row); h('span', '', r.label, row); });
          const go = button('rr-go', d); go.append(clipBox('sign'), R.ride);
          go.addEventListener('click', () => (hooks.renew ? renew : newRun).click());
          if (note) h('div', 'rr-dnote', note, d);
        }));
      },
      /** z_shuffle: a few side items; their tiles swap places now and then. */
      shuffle({ items, runNo }) {
        deck.hidden = false;
        const tiles = h('span', 'rr-tiles', null, deck);
        deck.append(fill(R.run, { n: runNo }));
        const els = items.slice(0, 3).map((it) => { const t = h('span', 'rr-tile', null, tiles); t.append(icon(it.id)); t.title = brandText(it.name); return t; });
        let order = els.map((_, i) => i);
        const place = () => els.forEach((t, i) => { t.style.left = order[i] * 37 + 'px'; });
        place();
        const t = win.setInterval(() => { if (!root.isConnected) { win.clearInterval(t); return; } if (calm() || root.closest('[inert]')) return; order = [order[2], order[0], order[1]]; place(); }, 2200);
      },
      /** z_share: send(text) -> Promise<bool> (share sheet or clipboard). */
      share({ lines, send, done }) {
        const summary = button('rr-chip', formats);
        summary.textContent = R.shareSummary;
        summary.addEventListener('click', async () => { if (await send(lines.join('. '))) toast(done); });
      },
      /** z_save: copy() -> text; load(text) -> true or an error line. */
      save({ copy, load, copied, bad }) {
        saveBtn.hidden = false;
        saveBtn.addEventListener('click', () => {
          sharePop.hidden = true; shareBtn.setAttribute('aria-expanded', 'false');
          savePop.hidden = !savePop.hidden;
          saveBtn.setAttribute('aria-expanded', String(!savePop.hidden));
          sfx('pop', .2);
          if (savePop.hidden || savePop.childElementCount) return;
          const row = h('div', 'rr-row', null, savePop);
          const c = button('rr-chip', row); c.append(clipBox('papers'), R.copySave);
          const p = button('rr-chip', row); p.append(clipBox('scroll'), R.pasteSave);
          const box = h('textarea', '', null, savePop); box.rows = 3; box.spellcheck = false; box.hidden = true; box.setAttribute('aria-label', R.save);
          const msg = h('div', 'rr-note', '', savePop);
          let loadMode = false;
          c.addEventListener('click', async () => {
            loadMode = false; box.hidden = false; box.readOnly = true; box.value = copy(); box.select?.();
            try { await win.navigator.clipboard.writeText(box.value); msg.textContent = copied; } catch { msg.textContent = ''; }
          });
          p.addEventListener('click', () => {
            if (loadMode && box.value) { const r = load(box.value); msg.textContent = r === true ? '' : (r || bad); return; }
            loadMode = true; box.hidden = false; box.readOnly = false; box.value = ''; box.focus();
          });
        });
      },
    },
  };
  REG.set(root, api);

  /* ---- a kept Look flies into its slot ---- */
  function flyTo(itemId, from, slot) {
    const target = slot.querySelector('.rr-ic');
    if (!from || !target || calm()) { sfx('equip', .32); pop(slot); return; }
    const to = target.getBoundingClientRect();
    target.style.visibility = 'hidden';
    const f = h('div', 'rr-flyer', null, doc.body);
    f.style.cssText = `left:${from.left}px;top:${from.top}px;width:${from.width}px;height:${from.height}px;`;
    f.append(icon(itemId));
    const dx = to.left - from.left, dy = to.top - from.top, s = to.width / Math.max(1, from.width);
    f.animate([{ transform: 'translate(0,0) scale(1) rotate(0)' }, { transform: `translate(${dx * .5}px,${dy * .5 - 70}px) scale(1.3) rotate(-20deg)`, offset: .5 }, { transform: `translate(${dx}px,${dy}px) scale(${s}) rotate(0)` }],
      { duration: 480, easing: 'cubic-bezier(.4,0,.3,1)' }).onfinish = () => {
      f.remove(); target.style.visibility = '';
      pop(slot); sfx('equip', .32);
      intro?.burst(intro.local(slot.getBoundingClientRect()), 7, 60);
    };
  }

  /* ---- presses ---- */
  function leave() {
    if (controller.signal.aborted) return;
    controller.abort();
    intro?.stop(false);
    poster();
    toastEl?.remove();
    if (wasQuiet !== undefined && audio) { audio.setQuiet(wasQuiet); audio.solo(null); }
  }
  renew.addEventListener('click', () => {
    if (!hooks.renew) return;
    sfx('gold', .34);
    const star = renew.querySelector('.rr-star');
    if (!calm() && star) {
      star.animate([{ transform: 'rotate(0) scale(1)' }, { transform: 'rotate(380deg) scale(1.25)', offset: .7 }, { transform: 'rotate(360deg) scale(1)' }], { duration: 620, easing: 'cubic-bezier(.2,.8,.3,1)' });
      intro?.burst(intro.local(star.getBoundingClientRect()), 14, 140);
    }
    win.setTimeout(() => { leave(); hooks.renew(); }, calm() ? 0 : 420);
  });
  newRun.addEventListener('click', () => {
    sfx('thud', .2, 1.4);
    if (!calm()) newRun.querySelector('.rr-ic')?.animate([{ transform: 'none' }, { transform: 'translateY(3px) scale(1.12,.78)', offset: .3 }, { transform: 'translateY(-2px) scale(.96,1.05)', offset: .65 }, { transform: 'none' }], { duration: 320 });
    win.setTimeout(() => { leave(); hooks.newRun(); }, calm() ? 0 : 260);
  });
  keepPlay.addEventListener('click', () => { sfx('w12', .2); leave(); hooks.keepPlaying?.(); });
  peel.addEventListener('click', () => {
    const on = road.classList.toggle('flipped');
    peel.setAttribute('aria-pressed', String(on));
    peelTag.textContent = on ? R.yourRoad : R.otherRoads;
    sfx('chip', .18, .85);
    if (!on || calm()) return;
    [...road.querySelectorAll('.rr-alt')].forEach((a, i) => a.animate([{ opacity: 0, transform: 'translateY(-16px) scale(.5) rotate(-10deg)' }, { opacity: 1, transform: 'scale(1.1)', offset: .6 }, { opacity: 1, transform: 'none' }], { duration: 320, delay: i * 45, fill: 'backwards', easing: 'ease-out' }));
  });

  /* ---- hover: a quiet note in the one key; press: a click ---- */
  const HOVERABLE = '.rr-step, .rr-endcard, .rr-tool, .rr-sticker, .rr-renew, .rr-newrun, .rr-look, .rr-chip, .rr-peel, .rr-stamp, .rr-kind, .rr-r5, .rr-go, .rr-credit';
  root.addEventListener('pointerover', (e) => {
    if (e.pointerType !== 'mouse') return;
    const t = e.target.closest(HOVERABLE);
    if (!t || t.contains(e.relatedTarget)) return;
    const sibs = [...t.parentElement.children].filter((c) => c.matches(HOVERABLE));
    hoverNote(sibs.indexOf(t) + 1);
  });
  root.addEventListener('pointerdown', (e) => { if (e.target.closest('button') && !e.target.closest('.rr-renew, .rr-newrun')) sfx('click', .3, .95 + Math.random() * .12); });

  /* ---- the poster tilts toward the pointer; its shadow stays on the table (one lamp) ---- */
  tilt.addEventListener('pointermove', (e) => {
    if (e.pointerType !== 'mouse' || calm()) return;
    const r = tilt.getBoundingClientRect(), x = (e.clientX - r.left) / r.width, y = (e.clientY - r.top) / r.height;
    tilt.classList.add('live');
    tilt.style.transform = `rotate(-1.2deg) perspective(900px) rotateX(${(.5 - y) * 9}deg) rotateY(${(x - .5) * 11}deg) scale(1.025)`;
    tilt.style.setProperty('--gx', x * 100 + '%'); tilt.style.setProperty('--gy', y * 100 + '%');
  });
  tilt.addEventListener('pointerleave', () => { tilt.classList.remove('live'); tilt.style.transform = ''; });

  /* ---- open it, then let the pieces fall ---- */
  let intro = null, skip = null;
  /** The poster column sits centred against the resting card once, so an open drawer never shoves it down. */
  function centrePoster() {
    if (openTool || !root.isConnected) return;
    if (win.getComputedStyle(col).display === 'contents') { posterCol.style.marginTop = ''; return; }
    posterCol.style.marginTop = '0px';
    posterCol.style.marginTop = Math.max(0, (col.offsetHeight - posterCol.offsetHeight) / 2) + 'px';
  }
  cards.open({
    node: root, className: 'rr-card', clear: true, guard: false, buttons: [{ label: '', keepOpen: true }],
    onOpen(backdrop) {
      backdrop.classList.add('rr-backdrop');
      backdrop.querySelector('.card-buttons')?.remove();
      const card = backdrop.querySelector('.rr-card');
      card.setAttribute('aria-labelledby', heading.id);
      card.tabIndex = -1;
      if (!card.closest('[inert]')) card.focus({ preventScroll: true });
      openChannel();
      win.addEventListener('resize', centrePoster);
      doc.fonts?.ready?.then(centrePoster);
      win.requestAnimationFrame(() => win.requestAnimationFrame(() => {
        centrePoster();
        const settle = () => { root.classList.add('settled'); skip?.remove(); skip = null; centrePoster(); };
        if (calm()) { root.classList.add('calm'); poster.start(0); sfx('chime', .28); settle(); return; }
        warm();
        intro = createIntro({ doc, root, scroller: backdrop, fx, sfx, stars: (i) => starSvg(i), poster, onSettled: settle });
        skip = button('rr-skip', backdrop); skip.textContent = R.skip;
        skip.addEventListener('click', () => intro.stop(true));
        win.setTimeout(() => { if (!controller.signal.aborted) intro.start(); }, 380);
      }));
    },
    onClose() { win.removeEventListener('resize', centrePoster); leave(); },
  });
  return root;
}

/** Hand-drawn tally marks for n runs (a dash for none). */
function tally(doc, n) {
  const NS = 'http://www.w3.org/2000/svg';
  const s = doc.createElementNS(NS, 'svg');
  s.setAttribute('class', 'rr-tally');
  const w = Math.max(1, Math.min(n, 12)) * 7 + 6;
  s.setAttribute('viewBox', `0 0 ${w} 16`); s.setAttribute('width', String(w)); s.setAttribute('aria-label', String(n));
  let d = '';
  for (let k = 0; k < Math.min(n, 12); k++) { const x = 4 + k * 7; d += `M${x} ${2 + (k % 2)} L${x + (k % 2 ? 1 : -1)} ${14 - (k % 2)} `; }
  if (!n) d = 'M3 8 L9 8';
  const p = doc.createElementNS(NS, 'path'); p.setAttribute('d', d); s.append(p);
  return s;
}

/** Every plain-word "off" in the ending line becomes a little red stamp (Unit Complete: "Shop: off. Counter: off."). */
function stampOffs(el) {
  const doc = el.ownerDocument, walk = doc.createTreeWalker(el, 4), nodes = [];
  while (walk.nextNode()) nodes.push(walk.currentNode);
  for (const t of nodes) {
    if (!/\boff\b/.test(t.nodeValue)) continue;
    const frag = doc.createDocumentFragment();
    t.nodeValue.split(/(\boff\b)/).forEach((part) => {
      if (part === 'off') { const s = doc.createElement('span'); s.className = 'rr-off'; s.textContent = part; frag.append(s); } else if (part) frag.append(part);
    });
    t.replaceWith(frag);
  }
}
