import test from 'node:test';
import assert from 'node:assert/strict';
import { AnimationMixer, AnimationClip, NumberKeyframeTrack, Object3D, LoopRepeat } from '../vendor/three/build/three.webgpu.min.js';
import { createRigBlend } from '../render/rigBlend.js';

function setup() {
  const model = new Object3D(), mixer = new AnimationMixer(model);
  const clips = { idle_bob: [0, .1, 0], max_speed: [0, .3, 0], hit_recoil: [0, -.2, 0] };
  const actions = Object.fromEntries(Object.entries(clips).map(([name, values]) => [name,
    mixer.clipAction(new AnimationClip(name, 1, [new NumberKeyframeTrack('.position[y]', [0, .5, 1], values)])).setLoop(LoopRepeat)]));
  const blend = createRigBlend(actions);
  const tick = dt => { blend.update(dt); mixer.update(dt); };
  return { model, mixer, actions, blend, tick };
}

test('an interrupted idle blend retains its exact visible weights and eases without a weight jump', () => {
  const { actions, blend, tick } = setup();
  blend.change({ clip: 'idle_bob', fade: .24 }); tick(.24);
  blend.change({ clip: 'max_speed', fade: .24 }); tick(.1);
  const before = Object.values(actions).map(a => a.getEffectiveWeight());
  const phase = actions.idle_bob.time;
  blend.change({ clip: 'idle_bob', fade: .24 }); tick(0);
  assert.deepEqual(Object.values(actions).map(a => a.getEffectiveWeight()), before);
  assert.equal(actions.idle_bob.time, phase, 'resuming the breath never restarts its loop');
  tick(.001);
  assert.ok(Math.abs(actions.idle_bob.getEffectiveWeight() - before[0]) < .00001);
  tick(.24);
  assert.equal(actions.idle_bob.getEffectiveWeight(), 1);
  assert.equal(actions.max_speed.getEffectiveWeight(), 0);
});

test('pause holds both blend and pose; reset restores the original rig without stale action weights', () => {
  const { model, actions, blend, tick } = setup();
  blend.change({ clip: 'idle_bob', fade: .24 }); tick(.12);
  const y = model.position.y, time = actions.idle_bob.time, weight = actions.idle_bob.getEffectiveWeight();
  tick(0); assert.equal(model.position.y, y); assert.equal(actions.idle_bob.time, time);
  assert.equal(actions.idle_bob.getEffectiveWeight(), weight);
  blend.change({ clip: 'hit_recoil', fade: .08 }); tick(.08);
  assert.equal(actions.hit_recoil.getEffectiveWeight(), 1, 'important reactions retain their timing');
  blend.reset(); assert.equal(model.position.y, 0);
  assert.ok(Object.values(actions).every(a => a.getEffectiveWeight() === 0));
});

test('blend progress agrees at 20 and 120 fps', () => {
  const run = hz => { const { actions, blend, tick } = setup(); blend.change({ clip: 'idle_bob', fade: .3 });
    for (let i = 0; i < hz / 5; i++) tick(1 / hz); return actions.idle_bob.getEffectiveWeight(); };
  assert.ok(Math.abs(run(20) - run(120)) < 1e-12);
});
