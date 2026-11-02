// Pass 3 (loading): the boot warm splits into groups (render/warmGroups.js) and the warming card shows the
// stage and a real progress bar (hud/cards.js setProgress), animated with transform-only keyframes.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { warmGroups, WARM } from '../render/warmGroups.js';

const mat = (id) => ({ id });
const item = (...ids) => ({ material: ids.length === 1 ? mat(ids[0]) : ids.map(mat) });

test('warm groups: at most perGroup new materials each, shared materials ride free, order kept', () => {
  const items = [item(1), item(2), item(3), item(1), item(4, 5), item(6), item(2), item(7)];
  const g = warmGroups(items, 3);
  assert.deepEqual(g.map((x) => x.length), [4, 3, 1]);
  assert.equal(g.flat().length, items.length, 'nothing dropped');
  assert.deepEqual(g.flat(), items, 'scene order kept');
  for (const grp of g) {
    const fresh = new Set();
    for (const o of grp) for (const m of [].concat(o.material)) fresh.add(m.id);
    assert.ok(fresh.size <= 3 + 2, 'a group stays small');
  }
  assert.deepEqual(warmGroups([]), []);
  assert.equal(warmGroups([item(1), item(1), item(1)]).length, 1);
  assert.ok(WARM.perGroup >= 2);
});

test('warming card: a bar and a ring animated only with transforms (the compositor keeps them moving)', () => {
  const css = readFileSync(new URL('../styles.css', import.meta.url), 'utf8');
  for (const k of ['c-load-sheen', 'c-load-spin']) {
    const m = css.match(new RegExp('@keyframes ' + k + ' \\{([^@]*?)\\}\\s*\\}'));
    assert.ok(m, k + ' keyframes');
    assert.ok(/transform/.test(m[1]) && !/(width|left|background|opacity|margin)\s*:/.test(m[1]), k + ' animates transform only');
  }
  const cards = readFileSync(new URL('../hud/cards.js', import.meta.url), 'utf8');
  assert.ok(/setProgress\(stage, p\)/.test(cards), 'cards.setProgress');
  assert.ok(/load\.p = Math\.max\(load\.p/.test(cards), 'the bar never runs backwards');
  const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  for (const stage of ['Bringing the racers in', 'Setting the course', 'Getting the lights ready', 'Ready to roll']) assert.ok(boot.includes(stage), stage);
  assert.ok(!/Warming up/.test(boot), 'no bare "Warming up" left');
});
