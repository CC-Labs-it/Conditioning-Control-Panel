/* hud/howto.js - the 1-2-3 card: the gist of the game in three prints (owner 2026-10-31: "our usual 3 beats card
 * with 1-2-3 that explains the gist of the game, easily and with some images"). Pure: strings only, no DOM; hud/cards.js
 * shows it.
 *
 * Picture first, near-zero text: three paper panels, each a numbered rubber stamp, a real capture of the game
 * (art/guide/, no Scrolller or personal media in any of them), a stamped title and one line, then the hands: an ink key
 * (inline SVG) and a caption for the device used last (hud/labels.js names): keyboard keys, pad buttons (Xbox,
 * PlayStation or numbered), or the touch zones.
 *   1  RACE THE SONG: the song lays the road and its words; hold throttle on GO (sim/race.js pays a press that starts
 *      in the last beat before go)
 *   2  DRIFT, BOOST: hold a drift through a bend, let go for speed
 *   3  POP AND THROW: drive through a bubble for a power-up, throw it
 * Ink is currentColor, the spot colour is the `pk` class (styles.css); keys carry `hw-key` so they press on the beat.
 */
import { deviceView, padLabels, KEY_LABELS, itemTip } from './labels.js';

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
/** The hands strip: ink keys at a fixed height, as wide as this device's keys need. */
const svg = (w, body) => '<svg class="hw-art" viewBox="0 0 ' + w + ' 52" aria-hidden="true" focusable="false" fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round">' + body + '</svg>';

/** Beat 1: the throttle, held on GO. */
function handsLaunch(v, p) {
  if (v.last === 'pad') return svg(48, trigger(2, 2, p.rt));
  if (v.last === 'touch') return svg(40, finger(20, 20));
  return svg(44, key(2, 4, 40, 40, 'W'));
}
/** Beat 2: steer, and the hop into a drift. */
function handsDrift(v, p) {
  if (v.last === 'pad') {
    return svg(84, '<circle cx="24" cy="26" r="17" stroke-width="2.2"/><circle cx="24" cy="26" r="8" class="ink-fill"/>' +
      '<path d="M2 26 l5 -4 M2 26 l5 4 M46 26 l-5 -4 M46 26 l-5 4" stroke-width="1.8"/>' + button(66, 26, 15, p.south));
  }
  if (v.last === 'touch') {
    return svg(62, '<rect x="2" y="9" width="58" height="34" rx="5" stroke-width="2"/><path d="M34 11 v30" stroke-width="1.2" stroke-dasharray="2 3"/>' +
      '<path d="M8 26 h20 M8 26 l4 -3 M8 26 l4 3 M28 26 l-4 -3 M28 26 l-4 3" stroke-width="1.6"/>' +
      '<circle cx="47" cy="26" r="6" class="pk-line hw-key" stroke-width="2"/>');
  }
  return svg(116, key(2, 12, 26, 26, 'A') + key(32, 12, 26, 26, 'D') + key(64, 12, 50, 26, 'Space'));
}
/** Beat 3: the throw. */
function handsThrow(v, p) {
  if (v.last === 'pad') return svg(40, button(20, 26, 16, p.west));
  if (v.last === 'touch') return svg(40, '<rect x="5" y="2" width="30" height="30" rx="7" stroke-width="2.2"/><circle cx="20" cy="17" r="6" class="pk-fill"/>' + finger(20, 24));
  return svg(44, key(2, 4, 40, 40, 'E'));
}

/** The three captures: real races on Threshold, cropped small (art/guide/, 520 x 273). */
export const BEATS = [
  { title: 'Race the song', gist: 'The song lays the road', img: 'art/guide/race-the-song.webp', alt: 'A kart on a rainbow road, a lyric painted across it' },
  { title: 'Drift, boost', gist: 'Hold through a bend, let go', img: 'art/guide/drift-boost.webp', alt: 'A kart sliding round a bend, sparks off the back' },
  { title: 'Pop and throw', gist: 'A bubble holds a power-up', img: 'art/guide/pop-and-throw.webp', alt: 'A kart driving at two bubbles on the road' },
];

/** What the throttle is called on the device in use. */
export function throttleName(v, p) { return v.last === 'pad' ? p.rt : v.last === 'touch' ? 'Touch' : 'W / Up'; }

/** The three panels for a device: { n, title, gist, img, alt, art, cap }. */
export function howtoPanels(devices = {}) {
  const v = deviceView(devices || {}), p = padLabels(v.family, v.profile);
  const thr = throttleName(v, p);
  const pad = v.last === 'pad', touch = v.last === 'touch';
  return [
    { n: 1, ...BEATS[0], art: handsLaunch(v, p), cap: touch ? 'Touch on GO' : 'Hold ' + thr + ' on GO' },
    { n: 2, ...BEATS[1], art: handsDrift(v, p), cap: touch ? 'Drag steers, hold drifts' : 'Steer, ' + (pad ? p.south : 'Space / Shift') + ' drifts' },
    { n: 3, ...BEATS[2], art: handsThrow(v, p), cap: 'Pop bubbles, ' + (pad ? p.west : touch ? 'tap' : KEY_LABELS.item) + ' throws' },
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

/** The card's inner html: three prints, camera hint, the go line and don't-show-again switch. */
export function howtoHtml(devices = {}, { off = false } = {}) {
  const v = deviceView(devices || {}), p = padLabels(v.family, v.profile);
  const panels = howtoPanels(devices).map((x) => '<li class="hw-panel" style="--hw-i:' + (x.n - 1) + '"><span class="hw-n" aria-hidden="true">' + x.n +
    '</span><img class="hw-shot" src="' + x.img + '" width="520" height="273" alt="' + esc(x.alt) + '" decoding="async" draggable="false">' +
    '<div class="hw-words"><h3 class="hw-title">' + esc(x.title) + '</h3><p class="hw-gist">' + esc(x.gist) + '</p>' +
    '<div class="hw-hands">' + x.art + '<p class="hw-cap">' + esc(x.cap) + '</p></div></div></li>').join('');
  const goLabel = v.last === 'touch' ? 'Start' : 'Press ' + throttleName(v, p) + ' to start';
  const viewLabel = v.last === 'pad' ? p.rs : v.last === 'touch' ? 'Tap View' : 'C';
  const rearLabel = v.last === 'pad' ? v.family === 'xbox' ? 'Hold left stick in' : 'Hold ' + p.ls
    : v.last === 'touch' ? 'Hold Rear' : 'Hold V';
  return '<ol class="hw-panels" data-last="' + v.last + '">' + panels + '</ol>' +
    '<p class="c-hint hw-items" style="margin:0 0 10px">' + esc(itemTip(devices)) + '.</p>' +
    '<p class="c-hint hw-camera" style="margin:0 0 14px"><strong>' + esc(viewLabel) + '</strong>: chase / first person. ' +
    '<strong>' + esc(rearLabel) + '</strong>: look behind.</p>' +
    '<div class="hw-foot"><button type="button" class="c-btn hot hw-go" data-act="howto-go">' + esc(goLabel) + '</button>' +
    '<button type="button" class="c-btn hw-off" data-act="glossary">Power-ups</button>' +
    '<button type="button" class="c-btn c-tog hw-off' + (off ? ' on' : '') + '" data-act="howto-off" aria-pressed="' + !!off + '">Don&#39;t show again</button></div>';
}
