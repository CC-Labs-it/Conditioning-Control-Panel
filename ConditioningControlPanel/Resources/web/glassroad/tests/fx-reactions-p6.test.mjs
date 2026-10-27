// Polish 6 (JUICE): bumps felt by impact (fx/cues.js bumpFeel); feel wave: a hard bump adds trauma (the side shake retired)
// and the self item use answering from the shipped sound bank (audio/sfxmap.js).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createFeel } from '../core/feel.js';
import { createBus } from '../core/events.js';
import { createState } from '../sim/state.js';
import { createFx } from '../fx/reactions.js';
import { bumpFeel, BUMP_LOOK } from '../fx/cues.js';
import { createPhotosafe } from '../fx/photosafe.js';
import { createGentle } from '../fx/gentle.js';
import { sfxPlan, SFX_GAIN } from '../audio/sfxmap.js';

const feelJson = JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8'));
function rig({ gentle = false } = {}) {
  const feel = createFeel(feelJson), bus = createBus();
  const g = createGentle(feel, { search: '' });
  if (gentle) g.set({ on: true });
  const fx = createFx(bus, feel, { gentle: g, photosafe: createPhotosafe(feel) });
  const kart = createState();
  // the largest screen shake (trauma aberration) seen over s seconds
  const peakX = (s, dt = 1 / 120) => { let m = 0; for (let t = 0; t < s - 1e-9; t += dt) { fx.update(dt, kart); m = Math.max(m, fx.state.aberrationPx); } return m; };
  return { feel, bus, fx, peakX };
}

test('bumpFeel: a graze is nothing, a real bump thuds, a hard one shakes, and the shake grows with dv', () => {
  const min = 1.5;
  assert.equal(bumpFeel(min * 0.9, min), null, 'graze');
  const soft = bumpFeel(min * 1.2, min);
  assert.ok(soft && soft.sideM === 0 && soft.rumbleTier === 1, 'a light bump thuds without a shake');
  const mid = bumpFeel(5, min), hard = bumpFeel(BUMP_LOOK.fullDvMps, min), over = bumpFeel(30, min);
  assert.ok(mid.sideM > 0 && mid.sideM < hard.sideM, 'shake grows with the closing speed');
  assert.equal(hard.sideM, BUMP_LOOK.sideAmpM[1]);
  assert.equal(over.sideM, hard.sideM, 'capped');
  assert.equal(hard.level, 1); assert.equal(hard.rumbleTier, 2);
  assert.ok(mid.level > soft.level);
});

test('bump trauma: never more than the brief 0.2, and never a hit-stop', () => {
  const r = rig(); r.bus.emit('bump', { a: 0, b: 2, dv: 30 }); r.fx.update(1 / 120);
  assert.ok(r.fx.state.trauma > 0.15 && r.fx.state.trauma <= 0.2 + 1e-9, 'trauma ' + r.fx.state.trauma);
  assert.equal(r.fx.state.holdS, 0);
});

test('fx: a hard bump on the player shakes sideways only, scaled by impact; bots, grazes and Gentle do not', () => {
  const a = rig(); a.bus.emit('bump', { a: 0, b: 2, dv: 4 }); const small = a.peakX(0.4);
  const b = rig(); b.bus.emit('bump', { a: 3, b: 0, dv: 9 }); const big = b.peakX(0.4);
  assert.ok(small > 0 && big > small, 'scaled: ' + small + ' < ' + big);
  const c = rig(); c.bus.emit('bump', { a: 0, b: 2, dv: 9 }); c.fx.update(1 / 60); assert.equal(c.fx.state.camPush, 0, 'no camera push from a bump');
  const bot = rig(); bot.bus.emit('bump', { a: 2, b: 3, dv: 9 }); assert.equal(bot.peakX(0.3), 0);
  const graze = rig(); graze.bus.emit('bump', { a: 0, b: 3, dv: 1 }); assert.equal(graze.peakX(0.3), 0);
  graze.fx.update(1 / 60); assert.equal(graze.fx.state.rumble, 0, 'a graze is nothing at all');
  const g = rig({ gentle: true }); g.bus.emit('bump', { a: 0, b: 3, dv: 9 }); assert.equal(g.peakX(0.4), 0, 'Gentle: no shake');
  const z = rig(); z.bus.emit('bump', { a: 0, b: 3, dv: 9 }); z.peakX(0.05); z.fx.reset(); assert.equal(z.peakX(0.1), 0, 'reset clears it');
});

test('sfx: using any self item plays from the shipped bank', () => {
  for (const id of ['surge', 'glass', 'echo', 'golden', 'unknown']) {
    const plan = sfxPlan('item-fire', { racer: 0, id, mode: 'self' });
    assert.ok(Array.isArray(plan) && plan.length > 0, id);
    for (const x of plan) assert.ok(x.id in SFX_GAIN, id + ' uses ' + x.id);
  }
});
