/* Phone hard caps, part two (lane S5b, 2026-11-22): wall counts stay whole when one drawn button stands for a fraction more,
 * and picture frames and ad strips draw one at a time on a small phone run, the drawn one wearing the count. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as Ed from '../ui/edition.js';
import * as E from '../sim/economy.js';
import { PHONE_CAPS } from '../data/editions.js';
import { capPlan, seatPool } from '../ui/phone-caps.js';
import { createLife } from '../sim/save.js';
import { createEngine } from '../ui/engine.js';

const small = Ed.readEdition({ caps: { edition: 'small', platform: 'web' } }, { hostname: 'tc.cclabs.app', hosted: true });
const ui = () => ({ doc: {}, buttonFor: () => ({}), layer: () => ({}) });
const strays = (n) => { const s = E.createRun({ ed: small }); E.grant(s, 'stray'); s.owned.stray = n; E.recalc(s); return s; };
const engineOf = (run) => createEngine({ run, life: createLife(), ui: ui(), registry: {}, prestigeRegistry: {} });

test('5 owned on a capped phone: wall counts stay whole and within one of the uncapped count', () => {
  const free = strays(5), capped = strays(5);
  const a = engineOf(free), b = engineOf(capped);
  const plan = capPlan(5, PHONE_CAPS.bouncingButtons);
  assert.equal(plan.times, 1.25);
  for (let k = 1; k <= 23; k++) {
    for (let i = 0; i < 5; i++) a.wallHit({ x: 0, y: 0 });
    for (let i = 0; i < plan.drawn; i++) b.wallHit({ x: 0, y: 0, times: plan.times });
    assert.ok(Number.isInteger(capped.walls), `round ${k}: ${capped.walls} is whole`);
    assert.ok(Math.abs(free.walls - capped.walls) <= 1, `round ${k}: ${free.walls} vs ${capped.walls}`);
  }
  assert.ok(Math.abs(free.total - capped.total) < 1e-6 * free.total, `money unchanged: ${free.total} vs ${capped.total}`);
});

test('one picture frame draws of three, wears the count, and the next takes over when it leaves', () => {
  const pool = seatPool('test-frames', 1);
  const drawn = [];
  const join = (name) => pool.join({ draw: () => { const el = { name, dataset: {} }; drawn.push(name); return el; } });
  const one = join('v_frame'), two = join('v_frame2'), three = join('v_frame3');
  assert.deepEqual(drawn, ['v_frame'], 'only the first builds anything');
  assert.equal(one.el.dataset.standsFor, '×3');
  assert.equal(one.times, 3, 'the drawn one stands for all three');
  assert.equal(two.times, 0); assert.equal(two.anchor(), one.el, 'a waiting row pays at the drawn one');
  one.leave();
  assert.deepEqual(drawn, ['v_frame', 'v_frame2'], 'switched off: the next waiting row draws');
  assert.equal(two.el.dataset.standsFor, '×2'); assert.equal(two.times, 2);
  three.leave();
  assert.equal(two.el.dataset.standsFor, undefined, 'alone, no badge');
  assert.equal(two.times, 1);
  two.leave();
  assert.equal(pool.size, 0);
});

test('uncapped: every row draws its own, no badge', () => {
  const pool = seatPool('test-free', null);
  const seats = [1, 2, 3].map(() => pool.join({ draw: () => ({ dataset: {} }) }));
  for (const s of seats) { assert.equal(s.drawn, true); assert.equal(s.el.dataset.standsFor, undefined); assert.equal(s.times, 1); }
  for (const s of seats) s.leave();
});

test('the frames and the ad strips go through the pool, under their PHONE_CAPS names', () => {
  const src = (p) => readFileSync(new URL(p, import.meta.url), 'utf8');
  assert.match(src('../ui/unlocks/routes/slides.js'), /seatPool\('pictureFrames', caps\.pictureFrames\)/);
  assert.match(src('../ui/unlocks/basics/trunk.js'), /seatPool\('adBanners', caps\.adBanners\)/);
  assert.match(src('../ui/unlocks/routes/sell.js'), /seatPool\('adBanners', caps\.adBanners\)/);
  assert.equal(PHONE_CAPS.pictureFrames, 1); assert.equal(PHONE_CAPS.adBanners, 1);
});
