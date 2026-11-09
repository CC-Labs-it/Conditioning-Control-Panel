/* ui/adcard/model.js - what the Living Ad card says and wears, from the run. Pure: no DOM, testable in node.
 *
 * One portrait per FIRST theme (Pick a Theme); the second theme (Second Theme) only brings its colour. Colours come from
 * the game palette: the first theme owns paper and ink, each theme brings an accent darkened until it reads on white.
 * ACCEPTED is yellow like the approved card unless a theme sits within 50 degrees of yellow. */

import { THEME_PALETTES, THEME_IDS, mixHex, secondTheme } from '../palette.js';
import { ADCARD_LINES, ADCARD_COPY } from '../../data/content/adcard.js';
import { brandTokens } from '../../data/brand.js';
import { whole } from '../../sim/format.js';

export const LLAMA_MIN = 3, LLAMA_MAX = 12;   // the intro shelters three, each opt-out adds one, up to twelve (LLAMA_WORDS)

const h2c = (h) => [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16));
const c2h = (c) => '#' + c.map((v) => Math.round(Math.max(0, Math.min(255, v))).toString(16).padStart(2, '0')).join('');
function lumi(c) {
  const [r, g, b] = c.map((v) => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
export const contrast = (a, b) => { const x = lumi(h2c(a)), y = lumi(h2c(b)); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); };
/** Darken until the colour reads on white (3.3:1, the approved card's pink). */
export function forText(h) {
  let c = h2c(h);
  for (let i = 0; i < 40 && contrast(c2h(c), '#ffffff') < 3.3; i++) c = c.map((v) => v * 0.94);
  return c2h(c);
}
function hueOf(h) {
  const [r, g, b] = h2c(h).map((v) => v / 255), mx = Math.max(r, g, b), mn = Math.min(r, g, b), d = mx - mn;
  if (d < 0.12) return null;
  const H = mx === r ? ((g - b) / d) % 6 : mx === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return (H * 60 + 360) % 360;
}
const HOTS = ['#ffe14d', '#ff3d8b', '#3de0ff', '#b6ff3d', '#ff8a3d'];

export function cardColours(first, second = null) {
  const P = THEME_PALETTES[first] || THEME_PALETTES.bimbo, Q = second && second !== first ? THEME_PALETTES[second] : null;
  const A = forText(P.a), B = Q ? forText(Q.a) : forText(mixHex(P.a, P.ink, 0.45));
  const paper = mixHex(P.soft, P.a, 0.12);
  const hs = [hueOf(A), hueOf(B)].filter((x) => x != null);
  const dist = (h) => (h == null ? 0 : Math.min(...hs.map((x) => { const d = Math.abs(h - x) % 360; return Math.min(d, 360 - d); }), 999));
  const hot = HOTS.find((h) => dist(hueOf(h)) >= 50) || HOTS.slice().sort((x, y) => dist(hueOf(y)) - dist(hueOf(x)))[0];
  const [hr, hg, hb] = h2c(hot);
  return Object.freeze({
    A, B, M: mixHex(A, B, 0.5), hot, ink: P.ink, paper, hlc: `rgba(${hr},${hg},${hb},.7)`,
    spiral: Object.freeze([P.a, paper, Q ? Q.a : mixHex(P.a, P.ink, 0.35), paper]),
  });
}

const esc = (t) => String(t).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const fill = (s, v) => String(s).replace(/\{(\w+)\}/g, (m, k) => (k in v ? v[k] : m));

/** A line's template as safe HTML: the text is escaped first, then only <a>, <b>, <hl> become spans. */
export function fillLine(tpl, vals) {
  const safe = Object.fromEntries(Object.entries(vals).map(([k, v]) => [k, esc(v)]));
  return esc(tpl).replace(/&lt;(\/?)(a|b|hl)&gt;/g, (m, close, t) => (close ? '</span>' : `<span class="${t}">`))
    .replace(/&quot;/g, '"').replace(/\{(name|unit|time|terms|pics)\}/g, (m, k) => safe[k] ?? m);
}

export function timeText(ms) {
  const M = ADCARD_COPY.minutes, mins = Math.max(1, Math.round(ms / 60000));
  if (mins < 60) return mins === 1 ? M.one : fill(M.many, { n: mins });
  const h = Math.floor(mins / 60), m = mins % 60;
  if (!m) return h === 1 ? M.hour : fill(M.hours, { h });
  return fill(h === 1 ? M.hm : M.hsm, { h, m });
}

/** Bar fill, 6-100: pictures against 1,500, Golden Buttons against 30, the score on a log scale up to ten million. */
export function barWidths({ pics = 0, total = 0, golden = 0 }) {
  const cap = (x) => Math.round(Math.max(6, Math.min(100, x)));
  return {
    pics: cap(100 * Math.sqrt(pics / 1500)),
    fuzz: cap(100 * Math.log10(Math.max(1, total)) / 7),
    golden: cap(100 * Math.sqrt(golden / 30)),
  };
}

/** One row of llamas whatever the herd: past eight they crowd (overlap) so each stays readable. */
export function llamaLayout(n) {
  const slot = 38 / n;
  if (n <= 8) return { font: Math.min(3.8, slot * 0.6), gap: Math.min(1.4, slot * 0.14), herd: false };
  const font = 38 / (1.37 * n - 0.3 * (n - 1));
  return { font, gap: -0.3 * font, herd: true };
}

const clampInt = (v, lo, hi) => Math.max(lo, Math.min(hi, Math.floor(Number.isFinite(v) ? v : lo)));

/** Everything the card shows. run = the engine's run, life = the lifetime save, name = the host's display name or ''. */
export function cardModel({ run, life = {}, name = '', adNumber = null, serialLabel = ADCARD_COPY.ad }) {
  const first = THEME_IDS.includes(run?.picks?.f5) ? run.picks.f5 : 'bimbo';
  const second = secondTheme(run);
  const tally = run?.store?.adcard || {};
  const seed = Number.isFinite(run?.seed) ? run.seed : 0;
  const ad = Number.isFinite(adNumber) && adNumber > 0 ? Math.floor(adNumber) : 1000 + (seed % 9000);
  const adText = whole(ad);
  const pics = clampInt(tally.pics, 0, 1e9), golden = clampInt(tally.golden, 0, 1e6), terms = clampInt(tally.terms, 0, 1e6);
  const total = Math.max(0, Number(run?.total) || 0);
  const llamas = clampInt(life.llamas ?? LLAMA_MIN, LLAMA_MIN, LLAMA_MAX);
  const lines = ADCARD_LINES[first];
  const [head, sub] = lines[(seed >>> 3) % lines.length];
  const vals = {
    name: String(name || '').trim().slice(0, 24) || fill(ADCARD_COPY.signee, { n: adText }),
    unit: fill(ADCARD_COPY.unit, { n: adText }),
    time: timeText(Number(run?.clock) || 0),
    terms: whole(terms),
    pics: whole(pics),
  };
  const w = barWidths({ pics, total, golden });
  return {
    first, second,
    labels: [ADCARD_COPY.themes[first], ...(second ? [ADCARD_COPY.themes[second]] : [])],
    colours: cardColours(first, second),
    serial: fill(serialLabel, { n: adText }),
    head: fillLine(head, vals),
    sub: fillLine(sub, vals),
    terms,
    stats: [
      { label: ADCARD_COPY.stats.pics, n: pics, w: w.pics },
      { label: brandTokens(ADCARD_COPY.stats.fuzz), n: Math.floor(total), w: w.fuzz },
      { label: ADCARD_COPY.stats.golden, n: golden, w: w.golden },
    ],
    llamas,
  };
}

/** The tallies the card reads, kept in the run's store (saved with the run). */
export function tally(run, key, n = 1) {
  if (!run || !Number.isFinite(n) || n <= 0) return;
  run.store ||= {};
  const t = (run.store.adcard ||= {});
  t[key] = (Number(t[key]) || 0) + n;
}
