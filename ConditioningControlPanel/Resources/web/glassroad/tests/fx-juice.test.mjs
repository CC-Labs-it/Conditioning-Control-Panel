import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';
import { createState } from '../sim/state.js';
import { createFx } from '../fx/reactions.js';
import { createPhotosafe } from '../fx/photosafe.js';
import { createGentle } from '../fx/gentle.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
function rig({ gentle = false } = {}) {
  const feel = createFeel(feelJson), bus = createBus();
  const g = createGentle(feel, { search: '' });
  if (gentle) g.set({ on: true });
  const fx = createFx(bus, feel, { gentle: g, photosafe: createPhotosafe(feel) });
  const kart = createState();
  const run = (s, dt = 1 / 60) => { for (let t = 0; t < s - 1e-9; t += dt) fx.update(dt, kart); };
  return { feel, bus, fx, run };
}
const shaken = (fx) => Math.hypot(...fx.state.shake) > 0;

test('juice: an item hit on the player shakes and rumbles; on a bot or under Gentle it does not', () => {
  const r = rig();
  r.bus.emit('item-hit', { victim: 3, by: 2, kind: 'spiral', verdict: 'hit' }); r.run(0.05);
  assert.ok(!shaken(r.fx) && r.fx.state.rumble === 0);
  // item feel: the player's own hit landing on a bot rumbles the pad as a confirm, but never shakes
  r.bus.emit('item-hit', { victim: 3, by: 0, kind: 'spiral', verdict: 'hit' }); r.run(0.05);
  assert.ok(!shaken(r.fx) && r.fx.state.rumble > 0);
  r.run(1);
  r.bus.emit('item-hit', { victim: 0, by: 2, kind: 'spiral', verdict: 'hit' }); r.run(0.05);
  assert.ok(shaken(r.fx) && r.fx.state.rumble > 0);
  const g = rig({ gentle: true });
  g.bus.emit('item-hit', { victim: 0, by: 2, kind: 'spiral', verdict: 'hit' }); g.run(0.05);
  assert.ok(!shaken(g.fx) && g.fx.state.rumble === 0);
});

test('juice: bumps shake only the player and only past the cue speed', () => {
  const r = rig(), min = r.feel.get('contact.cueMinDvMps');
  r.bus.emit('bump', { a: 2, b: 3, dv: 10 }); r.run(0.05);
  assert.ok(!shaken(r.fx));
  r.bus.emit('bump', { a: 0, b: 3, dv: min * 0.5 }); r.run(0.05);
  assert.ok(!shaken(r.fx) && r.fx.state.rumble === 0);
  r.bus.emit('bump', { a: 3, b: 0, dv: min * 3 }); r.run(0.05);
  assert.ok(shaken(r.fx) && r.fx.state.rumble > 0);
});

test('juice: a pickup bubble boost kicks the fov less than a pad and never shakes; Surge shakes', () => {
  const pad = rig(), bub = rig(), surge = rig();
  pad.bus.emit('boost-start', { kind: 'pad', tier: 0, amount: 0.2 });
  bub.bus.emit('boost-start', { kind: 'bubble', tier: 0, amount: 0.05 });
  surge.bus.emit('boost-start', { kind: 'surge', tier: 0, amount: 0.16 });
  for (const x of [pad, bub, surge]) x.run(0.3);
  assert.ok(bub.fx.state.fovKick > 0 && bub.fx.state.fovKick < pad.fx.state.fovKick);
  assert.ok(!shaken(bub.fx) && shaken(surge.fx));
});

test('juice: slipstream charge draws a half wake of speed lines, released on out', () => {
  const r = rig();
  r.bus.emit('slipstream', { racer: 4, state: 'in' }); r.run(0.5);
  assert.equal(r.fx.state.slipWake, 0);
  r.bus.emit('slipstream', { racer: 0, state: 'in' }); r.run(0.5);
  assert.ok(r.fx.state.slipWake > 0.99 && Math.abs(r.fx.state.speedLines - 0.5) < 0.01);
  r.bus.emit('slipstream', { racer: 0, state: 'out' }); r.run(1);
  assert.equal(r.fx.state.slipWake, 0);
});

test('juice: a pickup bubble brightens the bands once per photosafe grant', () => {
  const r = rig();
  r.bus.emit('bubble-pop', { racer: 0, i: 1 }); r.run(0.05);
  assert.ok(r.fx.state.bandBright > 0);
  const b = rig();
  b.bus.emit('bubble-pop', { racer: 2, i: 1 }); b.run(0.05);
  assert.equal(b.fx.state.bandBright, 0);
});
