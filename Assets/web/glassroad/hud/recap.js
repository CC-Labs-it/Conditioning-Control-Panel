/* hud/recap.js - the results recap pages (CONTRACT-LOGBOOK.md section 5). Pure: strings only, no DOM.
 *
 * Page 1 is the podium card (hud/cards.js draws it as before). Page 2 = three highlights and a course sketch:
 * the loop from hud.setTrack (x, z pairs sampled evenly along track s), pink dots where the player fell, a teal
 * stroke over the best third. Page 3 = rubber stamps for new bests, the medal, new achievements and unlocks;
 * it exists only when one of those has anything. A null or missing recap is one page: today's card.
 * cards.js owns the DOM, the in / out and the sounds; this file says what each page holds.
 */
import { formatTime } from './format.js';
import { fitMap, projectInto, thinLoop } from './minimap.js';

export const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);

const MEDAL_NAMES = { bronze: 'Bronze', silver: 'Silver', gold: 'Gold', glass: 'Glass' };
const MEDAL_UP = ['bronze', 'silver', 'gold', 'glass'];

/** True when page 3 has something to stamp. */
export function hasStamps(recap) {
  if (!recap || typeof recap !== 'object') return false;
  const pb = recap.pb || {};
  return !!(pb.race || pb.lap || pb.trial || (recap.medal && recap.medal.to) ||
    (Array.isArray(recap.achievements) && recap.achievements.length) || (Array.isArray(recap.unlocks) && recap.unlocks.length));
}

/** The page names for a recap: ['podium'], ['podium', 'run'] or ['podium', 'run', 'stamps']. */
export function recapPages(recap) {
  if (!recap || typeof recap !== 'object') return ['podium'];
  return hasStamps(recap) ? ['podium', 'run', 'stamps'] : ['podium', 'run'];
}

/** Page state: which pages exist, where we are, and a step forward that stops on the last page. */
export function recapState(recap) {
  const pages = recapPages(recap);
  let i = 0;
  return {
    pages,
    get i() { return i; },
    get page() { return pages[i]; },
    get last() { return i >= pages.length - 1; },
    /** One page on; false (and no change) on the last page. */
    next() { if (i >= pages.length - 1) return false; i++; return true; },
  };
}

/** The page dots: '<div class="rc-dots" ...><i class="on"></i><i></i></div>'. Empty for a single page. */
export function dotsHtml(pages, i) {
  if (!pages || pages.length < 2) return '';
  return '<div class="rc-dots" role="img" aria-label="Page ' + (i + 1) + ' of ' + pages.length + '">' +
    pages.map((_, k) => '<i' + (k === i ? ' class="on"' : '') + '></i>').join('') + '</div>';
}

const W = 180, H = 96, PAD = 8;
const frac = (v) => v - Math.floor(v);

/**
 * The course sketch as inline SVG: the loop in ink, the best third (sketch.bestSector 0..2) in teal, a pink
 * dot per fall (sketch.fallsAtS in metres along the course, mapped by sketch.lengthM). '' without a loop.
 * @param {{ xz: Float32Array|number[] }|null} loop
 */
export function sketchSvg(loop, sketch = {}) {
  const raw = loop && loop.xz;
  if (!raw || raw.length < 8) return '';
  const xz = thinLoop(raw, 240), n = Math.floor(xz.length / 2);
  const fit = fitMap(xz, W, H, PAD), p = [0, 0];
  const pt = (i) => { const k = ((i % n) + n) % n; projectInto(fit, xz[k * 2], xz[k * 2 + 1], p); return p[0].toFixed(1) + ' ' + p[1].toFixed(1); };
  let d = '';
  for (let i = 0; i < n; i++) d += (i ? 'L' : 'M') + pt(i);
  d += 'Z';
  const L = Number(sketch && sketch.lengthM) > 0 ? Number(sketch.lengthM) : 0;
  const at = (s) => Math.floor(frac(L > 0 ? s / L : 0) * n);
  let best = '';
  const b = sketch && sketch.bestSector;
  if (b === 0 || b === 1 || b === 2) {
    const from = Math.floor(b * n / 3), to = Math.floor((b + 1) * n / 3);
    let bd = '';
    for (let i = from; i <= to; i++) bd += (i === from ? 'M' : 'L') + pt(i);
    best = '<path class="rc-best" d="' + bd + '"/>';
  }
  const falls = (L > 0 && Array.isArray(sketch.fallsAtS) ? sketch.fallsAtS : []).filter(Number.isFinite).slice(0, 20)
    .map((s) => { const [x, y] = pt(at(s)).split(' '); return '<circle class="rc-fall" cx="' + x + '" cy="' + y + '" r="3.2"/>'; }).join('');
  const [sx, sy] = pt(0).split(' ');
  return '<svg class="rc-sketch" viewBox="0 0 ' + W + ' ' + H + '" role="img" aria-label="' + esc(sketchLabel(sketch)) + '">' +
    '<path class="rc-loop" d="' + d + '"/>' + best + '<rect class="rc-start" x="' + (Number(sx) - 1.5) + '" y="' + (Number(sy) - 4) + '" width="3" height="8"/>' +
    falls + '</svg>';
}

function sketchLabel(sketch = {}) {
  const f = Array.isArray(sketch.fallsAtS) ? Math.min(20, sketch.fallsAtS.filter(Number.isFinite).length) : 0;
  const thirds = ['first', 'middle', 'last'];
  const b = thirds[sketch.bestSector];
  return 'Course map' + (b ? ', best on the ' + b + ' third' : '') + (f ? ', ' + f + (f === 1 ? ' fall' : ' falls') : ', no falls');
}

/** Page 2: the three highlights and the sketch. */
export function runPageHtml(recap, loop = null) {
  const hl = (recap && Array.isArray(recap.highlights) ? recap.highlights : []).slice(0, 3);
  const sk = sketchSvg(loop, (recap && recap.sketch) || {});
  const key = sk ? '<p class="rc-key"><span class="k-best">Best third</span><span class="k-fall">Fell off</span></p>' : '';
  return '<ul class="rc-hl">' + hl.map((h, k) => '<li style="--i:' + k + '"><b>' + esc(h && h.value) + '</b><span>' + esc(h && h.label) + '</span></li>').join('') +
    '</ul>' + (sk ? '<div class="rc-map">' + sk + key + '</div>' : '');
}

/** The dry hint under a medal stamp: the next medal up and its time, or a line for the top one. */
export function medalHint(medal) {
  if (!medal || !medal.to) return '';
  const up = MEDAL_UP[MEDAL_UP.indexOf(medal.to) + 1];
  if (!up) return 'Nothing above glass. Go outside.';
  const t = medal.times && Number(medal.times[up]);
  return t > 0 ? MEDAL_NAMES[up] + ' is ' + formatTime(t) + '. Just saying.' : '';
}

/** The stamps on page 3, in order: bests, the medal, achievements, unlocks. Each { kind, head, line }. */
export function stampList(recap) {
  if (!recap) return [];
  const out = [], pb = recap.pb || {};
  if (pb.race) out.push({ kind: 'pb', head: 'New best race', line: '' });
  if (pb.lap) out.push({ kind: 'pb', head: 'New best lap', line: '' });
  if (pb.trial) out.push({ kind: 'pb', head: 'New best trial', line: '' });
  const m = recap.medal;
  if (m && MEDAL_NAMES[m.to]) out.push({ kind: 'medal medal-' + m.to, head: MEDAL_NAMES[m.to] + ' medal', line: medalHint(m) });
  for (const a of Array.isArray(recap.achievements) ? recap.achievements : []) if (a && a.name) out.push({ kind: 'ach', head: String(a.name), line: String(a.line || '') });
  for (const u of Array.isArray(recap.unlocks) ? recap.unlocks : []) if (u && u.label) out.push({ kind: 'unlock', head: 'Unlocked: ' + u.label, line: '' });
  return out;
}

/** Page 3: one stamp per item, a little tilt each, delays by order (cards.js decides thunk or fade). */
export function stampsPageHtml(recap) {
  const tilt = [-4, 3, -2, 5, -3, 2];
  return '<ul class="rc-stamps">' + stampList(recap).map((s, k) =>
    '<li class="rc-stamp ' + s.kind + '" style="--i:' + k + ';--r:' + tilt[k % tilt.length] + 'deg"><b>' + esc(s.head) + '</b>' +
    (s.line ? '<span>' + esc(s.line) + '</span>' : '') + '</li>').join('') + '</ul>';
}

/** Title per page after the podium. */
export const PAGE_TITLES = Object.freeze({ run: 'How it went', stamps: 'For the logbook' });

/** A dev fixture (?dev=1&recap=fixture) so the pages can be looked at after any race. */
export const RECAP_FIXTURE = Object.freeze({
  mode: 'trial',
  highlights: [{ key: 'driftBoosts3', label: 'Drift boosts, tier 3', value: '7' }, { key: 'airS', label: 'Seconds in the air', value: '11.4' },
    { key: 'overtakes', label: 'Overtakes', value: '9' }],
  sketch: { fallsAtS: [0.18, 0.62], bestSector: 1, lengthM: 1 },
  pb: { race: false, lap: true, trial: true },
  medal: { from: 'bronze', to: 'silver', times: { bronze: 200, silver: 185, gold: 172, glass: 165 } },
  achievements: [{ id: 'long-hold', name: 'Long Hold', line: 'Held a drift for four seconds' }],
  unlocks: [{ id: 'kart:velvet-lock', label: 'Velvet Lock' }],
  xp: null,
});
