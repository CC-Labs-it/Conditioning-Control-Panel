/* tests/staged-warm.test.mjs - the phone tier's staged shader preparation (render/stagedWarmPlan.js, pure part)
 * and the wiring: boot stages the warm on the phone tier only, the deferred runner steps once per frame, and
 * the intro clock waits for a group its next frames need.
 */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { STAGE, planStages, viewTime, dueIndex } from '../render/stagedWarmPlan.js';
import { createDeferredWarm } from '../render/stagedWarm.js';

const src = (p) => readFileSync(new URL('../' + p, import.meta.url), 'utf8');

test('planStages pins the cover set and groups the rest in need order, perGroup fresh materials each', () => {
  const e = (name, needT, mats, pinned = false) => ({ item: name, needT, mats, pinned });
  const entries = [e('road', 0, ['r']), e('late', 9, ['x', 'y']), e('pinned', 20, ['p'], true), e('mid', 3, ['a', 'b', 'c']),
    e('mid2', 3.5, ['c', 'd']), e('mid3', 4, ['e', 'f', 'g']), e('early', 0.4, ['q'])];
  const plan = planStages(entries, { coverS: 0.5, perGroup: 4 });
  assert.deepEqual(plan.now, ['road', 'pinned', 'early']);
  assert.deepEqual(plan.groups.map((g) => g.items), [['mid', 'mid2'], ['mid3'], ['late']]);
  assert.deepEqual(plan.groups.map((g) => g.needT), [3, 4, 9]);
  assert.equal(dueIndex(plan.groups, 0, 1, STAGE.leadS), 0);
  assert.equal(dueIndex(plan.groups, 0, 0, 0.4), -1);
  assert.equal(dueIndex(plan.groups, 3, 99, 99), -1);
});

test('viewTime runs the intro, then the countdown, then race time; the menu is -1', () => {
  assert.equal(viewTime({ state: 'menu' }), -1);
  assert.equal(viewTime({ state: 'intro', introElapsed: 4.2 }), 4.2);
  assert.equal(viewTime({ state: 'race', introS: 10, goS: 3, goInS: 2, phase: 'countdown' }), 11);
  assert.equal(viewTime({ state: 'race', introS: 10, goS: 3, raceTimeS: 5, phase: 'racing' }), 18);
});

test('the deferred runner shows one group for the synchronous part of a compile, then restores the scene', async () => {
  const o = (name, visible = true) => ({ name, visible, frustumCulled: true, parent: null, material: null });
  const a = o('a'), b = o('b'), c = o('c', false);
  const scene = { name: 'scene' }; for (const x of [a, b, c]) x.parent = scene;
  const seen = [];
  let resolveCompile;
  const post = { compileStart() { seen.push([a.visible, b.visible, c.visible, c.frustumCulled]); return new Promise((r) => { resolveCompile = r; }); } };
  const d = createDeferredWarm({ initTexture() {} }, () => post, { all: [a, b, c], groups: [{ items: [c], needT: 3 }, { items: [b], needT: 6 }], scene });
  assert.equal(d.behind(0.5), false);
  assert.equal(d.behind(2.8), true, 'a group needed inside the hold window is behind');
  d.step();
  assert.deepEqual(seen, [[false, false, true, false]], 'only the group is visible, unculled, for the projection');
  assert.deepEqual([a.visible, b.visible, c.visible, c.frustumCulled], [true, true, false, true], 'restored in the same task');
  d.step();
  assert.equal(seen.length, 1, 'one group in flight');
  resolveCompile(); await new Promise((r) => setTimeout(r, 0)); await new Promise((r) => setTimeout(r, 0));
  assert.equal(d.stats.next, 1);
  assert.equal(d.behind(2.8), false);
  d.dispose(); d.step();
  assert.equal(seen.length, 1, 'disposed: no more steps');
});

test('boot stages the warm on the phone tier only, steps the runner each frame and holds the intro clock', () => {
  const boot = src('boot.js');
  assert.match(boot, /const staged = renderer\.tier === 'phone' && !warmPose && qs\.get\('stage'\) !== '0';/);
  assert.match(boot, /deferred = renderer\.deferredWarm\(warm, world\.scene\);/);
  assert.match(boot, /if \(!frozen && !\(deferred && deferred\.behind\(introElapsed \+ frameDt\)\)\) introElapsed = Math\.min/);
  assert.match(boot, /if \(deferred && !deferred\.done\) \{[\s\S]{0,400}deferred\.step\(\);/);
  assert.match(boot, /lifetime\.defer\(\(\) => \{ deferred\?\.dispose\(\); deferred = null; \}\);/);
  const renderer = src('render/renderer.js');
  assert.match(renderer, /deferredWarm\(warm, scene\)/);
  assert.match(renderer, /casting = items\.filter\(\(o\) => !o\.receiveShadow && !\(later && later\.has\(o\)\)\)/);
  assert.match(src('render/post.js'), /compileStart\(\) \{/);
  assert.ok(!/from 'three/.test(src('render/stagedWarm.js')), 'the runner has no three import (renderer test stubs)');
});
