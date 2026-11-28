/* The recap on a narrow screen (ui/recap/recap.css): its scroll area starts below the game's top buttons, so the
 * free edition's upsell heading never scrolls up under the lock, sound and Wake Up buttons (390x844 still, wave 1). */
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const css = readFileSync(new URL('../ui/recap/recap.css', import.meta.url), 'utf8').replace(/\r/g, '');
const TOP = 'max(56px, env(safe-area-inset-top))';

test('the recap backdrop keeps the same top gap at rest, as padding on wide screens', () => {
  const base = css.match(/\.backdrop\.rr-backdrop \{[^}]*\}/)[0];
  assert.ok(base.includes('padding: ' + TOP + ' '), 'the gap the top buttons sit in');
});

test('on narrow screens the gap is a border in the backdrop colour, so scrolled text leaves below the buttons', () => {
  const narrow = [...css.matchAll(/@media \(max-width: 880px\) \{([\s\S]*?)\n\}/g)].map((m) => m[1]).join('\n');
  const rule = narrow.match(/\.backdrop\.rr-backdrop \{[^}]*\}/);
  assert.ok(rule, 'a narrow-screen rule for the recap backdrop');
  assert.ok(rule[0].includes('padding-top: 0;'));
  assert.ok(rule[0].includes('border-top: ' + TOP + ' solid #0c1114;'), 'same height as the padding it replaces, same colour as the backdrop');
  assert.ok(css.includes('background-color: #0c1114;'));
});
