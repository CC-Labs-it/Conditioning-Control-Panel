import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createJelly, jellyShape, JELLY_PRESS, PRESS_KICK, WALL_KICK } from '../ui/unlocks/basics/jelly.js';

/** Run a kicked spring at 60 fps and keep every displacement. */
function trace(jelly, seconds = 1.5) {
  const out = [];
  for (let i = 0; i < seconds * 60; i++) out.push(jelly.step(1 / 60));
  return out;
}

test('a wall kick squashes, overshoots back past rest at least twice, then settles to exactly 0', () => {
  const j = createJelly();
  j.kick(WALL_KICK.plain);
  const s = trace(j);
  const peak = Math.max(...s);
  assert.ok(peak > 0.1 && peak < 0.25, `plain peak ${peak}`);
  let crossings = 0;
  for (let i = 1; i < s.length; i++) if (Math.sign(s[i]) !== Math.sign(s[i - 1]) && s[i] && s[i - 1]) crossings++;
  assert.ok(crossings >= 2, `elastic: ${crossings} crossings`);
  assert.equal(s.at(-1), 0);
  assert.equal(j.moving(), false);
});

test('Wall Pop squashes deeper than a plain copy, and a pile of kicks stays inside the clamp', () => {
  const plain = createJelly(), pop = createJelly();
  plain.kick(WALL_KICK.plain); pop.kick(WALL_KICK.pop);
  assert.ok(Math.max(...trace(pop)) > Math.max(...trace(plain)) * 1.6);
  const pile = createJelly();
  for (let i = 0; i < 20; i++) { pile.kick(WALL_KICK.pop); pile.step(1 / 60); }
  for (const s of trace(pile)) assert.ok(Math.abs(s) <= 0.45 + 1e-9);
});

test('a press squeezes in and boings past rest', () => {
  const j = createJelly(JELLY_PRESS);
  j.kick(PRESS_KICK);
  const s = trace(j, 1);
  assert.ok(Math.max(...s) > 0.06);
  assert.ok(Math.min(...s) < -0.01, 'overshoots out');
  assert.equal(s.at(-1), 0);
});

test('a long frame is capped and never blows the spring up', () => {
  const j = createJelly();
  j.kick(WALL_KICK.pop);
  for (const dt of [0.5, 2, NaN, -1, 1 / 30]) assert.ok(Math.abs(j.step(dt)) <= 0.45);
});

test('the shape flattens along the hit axis and bulges across it', () => {
  assert.deepEqual(jellyShape(0), { along: 1, across: 1 });
  const { along, across } = jellyShape(0.3);
  assert.ok(along < 1 && across > 1);
});

test('Gentle and reduced motion never squash a copy', () => {
  const src = readFileSync(new URL('../ui/unlocks/basics.js', import.meta.url), 'utf8');
  assert.match(src, /if \(ctx\.gentle \|\| ctx\.reduced\) \{ hitAt = pressAt = -1; wall\.reset\(\); squeeze\.reset\(\); return ''; \}/);
});
