import { test } from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { createState } from '../sim/state.js';
import { LOOK } from '../render/look.js';
import { HUMAN_TAG, BOT_TAG_FADE, tagKind, tagTarget, stepTagAlpha, tagScaleK } from '../render/nameTag.js';

const hooks = registerHooks({ resolve(s, c, next) {
  const files = { three: 'build/three.webgpu.min.js', 'three/webgpu': 'build/three.webgpu.min.js', 'three/tsl': 'build/three.tsl.min.js' };
  const file = files[s] || (s.startsWith('three/addons/') ? 'addons/' + s.slice(13) : null);
  return file ? { url: new URL('../vendor/three/' + file, import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { buildKarts } = await import('../render/karts.js');
const { BoxGeometry, Group } = await import('three/webgpu');
hooks.deregister();

// owner 2026-10-30: "we need to make the players (human) visible with a tag"
test('name tags: an online human seat is human, bots, ghosts and the player are not', () => {
  assert.equal(tagKind({ isBot: false, seat: 3, name: 'Ari' }, 2), 'human');
  assert.equal(tagKind({ isBot: false, seat: 0, name: 'Ari' }, 1), 'human');
  assert.equal(tagKind({ isBot: true, seat: 4, name: 'Lull' }, 3), 'bot');
  assert.equal(tagKind({ isBot: false, name: 'Ghost', tint: 0 }, 1), 'bot', 'the solo ghost has no seat');
  assert.equal(tagKind({ isBot: false, seat: 1, name: 'You' }, 0), null, 'the player never wears a tag');
  assert.equal(tagKind(null, 2), null);
});

test('name tags: a human reads far and past the rival LOD, a bot only near', () => {
  assert.equal(tagTarget('human', 60, false, LOOK), 1, 'past the LOD a human still shows');
  assert.equal(tagTarget('bot', 60, false, LOOK), 0);
  assert.ok(tagTarget('human', LOOK.tagNearM + 20, false, LOOK) > 0.99, 'a human outlasts the bot range');
  assert.equal(tagTarget('human', HUMAN_TAG.farM + 1, false, LOOK), 0, 'gone past farM');
  assert.equal(tagTarget('human', 1, true, LOOK), 0, 'not at point blank');
  assert.equal(tagTarget('bot', 20, true, LOOK), 1);
  assert.equal(tagTarget(null, 20, true, LOOK), 0);
  // readable at distance: the human tag grows with range, the bot tag does not
  assert.ok(tagScaleK('human', 100, 1, true) > 2);
  assert.equal(tagScaleK('bot', 100, 1, true), 1);
});

test('name tags: fades IN and OUT over time; motion off has no grow, nothing pulses', () => {
  let a = 0;
  a = stepTagAlpha(a, 1, 0.1); assert.ok(a > 0 && a < 1, 'not a pop');
  for (let i = 0; i < 10; i++) a = stepTagAlpha(a, 1, 0.1);
  assert.equal(a, 1);
  a = stepTagAlpha(a, 0, 0.1); assert.ok(a > 0 && a < 1, 'not a cut');
  for (let i = 0; i < 10; i++) a = stepTagAlpha(a, 0, 0.1);
  assert.equal(a, 0);
  assert.equal(stepTagAlpha(0.4, 1, 0), 0.4, 'paused holds');
  assert.ok(HUMAN_TAG.outS >= HUMAN_TAG.inS && BOT_TAG_FADE.inS > 0);
  assert.equal(tagScaleK('human', 10, 0.2, true), 1, 'still: no grow');
  assert.ok(tagScaleK('human', 10, 0.2, false) < 1, 'motion on: grows in');
  assert.equal(tagScaleK('human', 10, 1, false), tagScaleK('human', 10, 1, true), 'settled tag is steady');
});

function withCanvas(fn) {
  const ctx = new Proxy({ measureText: (t) => ({ width: String(t).length * 20 }) }, { get: (o, k) => (k in o ? o[k] : () => {}), set: () => true });
  globalThis.document = { createElement: () => ({ width: 0, height: 0, getContext: () => ctx }) };
  try { return fn(); } finally { delete globalThis.document; }
}

test('name tags: karts draw a pink over-traffic tag for a human, the muted one for a bot; a left seat fades out', () => withCanvas(() => {
  const karts = buildKarts(null, 3, new BoxGeometry()), scene = new Group();
  for (const k of karts.list) scene.add(k.root);
  const st = createState(), [, bot, human] = karts.list;
  for (let i = 0; i < 30; i++) {
    karts.pose(bot, st, {}, 1 / 30, 20, { name: 'Lull', tagKind: 'bot' });
    karts.pose(human, st, {}, 1 / 30, 20, { name: 'Ari', tagKind: 'human' });
  }
  assert.equal(bot.tag.material.depthTest, true, 'a bot tag sits in the world');
  assert.equal(human.tag.material.depthTest, false, 'a human tag reads through traffic');
  assert.ok(human.tag.scale.x > bot.tag.scale.x, 'the human pill is the bigger one');
  assert.equal(human.tag.material.opacity, 1); assert.equal(human.tag.visible, true);

  // the seat leaves: the kart goes at once, the tag stays in the world and fades out, then is gone
  const tag = human.tag;
  karts.leave(human, 0.1);
  assert.equal(human.root.visible, false);
  assert.equal(tag.parent, scene, 'kept in the world');
  assert.ok(tag.material.opacity > 0 && tag.material.opacity < 1, 'fading, not cut');
  for (let i = 0; i < 10; i++) karts.leave(human, 0.1);
  assert.equal(tag.parent, null, 'removed once faded');
  assert.equal(human.leftTag, null);

  // back again (a rejoin): fades in from zero
  karts.pose(human, st, {}, 0.05, 20, { name: 'Ari', tagKind: 'human' });
  assert.ok(human.tag && human.tag.material.opacity > 0 && human.tag.material.opacity < 1);
  // a bot seat taken over by a human swaps the style
  karts.pose(bot, st, {}, 0.05, 20, { name: 'Lull', tagKind: 'human' });
  assert.equal(bot.tag.material.depthTest, false);
  karts.dispose();
}));
