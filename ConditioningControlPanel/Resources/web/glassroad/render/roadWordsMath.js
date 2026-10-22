/* render/roadWordsMath.js - how the painted road words look (render/roadWords.js), pure.
 *
 * Owner: "make the text written on the track actually cool and VARY WITH THE FX; diegetic fonts and styles".
 * One authored LOOK per music mode, picked per phrase and kept for the whole phrase (Afterimage rule: authored
 * font variation between phrases, a recurring identity keeps its face):
 *   break           SILK       thin whispered italic, soft white paint, barely any contour
 *   intro / outro   ROUNDED    soft round paint, sweet and calm
 *   build           EDITORIAL  glossy serif italic, warm cream paint with a plum contour
 *   groove          STENCIL    condensed road paint, a stencil gap and worn thermoplastic, the kick pulses it
 *   looping groove  MONO       machine print, the helix glitch slides its slices on the beat
 *   drop            NEON / GLOSS   alternating by phrase: a neon tube (display face) or hot pink gloss (serif)
 *   peak            NEON       always the tube, the glow rides the energy
 * Shared by every look: thick ink with a clear core and a soft contour (the atlas carries fill, contour and
 * glow in its three channels), the IN wipe from a glow line, the karaoke light on the voice, the drop wave
 * lifting the letters as it passes, the trigger shatter cracking them into hue-shifted shards, the OUT fade.
 * Gentle: no glitch slide, no kick pulse (a slow breath instead), slower IN and light, smaller lift; the drop
 * boost only with the photosafe grant (roadWords.js asks once per drop).
 */
import { sectionOf } from '../audio/chart.js';

export const STYLE = Object.freeze({ SILK: 0, ROUNDED: 1, EDITORIAL: 2, STENCIL: 3, MONO: 4, NEON: 5, GLOSS: 6 });

// ink / edge / glow are linear rgb (over 1 = the bloom pass turns the core into light); em = font size share of
// the atlas row (faces differ in cap height); spacing = letter spacing in em; upper = the marking is capitals
export const STYLES = Object.freeze([
  look({ id: 'silk', face: 'silk', weight: 600, em: 0.98, spacing: 0.02, upper: false,
    ink: [1.05, 1.0, 1.03], inkA: 0.9, edge: [0.32, 0.08, 0.26], edgeA: 0.35, glow: [1.0, 0.62, 0.86], glowA: 0.4, stroke: 0.1 }),
  look({ id: 'rounded', face: 'rounded', weight: 900, em: 0.8, spacing: 0.03, upper: false,
    ink: [1.08, 1.02, 1.06], inkA: 0.95, edge: [0.26, 0.05, 0.22], edgeA: 0.6, glow: [1.0, 0.6, 0.86], glowA: 0.32, stroke: 0.14 }),
  look({ id: 'editorial', face: 'editorial', weight: 400, em: 0.86, spacing: 0.02, upper: false,
    ink: [1.12, 0.96, 0.78], inkA: 0.96, edge: [0.22, 0.03, 0.17], edgeA: 0.75, glow: [1.0, 0.74, 0.42], glowA: 0.36, stroke: 0.14 }),
  look({ id: 'stencil', face: 'condensed', weight: 700, em: 0.74, spacing: 0.06, upper: true,
    ink: [1.18, 1.15, 1.13], inkA: 0.96, edge: [0.09, 0.02, 0.08], edgeA: 0.8, glow: [1.0, 0.9, 0.95], glowA: 0.16, stroke: 0.16,
    stencil: 1, wear: 0.55, kick: 0.4 }),
  look({ id: 'mono', face: 'mono', weight: 700, em: 0.72, spacing: 0.0, upper: true,
    ink: [0.72, 1.02, 1.12], inkA: 0.95, edge: [0.02, 0.04, 0.12], edgeA: 0.75, glow: [0.3, 0.8, 1.0], glowA: 0.5, stroke: 0.14,
    glitch: 1, wear: 0.2, kick: 0.2 }),
  look({ id: 'neon', face: 'display', weight: 700, em: 0.8, spacing: 0.05, upper: true,
    ink: [1.6, 0.36, 1.02], inkA: 1, edge: [0.22, 0.0, 0.14], edgeA: 0.62, glow: [1.0, 0.14, 0.58], glowA: 1.1, stroke: 0.1,
    glitch: 0.35, kick: 0.45, energy: 1 }),
  look({ id: 'gloss', face: 'editorial', weight: 400, em: 0.86, spacing: 0.02, upper: false,
    ink: [1.35, 0.3, 0.78], inkA: 1, edge: [0.2, 0.0, 0.14], edgeA: 0.8, glow: [1.0, 0.3, 0.7], glowA: 0.7, stroke: 0.14,
    kick: 0.25, energy: 0.7, gloss: 1 }),
]);

function look(o) {
  return Object.freeze({ stencil: 0, wear: 0, kick: 0, glitch: 0, energy: 0, gloss: 0, ...o });
}

export const ROAD_WORD_LOOK = Object.freeze({
  inS: 0.5, gentleInS: 1.0,              // the IN wipe from a glow line (near edge to far)
  frontW: 0.08,                          // the glow line's width (share of the marking's length)
  outS: 1.1,                             // the OUT fade (sim/roadWords.js ROAD_LOOK.outS)
  sungRiseS: 0.07, gentleSungRiseS: 0.3, // the karaoke light rises on the word's start...
  sungFadeS: 0.7, gentleSungFadeS: 1.1,  // ...holds while it is sung, settles after
  sungRest: 0.22,                        // a sung word keeps this much of its light (spent, not dark)
  liftSungM: 0.1, gentleLiftSungM: 0.04,
  kickGentle: 0, breathGentle: 0.08,     // Gentle: no kick pulse, a slow breath instead
  glitchM: 0.4,                          // the helix glitch slide (metres, lateral) at full helix
  waveLiftM: 0.42, waveWM: 9,            // the drop wave lifts the letters this much, over this length of road
  dropBoost: 0.8,                        // extra glow on the drop when photosafe granted it
  energyGlow: 0.9,                       // the neon glow at full mix energy
  // mood -> look; drop alternates NEON and GLOSS by phrase; a groove that loops this much prints in MONO
  monoLoop: 0.8,
});

/** Deterministic 0..1 from an index (the drop look alternation, shard seeds). */
export function hash01(i, seed = 7) {
  let h = (Math.imul(seed | 0, 0x9e3779b1) ^ Math.imul((i | 0) + 1, 0x85ebca6b)) >>> 0;
  h = Math.imul(h ^ (h >>> 16), 0x7feb352d) >>> 0;
  h = Math.imul(h ^ (h >>> 15), 0x846ca68b) >>> 0;
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

/** The look of a phrase starting at tS (index phrase): the section's mood picks it. */
export function phraseStyle(song, tS, phrase, L = ROAD_WORD_LOOK) {
  const sec = song ? sectionOf(song, tS) : null;
  const mood = sec ? sec.mood : 'groove';
  if (mood === 'break') return STYLE.SILK;
  if (mood === 'intro' || mood === 'outro') return STYLE.ROUNDED;
  if (mood === 'build') return STYLE.EDITORIAL;
  if (mood === 'peak') return STYLE.NEON;
  if (mood === 'drop') return phrase % 2 === 0 ? STYLE.NEON : STYLE.GLOSS;
  return sec && sec.loop >= L.monoLoop ? STYLE.MONO : STYLE.STENCIL;
}

/** The marking's text as painted: capitals for the road-paint looks, as sung for the scripts. */
export function paintText(text, style) {
  const st = STYLES[style] || STYLES[STYLE.STENCIL];
  return st.upper ? String(text).toUpperCase() : String(text).toLowerCase();
}

/**
 * Shelf packing for the atlas: items [{ w }] (px, padding included) into rows of rowPx in a W wide sheet.
 * Returns { x: Float32Array, y: Float32Array, rows, heightPx } or null when the rows need more than maxH.
 */
export function packAtlas(widths, W, rowPx, maxH) {
  const n = widths.length, x = new Float32Array(n), y = new Float32Array(n);
  let cx = 0, row = 0;
  for (let i = 0; i < n; i++) {
    const w = Math.min(W, widths[i]);
    if (cx + w > W) { row++; cx = 0; }
    if ((row + 1) * rowPx > maxH) return null;
    x[i] = cx; y[i] = row * rowPx; cx += w;
  }
  return { x, y, rows: n ? row + 1 : 0, heightPx: n ? (row + 1) * rowPx : 0 };
}

const sat = (x) => (x < 0 ? 0 : x > 1 ? 1 : x);
const ease = (x) => { x = sat(x); return x * x * (3 - 2 * x); };

/** The karaoke light (0..1): rises on the word's start, holds while sung (lenS), settles to sungRest after. */
export function markSung(v, gentle = false, L = ROAD_WORD_LOOK) {
  const since = -v.remainS;
  if (!(since >= 0)) return 0;
  const rise = gentle ? L.gentleSungRiseS : L.sungRiseS, fade = gentle ? L.gentleSungFadeS : L.sungFadeS;
  const len = v.lenS > 0 ? v.lenS : 0.25;
  const up = ease(since / rise), down = since <= len ? 1 : 1 - (1 - L.sungRest) * ease((since - len) / fade);
  return up * down;
}

/** One marking this frame: { alpha, inK (wipe 0..1), sung, liftM } from its view (sim/roadWords.js). */
export function markLook(v, { gentle = false, L = ROAD_WORD_LOOK } = {}, out = {}) {
  const inS = gentle ? L.gentleInS : L.inS;
  out.inK = ease(v.inT / inS);
  out.sung = markSung(v, gentle, L);
  out.alpha = v.state === 'out' ? 1 - ease(v.outT / L.outS) : 1;
  out.liftM = out.sung * (gentle ? L.gentleLiftSungM : L.liftSungM);
  return out;
}

/**
 * The shared numbers of the frame from the music frame (render/musicRoadMath.js musicFrame): { pulse (kick or
 * breath, 0..1), glitch (metres of slide this beat, signed by the shader), glow (energy glow gain), wave and
 * waveD (the drop's lift), shatter (0..1), beat (index + phase) }. dropOk: photosafe granted this drop its boost.
 */
export function frameLook(mf, { gentle = false, dropOk = false, L = ROAD_WORD_LOOK } = {}, out = {}) {
  if (!mf || !mf.on) {
    out.pulse = 0; out.glitch = 0; out.glow = 0.5; out.wave = 0; out.waveD = 0; out.shatter = 0; out.beat = 0; out.calm = 0;
    return out;
  }
  const breath = 0.5 - 0.5 * Math.cos(mf.barPhase * Math.PI * 2);
  out.pulse = gentle ? L.breathGentle * breath : mf.kick * (1 - mf.calm * 0.7);
  // the slide snaps in on the beat and eases home over the first third of it: a move, never a luminance strobe
  out.glitch = gentle ? 0 : L.glitchM * mf.helix * Math.exp(-mf.beatPhase * 6);
  out.glow = 0.45 + L.energyGlow * mf.mix * (0.4 + 0.6 * mf.big) + (dropOk ? L.dropBoost * mf.waveAmp : 0);
  out.wave = mf.waveAmp * (gentle ? 0.5 : 1);
  out.waveD = mf.waveD;
  out.shatter = mf.shatter;
  out.beat = mf.beatIndex;
  out.calm = mf.calm;
  return out;
}
