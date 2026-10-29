import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import {
  SPECTRUM, SPECTRUM_WEIGHTS, SPECTRUM_BASE, SPECTRUM_LOOK as L, PATTERNS, SECTION_PATTERNS, RAMP_ROWS,
  sectionKey, hexRgb, rgbHex, luma, levelLuma, saturation, stops, rampRaw, rampAt, rampBytes, hueAt, propColour,
  fitPeriod, patternZones, patternAt, sweepMix, patternBytes, flowMps, dominantShare,
} from '../render/spectrum.js';

const tr = loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json', import.meta.url), 'utf8')));
const KEYS = ['dawn', 'deep', 'afterglow'];
const near = (a, b, e = 1e-6) => Math.abs(a - b) <= e;
/** HSV hue in degrees of an sRGB colour. */
function hue(c) {
  const [r, g, b] = c, M = Math.max(r, g, b), m = Math.min(r, g, b), d = M - m;
  if (d <= 0) return 0;
  const h = M === r ? ((g - b) / d) % 6 : M === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return (h * 60 + 360) % 360;
}
const isPinkish = (c) => { const h = hue(c); return h >= 255 || h < 5; };   // violet, magenta, pink (and the pink-red edge)

test('spectrum: SPECTRUM holds only hex strings, three sections, one weight per hue, weights sum to 1', () => {
  assert.deepEqual(Object.keys(SPECTRUM), KEYS);
  assert.deepEqual(RAMP_ROWS, KEYS);
  for (const k of KEYS) {
    assert.ok(SPECTRUM[k].length >= 10, k);
    for (const h of SPECTRUM[k]) assert.match(h, /^#[0-9a-f]{6}$/);
    assert.equal(SPECTRUM_WEIGHTS[k].length, SPECTRUM[k].length);
    assert.ok(near(SPECTRUM_WEIGHTS[k].reduce((a, b) => a + b, 0), 1, 1e-9), k + ' weights');
    assert.equal(SPECTRUM_BASE[k].length, 3);
  }
});

test('spectrum: pink, magenta and violet dominate at about 40 percent, the rest spans the wheel at high saturation', () => {
  const bins = { cyan: [175, 200], blue: [215, 245], teal: [155, 175], lime: [80, 110], gold: [40, 55], orange: [15, 35], red: [345, 368] };
  for (const k of KEYS) {
    const share = dominantShare(k);
    assert.ok(share >= 0.35 && share <= 0.45, k + ' dominant ' + share);
    for (const [i, h] of SPECTRUM[k].entries()) {
      const c = hexRgb(h);
      assert.ok(saturation(c) >= 0.8, k + ' ' + h + ' saturation ' + saturation(c));
      if (i < 3) assert.ok(isPinkish(c), k + ' dominant hue ' + h + ' at ' + hue(c));
    }
    const hues = SPECTRUM[k].map((h) => hue(hexRgb(h)));
    for (const [name, [a, b]] of Object.entries(bins)) assert.ok(hues.some((x) => (x >= a && x <= b) || (x + 360 >= a && x + 360 <= b)), k + ' has ' + name);
  }
});

test('spectrum: the ramp shows the dominant hues about 40 percent of its length', () => {
  for (const k of KEYS) {
    let pink = 0;
    const N = 2000;
    for (let i = 0; i < N; i++) if (isPinkish(rampRaw(k, (i + 0.5) / N))) pink++;
    assert.ok(pink / N > 0.3 && pink / N < 0.55, k + ' ramp pink share ' + pink / N);
  }
});

test('spectrum: sectionKey maps palette ids, section ids, acts, indices and objects', () => {
  assert.equal(sectionKey('deep'), 'deep');
  assert.equal(sectionKey('induction'), 'dawn');
  assert.equal(sectionKey('release'), 'afterglow');
  assert.equal(sectionKey(4), 'deep');
  assert.equal(sectionKey(-1), 'afterglow');
  assert.equal(sectionKey({ palette: 'afterglow' }), 'afterglow');
  assert.equal(sectionKey('nope'), 'dawn');
  assert.equal(rgbHex(hexRgb('#12ab9f')), '#12ab9f');
});

test('spectrum: levelLuma only pulls bright hues down and keeps their hue and saturation', () => {
  const dark = hexRgb('#2a5cff'), bright = hexRgb('#ffcc0a');
  assert.deepEqual(levelLuma(dark), dark, 'a dark hue is never lifted (that washes it out)');
  const out = levelLuma(bright);
  assert.ok(luma(out) < luma(bright));
  assert.ok(luma(out) >= L.levelTarget - 1e-9, 'never below the target');
  assert.ok(Math.abs(hue(out) - hue(bright)) < 2, 'hue holds');
  assert.ok(Math.abs(saturation(out) - saturation(bright)) < 0.03, 'saturation holds');
});

test('spectrum: the ramp wraps without a seam and its luminance changes gently along x', () => {
  for (const k of KEYS) {
    const a = rampAt(k, 0.9999), b = rampAt(k, 0.0001);
    assert.ok(Math.max(...a.map((v, i) => Math.abs(v - b[i]))) < 0.02, k + ' wrap');
    let maxStep = 0, lo = 1, hi = 0;
    const N = 512;
    for (let i = 0; i < N; i++) {
      const l0 = luma(rampAt(k, i / N)), l1 = luma(rampAt(k, (i + 1) / N));
      maxStep = Math.max(maxStep, Math.abs(l1 - l0)); lo = Math.min(lo, l0); hi = Math.max(hi, l0);
    }
    assert.ok(maxStep < 0.04, k + ' luminance step ' + maxStep);
    assert.ok(hi - lo < 0.3, k + ' luminance range ' + (hi - lo));
    // the hue clock: a full turn takes hueCycleS, so luminance at a fixed point only drifts (a slow
    // ramp, never a swing faster than the 3 Hz flicker band)
    const perS = maxStep * N / L.hueCycleS;
    assert.ok(perS < 0.3, k + ' luminance drift per second ' + perS);
  }
});

test('spectrum: rampBytes is one opaque row per section, matching rampAt', () => {
  const n = 64, b = rampBytes(n);
  assert.equal(b.length, n * 3 * 4);
  for (let row = 0; row < 3; row++) {
    const c = rampAt(KEYS[row], 10.5 / n), o = (row * n + 10) * 4;
    for (let j = 0; j < 3; j++) assert.ok(Math.abs(b[o + j] - c[j] * 255) <= 1);
    assert.equal(b[o + 3], 255);
  }
});

test('spectrum: hueAt is periodic in s (rampM) and in time (hueCycleS)', () => {
  const a = hueAt(40, 3, 'deep');
  assert.equal(a.length, 3);
  for (const v of a) assert.ok(v >= 0 && v <= 1);
  const b = hueAt(40 + L.rampM, 3, 'deep'), c = hueAt(40, 3 + L.hueCycleS, 'deep');
  for (let i = 0; i < 3; i++) { assert.ok(near(a[i], b[i], 1e-6)); assert.ok(near(a[i], c[i], 1e-6)); }
  assert.notDeepEqual(hueAt(40, 3, 'deep'), hueAt(40 + L.rampM / 2, 3, 'deep'));
});

test('spectrum: propColour picks hues from the section in proportion to their weights', () => {
  for (const k of KEYS) {
    const count = new Map(), N = 4000;
    for (let i = 0; i < N; i++) { const h = propColour(i, k); count.set(h, (count.get(h) || 0) + 1); }
    for (const [i, h] of SPECTRUM[k].entries()) assert.ok(Math.abs((count.get(h) || 0) / N - SPECTRUM_WEIGHTS[k][i]) < 0.02, k + ' ' + h);
    assert.equal(propColour(7, k), propColour(7, k), 'deterministic');
  }
  assert.ok(new Set([0, 1, 2, 3, 4, 5].map((i) => propColour(i, 'dawn'))).size >= 4, 'neighbouring props differ');
});

test('spectrum: stops cover 0..1 in order', () => {
  for (const k of KEYS) {
    const st = stops(k);
    assert.equal(st[0][0], 0); assert.equal(st[st.length - 1][1], 1);
    for (let i = 1; i < st.length; i++) assert.ok(near(st[i][0], st[i - 1][1]));
  }
});

test('patterns: fitPeriod divides the lap exactly and stays near the base', () => {
  for (const base of [L.rampM, L.packetM, L.chevronM, L.checkerM, L.ringCellM, L.weaveM, 30, 54, 12]) {
    const p = fitPeriod(tr.lengthM, base), n = tr.lengthM / p;
    assert.ok(near(n, Math.round(n), 1e-9), base + ' divides');
    assert.ok(Math.abs(p - base) / base < 0.1, base + ' near: ' + p);
  }
  assert.equal(fitPeriod(0, 5), 5);
});

test('patterns: zones cover the lap in order, each section runs its own list, lanes carry a large share', () => {
  const z = patternZones(tr.sections);
  assert.equal(z.length, tr.sections.reduce((a, s) => a + SECTION_PATTERNS[sectionKey(s)].length, 0));
  assert.equal(z[0].fromS, 0); assert.ok(near(z[z.length - 1].toS, tr.lengthM, 1e-6));
  for (let i = 1; i < z.length; i++) assert.ok(near(z[i].fromS, z[i - 1].toS, 1e-6));
  for (const zone of z) assert.ok(zone.pattern >= 0 && zone.pattern < PATTERNS.length);
  const lanes = z.filter((q) => PATTERNS[q.pattern] === 'lanes').reduce((a, q) => a + q.toS - q.fromS, 0);
  assert.ok(lanes / tr.lengthM > 0.3, 'lanes share ' + lanes / tr.lengthM);
  assert.equal(new Set(z.map((q) => q.pattern)).size, PATTERNS.length, 'every pattern shows up');
});

test('patterns: patternAt holds one pattern mid-zone and names both sides near a boundary, across the line too', () => {
  const z = patternZones(tr.sections), Lm = tr.lengthM;
  const mid = (z[1].fromS + z[1].toS) / 2;
  assert.deepEqual(patternAt(mid, z, Lm), { a: z[1].pattern, b: z[1].pattern, edgeS: mid });
  const before = patternAt(z[1].toS - 5, z, Lm), after = patternAt(z[1].toS + 5, z, Lm);
  assert.deepEqual([before.a, before.b], [z[1].pattern, z[2].pattern]);
  assert.deepEqual([after.a, after.b], [z[1].pattern, z[2].pattern]);
  assert.ok(near(before.edgeS, z[1].toS, 1e-6) && near(after.edgeS, z[1].toS, 1e-6));
  const end = patternAt(Lm - 3, z, Lm), start = patternAt(3, z, Lm);
  assert.deepEqual([end.a, end.b], [z[z.length - 1].pattern, z[0].pattern]);
  assert.deepEqual([start.a, start.b], [z[z.length - 1].pattern, z[0].pattern]);
  assert.ok(near(end.edgeS, Lm, 1e-6) && near(start.edgeS, 0, 1e-6));
});

test('patterns: the sweep runs from a to b across the morph window, the road edges leading', () => {
  const edge = 300;
  assert.equal(sweepMix(edge - L.morphM, 0, edge), 0);
  assert.equal(sweepMix(edge + L.morphM, 0, edge), 1);
  assert.ok(near(sweepMix(edge, 0, edge), 0.5));
  assert.ok(sweepMix(edge - 4, 6, edge) > sweepMix(edge - 4, 0, edge), 'edges lead');
  assert.equal(sweepMix(edge - 4, 6, edge), sweepMix(edge - 4, -6, edge), 'symmetric');
  // the whole sweep fits inside the morph window for a 16 m half width
  assert.equal(sweepMix(edge - L.morphM + 0.01, 8, edge) < 0.02, true);
  assert.ok(L.sweepSoftM + 8 * L.sweepSlope < L.morphM);
});

test('patterns: patternBytes decodes back to patternAt within half a metre', () => {
  const z = patternZones(tr.sections), Lm = tr.lengthM, b = patternBytes(z, Lm), n = Math.min(8192, Math.ceil(Lm)), step = Lm / n;
  assert.equal(b.length, n * 4);
  for (let i = 0; i < n; i += 3) {
    const c = (i + 0.5) * step, p = patternAt(c, z, Lm);
    assert.equal(Math.round(b[i * 4] / 40), p.a); assert.equal(Math.round(b[i * 4 + 1] / 40), p.b);
    const edgeS = c + (b[i * 4 + 2] - 128) * 0.5;
    if (p.a !== p.b) assert.ok(Math.abs(edgeS - p.edgeS) <= 0.26, 'texel ' + i);
  }
});

test('flow: faster with kart speed, clamped, Gentle halves it', () => {
  assert.equal(flowMps(0), L.flowBaseMps);
  assert.ok(flowMps(40) > flowMps(20));
  assert.equal(flowMps(1e6), flowMps(L.flowMaxMps));
  assert.ok(near(flowMps(30, 0.5), flowMps(30) / 2));
  assert.equal(flowMps(-5), L.flowBaseMps);
});

test('spectrum: other lanes can flatten SPECTRUM into hexes (render/atlas.js spectrumHexes)', async () => {
  const { spectrumHexes } = await import('../render/atlas.js');
  const hexes = spectrumHexes(SPECTRUM);
  assert.ok(hexes.length >= 20);
  for (const h of hexes) assert.ok(Object.values(SPECTRUM).some((row) => row.includes(h)));
});

test('spectrum: road blocks streaming past at top speed never flash (photosafe, review finding)', async () => {
  const { roadRampAt, roadLevel, flowMps } = await import('../render/spectrum.js');
  const feel = JSON.parse((await import('node:fs')).readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
  const v = feel.speed.topMps * feel.speed.maxPct, rel = v + flowMps(v, 1);
  // hue step between neighbouring blocks (ramp turns) and block length (m) of every stepped pattern
  const blocks = [
    ['chevrons', L.chevronHueSteps * L.chevronM / L.rampM, L.chevronM],
    ['checker', 0.5, L.checkerM], ['checker row', 0.5 + L.checkerM / L.rampM, L.checkerM],
    ['weave', 0.33, L.weaveM],
  ];
  for (const k of KEYS) {
    let lo = 1, hi = 0;
    for (let i = 0; i < 512; i++) { const l = luma(roadRampAt(k, i / 512)); lo = Math.min(lo, l); hi = Math.max(hi, l); }
    assert.ok(hi / lo <= (1 + L.roadLevelBand) / (1 - L.roadLevelBand) + 1e-6, k + ' road band ' + lo.toFixed(3) + '..' + hi.toFixed(3));
    for (const [name, step, lenM] of blocks) {
      let worstD = 0, worstR = 1;
      for (let i = 0; i < 256; i++) {
        const a = luma(roadRampAt(k, i / 256)), b = luma(roadRampAt(k, i / 256 + step));
        worstD = Math.max(worstD, Math.abs(a - b)); worstR = Math.max(worstR, Math.max(a, b) / Math.min(a, b));
      }
      const hz = rel / lenM;
      assert.ok(worstD < L.roadFlashDelta || hz <= L.flickerHz, `${k} ${name}: step ${worstD.toFixed(3)} (x${worstR.toFixed(2)}) at ${hz.toFixed(1)} Hz`);
      assert.ok(worstR <= 1.25, `${k} ${name} ratio x${worstR.toFixed(2)}`);
    }
  }
  // the band holds for any colour, bright or dark
  for (const h of ['#ffff00', '#0000ff', '#ffffff', '#000000', '#00ffff']) {
    const l = luma(roadLevel(hexRgb(h)));
    assert.ok(l >= L.roadLevelTarget * (1 - L.roadLevelBand) - 1e-3 && l <= L.roadLevelTarget * (1 + L.roadLevelBand) + 1e-3, h + ' ' + l);
  }
});
