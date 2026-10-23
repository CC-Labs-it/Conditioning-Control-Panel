/* render/wordRush.js - a popped word rushes at the camera (pass 4, owner 2026-10-23: "when we gather that word,
 * the subliminal effect comes at us toward the POV like the Back Room slot machine").
 *
 * The LOOK of the Back Room slot callout (backroom shared/hypno/callout.js, ported by eye, never imported: this
 * tree never reaches into another web tree): the word centred on screen, 60% -> 130% toward the viewer over
 * about a second with a small lean and stretch, a short hold, then a dissolve that pushes a little further on
 * while a red and a blue ghost split off it. Pink gradient fill, dark plum stroke, a hard drop shadow and a
 * pink halo, and a light sweep across the letters on the way in. The word wears its bubble's face
 * (wordBubblesMath bubbleFace), so a trigger keeps one identity from the road to the screen.
 * Tiers by what was popped: a plain bubble is small, a gate word big, a trigger phrase the hero (bigger, and a
 * short shake on arrival). One rush at a time: a second pop replaces the first, never stacks.
 * A DOM layer over the canvas with its own injected style (pointer-events none), above the HUD race chrome and
 * under every card (the pause card covers it; the clock stops while paused). It hides the middle of the screen
 * only for the rush itself; the place and lap sit in the corners.
 * Gentle: slower, softer and smaller, no ghost split, no shake, no lean; the opacity rise keeps the 300 ms floor.
 * There is no flash anywhere in it (the sweep is a highlight on the letters), so it asks nothing of photosafe.
 * rushLook is pure (tests/words.test.mjs); createWordRush listens to `word-pop` on the bus.
 */
import { FACES } from './fonts.js';
import { bubbleFace, asciiWord, BUBBLE_TEXT as BT } from './wordBubblesMath.js';

export const RUSH = Object.freeze({
  from: 0.6, to: 1.3, zoomMs: 1000, holdMs: 200, outMs: 400,   // the slot callout's own schedule
  inMs: 70,                                                    // the word is there on the pop (Law I), a blink of fade
  gentleTo: 1.12, gentleZoomMs: 1400, gentleOutMs: 700, gentleRiseMs: 300,
  leanDeg: [-3, 2], stretch: 0.05,                             // the lean and stretch on the way in (not under Gentle)
  vh: Object.freeze({ small: 12, big: 17, hero: 23 }), gentleVh: 0.85,
  fitVw: 0.9,                                                  // at its biggest the word stays inside 90% of the width
  ghostEm: 0.3, ghostAlpha: 0.72,
  sweepFrom: 0.1, sweepMs: 750,                                // the light band runs across from 10% of the zoom
  shakePx: 6, shakeMs: 320,
  topPct: 42,                                                  // the word's centre, % of the screen height (over the road)
});

const TIERS = Object.freeze(['small', 'big', 'hero']);
/** small for a plain bubble, big for a gate word, hero for a trigger phrase. */
export const rushTier = (e) => (e && e.row === 5 ? 'hero' : e && e.row === 3 ? 'big' : 'small');

const sat = (x) => (x < 0 ? 0 : x > 1 ? 1 : x);
const ease = (x) => { x = sat(x); return x * x * (3 - 2 * x); };

/** How long a rush lasts, ms. */
export const rushMs = (gentle = false, R = RUSH) => (gentle ? R.gentleZoomMs + R.holdMs + R.gentleOutMs : R.zoomMs + R.holdMs + R.outMs);

/**
 * The rush at t ms after the pop: { scale, alpha, rot (deg), sy (y stretch), ghost (alpha), ghostEm (split),
 * sweep (0..1 across the word, -1 none), dx, dy (px shake), done }.
 */
export function rushLook(tMs, tier = 'small', gentle = false, R = RUSH, out = {}) {
  const zoom = gentle ? R.gentleZoomMs : R.zoomMs, fade = gentle ? R.gentleOutMs : R.outMs, to = gentle ? R.gentleTo : R.to;
  const t = Math.max(0, tMs);
  out.rot = 0; out.sy = 1; out.ghost = 0; out.ghostEm = 0; out.sweep = -1; out.dx = 0; out.dy = 0;
  out.done = t >= zoom + R.holdMs + fade;
  if (t < zoom) {
    const q = t / zoom, e = 1 - (1 - q) ** 3;
    out.scale = R.from + (to - R.from) * e;
    out.alpha = gentle ? ease(t / R.gentleRiseMs) : sat(t / R.inMs);
    if (!gentle) {
      // lean left by 40% of the zoom, right by 72%, upright on arrival (the callout's keyframes, eased between)
      const [a, b] = R.leanDeg;
      out.rot = q < 0.4 ? a * ease(q / 0.4) : q < 0.72 ? a + (b - a) * ease((q - 0.4) / 0.32) : b * (1 - ease((q - 0.72) / 0.28));
      out.sy = 1 + R.stretch * Math.sin(Math.PI * sat(q / 0.72));
    }
  } else if (t < zoom + R.holdMs) {
    out.scale = to * (1 - 0.03 * Math.sin(Math.PI * (t - zoom) / R.holdMs));
    out.alpha = 1;
  } else {
    const q = sat((t - zoom - R.holdMs) / fade);
    out.scale = to * (1 + (gentle ? 0.03 : 0.06) * q);
    out.alpha = 1 - ease(q);
    if (!gentle) { out.ghost = R.ghostAlpha * Math.sin(Math.PI * sat(q / 0.8)); out.ghostEm = R.ghostEm * ease(q); }
  }
  const sw = (t - zoom * R.sweepFrom) / (R.sweepMs * (gentle ? 1.6 : 1));
  if (sw >= 0 && sw <= 1) out.sweep = ease(sw);
  if (tier === 'hero' && !gentle && t < R.shakeMs) {
    const k = (1 - t / R.shakeMs) * R.shakePx;
    out.dx = k * Math.sin(t * 0.09); out.dy = k * Math.cos(t * 0.13) * 0.6;
  }
  return out;
}

// the word's background layers: the sweep band (moved per frame), the gloss band, three glitter speckles, the pink fill
const REST_POS = ',0 0,.11em .07em,0 .19em,.23em .3em,0 0';
// the sticker build: a plum UNDER copy wears the thick outline, the hard drop and the halo; the GHOSTS split off it
// on the way out; the WORD on top is the shiny fill alone (a stroke on the fill itself eats thin serifs)
// polish 6 (PERF2): the drop and the halo are text-shadows (the drop sits lower: a text-shadow is cast by the fill, not
// the stroke) and the ghosts blend normally. A filter: drop-shadow chain
// and mix-blend-mode over the WebGL canvas made Firefox re-rasterise the scaled word offscreen every frame: about
// 2 s of 40 ms frames per hero rush (dev/hitch-check.mjs, s16 break and the finish)
const CSS = `
.gr-rush{position:fixed;inset:0;z-index:9;pointer-events:none;overflow:hidden;display:none}
.gr-rush-box{position:absolute;left:50%;top:${RUSH.topPct}%;transform:translate(-50%,-50%) scale(.6);opacity:0;will-change:transform,opacity;
  line-height:1;white-space:nowrap;text-align:center}
.gr-rush-word,.gr-rush-under,.gr-rush-ghost{padding:0 .14em}
.gr-rush-word{position:relative;display:block;color:transparent;-webkit-text-fill-color:transparent;
  background-image:linear-gradient(105deg,transparent 40%,rgba(255,255,255,.9) 50%,transparent 60%),
    linear-gradient(180deg,rgba(255,255,255,.6) 10%,rgba(255,255,255,.2) 45%,rgba(255,255,255,0) 47%),
    radial-gradient(circle,#fff 0 .011em,transparent .024em),
    radial-gradient(circle,rgba(255,255,255,.85) 0 .007em,transparent .017em),
    radial-gradient(circle,rgba(255,240,250,.9) 0 .009em,transparent .02em),
    linear-gradient(180deg,${BT.fill.map(([at, c]) => `${c} ${Math.round(at * 100)}%`).join(',')});
  background-size:300% 100%,100% 100%,.53em .41em,.29em .37em,.71em .23em,100% 100%;background-position:120% 0${REST_POS};
  background-repeat:no-repeat,no-repeat,repeat,repeat,repeat,no-repeat;-webkit-background-clip:text;background-clip:text}
.gr-rush-under{position:absolute;inset:0;color:${BT.plum};-webkit-text-fill-color:${BT.plum};-webkit-text-stroke:.11em ${BT.plum};
  text-shadow:0 .12em 0 #16020f,0 .07em .03em #16020f,0 0 .3em rgba(255,64,170,.8),0 0 .6em rgba(255,64,170,.35)}
.gr-rush[data-tier=hero] .gr-rush-under{text-shadow:0 .12em 0 #16020f,0 .07em .03em #16020f,0 0 .28em rgba(255,64,170,.9),0 0 .75em rgba(255,150,215,.55)}
.gr-rush-ghost{position:absolute;inset:0;opacity:0}
.gr-rush-ghost.r{color:#ff3d8f;-webkit-text-fill-color:#ff3d8f}.gr-rush-ghost.b{color:#5fb0ff;-webkit-text-fill-color:#5fb0ff}
`;

/**
 * @param opts { bus (word-pop), feel (gentle.on), doc, mount (default document.body) }
 * @returns {{ show(e: object): void, update(dt: number, paused?: boolean): void, clear(): void, dispose(): void,
 *   readonly active: boolean, readonly tier: string|null, readonly text: string }}
 */
export function createWordRush({ bus = null, feel = null, doc = globalThis.document, mount = null } = {}) {
  const host = mount || (doc && doc.body);
  const gentle = () => !!(feel && feel.get('gentle.on'));
  let layer = null, box = null, word = null, under = null, ghosts = [], style = null;
  if (doc && host) {
    style = doc.createElement('style');
    style.setAttribute('data-gr-rush', '');
    style.textContent = CSS;
    (doc.head || host).appendChild(style);
    layer = doc.createElement('div');
    layer.className = 'gr-rush';
    layer.setAttribute('aria-hidden', 'true');
    box = doc.createElement('div');
    box.className = 'gr-rush-box';
    under = doc.createElement('span');
    under.className = 'gr-rush-under';
    box.appendChild(under);
    word = doc.createElement('span');
    word.className = 'gr-rush-word';
    for (const k of ['r', 'b']) { const g = doc.createElement('span'); g.className = 'gr-rush-ghost ' + k; ghosts.push(g); box.appendChild(g); }
    box.appendChild(word);
    layer.appendChild(box);
    host.appendChild(layer);
  }
  let metric;   // one 2D context to measure the face with (absent in node tests: an estimate)
  function widthOf(text, font, px) {
    if (metric === undefined) { try { metric = doc.createElement('canvas').getContext('2d') || null; } catch { metric = null; } }
    if (metric && metric.measureText) { metric.font = font; const w = metric.measureText(text).width; if (w > 0) return w; }
    return text.length * px * 0.55;
  }
  const look = {};
  let t = -1, tier = null, text = '', g = false;

  function show(e) {
    if (!e || !e.w) return;
    tier = rushTier(e); g = gentle(); t = 0;
    const f = bubbleFace(e.row || 0), face = FACES[f.face];
    text = asciiWord(e.w);
    if (f.upper) text = text.toUpperCase();
    if (!layer) return;
    const vw = globalThis.innerWidth || 1280, vh = globalThis.innerHeight || 720;
    let px = RUSH.vh[tier] * (g ? RUSH.gentleVh : 1) * vh / 100;
    const peak = g ? RUSH.gentleTo : RUSH.to;
    const w = widthOf(text, face.css(px, f.weight), px) * 1.15;   // the padding and the stroke
    if (w * peak > vw * RUSH.fitVw) px *= (vw * RUSH.fitVw) / (w * peak);
    const font = face.css(px, f.weight);
    word.textContent = text; word.style.font = font;
    under.textContent = text; under.style.font = font;
    for (const gh of ghosts) { gh.textContent = text; gh.style.font = font; gh.style.opacity = '0'; }
    layer.setAttribute('data-tier', tier);
    layer.style.display = 'block';
    apply();
  }

  function apply() {
    if (!layer) return;
    rushLook(t, tier, g, RUSH, look);
    box.style.opacity = look.alpha.toFixed(3);
    box.style.transform = `translate(calc(-50% + ${look.dx.toFixed(1)}px), calc(-50% + ${look.dy.toFixed(1)}px)) rotate(${look.rot.toFixed(2)}deg) scale(${look.scale.toFixed(3)}, ${(look.scale * look.sy).toFixed(3)})`;
    // the light band: the first background layer slides from right of the word to left of it
    word.style.backgroundPosition = (look.sweep < 0 ? '120%' : `${(120 - 140 * look.sweep).toFixed(1)}%`) + ' 0' + REST_POS;
    for (let i = 0; i < ghosts.length; i++) {
      const gh = ghosts[i];
      gh.style.opacity = look.ghost.toFixed(3);
      gh.style.transform = look.ghost > 0 ? `translateX(${((i ? 1 : -1) * look.ghostEm).toFixed(3)}em)` : 'none';
    }
  }

  function clear() {
    t = -1; tier = null; text = '';
    if (layer) layer.style.display = 'none';
  }

  function update(dt, paused = false) {
    if (t < 0) return;
    if (!paused) t += Math.min(100, Math.max(0, (dt || 0) * 1000));
    if (t >= rushMs(g)) { clear(); return; }
    apply();
  }

  const off = bus ? bus.on('word-pop', (e) => show(e)) : null;
  return {
    show, update, clear,
    get active() { return t >= 0; },
    get tier() { return tier; },
    get text() { return text; },
    dispose() { if (off) off(); clear(); if (layer) layer.remove(); if (style) style.remove(); },
  };
}

export { TIERS as RUSH_TIERS };
