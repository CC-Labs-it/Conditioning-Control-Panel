/* The colours: every theme and every mix stays readable, the mix really is two colours, and a reload lands on the same ones. */
import test from 'node:test';
import assert from 'node:assert/strict';
import { paletteOf, paletteCss, mixHex, secondTheme, THEME_IDS, THEME_PAGES, THEME_PALETTES } from '../ui/palette.js';

const rgb = (h) => [1, 3, 5].map((i) => parseInt(h.slice(i, i + 2), 16));
const toLin = (v) => { const c = v / 255; return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4; };
const fromLin = (c) => Math.round(255 * Math.min(1, Math.max(0, c <= 0.0031308 ? c * 12.92 : 1.055 * c ** (1 / 2.4) - 0.055)));
const lum = (h) => { const [r, g, b] = rgb(h).map(toLin); return 0.2126 * r + 0.7152 * g + 0.0722 * b; };
const contrast = (x, y) => { const [a, b] = [lum(x), lum(y)].sort((p, q) => q - p); return (a + 0.05) / (b + 0.05); };

/* What the css --mix does: color-mix(in oklch, a, b), halfway, the short way round the hue wheel. */
function oklch(h) {
  const [r, g, b] = rgb(h).map(toLin);
  const [l, m, s] = [0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b, 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b, 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b].map(Math.cbrt);
  const L = 0.2104542553 * l + 0.793617785 * m - 0.0040720468 * s, A = 1.9779984951 * l - 2.428592205 * m + 0.4505937099 * s, B = 0.0259040371 * l + 0.7827717662 * m - 0.808675766 * s;
  return [L, Math.hypot(A, B), Math.atan2(B, A)];
}
function oklchMix(x, y) {
  const [l1, c1, h1] = oklch(x), [l2, c2, h2] = oklch(y);
  let d = h2 - h1;
  if (d > Math.PI) d -= 2 * Math.PI;
  if (d < -Math.PI) d += 2 * Math.PI;
  const L = (l1 + l2) / 2, C = (c1 + c2) / 2, H = h1 + d / 2, A = C * Math.cos(H), B = C * Math.sin(H);
  const [l, m, s] = [L + 0.3963377774 * A + 0.2158037573 * B, L - 0.1055613458 * A - 0.0638541728 * B, L - 0.0894841775 * A - 1.291485548 * B].map((v) => v ** 3);
  const out = [4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s, -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s, -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s].map(fromLin);
  return `#${out.map((v) => v.toString(16).padStart(2, '0')).join('')}`;
}

const run = (first, second) => ({ picks: first ? { f5: first } : {}, owned: {}, store: second ? { taste2: { theme: second } } : {} });
const PAIRS = THEME_IDS.flatMap((a) => THEME_IDS.filter((b) => b !== a).map((b) => [a, b]));
const ALL = [...PAIRS, ...THEME_IDS.map((id) => [id, null])];

test('before a pick: grey, then pink with the Colour row, and no themes', () => {
  assert.equal(paletteOf(run()).a, '#8a867d');
  assert.deepEqual([...paletteOf(run()).themes], []);
  assert.equal(paletteOf({ picks: {}, owned: { colour: 1 }, store: {} }).a, '#ff69b4');
  assert.equal(paletteOf({ picks: {}, owned: { colour: 1 }, store: { paint: { name: 'teal' } } }).a, '#2cc4b4');
});

test('one theme: a and b agree, and its spiral is its own', () => {
  for (const id of THEME_IDS) {
    const p = paletteOf(run(id));
    assert.equal(p.a, THEME_PALETTES[id].a);
    assert.equal(p.b, p.a);
    assert.deepEqual([...p.themes], [id]);
    assert.deepEqual([...p.spiral], [...THEME_PALETTES[id].spiral]);
  }
});

test('two themes: both colours are in it, in both spirals, from the save alone', () => {
  for (const [a, b] of PAIRS) {
    const p = paletteOf(JSON.parse(JSON.stringify(run(a, b))));
    assert.equal(p.a, THEME_PALETTES[a].a);
    assert.equal(p.b, THEME_PALETTES[b].a);
    assert.deepEqual([...p.themes], [a, b]);
    assert.deepEqual([...p.spiral], [THEME_PALETTES[a].spiral[0], THEME_PALETTES[b].spiral[0]]);
  }
});

test('a second theme equal to the first, or unknown, is no second theme', () => {
  assert.equal(secondTheme(run('bimbo', 'bimbo')), null);
  assert.equal(secondTheme(run('bimbo', 'nope')), null);
  assert.deepEqual([...paletteOf(run('bimbo', 'nope')).themes], ['bimbo']);
});

test('the eight hues are told apart', () => {
  const hues = THEME_IDS.map((id) => THEME_PALETTES[id].a);
  assert.equal(new Set(hues).size, 8);
  for (const x of hues) for (const y of hues) {
    if (x === y) continue;
    const [p, q] = [rgb(x), rgb(y)];
    const d = p.reduce((n, v, i) => n + Math.abs(v - q[i]), 0);
    assert.ok(d >= 60, `${x} and ${y} are too close`);
  }
});

test('ink reads on a, b, the mix and soft, for every pair', () => {
  for (const [a, b] of ALL) {
    const p = paletteOf(run(a, b));
    for (const [name, bg] of [['a', p.a], ['b', p.b], ['mix', oklchMix(p.a, p.b)], ['soft', p.soft]]) {
      const c = contrast(p.ink, bg);
      assert.ok(c >= 4.5, `${a}+${b}: ink on ${name} ${bg} is ${c.toFixed(2)}`);
    }
  }
});

/* The css: accent text is the colour at 45% over the page's ink; tiles are 22% hue over the panel, under the page's ink. */
test('accent text and tiles read on every page, for every pair', () => {
  for (const [a, b] of ALL) {
    const p = paletteOf(run(a, b));
    const P = THEME_PAGES[a];
    for (const colour of [oklchMix(p.a, p.b), p.a, p.b]) {   // the mix, and the two ends of the mixed counter
      const text = mixHex(P['--ink'], colour, 0.45);
      const onPage = contrast(text, P['--page']), onPanel = contrast(text, P['--panel']);
      assert.ok(onPage >= 3, `${a}+${b}: accent text ${text} on page is ${onPage.toFixed(2)}`);
      assert.ok(onPanel >= 3, `${a}+${b}: accent text ${text} on panel is ${onPanel.toFixed(2)}`);
      const onTile = contrast(P['--ink'], mixHex(P['--panel'], colour, 0.22));
      assert.ok(onTile >= 4.5, `${a}+${b}: ink on a tile is ${onTile.toFixed(2)}`);
    }
  }
});

test('the css holds the accents only from the pick on', () => {
  assert.ok(!paletteCss(paletteOf(run())).includes('--line'));
  const css = paletteCss(paletteOf(run('goon')));
  assert.ok(/--line:[^;]*!important/.test(css));
  assert.ok(css.includes('.hue-goon{--hue-a:#f2a33c;'));
  assert.ok(!/--page/.test(css), 'the page stays the player\'s');
});

test('theme pages read: ink on page and panel', () => {
  for (const id of THEME_IDS) {
    const P = THEME_PAGES[id];
    assert.ok(contrast(P['--ink'], P['--page']) >= 4.5, `${id} ink on page`);
    assert.ok(contrast(P['--ink'], P['--panel']) >= 4.5, `${id} ink on panel`);
    assert.ok(contrast(P['--ink-soft'], P['--panel']) >= 4.5, `${id} soft ink on panel`);
  }
});
