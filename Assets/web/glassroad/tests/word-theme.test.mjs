import test from 'node:test';
import assert from 'node:assert/strict';
import { WORD_THEMES, wordTheme, wordFace, wordAccent } from '../render/wordTheme.js';
import { FACES } from '../render/fonts.js';
import { createWordRush, RUSH } from '../render/wordRush.js';

function fakeDoc() {
  const el = () => ({ style: {}, children: [], attrs: {},
    appendChild(c) { this.children.push(c); c.parent = this; },
    setAttribute(k, v) { this.attrs[k] = v; },
    remove() { this.parent?.children.splice(this.parent.children.indexOf(this), 1); } });
  return { createElement: el, body: el(), head: el() };
}

test('all five course identities have bundled faces and distinct palettes and lettering finishes', () => {
  const profiles = Object.values(WORD_THEMES);
  assert.equal(profiles.length, 5);
  assert.equal(new Set(profiles.map((p) => p.motif)).size, 5);
  assert.equal(new Set(profiles.map((p) => p.fill.map((x) => x[1]).join())).size, 5);
  for (const theme of profiles) for (const row of [0, 3, 5]) {
    const f = wordFace(theme, row);
    assert.ok(FACES[f.face], `${theme.id}/${row} uses a loaded font`);
    assert.ok(FACES[f.face].css(80, f.weight).includes('gr-'));
  }
  assert.equal(wordTheme({ id: 'course', theme: 'mainframe' }).id, 'mainframe');
  assert.equal(wordTheme(null, { theme: 'keep' }).id, 'keep');
  assert.equal(wordTheme('unknown').id, 'threshold');
});

test('Mainframe accents stay green even during a teal section; dark primaries retain legible colours', () => {
  const accent = wordAccent(WORD_THEMES.mainframe, { primaryHex: () => '#2aa7ac' });
  const [r, g, b] = accent.slice(1).match(/../g).map((x) => parseInt(x, 16));
  assert.ok(g > r && g > b * 1.7);
  assert.equal(wordAccent(WORD_THEMES.keep, { primaryHex: () => '#100e1c' }), WORD_THEMES.keep.accent);
  assert.equal(wordAccent(WORD_THEMES.goonworks, { primaryHex: () => '#f8ad3f' }), '#f8ad3f');
  assert.equal(wordAccent(WORD_THEMES.threshold, { primaryHex: () => 'garbage' }), WORD_THEMES.threshold.accent);
});

for (const theme of Object.values(WORD_THEMES)) {
  test(`${theme.id}: chart text, colour, font, bounded rush and pause remain consistent`, () => {
    const doc = fakeDoc(), rush = createWordRush({ doc, track: theme.id });
    rush.show({ w: 'Focus', row: 0 });
    const layer = doc.body.children[0], box = layer.children[0], under = box.children[0], word = box.children.at(-1);
    assert.equal(layer.attrs['data-theme'], theme.id);
    assert.equal(rush.text, theme.id === 'mainframe' ? 'FOCUS' : 'Focus');
    assert.ok(word.style.font.includes(FACES[wordFace(theme, 0).face].family));
    assert.ok(under.style.textShadow.includes(theme.ink));
    assert.ok(doc.head.children[0].textContent.includes(theme.fill[2][1]));
    assert.ok(!doc.head.children[0].textContent.includes('mix-blend-mode:'));
    rush.update(0.1);
    const held = box.style.transform;
    rush.update(0.1, true);
    assert.equal(box.style.transform, held);
    rush.show({ w: 'a phrase with spaces', row: 5 });
    assert.equal(rush.text.toLowerCase(), 'a phrase with spaces');
    for (let i = 0; i < 40; i++) rush.update(0.1);
    assert.equal(rush.active, false);
    rush.dispose();
    assert.equal(doc.body.children.length, 0);
    assert.equal(doc.head.children.length, 0);
  });
}

test('Gentle Mainframe rush has the existing slow rise and no split, lean or shake', () => {
  const doc = fakeDoc(), rush = createWordRush({ doc, track: 'mainframe', feel: { get: () => true } });
  rush.show({ w: 'Focus', row: 5 });
  rush.update(0.1);
  const box = doc.body.children[0].children[0];
  assert.ok(Number(box.style.opacity) < 0.5);
  assert.match(box.style.transform, /rotate\(0\.00deg\)/);
  assert.equal(box.children[1].style.opacity, '0.000');
  assert.equal(box.children[2].style.opacity, '0.000');
  assert.ok(RUSH.gentleRiseMs >= 300);
  rush.dispose();
});
