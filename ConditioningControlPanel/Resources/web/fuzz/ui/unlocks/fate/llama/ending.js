/* ui/unlocks/fate/llama/ending.js - The Panel (ll_end), the run's show between the fade and the credits (owner 2026-11-21,
 * handoff-1121/TC-LLAMA-ROUND-BRIEF.md change 2). Three scenes on paper, each with one text card in the fate card look
 * (ui/fate-look.js: black notched card, white frame, dashed inner frame), then Section 50's line and the way on:
 *   1. the panel has dragged a table into a field, your file is on it (the panel photo, a brass name plate under each llama);
 *   2. the chair reads it, chews it, spits (the chair's chew photo, then its spit photo); a red stamp: Verdict: mostly chewing;
 *   3. the new sticker (e_ll_end, already in the book: sim/save.js recordEnding), and the chair joins the Living Ad card
 *      (ui/adcard/model.js `chair`), which the recap shows next.
 * No narration and no speech: the cards are the words. Gentle visuals and reduced motion get fades only (250 ms or more),
 * nothing moves. Each scene moves on by itself, or on Next / a tap. Panic takes the stage down at once. Pictures: ./art.js
 * (real since the named llamas, handoff-1121/llamas/BRIEF.md); names: data/content/llamas.js. */

import { ENDINGS } from '../../../../data/content/endings.js';
import { STICKER_BY_ID } from '../../../../data/content/stickers.js';
import { LLAMA_BY_ID, PANEL_SEATS } from '../../../../data/content/llamas.js';
import { FATE_CARD, FATE_CHALK, FATE_RED } from '../../../fate-look.js';
import { ART } from './art.js';

/** How long each beat holds, in ms, before the show moves on by itself. Still (Gentle or reduced motion) holds the
 *  same scenes for the same reading time; only the motion inside them goes. */
export const PANEL_MS = Object.freeze({ bench: 5600, read: 1400, chew: 2600, spit: 1100, verdict: 3400, sticker: 6400, closing: 25000 });
/** The fade the run ends on: the courtroom's paper. */
export const PANEL_PAPER = '#f3ead6';
/** The whole show if nobody presses anything (ms): well under the engine's 90 s cap for an ending show. */
export const panelLength = () => Object.values(PANEL_MS).reduce((a, b) => a + b, 0);
/** Where each panel llama sits across the photo (centre, % of its width): left, chair, right. */
export const SEAT_X = Object.freeze({ margaret: 22.5, reginald: 51, henry: 78 });

const NOTCH = (n) => `polygon(${n}px 0,calc(100% - ${n}px) 0,100% ${n}px,100% calc(100% - ${n}px),calc(100% - ${n}px) 100%,${n}px 100%,0 calc(100% - ${n}px),0 ${n}px)`;
const FONT = 'var(--shop-font, "Barlow", "Segoe UI", system-ui, sans-serif)';
const CSS = `
.llp { position: absolute; inset: 0; overflow: hidden; pointer-events: auto; background: ${PANEL_PAPER};
  background-image: radial-gradient(ellipse at 50% 30%, rgba(255,255,255,.55), transparent 60%), repeating-linear-gradient(0deg, rgba(120,90,50,.035) 0 2px, transparent 2px 6px);
  color: #2a2130; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 14px; padding: 16px; box-sizing: border-box; font: 600 16px/1.3 ${FONT}; user-select: none; -webkit-user-select: none; }
.llp-scene { position: relative; width: min(94vw, calc((100vh - 230px) * 16 / 9), 900px); aspect-ratio: 16 / 9; flex: none; }
.llp-scene > * { position: absolute; display: block; }
.llp-photo { inset: 0; width: 100%; height: 100%; object-fit: cover; border-radius: 6px; box-shadow: 0 10px 28px rgba(42,33,48,.3); }
.llp-chair { top: 0; left: 50%; height: 100%; aspect-ratio: 640 / 794; translate: -50% 0; object-fit: cover; box-sizing: border-box; border: 4px solid #fff; border-radius: 6px;
  box-shadow: 0 10px 28px rgba(42,33,48,.35); opacity: 0; transform-origin: 50% 100%; }
.llp-plate { top: 85.5%; translate: -50% 0; max-width: 28%; box-sizing: border-box; padding: .25em .6em; text-align: center; font: 800 clamp(7px, 1.15vw, 11px)/1.15 Georgia, serif; letter-spacing: .04em;
  background: linear-gradient(#f3dc94, #cfa245 52%, #a87b25); border: 2px solid #2a2130; border-radius: 3px; color: #2a1a04; box-shadow: 0 2px 4px rgba(0,0,0,.35); }
.llp-plate.chair { max-width: 36%; font-size: clamp(8px, 1.35vw, 13px); }
@media (max-width: 599.98px) { .llp-plate.side { display: none; } }
.llp-sticker { left: 50%; top: 46%; width: 30%; translate: -50% -50%; filter: drop-shadow(0 6px 10px rgba(42,33,48,.3)); }
.llp-herd { left: 50%; top: 84%; translate: -50% 0; display: flex; align-items: flex-end; white-space: nowrap; padding: .25em .6em; border-radius: 6px; background: #fff; box-shadow: 0 2px 0 rgba(42,33,48,.18);
  font: clamp(14px, 2.6vw, 26px)/1 "Segoe UI Emoji", "Apple Color Emoji", "Noto Color Emoji", sans-serif; }
.llp-herd span { display: inline-block; position: relative; }
.llp-herd .chair img { display: block; width: 1.2em; height: 1.2em; margin-right: .1em; }
.llp-foot { position: relative; min-height: 132px; width: min(94vw, 620px); display: flex; flex-direction: column; align-items: center; gap: 10px; }
.llp-card { position: relative; isolation: isolate; max-width: 100%; box-sizing: border-box; padding: 20px 30px; color: ${FATE_CHALK}; text-align: center; font: 700 clamp(16px, 2.4vw, 22px)/1.3 ${FONT}; }
.llp-card::before { content: ''; position: absolute; inset: 0; z-index: -1; background: ${FATE_CARD}; clip-path: ${NOTCH(12)}; outline: 2px solid color-mix(in srgb, ${FATE_CHALK} 62%, transparent); outline-offset: -8px; }
.llp-card::after { content: ''; position: absolute; inset: 13px; border: 1px dashed ${FATE_CHALK}; opacity: .32; pointer-events: none; }
.llp-card small { display: block; margin-top: 6px; font-weight: 600; font-size: .78em; opacity: .8; }
.llp-stamp { position: absolute; right: -10px; top: -42px; rotate: -8deg; padding: 4px 10px; border: 3px solid ${FATE_RED}; color: ${FATE_RED}; background: #fff8ef;
  font: 900 clamp(13px, 2vw, 18px)/1.1 ${FONT}; letter-spacing: .04em; text-transform: uppercase; opacity: 0; }
.llp-row { display: flex; gap: 14px; align-items: center; }
.llp-btn { min-height: 44px; padding: 6px 16px; background: transparent; color: inherit; border: 0; text-decoration: underline; font: 600 15px ${FONT}; cursor: pointer; }
.llp-btn.go { border: 2px solid currentColor; text-decoration: none; border-radius: 4px; }
`;

const wait = (ctx, ms) => new Promise((res) => ctx.after(ms, res));

export async function panelEnding(ctx) {
  const end = ENDINGS.ll_end;
  const still = !!(ctx.gentle || ctx.reduced);
  ctx.css(CSS);
  const root = ctx.el('div', '', null, 'ending');
  root.className = 'llp';
  const doc = root.ownerDocument;
  const make = (tag, cls, parent, text) => { const e = doc.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; parent.append(e); return e; };
  const pic = (cls, src, parent) => { const e = make('img', cls, parent); e.alt = ''; e.decoding = 'async'; e.draggable = false; e.src = src; return e; };
  const fade = (el, from, to, ms = 600, delay = 0) => ctx.anim(el, [{ opacity: from }, { opacity: to }], { duration: Math.max(250, ms), delay, fill: 'forwards' });

  /* ---- the courtroom: the panel photo, a name plate under each llama ---- */
  const scene = make('div', 'llp-scene', root);
  scene.setAttribute('role', 'img');
  scene.setAttribute('aria-label', end.scenes[0].card);
  const photo = pic('llp-photo', ART.panel, scene);
  const plates = PANEL_SEATS.map((id) => {
    const p = make('div', `llp-plate ${id === 'reginald' ? 'chair' : 'side'}`, scene, LLAMA_BY_ID[id].name);
    p.style.left = `${SEAT_X[id]}%`;
    return p;
  });
  const chew = pic('llp-chair', ART.chew, scene);
  const spat = pic('llp-chair', ART.spit, scene);
  chew.alt = spat.alt = LLAMA_BY_ID.reginald.name;

  /* ---- the card under it ---- */
  const foot = make('div', 'llp-foot', root);
  const card = make('div', 'llp-card', foot);
  card.setAttribute('role', 'status');
  const words = make('span', '', card);
  const sub = make('small', '', card); sub.hidden = true;
  const stamp = make('div', 'llp-stamp', card);
  const row = make('div', 'llp-row', foot);
  const next = make('button', 'llp-btn', row, end.next); next.type = 'button';

  /* Next (or a tap on the scene) ends the beat now. */
  let skip = null;
  const advance = () => { const s = skip; skip = null; s?.(); };
  next.addEventListener('click', advance);
  scene.addEventListener('click', advance);
  const beat = (ms) => new Promise((res) => { skip = res; ctx.after(ms, () => { if (skip === res) skip = null; res(); }); });

  const say = (text, small = null) => {
    words.textContent = text;
    sub.hidden = !small; sub.textContent = small || '';
    scene.setAttribute('aria-label', text);
    fade(card, 0, 1, still ? 400 : 300);
  };

  let cancel;
  const cancelled = new Promise((resolve) => { cancel = resolve; });
  ctx.on('suspend', (e) => { if (e?.on && e.reason === 'panic') { root.remove(); cancel(); } });
  ctx.cleanup(() => cancel?.());

  async function show() {
    /* 1. The panel has dragged a table into a field. Your file is on it. */
    fade(scene, 0, 1, 700);
    say(end.scenes[0].card);
    await beat(PANEL_MS.bench);

    /* 2. The chair reads it. Chews it. Spits. Verdict: mostly chewing. The court dims behind the chair's close-up. */
    say(end.scenes[1].card);
    fade(photo, 1, 0.22, 500);
    for (const p of plates) fade(p, 1, 0, 400);
    if (still) fade(chew, 0, 1, 500);
    else ctx.anim(chew, [{ opacity: 0, transform: 'scale(.94)' }, { opacity: 1, transform: 'none' }], { duration: 500, easing: 'ease-out', fill: 'forwards' });
    await wait(ctx, PANEL_MS.read);
    /* chewing, about 1.5 times a second: well under the 3 Hz ceiling */
    if (!still) ctx.anim(chew, [{ transform: 'none' }, { transform: 'translateY(.8%) scaleY(.99)' }, { transform: 'none' }], { duration: 650, iterations: 3, easing: 'ease-in-out' });
    await wait(ctx, PANEL_MS.chew);
    if (still) { fade(spat, 0, 1, 300); fade(chew, 1, 0, 300); }
    else {
      ctx.anim(spat, [{ opacity: 0, transform: 'none' }, { opacity: 1, transform: 'translateY(-1.5%) scale(1.03)', offset: 0.3 }, { opacity: 1, transform: 'none' }], { duration: 600, easing: 'ease-out', fill: 'forwards' });
      fade(chew, 1, 0, 250, 120);
      ctx.audio.play('assets/audio/sfx/chip_pop.mp3', { bus: 'sfx', gain: 0.25 });
    }
    await wait(ctx, PANEL_MS.spit);
    stamp.textContent = end.scenes[1].verdict;
    if (still) fade(stamp, 0, 1, 400);
    else ctx.anim(stamp, [{ opacity: 0, transform: 'scale(2.2)' }, { opacity: 1, transform: 'scale(.94)', offset: 0.62 }, { opacity: 1, transform: 'none' }], { duration: 340, easing: 'ease-in', fill: 'forwards' });
    ctx.after(260, () => ctx.audio.play('assets/audio/sfx/thud.mp3', { bus: 'sfx', gain: 0.45 }));
    await beat(PANEL_MS.verdict);

    /* 3. A new sticker, and the chair joins the Living Ad card. */
    fade(stamp, 1, 0, 300);
    fade(spat, 1, 0, 500);
    const sticker = pic('llp-sticker', STICKER_BY_ID.e_ll_end?.art?.src || ART.sticker, scene);
    sticker.alt = STICKER_BY_ID.e_ll_end?.name || '';
    if (still) fade(sticker, 0, 1, 500);
    else ctx.anim(sticker, [{ opacity: 0, transform: 'scale(.6) rotate(-14deg)' }, { opacity: 1, transform: 'scale(1.06) rotate(3deg)', offset: 0.7 }, { opacity: 1, transform: 'none' }], { duration: 520, easing: 'ease-out', fill: 'forwards' });
    ctx.audio.play('assets/audio/sfx/reveal_chime.mp3', { bus: 'sfx', gain: 0.45 });
    /* The herd row as the Living Ad card will draw it: the chair first, round (ui/adcard/card.js herdHtml). */
    const herd = make('div', 'llp-herd', scene);
    herd.setAttribute('aria-hidden', 'true');
    const n = Math.max(3, Math.min(12, Number(ctx.life?.llamas) || 12));
    const head = make('span', 'chair', herd);
    pic('', ART.chair, head);
    for (let i = 1; i < n; i++) make('span', '', herd, '\u{1F999}');
    if (still) fade(herd, 0, 1, 500, 700);
    else {
      fade(herd, 0, 1, 400, 600);
      ctx.anim(head, [{ transform: 'translateX(-260%)', opacity: 0 }, { transform: 'translateY(-30%)', opacity: 1, offset: 0.7 }, { transform: 'none', opacity: 1 }], { duration: 700, delay: 900, easing: 'ease-out', fill: 'backwards' });
    }
    say(end.scenes[2].card + (STICKER_BY_ID.e_ll_end ? ` ${STICKER_BY_ID.e_ll_end.name}.` : ''), end.scenes[2].sub);
    await beat(PANEL_MS.sticker);

    /* The closing line (Section 50), and the way on to the credits. */
    fade(scene, 1, still ? 0.35 : 0.25, 700);
    say(end.closing);
    next.textContent = end.go;
    next.classList.add('go');
    next.focus({ preventScroll: true });
    await beat(PANEL_MS.closing);
  }

  await Promise.race([show(), cancelled]);
}
