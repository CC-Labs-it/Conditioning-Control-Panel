/* hud/itemFeel.js - the thrower's half of an item on the HUD (item feel, feel group `itemFeel`).
 *
 * The world already pops and sprays where an item lands, but that is often far up the road. This layer makes
 * the player's own shots unmistakable wherever they land:
 *   use       the spent bubble flies out of the slot by mode (throw: up the road, lob: an arc, wide: a pulse,
 *             trap: back past the kart, self: into the kart); hud.js sets data-fling, the CSS here plays it
 *   landed    a hit marker (pink X round a diamond) above the kart, a comic stamp beside the slot (BONK!,
 *             DOUBLE!, TRIPLE!), a short pink screen-edge flash (at most one per edgeMinGapS) and a rich toast
 *   blocked   a grey marker, a grey stamp (BLOCKED, SNAPPED, IMMUNE, DECOY, SHRUGGED) and a blocked toast
 *   taken     the toast names the item and who threw it (OUCH! Pendulum from Hush)
 * Words, combos and toast policy are pure (fx/itemFeelMath.js). Gentle and OS reduced motion keep fades only and
 * drop the edge flash. Its CSS is injected once (#gr-itemfeel-css), so styles.css stays untouched.
 */
import { comboNext, confirmWord, hitToast, BLOCK_WORDS, PRIORITY } from '../fx/itemFeelMath.js';

export const ITEM_FEEL_MARKUP =
  '<div class="if-edge" aria-hidden="true"></div>' +
  '<div class="if-mark" aria-hidden="true"><i></i><i></i><i></i><i></i><b></b></div>' +
  '<div class="if-stamp" aria-hidden="true"></div>';

const CSS = `
/* ---- item feel (hud/itemFeel.js): fling, hit marker, stamp, edge flash, toast tones ---- */
.hud { --if-mark-s: 0.45s; --if-stamp-s: 1s; --if-edge-s: 0.4s; --if-edge-a: 0.55; --if-fling-s: 0.42s; }
.hud .hud-item.m-use[data-fling] .hud-item-ghost { animation-duration: var(--if-fling-s); animation-fill-mode: both; z-index: 3; }
.hud .hud-item.m-use[data-fling="throw"] .hud-item-ghost, .hud .hud-item.m-use[data-fling="bounce"] .hud-item-ghost,
.hud .hud-item.m-use[data-fling="tether"] .hud-item-ghost { animation-name: if-fling-throw; animation-timing-function: cubic-bezier(.2,.7,.3,1); }
.hud .hud-item.m-use[data-fling="lob"] .hud-item-ghost { animation-name: if-fling-lob; animation-timing-function: linear; }
.hud .hud-item.m-use[data-fling="wide"] .hud-item-ghost { animation-name: if-fling-wide; animation-timing-function: ease-out; }
.hud .hud-item.m-use[data-fling="drop"] .hud-item-ghost { animation-name: if-fling-drop; animation-timing-function: cubic-bezier(.5,0,.8,.6); }
.hud .hud-item.m-use[data-fling="self"] .hud-item-ghost { animation-name: if-fling-self; animation-timing-function: cubic-bezier(.4,0,.7,1); }
@keyframes if-fling-throw { 0% { transform: none; opacity: 1; } 18% { transform: translateY(-8px) scale(1.25) rotate(-8deg); opacity: 1; }
  100% { transform: translateY(32vh) scale(.22) rotate(20deg); opacity: 0; } }
@keyframes if-fling-lob { 0% { transform: none; opacity: 1; } 30% { transform: translate(6px, -26px) scale(1.3); opacity: 1; }
  60% { transform: translate(10px, 8vh) scale(.8); opacity: .9; } 100% { transform: translate(12px, 28vh) scale(.3); opacity: 0; } }
@keyframes if-fling-wide { 0% { transform: scale(1); opacity: 1; filter: none; } 35% { transform: scale(1.7); opacity: .9; filter: drop-shadow(0 0 10px #ff4fb0); }
  100% { transform: scale(3.2); opacity: 0; filter: drop-shadow(0 0 18px #ff4fb0); } }
@keyframes if-fling-drop { 0% { transform: none; opacity: 1; } 25% { transform: translateY(-6px) scale(1.1); opacity: 1; }
  100% { transform: translateY(70vh) scale(1.5) rotate(-25deg); opacity: 0; } }
@keyframes if-fling-self { 0% { transform: none; opacity: 1; } 20% { transform: scale(1.35); opacity: 1; filter: drop-shadow(0 0 8px #ffd36b); }
  100% { transform: translateY(52vh) scale(.5); opacity: 0; filter: drop-shadow(0 0 14px #ffd36b); } }
.hud.gentle .hud-item.m-use[data-fling] .hud-item-ghost { animation-name: if-fade; animation-timing-function: ease-out; }

.if-edge { position: absolute; inset: 0; pointer-events: none; opacity: 0;
  background: radial-gradient(ellipse 75% 70% at 50% 50%, transparent 58%, rgba(255, 79, 176, .55) 88%, rgba(255, 79, 176, .95) 100%); }
.if-edge.on { animation: if-edge var(--if-edge-s) ease-out both; }
@keyframes if-edge { 0% { opacity: 0; } 18% { opacity: var(--if-edge-a); } 100% { opacity: 0; } }

.if-mark { position: absolute; left: 50%; top: 40%; width: 64px; height: 64px; margin: -32px 0 0 -32px; opacity: 0; pointer-events: none; --mk: #ff3fae; }
.if-mark i { position: absolute; left: 50%; top: 50%; width: 5px; height: 17px; margin: -8.5px 0 0 -2.5px; border-radius: 3px; background: var(--mk);
  box-shadow: 0 0 0 1.5px #1c1722, 0 0 10px var(--mk); }
.if-mark i:nth-child(1) { transform: rotate(45deg) translateY(-19px); } .if-mark i:nth-child(2) { transform: rotate(135deg) translateY(-19px); }
.if-mark i:nth-child(3) { transform: rotate(225deg) translateY(-19px); } .if-mark i:nth-child(4) { transform: rotate(315deg) translateY(-19px); }
.if-mark b { position: absolute; left: 50%; top: 50%; width: 12px; height: 12px; margin: -6px 0 0 -6px; transform: rotate(45deg); background: #fff4fa;
  box-shadow: 0 0 0 2px var(--mk), 0 0 0 3.5px #1c1722, 0 0 14px var(--mk); }
.if-mark.blk { --mk: #b9b1c2; } .if-mark.blk b { background: transparent; }
.if-mark.on { animation: if-mark var(--if-mark-s) ease-out both; }
.if-mark.blk.on { animation-name: if-mark-blk; }
@keyframes if-mark { 0% { opacity: 0; transform: scale(1.9) rotate(-20deg); } 14% { opacity: 1; transform: scale(.92) rotate(0); }
  28% { transform: scale(1.06); } 60% { opacity: 1; transform: scale(1); } 100% { opacity: 0; transform: scale(1.18); } }
@keyframes if-mark-blk { 0% { opacity: 0; transform: scale(1.2); } 20% { opacity: .9; transform: scale(.85); } 70% { opacity: .8; } 100% { opacity: 0; transform: scale(.8); } }

.if-stamp { position: absolute; left: calc(50% + 46px); top: calc(max(12px, env(safe-area-inset-top)) + 10px); pointer-events: none; opacity: 0;
  font: 900 30px/1 var(--font); letter-spacing: .02em; color: #ff3fae; white-space: nowrap; transform-origin: 0 60%;
  -webkit-text-stroke: 1.5px #1c1722; paint-order: stroke fill;
  text-shadow: 2px 2px 0 #fff4fa, 4px 4px 0 #1c1722, 0 0 18px rgba(255, 63, 174, .6); }
.if-stamp.combo { color: #ffd36b; font-size: 36px; text-shadow: 2px 2px 0 #fff4fa, 4px 4px 0 #1c1722, 0 0 22px rgba(255, 211, 107, .8); }
.if-stamp.blk { color: #c9c1d2; font-size: 22px; text-shadow: 2px 2px 0 #1c1722; }
.if-stamp.on { animation: if-stamp var(--if-stamp-s) cubic-bezier(.2,.9,.3,1) both; }
.if-stamp.blk.on { animation-name: if-stamp-blk; }
@keyframes if-stamp { 0% { opacity: 0; transform: scale(2.4) rotate(-14deg); } 12% { opacity: 1; transform: scale(.9) rotate(-7deg); }
  22% { transform: scale(1.08) rotate(-8deg); } 75% { opacity: 1; transform: scale(1) rotate(-8deg); } 100% { opacity: 0; transform: translateY(-10px) scale(1) rotate(-8deg); } }
@keyframes if-stamp-blk { 0% { opacity: 0; transform: translateX(-6px) rotate(4deg); } 15% { opacity: 1; transform: translateX(3px) rotate(4deg); }
  25% { transform: translateX(-2px) rotate(4deg); } 75% { opacity: 1; transform: rotate(4deg); } 100% { opacity: 0; transform: translateY(6px) rotate(4deg); } }

.hud-toast[data-tone] .t-word { font-weight: 900; margin-right: 6px; letter-spacing: .03em; }
.hud-toast[data-tone="hit"] { border-color: #d4307c; box-shadow: 3px 3px 0 #d4307c; }
.hud-toast[data-tone="hit"] .t-word { color: #d4307c; }
.hud-toast[data-tone="ouch"] .t-word { color: #7a2bd6; }
.hud-toast[data-tone="blocked"] { background: #ddd6e2; color: #4b4453; }
.hud-toast[data-tone="blocked"] .t-word { color: #1c1722; }
.hud-toast[data-tone="saved"] .t-word { color: #1f7fa6; }
.hud-toast .t-who { font-weight: 800; }
.hud-toast .t-who[style]::before { content: ''; display: inline-block; width: .7em; height: .7em; margin: 0 4px -1px 1px; border-radius: 50%;
  background: var(--who); box-shadow: 0 0 0 1.5px #1c1722; }
.hud-toast .t-place { margin-left: 5px; font-size: .82em; color: #6a6272; }
.hud-toast.bump { animation: if-toast-bump .32s ease-out; }
@keyframes if-toast-bump { 0% { transform: translate(-50%, 0) scale(1.18); } 100% { transform: translate(-50%, 0) scale(1); } }
@keyframes if-fade { 0% { opacity: 1; } 100% { opacity: 0; } }

.hud.gentle .if-edge { display: none; }
.hud.gentle .if-mark.on, .hud.gentle .if-stamp.on { animation-name: if-fade; animation-duration: calc(var(--if-stamp-s) * 1.2); }
.hud.gentle .hud-toast.bump { animation: none; }
@media (prefers-reduced-motion: reduce) {
  .if-edge { display: none; }
  .if-mark.on, .if-stamp.on { animation-name: if-fade; }
  .hud .hud-item.m-use[data-fling] .hud-item-ghost { animation-name: if-fade; }
  .hud-toast.bump { animation: none; }
}
`;

const num = (feel, k, d) => { try { const v = Number(feel.get(k)); return Number.isFinite(v) && v > 0 ? v : d; } catch (e) { return d; } };

/**
 * root: the HUD root (ITEM_FEEL_MARKUP already inside). toast(msg, opts) is the HUD's priority toast.
 * names(i) -> the racer's label; tints(i) -> its palette tint (1..7) or 0. @returns {{ hit(p), hook(p), standings(order), reset(), dispose() }}
 */
export function createItemFeel(root, { doc = globalThis.document, feel, now, toast, names, tints = () => 0, itemName = (k) => k, fetchFn = globalThis.fetch } = {}) {
  const q = (s) => root.querySelector(s);
  const el = { edge: q('.if-edge'), mark: q('.if-mark'), stamp: q('.if-stamp') };
  try {
    if (doc && doc.head && !doc.getElementById?.('gr-itemfeel-css')) {
      const s = doc.createElement('style'); s.id = 'gr-itemfeel-css'; s.textContent = CSS; doc.head.appendChild(s);
    }
  } catch (e) { /* no stylesheet: the HUD still toasts */ }
  let order = [], colours = null, asked = false, edgeAt = -1e9, alive = true;
  const combo = { n: 0, t0: -1e9 }, victims = [];
  const gentle = () => { try { return !!feel.get('gentle.on'); } catch (e) { return false; } };
  const tS = () => now() / 1000;

  /** Kart cup colours (art/palettes.json `karts`, tint 1..7), fetched once on the first hit; none until then. */
  function palette() {
    if (asked || typeof fetchFn !== 'function') return;
    asked = true;
    try {
      Promise.resolve(fetchFn('art/palettes.json')).then((r) => r && r.ok ? r.json() : null)
        .then((j) => { if (alive && j && j.karts) colours = Object.values(j.karts).map((k) => k && k.cup); }).catch(() => {});
    } catch (e) { /* no colours */ }
  }
  const who = (i) => {
    const t = tints(i) || i;
    return { name: names(i), colour: colours && t > 0 ? colours[(t - 1) % colours.length] || '' : '', place: order.indexOf(i) + 1 };
  };
  const vars = () => {
    const st = root.style;
    if (!st || !st.setProperty) return;
    st.setProperty('--if-mark-s', num(feel, 'itemFeel.markerS', 0.45) + 's');
    st.setProperty('--if-stamp-s', num(feel, 'itemFeel.stampS', 1) + 's');
    st.setProperty('--if-edge-s', num(feel, 'itemFeel.edgeS', 0.4) + 's');
    st.setProperty('--if-edge-a', String(num(feel, 'itemFeel.edgeAlpha', 0.55)));
    st.setProperty('--if-fling-s', num(feel, 'itemFeel.flingS', 0.42) + 's');
  };
  const play = (node, add = [], drop = []) => {
    if (!node || !node.classList) return;
    node.classList.remove('on', ...drop);
    void node.offsetWidth;
    node.classList.add(...add, 'on');
  };
  function stamp(word, kind) {
    if (!el.stamp) return;
    el.stamp.textContent = word;
    play(el.stamp, kind ? [kind] : [], ['combo', 'blk']);
  }

  function hit(p = {}) {
    const { victim = -1, by = -1, kind = '', verdict = 'hit' } = p;
    if (by !== 0 && victim !== 0) return;
    palette(); vars();
    let n = 1;
    if (by === 0 && victim !== 0 && verdict === 'hit') {
      const t = tS();
      n = comboNext(combo, t, num(feel, 'itemFeel.comboWindowS', 0.6));
      if (n === 1) victims.length = 0;
      if (!victims.includes(victim)) victims.push(victim);
      play(el.mark, [], ['blk']);
      stamp(confirmWord(kind, n), n > 1 ? 'combo' : '');
      if (!gentle() && t - edgeAt >= num(feel, 'itemFeel.edgeMinGapS', 1.2)) { edgeAt = t; play(el.edge); }
    } else if (by === 0 && victim !== 0) {
      play(el.mark, ['blk']);
      stamp(BLOCK_WORDS[verdict] || 'MISSED', 'blk');
    }
    const m = hitToast(p, who, itemName, { count: n, names: victims.slice() });
    if (m) toast(m.parts, { priority: m.priority, group: m.group, tone: m.tone });
  }

  /** Your Tether caught someone: a stamp and a toast (the spin and its confirm come later as an item-hit). */
  function hook({ by = -1, target = -1 } = {}) {
    if (by !== 0 || target < 0) return;
    palette(); vars();
    stamp('HOOKED!', '');
    const w = who(target);
    toast([{ text: 'HOOKED!', cls: 'word' }, { text: 'Tether on ' }, { text: w.name, cls: 'who', colour: w.colour }],
      { priority: PRIORITY.blocked, group: 'confirm', tone: 'hit' });
  }

  return {
    hit, hook,
    standings(o) { order = Array.isArray(o) ? o.slice() : []; if (order.length) palette(); },
    reset() { combo.n = 0; combo.t0 = -1e9; victims.length = 0; edgeAt = -1e9; order = []; },
    dispose() { alive = false; },
  };
}
