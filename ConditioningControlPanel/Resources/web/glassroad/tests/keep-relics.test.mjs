import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { getKeepLayout } from '../track/keep-layout.js';
import { prisonPlan } from '../render/keepPrison.js';
import { keepRelics } from '../render/keepRelics.js';
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json', import.meta.url))));
const layout = getKeepLayout(track), plan = prisonPlan(track, layout);
test('Colossal locks form overhead passages and floating ruins stay distinct from the road', () => {
  const relics = keepRelics(track, layout, plan.mounts);
  const locks = relics.filter(b => b.name === 'colossal-leaning-lock-body');
  assert.ok(locks.length >= 2, 'multiple lock passages survive stacked route clearance');
  assert.ok(relics.filter(b => b.name === 'floating-masonry-fragment').length >= 40);
  for (const b of relics) {
    assert.ok(b.size.every(v => v > 0));
    assert.ok(b.p.every(Number.isFinite));
    for (const m of plan.mounts) assert.ok(Math.hypot(...b.p.map((v,i) => v - m.p[i])) >= Math.hypot(m.w, m.h) / 2 + Math.hypot(...b.size) / 2 + 3, 'media sightline envelope');
  }
  for (const b of locks) {
    assert.ok(b.size[0] >= 43, 'colossal silhouette');
    assert.ok(Math.abs(b.tilt) > .1, 'leans visibly');
  }
});
