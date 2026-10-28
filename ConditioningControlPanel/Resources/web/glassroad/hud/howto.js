/* hud/howto.js - the 1-2-3 how-to card's panels (music road pass 4, owner: "a small 3-beat INFOGRAPHIC, a 1-2-3
 * layout card explaining the main controls and what to do"). Pure: strings only, no DOM; hud/cards.js shows it.
 *
 * Picture first, near-zero text: three paper panels, each a numbered rubber stamp, an ink drawing (inline SVG, no
 * image files) and one short line. Device-aware (the device used last, hud/labels.js names): keyboard keys, pad
 * buttons (Xbox, PlayStation or numbered), or the touch zones.
 *   1  the launch: hold throttle on GO (sim/race.js pays a press that starts in the last beat before go)
 *   2  steer, and hop into a drift for a boost
 *   3  drive through the pink word bubbles (the words on the road are the song) and throw an item
 * Ink is currentColor, the spot colour is the `pk` class (styles.css); keys carry `hw-key` so they press on the beat.
 */
import { deviceView, padLabels } from './labels.js';

const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

/** A keycap: a dark base with the paper top lifted off it (the press drops the top onto the base). */
function key(x, y, w, h, label, cls = '') {
  const fs = label.length > 2 ? 11 : 17;
  return '<g class="hw-key ' + cls + '" style="transform-origin:' + (x + w / 2) + 'px ' + (y + h) + 'px">' +
    '<rect x="' + x + '" y="' + (y + 4) + '" width="' + w + '" height="' + h + '" rx="6" class="ink-fill"/>' +
    '<g class="hw-top"><rect x="' + x + '" y="' + y + '" width="' + w + '" height="' + h + '" rx="6" class="paper-fill" stroke-width="2.2"/>' +
    '<text x="' + (x + w / 2) + '" y="' + (y + h / 2 + fs * 0.36) + '" font-size="' + fs + '" text-anchor="middle" class="hw-t">' + esc(label) + '</text></g></g>';
}
/** A round pad button. */
function button(cx, cy, r, label, cls = '') {
  const fs = label.length > 2 ? 9 : 14;
  return '<g class="hw-key ' + cls + '" style="transform-origin:' + cx + 'px ' + cy + 'px">' +
    '<circle cx="' + cx + '" cy="' + (cy + 3) + '" r="' + r + '" class="ink-fill"/>' +
    '<g class="hw-top"><circle cx="' + cx + '" cy="' + cy + '" r="' + r + '" class="paper-fill" stroke-width="2.2"/>' +
    '<text x="' + cx + '" y="' + (cy + fs * 0.36) + '" font-size="' + fs + '" text-anchor="middle" class="hw-t">' + esc(label) + '</text></g></g>';
}
/** A shoulder trigger seen from above the pad. */
function trigger(x, y, label) {
  return '<g class="hw-key" style="transform-origin:' + (x + 22) + 'px ' + (y + 40) + 'px">' +
    '<path d="M' + x + ' ' + (y + 44) + ' V' + (y + 18) + ' Q' + x + ' ' + y + ' ' + (x + 22) + ' ' + y + ' Q' + (x + 44) + ' ' + y + ' ' + (x + 44) + ' ' + (y + 18) + ' V' + (y + 44) + ' Z" class="ink-fill" transform="translate(0 4)"/>' +
    '<g class="hw-top"><path d="M' + x + ' ' + (y + 44) + ' V' + (y + 18) + ' Q' + x + ' ' + y + ' ' + (x + 22) + ' ' + y + ' Q' + (x + 44) + ' ' + y + ' ' + (x + 44) + ' ' + (y + 18) + ' V' + (y + 44) + ' Z" class="paper-fill" stroke-width="2.2"/>' +
    '<text x="' + (x + 22) + '" y="' + (y + 30) + '" font-size="' + (label.length > 2 ? 10 : 14) + '" text-anchor="middle" class="hw-t">' + esc(label) + '</text></g></g>';
}
/** A fingertip pressing the glass, with a ring. */
function finger(cx, cy, cls = '') {
  return '<g class="hw-key ' + cls + '" style="transform-origin:' + cx + 'px ' + cy + 'px">' +
    '<circle cx="' + cx + '" cy="' + cy + '" r="16" fill="none" class="pk-line hw-ring" stroke-width="2"/>' +
    '<path d="M' + (cx - 7) + ' ' + (cy + 26) + ' V' + (cy + 2) + ' Q' + (cx - 7) + ' ' + (cy - 8) + ' ' + cx + ' ' + (cy - 8) + ' Q' + (cx + 7) + ' ' + (cy - 8) + ' ' + (cx + 7) + ' ' + (cy + 2) + ' V' + (cy + 26) + '" class="paper-fill" stroke-width="2.2"/>' +
    '<path d="M' + (cx - 4) + ' ' + (cy - 1) + ' Q' + cx + ' ' + (cy - 5) + ' ' + (cx + 4) + ' ' + (cy - 1) + '" fill="none" stroke-width="1.4"/></g>';
}
const svg = (body) => '<svg class="hw-art" viewBox="0 0 160 112" aria-hidden="true" focusable="false" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round">' + body + '</svg>';

/** Panel 1: three countdown pips, the GO stamp, the throttle held under it. */
function artLaunch(v, p) {
  const pips = [26, 50, 74].map((x, i) => '<circle cx="' + x + '" cy="20" r="7" stroke-width="2" class="' + (i === 2 ? 'pk-line' : '') + '"/>').join('');
  const go = '<g transform="rotate(-8 124 20)"><rect x="100" y="7" width="48" height="26" rx="3" class="pk-line" stroke-width="2.4"/>' +
    '<text x="124" y="27" font-size="17" text-anchor="middle" class="hw-stamp-t">GO</text></g>';
  const cue = '<path d="M118 38 C 112 50, 100 54, 92 60" stroke-width="1.6" stroke-dasharray="3 4"/><path d="M92 60 l7 -1 M92 60 l2 -6" stroke-width="1.6"/>';
  let dev;
  if (v.last === 'pad') dev = trigger(58, 58, p.rt);
  else if (v.last === 'touch') dev = finger(80, 74);
  else dev = key(56, 58, 48, 40, 'W');
  return svg(pips + go + cue + dev);
}
/** Panel 2: a bend in the road, a kart sliding round it with sparks, and the hands that do it. */
function artDrift(v, p) {
  const road = '<path d="M60 112 C 62 84, 78 62, 112 52 L150 44" stroke-width="2.2"/><path d="M100 112 C 100 94, 112 80, 132 74 L156 68" stroke-width="2.2"/>' +
    '<path d="M80 110 C 82 90, 96 72, 124 63" stroke-width="1.4" stroke-dasharray="5 6"/>';
  // a kart seen from above, sliding nose-in round the bend: four wheels, a pink seat, sparks off the back wheels
  const kart = '<g transform="rotate(48 94 86)">' +
    '<rect x="83" y="76" width="5" height="7" rx="1.5" class="ink-fill"/><rect x="100" y="76" width="5" height="7" rx="1.5" class="ink-fill"/>' +
    '<rect x="83" y="90" width="5" height="7" rx="1.5" class="ink-fill"/><rect x="100" y="90" width="5" height="7" rx="1.5" class="ink-fill"/>' +
    '<path d="M87 74 Q94 70 101 74 L102 97 Q94 100 86 97 Z" class="paper-fill" stroke-width="2"/><circle cx="94" cy="88" r="3.6" class="pk-fill"/></g>' +
    '<path d="M76 98 l-9 3 M78 103 l-7 7 M84 104 l-2 8" class="pk-line" stroke-width="2"/>' +
    '<path d="M70 92 C 72 80, 80 72, 88 68" stroke-width="1.3" stroke-dasharray="2 4" class="pk-line"/>';
  let dev;
  if (v.last === 'pad') {
    dev = '<circle cx="30" cy="30" r="17" stroke-width="2.2"/><circle cx="30" cy="30" r="8" class="ink-fill"/>' +
      '<path d="M6 30 l5 -4 M6 30 l5 4 M54 30 l-5 -4 M54 30 l-5 4" stroke-width="1.8"/>' + button(30, 84, 15, p.south);
  } else if (v.last === 'touch') {
    dev = '<rect x="6" y="8" width="58" height="34" rx="5" stroke-width="2"/><path d="M38 10 v30" stroke-width="1.2" stroke-dasharray="2 3"/>' +
      '<path d="M12 25 h20 M12 25 l4 -3 M12 25 l4 3 M32 25 l-4 -3 M32 25 l-4 3" stroke-width="1.6"/>' +
      '<circle cx="51" cy="25" r="6" class="pk-line hw-key" stroke-width="2"/>';
  } else {
    dev = key(6, 10, 26, 26, 'A') + key(38, 10, 26, 26, 'D') + key(8, 76, 50, 22, 'Space');
  }
  return svg(road + kart + dev);
}
/** Panel 3: a pink word bubble ahead, a word painted down the road, and the throw. */
function artWords(v, p) {
  // the road runs away to the bubble; a word lies painted on it (the song), the bubble floats where it ends
  const road = '<path d="M60 112 L96 64 M150 112 L114 64" stroke-width="2.2"/><path d="M105 70 v5 M105 82 v6" stroke-width="1.6"/>' +
    '<text x="0" y="0" font-size="15" text-anchor="middle" class="hw-road-t" transform="translate(106 106) scale(1.35 0.7)">song</text>';
  const bubble = '<circle cx="105" cy="38" r="21" class="pk-soft" stroke-width="2.2"/><path d="M92 30 q5 -7 13 -8" stroke-width="2" class="paper-line"/>' +
    '<text x="105" y="43" font-size="13" text-anchor="middle" class="hw-bub-t">pink</text>' +
    '<path d="M82 16 l-5 -5 M105 10 v-7 M128 16 l5 -5 M134 38 h7" class="pk-line" stroke-width="2"/>';
  // the throw: from the item key ahead to the bubble
  const arc = '<path d="M26 58 C 30 30, 56 22, 78 30" stroke-width="1.6" stroke-dasharray="3 4"/><path d="M78 30 l-7 -3 M78 30 l-5 5" stroke-width="1.6"/>';
  let dev;
  if (v.last === 'pad') dev = button(24, 80, 15, p.west);
  else if (v.last === 'touch') dev = '<rect x="10" y="62" width="30" height="30" rx="7" stroke-width="2.2"/><circle cx="25" cy="77" r="6" class="pk-fill"/>' + finger(25, 86);
  else dev = key(8, 64, 32, 32, 'E');
  return svg(road + bubble + arc + dev);
}

/** What the throttle is called on the device in use. */
export function throttleName(v, p) { return v.last === 'pad' ? p.rt : v.last === 'touch' ? 'Touch' : 'W'; }

/** The three panels for a device: { n, art, cap }. */
export function howtoPanels(devices = {}) {
  const v = deviceView(devices || {}), p = padLabels(v.family, v.profile);
  const thr = throttleName(v, p);
  const pad = v.last === 'pad', touch = v.last === 'touch';
  return [
    { n: 1, art: artLaunch(v, p), cap: touch ? 'Touch on GO' : 'Hold ' + thr + ' on GO' },
    { n: 2, art: artDrift(v, p), cap: touch ? 'Drag steers, hold drifts' : 'Steer, ' + (pad ? p.south : 'Space') + ' drifts' },
    { n: 3, art: artWords(v, p), cap: 'Pop pink words, ' + (pad ? p.west : touch ? 'tap' : 'E') + ' throws' },
  ];
}

/** A row of chips for one option: the picked chip is pressed in (act 'pick', hud/cards.js stores it). */
function chips(key, icon, label, list, now, hint = '') {
  return '<div class="pr-row" role="group" aria-label="' + label + '">' + icon + '<span class="pr-l">' + label + '</span><div class="c-chips">' +
    list.map(([v, t]) => '<button type="button" class="c-btn c-chip' + (v === now ? ' on' : '') + '" data-act="pick" data-key="' + key +
      '" data-val="' + v + '" aria-pressed="' + (v === now) + '">' + esc(t) + '</button>').join('') + '</div>' +
    (hint ? '<small class="pr-h">' + esc(hint) + '</small>' : '') + '</div>';
}
const ICON = (body) => '<svg class="pr-i" viewBox="0 0 32 32" aria-hidden="true" focusable="false" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round">' + body + '</svg>';
/** A closed loop with a flag at the line, and a wheel. */
const LAP_ICON = ICON('<path d="M8 22 C 2 18, 3 8, 12 8 H20 C 29 8, 30 18, 24 22 C 20 25, 12 25, 8 22 Z"/><path d="M16 4 v8 M16 4 h6 l-2 2 l2 2 h-6" class="pk-line"/>');
const WHEEL_ICON = ICON('<circle cx="16" cy="16" r="11"/><circle cx="16" cy="16" r="3" class="pk-line"/><path d="M5 16 h8 M19 16 h8 M16 19 v8"/>');

/** The pre-race rows (playtest 2026-10-28): Laps 1 / 2 / 3 and Steer assist, above the how-to panels.
 * opts: the hud/options.js record; label(key, value): the chip text (options.js choiceLabel); hint(mode): the
 * assist line under its chips (options.js assistHint). */
export function preraceHtml(opts = {}, label = (k, v) => String(v), hint = () => '') {
  const lapRow = chips('laps', LAP_ICON, 'Laps', [1, 2, 3].map((n) => [n, String(n)]), Number(opts.laps) || 1);
  const assistRow = chips('steerAssist', WHEEL_ICON, 'Assist', ['light', 'strong', 'off'].map((m) => [m, label('steerAssist', m)]), opts.steerAssist || 'light',
    hint(opts.steerAssist || 'light'));
  return '<div class="pr-rows">' + lapRow + assistRow + '</div>';
}

/** The card's inner html: three panels, camera hint, the go line and don't-show-again switch. */
export function howtoHtml(devices = {}, { off = false } = {}) {
  const v = deviceView(devices || {}), p = padLabels(v.family, v.profile);
  const panels = howtoPanels(devices).map((x) => '<li class="hw-panel" style="--hw-i:' + (x.n - 1) + '"><span class="hw-n" aria-hidden="true">' + x.n +
    '</span>' + x.art + '<p class="hw-cap">' + esc(x.cap) + '</p></li>').join('');
  const goLabel = v.last === 'touch' ? 'Start' : 'Press ' + throttleName(v, p) + ' to start';
  const viewLabel = v.last === 'pad' ? p.rs : v.last === 'touch' ? 'Tap View' : 'C';
  const rearLabel = v.last === 'pad' ? v.family === 'xbox' ? 'Hold left stick in' : 'Hold ' + p.ls
    : v.last === 'touch' ? 'Hold Rear' : 'Hold V';
  return '<ol class="hw-panels" data-last="' + v.last + '">' + panels + '</ol>' +
    '<p class="c-hint hw-camera" style="margin:0 0 14px"><strong>' + esc(viewLabel) + '</strong>: chase / first person. ' +
    '<strong>' + esc(rearLabel) + '</strong>: look behind.</p>' +
    '<div class="hw-foot"><button type="button" class="c-btn hot hw-go" data-act="howto-go">' + esc(goLabel) + '</button>' +
    '<button type="button" class="c-btn c-tog hw-off' + (off ? ' on' : '') + '" data-act="howto-off" aria-pressed="' + !!off + '">Don&#39;t show again</button></div>';
}
