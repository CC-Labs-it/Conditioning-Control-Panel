/* tests/options-top-close.test.mjs - desk run 2026-10-30: the Options card has a close at the top (the same act as
 * Back), shown on Options only, kept in reach while the list scrolls, never the first focus for the pad. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const cards = readFileSync(new URL('../hud/cards.js', import.meta.url), 'utf8');
const css = readFileSync(new URL('../styles.css', import.meta.url), 'utf8');

test('Options: a top close that acts as Back, on the Options card only', () => {
  assert.match(cards, /btn\('back', '<span aria-hidden="true">&times;<\/span>', 'c-close', ' aria-label="Close" title="Close"'\)/,
    'the close is a Back button with a spoken name');
  assert.match(cards, /title\.before\(topClose\)/, 'it sits above the title');
  assert.match(cards, /topClose\.hidden = k !== 'options';/, 'shown on Options only');
  assert.match(cards, /const BACKS = \['options',/, 'Back on Options returns to the opener');
  assert.match(cards, /list\.find\(\(e\) => !e\.classList\.contains\('c-close'\)\)/, 'the pad focus starts on the list, not the close');
  assert.match(css, /#card \.c-top-close \{ position: sticky;/, 'sticky while the list scrolls');
  assert.match(css, /#card \.c-top-close\[hidden\] \{ display: none; \}/, 'hidden means gone');
});
