/* ui/unlocks/fate/llama/art.js - The Panel ending's PLACEHOLDER drawings, in the same waxy crayon and dark ink as the Go
 * Outside cards (../outside/art.js). Each piece is baked once into an SVG picture, so the filters cost nothing after the
 * first paint and the show only moves whole pictures. Nothing here touches the DOM at import time.
 *
 * Owed (proper ink-and-crayon drawings, the style of assets/documentary/art/sources.json), each a transparent webp that
 * replaces one function below at the same aspect: see OWED. */

import { svg, url, cutout, inked, wax, INK, PAPER } from '../outside/art.js';

/** The drawings this ending still owes, with the size each is shown at (CSS px on a 1440 wide desktop, art at 2x). */
export const OWED = Object.freeze([
  { file: 'assets/fate/llama/panel.webp', size: '1600 x 900 (16:9), 800 x 450 on screen', what: 'Scene 1: three llamas behind a long judges\' bench, the middle one (the chair) a little taller, a manila file on the bench in front of it. Empty courtroom wall behind.' },
  { file: 'assets/fate/llama/chair-chew.webp', size: '480 x 600 (4:5), 240 x 300 on screen', what: 'Scene 2: the chair llama, head and neck, chewing a page of the file, bored half-lidded eyes. Transparent background.' },
  { file: 'assets/fate/llama/chair-spit.webp', size: '480 x 600 (4:5), 240 x 300 on screen', what: 'Scene 2: the same llama a beat later, spitting a crumpled paper wad. Transparent background.' },
  { file: 'assets/stickers/e_ll_end.webp', size: '512 x 512, sticker shape with a white cut border like p_llama.webp', what: 'Scene 3 and the Sticker Book: the chair llama wearing a judge\'s white collar, holding nothing, smug.' },
  { file: 'assets/adcard/chair.webp', size: '128 x 160, shown at herd size (about 4% of the card width)', what: 'The chair as it sits in the Living Ad card herd row, first in line, wearing the collar.' },
]);

const WOOL = '#f7f1e3', SHADE = '#e4d8bd', PINK = '#f2a6bf', MUZZLE = '#efe3cb', WOOD = '#b5824f', WOOD_LIGHT = '#cf9f6c', MANILA = '#efcf86';

/* A front-facing llama from the chest up (the bench hides the rest), 120 x 170. */
const NECK = 'M42 172 C42 140 44 112 46 94 L74 94 C76 112 78 140 78 172 Z';
const HEAD = 'M36 64 C36 42 48 34 60 34 C72 34 84 42 84 64 C84 80 76 90 72 96 L48 96 C44 90 36 80 36 64 Z';
const SNOUT = 'M47 78 C47 98 53 104 60 104 C67 104 73 98 73 78 C68 74 52 74 47 78 Z';
const EAR_L = 'M44 46 C36 28 36 14 40 6 C48 12 52 26 53 40 Z', EAR_R = 'M76 46 C84 28 84 14 80 6 C72 12 68 26 67 40 Z';
const IN_L = 'M44 36 C41 27 41 18 42 13 C46 19 48 26 48 34 Z', IN_R = 'M76 36 C79 27 79 18 78 13 C74 19 72 26 72 34 Z';
const TUFT = 'M45 42 C45 31 52 28 55 35 C56 26 64 26 65 35 C68 28 75 31 75 42';
const LIDS = 'M46 62 C49 59 55 59 58 62 M62 62 C65 59 71 59 74 62';
const FACE = 'M55 88 C56 90 57 90 58 88 M62 88 C63 90 64 90 65 88 M54 96 C58 99 62 99 66 96';
const FLEECE = 'M44 124 C48 120 52 124 56 120 C60 124 64 120 68 124 C72 120 76 124 78 122 M43 146 C47 142 51 146 55 142 C59 146 63 142 67 146 C71 142 75 146 78 144';
const COLLAR = 'M48 100 L60 118 L72 100 L66 98 L60 108 L54 98 Z';

const llamaCache = new Map();
/** One panel llama. kind: 'chair' (the middle one, a white collar) or 'side'. */
export function llamaUrl(kind = 'side') {
  if (llamaCache.has(kind)) return llamaCache.get(kind);
  const body = cutout(NECK + ' ' + HEAD + ' ' + EAR_L + ' ' + EAR_R, 10)
    + wax(NECK, WOOL) + wax(HEAD, WOOL) + wax(EAR_L, WOOL) + wax(EAR_R, WOOL)
    + wax(IN_L, PINK) + wax(IN_R, PINK) + wax(SNOUT, MUZZLE)
    + wax('M46 94 L74 94 L76 112 L44 112 Z', SHADE, 'opacity=".7"')
    + (kind === 'chair' ? wax(COLLAR, PAPER) + inked(COLLAR, 2.4) : '')
    + inked(NECK) + inked(HEAD) + inked(EAR_L) + inked(EAR_R) + inked(SNOUT, 2.8) + inked(TUFT, 2.8)
    + inked(LIDS, 2.8) + inked(FACE, 2.2) + inked(FLEECE, 2)
    + `<circle cx="52" cy="65" r="2.6" fill="${INK}"/><circle cx="68" cy="65" r="2.6" fill="${INK}"/>`;
  const u = url(svg('-4 -4 128 180', body));
  llamaCache.set(kind, u);
  return u;
}

let benchCache = null;
/** The long bench, 800 x 150: a top board and a panelled front. */
export function benchUrl() {
  if (benchCache) return benchCache;
  const top = 'M8 18 L792 18 L792 44 L8 44 Z', front = 'M28 44 L772 44 L772 150 L28 150 Z';
  const body = cutout(top + ' ' + front, 8) + wax(front, WOOD) + wax(top, WOOD_LIGHT)
    + inked(top, 3.6) + inked(front, 3.6)
    + inked('M282 58 L282 138 M518 58 L518 138 M44 58 L756 58 M44 138 L756 138', 2.2)
    + inked('M60 80 C140 76 220 84 262 80 M300 96 C380 92 460 100 500 96 M540 84 C620 80 700 88 740 84', 1.4);
  benchCache = url(svg('0 0 800 150', body));
  return benchCache;
}

let fileCache = null;
/** Your file: a manila folder with a page sticking out, 120 x 84. */
export function fileUrl() {
  if (fileCache) return fileCache;
  const page = 'M16 4 L100 4 L100 36 L16 36 Z', folder = 'M6 18 L44 18 L52 10 L114 10 L114 78 L6 78 Z';
  const body = cutout(page + ' ' + folder, 6) + wax(page, PAPER) + inked(page, 2.2)
    + inked('M24 14 L92 14 M24 22 L80 22 M24 30 L88 30', 1.4) + wax(folder, MANILA) + inked(folder, 3);
  fileCache = url(svg('0 0 120 84', body));
  return fileCache;
}

let wadCache = null;
/** What is left of the file: a crumpled, slightly damp paper wad, 40 x 40. */
export function wadUrl() {
  if (wadCache) return wadCache;
  const d = 'M8 20 C6 10 16 6 22 8 C32 6 36 16 32 24 C34 32 24 36 18 32 C10 34 6 28 8 20 Z';
  const body = cutout(d, 4) + wax(d, PAPER) + inked(d, 2.4) + inked('M14 18 L22 22 L18 28 M24 12 L26 20 L32 22', 1.4);
  wadCache = url(svg('0 0 40 40', body));
  return wadCache;
}
