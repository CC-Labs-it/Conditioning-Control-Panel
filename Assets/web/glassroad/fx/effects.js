/* fx/effects.js - the LOCAL player's screen effect (CONTRACT-B1.md section 3).
 *
 * Listens to item-hit for victim 0 with verdict 'hit' on a kind that touches the view (ITEMS[kind].view
 * full or mild) or Shatter. One effect at a time: a new hit replaces the running one (effect-end
 * { replaced: true }, then effect-start); the old one keeps fading as prevKind / prevK over the new IN, so
 * nothing cuts hard. IN = items.effectInS (Gentle rise floor), held to durationS, OUT = Gentle fade of
 * items.effectOutS. Every start asks photosafe; a refusal lands it softer (IN = items.photosafeInS).
 * Gentle belongs to the player: Flash becomes a corner card, Spiral a soft dim, blur halved, no flicker.
 * Mercy is not decided here (sim/items.js); this module plays what it is told. Three-free.
 * Polish 4: screen.remain (1 at the start, 0 when the hold ends) drives the countdown meter, and screen.hit is
 * a short hue tint at the moment of the hit (render/screenfx.js keeps the picture's luminance, so it is no
 * brightness step; it rides the start's photosafe answer: refused, it lands at half and slower; Gentle halves it).
 */
import { ITEMS } from '../sim/itemTable.js';
import { HIT_LOOK as H } from '../render/items/look.js';

const SCREEN_KINDS = new Set(['flash', 'spiral', 'brainDrain', 'freeze', 'blackout', 'pink', 'melt', 'shatter']);
const FLASHY = new Set(['flash', 'blackout']);

export function createEffects(bus, feel, { gentle, photosafe } = {}) {
  const g = (k) => feel.get('items.' + k);
  const gOn = () => !!(gentle ? gentle.on : feel.get('gentle.on'));
  const rise = (s) => (gentle ? gentle.rise(s) : s);
  const fade = (s) => (gentle ? gentle.fade(s) : s);

  const screen = {
    kind: 'none', t: 0, durS: 0, k: 0, strength: 0, prevKind: 'none', prevK: 0,
    params: { gentle: false, corner: false, dimOnly: false, blurMul: 1, flicker: false, cover: 0, scale: 0, seed: 0, by: -1 },
    remain: 0, hit: { kind: 'none', k: 0, t: -1, amt: 0, inS: 0, holdS: 0, outS: 0 },
  };
  let inS = 0, outS = 0, prevFrom = 0, prevT = 0, prevS = 0, paused = false, seed = 0;

  function applyParams() {
    const on = gOn(), p = screen.params;
    p.gentle = on;
    p.corner = on && screen.kind === 'flash';
    p.dimOnly = on && screen.kind === 'spiral';
    p.blurMul = on ? g('gentleBlurMul') : 1;
    p.flicker = !on;
    // cover = the full Flash card's share of the view; under Gentle the corner card is that card scaled
    // LINEARLY by scale (items.gentleFlashScale), 0 = no scale given (render keeps its own default)
    p.cover = screen.kind === 'flash' ? g('flashCover') : 0;
    p.scale = p.corner ? g('gentleFlashScale') : 0;
    screen.strength = screen.kind === 'none' ? 0 : p.dimOnly ? g('gentleSpiralDim') : 1;
  }

  function start({ kind, durationS, by }) {
    if (screen.kind !== 'none') {
      const old = screen.kind;
      screen.prevKind = old; prevFrom = screen.k; prevT = 0;
      bus.emit('effect-end', { kind: old, replaced: true });
    }
    inS = rise(g('effectInS'));
    const granted = photosafe ? photosafe.request(FLASHY.has(kind) ? 'flash' : 'palette') : true;
    if (!granted) inS = Math.max(inS, g('photosafeInS'));
    const h = screen.hit;
    h.kind = kind; h.t = 0;
    h.amt = H.tintAmt * (gOn() ? H.tintGentle : 1) * (granted ? 1 : H.tintRefusedMul);
    h.inS = rise(granted ? H.tintInS : Math.max(H.tintInS, H.tintRefusedInS)); h.holdS = H.tintHoldS; h.outS = fade(H.tintOutS);
    prevS = inS;
    outS = fade(g('effectOutS'));
    screen.kind = kind; screen.t = 0; screen.durS = Math.max(0, durationS || 0); screen.k = 0; screen.remain = screen.durS > 0 ? 1 : 0;
    screen.params.seed = ++seed; screen.params.by = by == null ? -1 : by;
    applyParams();
    bus.emit('effect-start', { kind, durationS: screen.durS, by: screen.params.by });
  }

  function envelope(t) {
    const up = inS > 0 ? Math.min(1, t / inS) : 1;
    const down = t <= screen.durS ? 1 : outS > 0 ? Math.max(0, 1 - (t - screen.durS) / outS) : 0;
    return Math.min(up, down);
  }

  function stepHit(dt) {
    const h = screen.hit;
    if (h.t < 0) return;
    h.t += dt;
    const up = h.inS > 0 ? Math.min(1, h.t / h.inS) : 1, d = h.t - h.inS - h.holdS;
    const down = d <= 0 ? 1 : h.outS > 0 ? Math.max(0, 1 - d / h.outS) : 0;
    h.k = h.amt * Math.min(up * up * (3 - 2 * up), down * down * (3 - 2 * down));
    if (d >= h.outS) { h.t = -1; h.k = 0; h.kind = 'none'; }
  }

  function update(dt) {
    if (paused || !(dt > 0)) return;
    stepHit(dt);
    if (screen.prevKind !== 'none') {
      prevT += dt;
      screen.prevK = prevS > 0 ? Math.max(0, prevFrom * (1 - prevT / prevS)) : 0;
      if (screen.prevK <= 0) { screen.prevKind = 'none'; screen.prevK = 0; }
    }
    if (screen.kind === 'none') return;
    screen.t += dt;
    screen.remain = screen.durS > 0 ? Math.max(0, 1 - screen.t / screen.durS) : 0;
    if (screen.t >= screen.durS + outS && screen.t >= inS) {
      const kind = screen.kind;
      screen.kind = 'none'; screen.k = 0; screen.t = 0; screen.durS = 0; screen.remain = 0;
      applyParams();
      bus.emit('effect-end', { kind, replaced: false });
      return;
    }
    screen.k = envelope(screen.t);
    applyParams();
  }

  function reset() {
    screen.kind = 'none'; screen.t = 0; screen.durS = 0; screen.k = 0; screen.prevKind = 'none'; screen.prevK = 0; screen.remain = 0;
    Object.assign(screen.hit, { kind: 'none', k: 0, t: -1, amt: 0 });
    inS = outS = prevFrom = prevT = prevS = 0; paused = false;
    applyParams();
  }

  const offs = [
    bus.on('item-hit', (e) => {
      if (!e || e.victim !== 0 || e.verdict !== 'hit' || !SCREEN_KINDS.has(e.kind)) return;
      const it = ITEMS[e.kind];
      if (!it || (it.view !== 'full' && it.view !== 'mild' && e.kind !== 'shatter')) return;
      start(e);
    }),
    bus.on('pause', (e) => { paused = !!(e && e.on); }),
  ];

  applyParams();
  return { screen, update, reset, dispose() { for (const off of offs) off(); } };
}
