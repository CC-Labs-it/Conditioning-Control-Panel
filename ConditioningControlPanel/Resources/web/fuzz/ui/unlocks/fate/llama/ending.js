/* ui/unlocks/fate/llama/ending.js - The Panel (ll_end), the run's show between the fade and the credits (owner 2026-11-21,
 * handoff-1121/TC-LLAMA-ROUND-BRIEF.md change 2). Three scenes on paper, each with one text card in the fate card look
 * (ui/fate-look.js: black notched card, white frame, dashed inner frame), then Section 50's line and the way on:
 *   1. the panel sits behind a long bench, your file is on it;
 *   2. the chair reads it, chews it, spits; a red stamp: Verdict: mostly chewing;
 *   3. the new sticker (e_ll_end, already in the book: sim/save.js recordEnding), and the chair joins the Living Ad card
 *      (ui/adcard/model.js `chair`), which the recap shows next.
 * No narration and no speech: the cards are the words. Gentle visuals and reduced motion get fades only (250 ms or more),
 * nothing moves. Each scene moves on by itself, or on Next / a tap. Panic takes the stage down at once. Drawings are
 * placeholders (./art.js OWED). */

import { ENDINGS } from '../../../../data/content/endings.js';
import { STICKER_BY_ID } from '../../../../data/content/stickers.js';
import { FATE_CARD, FATE_CHALK, FATE_RED } from '../../../fate-look.js';
import { llamaUrl, benchUrl, fileUrl, wadUrl } from './art.js';

/** How long each beat holds, in ms, before the show moves on by itself. Still (Gentle or reduced motion) holds the
 *  same scenes for the same reading time; only the motion inside them goes. */
export const PANEL_MS = Object.freeze({ bench: 5600, read: 1400, chew: 2600, spit: 1100, verdict: 3400, sticker: 6400, closing: 25000 });
/** The fade the run ends on: the courtroom's paper. */
export const PANEL_PAPER = '#f3ead6';
/** The whole show if nobody presses anything (ms): well under the engine's 90 s cap for an ending show. */
export const panelLength = () => Object.values(PANEL_MS).reduce((a, b) => a + b, 0);

const NOTCH = (n) => `polygon(${n}px 0,calc(100% - ${n}px) 0,100% ${n}px,100% calc(100% - ${n}px),calc(100% - ${n}px) 100%,${n}px 100%,0 calc(100% - ${n}px),0 ${n}px)`;
const FONT = 'var(--shop-font, "Barlow", "Segoe UI", system-ui, sans-serif)';
const CSS = `
.llp { position: absolute; inset: 0; overflow: hidden; pointer-events: auto; background: ${PANEL_PAPER};
  background-image: radial-gradient(ellipse at 50% 30%, rgba(255,255,255,.55), transparent 60%), repeating-linear-gradient(0deg, rgba(120,90,50,.035) 0 2px, transparent 2px 6px);
  color: #2a2130; display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 14px; padding: 16px; box-sizing: border-box; font: 600 16px/1.3 ${FONT}; user-select: none; -webkit-user-select: none; }
.llp-scene { position: relative; width: min(94vw, calc((100vh - 230px) * 16 / 9), 900px); aspect-ratio: 16 / 9; flex: none; }
.llp-scene > * { position: absolute; display: block; }
.llp-wall { inset: 0 0 34% 0; border-bottom: 3px solid rgba(42,33,48,.25); background: repeating-linear-gradient(90deg, rgba(42,33,48,.05) 0 1px, transparent 1px 12%); }
.llp-llama { width: 15%; top: 29%; transform-origin: 50% 100%; }
.llp-llama.chair { width: 17%; left: 41.5%; top: 25%; }
.llp-bench { left: 0; width: 100%; top: 57%; }
.llp-plate { left: 50%; top: 63%; translate: -50% 0; padding: .2em .7em; font: 800 clamp(8px, 1.4vw, 13px)/1.2 Georgia, serif; letter-spacing: .12em;
  background: linear-gradient(#f3dc94, #cfa245 52%, #a87b25); border: 2px solid #2a2130; border-radius: 3px; color: #2a1a04; }
.llp-file { width: 10%; left: 45%; top: 47%; rotate: -6deg; }
.llp-wad { width: 4%; left: 52%; top: 44%; opacity: 0; }
.llp-sticker { left: 50%; top: 50%; width: 30%; translate: -50% -50%; filter: drop-shadow(0 6px 10px rgba(42,33,48,.25)); }
.llp-herd { left: 50%; top: 86%; translate: -50% 0; white-space: nowrap; padding: .25em .6em; border-radius: 6px; background: #fff; box-shadow: 0 2px 0 rgba(42,33,48,.18);
  font: clamp(14px, 2.6vw, 26px)/1 "Segoe UI Emoji", "Apple Color Emoji", "Noto Color Emoji", sans-serif; }
.llp-herd span { display: inline-block; position: relative; }
.llp-herd .chair i { position: absolute; left: 50%; bottom: 82%; translate: -50% 0; font-size: .52em; font-style: normal; }
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

  /* ---- the courtroom ---- */
  const scene = make('div', 'llp-scene', root);
  scene.setAttribute('role', 'img');
  scene.setAttribute('aria-label', end.scenes[0].card);
  make('div', 'llp-wall', scene);
  const left = pic('llp-llama', llamaUrl('side'), scene); left.style.left = '24%';
  const right = pic('llp-llama', llamaUrl('side'), scene); right.style.left = '61%';
  const chair = pic('llp-llama chair', llamaUrl('chair'), scene);
  const bench = pic('llp-bench', benchUrl(), scene);
  const plate = make('div', 'llp-plate', scene, end.plate);
  const file = pic('llp-file', fileUrl(), scene);
  const wad = pic('llp-wad', wadUrl(), scene);

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
    /* 1. The panel sits behind a long bench. Your file is on it. */
    fade(scene, 0, 1, 700);
    say(end.scenes[0].card);
    await beat(PANEL_MS.bench);

    /* 2. The chair reads it. Chews it. Spits. Verdict: mostly chewing. */
    say(end.scenes[1].card);
    if (!still) ctx.anim(chair, [{ transform: 'none' }, { transform: 'rotate(-4deg) translateY(2%)' }], { duration: 500, fill: 'forwards', easing: 'ease-out' });
    await wait(ctx, PANEL_MS.read);
    if (still) fade(file, 1, 0, 500);
    else {
      ctx.anim(file, [{ transform: 'none', opacity: 1 }, { transform: 'translate(10%, -150%) scale(.35) rotate(20deg)', opacity: 1, offset: 0.8 }, { transform: 'translate(10%, -160%) scale(.2)', opacity: 0 }], { duration: 900, easing: 'ease-in', fill: 'forwards' });
      /* chewing, about 1.5 times a second: well under the 3 Hz ceiling */
      ctx.anim(chair, [{ transform: 'rotate(-4deg) translateY(2%)' }, { transform: 'rotate(-3deg) translateY(3.5%) scaleY(.98)' }, { transform: 'rotate(-4deg) translateY(2%)' }], { duration: 650, delay: 800, iterations: 3, easing: 'ease-in-out' });
    }
    await wait(ctx, PANEL_MS.chew);
    const w = scene.clientWidth || 800;
    if (still) { wad.style.left = '78%'; wad.style.top = '74%'; fade(wad, 0, 1, 400); }
    else {
      ctx.anim(chair, [{ transform: 'rotate(-4deg) translateY(2%)' }, { transform: 'rotate(5deg) translateY(-2%)', offset: 0.35 }, { transform: 'none' }], { duration: 700, fill: 'forwards', easing: 'ease-out' });
      ctx.anim(wad, [{ opacity: 1, transform: 'none' }, { opacity: 1, transform: `translate(${(w * 0.16).toFixed(0)}px, ${(-w * 0.06).toFixed(0)}px) rotate(200deg)`, offset: 0.45 },
        { opacity: 1, transform: `translate(${(w * 0.26).toFixed(0)}px, ${(w * 0.17).toFixed(0)}px) rotate(420deg)` }], { duration: 900, delay: 120, easing: 'ease-in', fill: 'forwards' });
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
    /* the court steps back; the chair leaves the bench for the card */
    for (const el of [left, right, file, wad, chair]) fade(el, 1, 0, 500);
    for (const el of [bench, plate]) fade(el, 1, 0.2, 600);
    const sticker = pic('llp-sticker', STICKER_BY_ID.e_ll_end?.art?.src || 'assets/stickers/p_llama.webp', scene);
    sticker.alt = STICKER_BY_ID.e_ll_end?.name || '';
    if (still) fade(sticker, 0, 1, 500);
    else ctx.anim(sticker, [{ opacity: 0, transform: 'scale(.6) rotate(-14deg)' }, { opacity: 1, transform: 'scale(1.06) rotate(3deg)', offset: 0.7 }, { opacity: 1, transform: 'none' }], { duration: 520, easing: 'ease-out', fill: 'forwards' });
    ctx.audio.play('assets/audio/sfx/reveal_chime.mp3', { bus: 'sfx', gain: 0.45 });
    /* The herd row as the Living Ad card will draw it: the chair first, wearing the scales (ui/adcard/card.js herdHtml). */
    const herd = make('div', 'llp-herd', scene);
    herd.setAttribute('aria-hidden', 'true');
    const n = Math.max(3, Math.min(12, Number(ctx.life?.llamas) || 12));
    const head = make('span', 'chair', herd, '\u{1F999}'); make('i', '', head, '\u2696\uFE0F');
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
