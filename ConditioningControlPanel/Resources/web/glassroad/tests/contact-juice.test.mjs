import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { SLIP_RIBBON, newRibbon, stepRibbon, ribbonLook, dashAt } from '../render/slipMath.js';
import { hardBumpBurst, bumpCount } from '../render/kartPose.js';
import { hardBumpTrauma, TRAUMA, FX_CUES } from '../fx/cues.js';
import { sfxPlan } from '../audio/sfxmap.js';
import { LOOK } from '../render/look.js';

// interaction wave lane A: hard-bump juice and the slipstream ribbon envelope
const feel = createFeel(JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8')));
const HARD = feel.get('contact.hardBumpDvMps');
const run = (r, input, s, opts = {}) => { for (let t = 0; t < s - 1e-9; t += 1 / 60) stepRibbon(r, input, 1 / 60, opts); return r; };

test('ribbon: eases in with the draft, grows with the charge, fades out after', () => {
  const r = newRibbon();
  stepRibbon(r, { slip: 0, from: 2 }, 1 / 60);
  assert.ok(r.a > 0 && r.a < 0.1, 'an IN, never a snap ' + r.a);
  run(r, { slip: 0, from: 2 }, 0.5);
  const fresh = ribbonLook(r).width;
  run(r, { slip: 1, from: 2 }, 0.5);
  assert.ok(ribbonLook(r).width > fresh, 'charge widens the ribbon');
  run(r, { slip: 0, from: -1 }, 0.1);
  assert.ok(r.a > 0 && r.from === 2, 'fading keeps its kart ahead');
  run(r, { slip: 0, from: -1 }, 2);
  assert.equal(r.a, 0); assert.equal(r.from, -1); assert.equal(ribbonLook(r).alpha, 0);
});

test('ribbon: pops on fire (wider, whiter), then the pop decays', () => {
  const r = run(newRibbon(), { slip: 1, from: 1 }, 0.6), before = ribbonLook(r);
  stepRibbon(r, { slip: 0, from: 1, fired: true }, 1 / 60);
  const pop = ribbonLook(r);
  assert.ok(pop.width > before.width * 1.5 && pop.white > 0.8);
  run(r, { slip: 0, from: -1 }, SLIP_RIBBON.popS + 0.05);
  assert.equal(r.pop, 0);
});

test('ribbon: Gentle rises no faster than 300 ms, fades slower, pops softer and never scrolls', () => {
  const g = run(newRibbon(), { slip: 1, from: 1 }, 0.1, { gentle: true }), f = run(newRibbon(), { slip: 1, from: 1 }, 0.1);
  assert.ok(g.a < f.a, 'slower rise');
  stepRibbon(g, { slip: 0, from: 1, fired: true }, 1 / 60, { gentle: true });
  stepRibbon(f, { slip: 0, from: 1, fired: true }, 1 / 60);
  assert.ok(ribbonLook(g).white < ribbonLook(f).white, 'softer pop');
  for (const u of [0.2, 0.5, 0.8]) assert.equal(dashAt(u, 8, 0, { gentle: true }), dashAt(u, 8, 3.7, { gentle: true }));
  assert.notEqual(dashAt(0.5, 8, 0), dashAt(0.5, 8, 0.05), 'full motion streaks flow');
  assert.equal(dashAt(0, 8, 1), 0); assert.ok(dashAt(1, 8, 1) < 1e-9, 'ends tapered');
});

test('hard bump: a bigger, faster burst than the bump and a trauma pulse that grows with dv', () => {
  const lo = hardBumpBurst(HARD, HARD, LOOK), hi = hardBumpBurst(HARD * 3, HARD, LOOK);
  assert.equal(lo.k, 0); assert.equal(hi.k, 1);
  assert.ok(hi.count > lo.count && hi.speedMul > lo.speedMul);
  assert.ok(lo.count >= bumpCount(0, LOOK) && lo.speedMul > 1);
  assert.equal(hardBumpTrauma(HARD - 0.1, HARD), 0);
  assert.ok(hardBumpTrauma(HARD * 2, HARD) > hardBumpTrauma(HARD, HARD));
  assert.ok(hardBumpTrauma(HARD * 2, HARD, false) < hardBumpTrauma(HARD * 2, HARD, true), 'the winner feels less');
  assert.ok(hardBumpTrauma(100, HARD) <= TRAUMA.hardBump);
});

test('hard bump: the fx cue shocks only the player, and the bump sample plays louder', () => {
  const shocks = [], fx = { feel, shock: (a) => shocks.push(a) };
  FX_CUES['hard-bump'](fx, { winner: 2, loser: 3, dv: 10 });
  assert.equal(shocks.length, 0);
  FX_CUES['hard-bump'](fx, { winner: 2, loser: 0, dv: 10 });
  assert.equal(shocks.length, 1);
  const soft = sfxPlan('bump', { a: 0, b: 1, dv: HARD - 1 }, { feel })[0], hard = sfxPlan('bump', { a: 0, b: 1, dv: HARD }, { feel })[0];
  assert.ok(hard.vol > soft.vol * 1.2, soft.vol + ' -> ' + hard.vol);
  assert.equal(sfxPlan('boost-start', { kind: 'bump' }, { feel })[0].id, 'boost');
});
