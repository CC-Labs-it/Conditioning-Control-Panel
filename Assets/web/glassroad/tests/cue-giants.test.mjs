import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';

const hooks = registerHooks({ resolve(specifier, context, next) {
  if (specifier.startsWith('three/addons/'))
    return { url: new URL('../vendor/' + specifier, import.meta.url).href, shortCircuit: true };
  if (specifier === 'three' || specifier === 'three/webgpu')
    return { url: new URL('../vendor/three/build/three.webgpu.min.js', import.meta.url).href, shortCircuit: true };
  return next(specifier, context);
} });
const { Scene, Box3, Raycaster, Vector3 } = await import('three/webgpu');
const { createCueGiants } = await import('../render/cueGiants.js');
hooks.deregister();

const row = id => ({ id, kind: 'PROP', locked: true, alpha: 1, startS: 10, endS: 20,
  frame: { p: [0, 0, 0], b: [1, 0, 0], n: [0, 1, 0], t: [0, 0, -1], w: 20 },
  params: { variant: 'sole' }, joltTimes: [12] });

test('the giant foot has a continuous instep, raised inner arch and five attached graduated toes', () => {
  const giants = createCueGiants(new Scene()), foot = giants.object.getObjectByName('cue-foot');
  const body = foot.getObjectByName('cue-foot-body'), toes = foot.userData.toes;
  assert.equal(foot.children.filter(child => child.isMesh).length, 1, 'one body surface replaces stacked ellipsoids');
  assert.equal(toes.length, 5);
  body.updateMatrixWorld(true);
  const under = (x, z) => {
    const ray = new Raycaster(new Vector3(x, -3, z), new Vector3(0, 1, 0));
    const hits = ray.intersectObject(body); assert.ok(hits.length, `surface at ${x},${z}`);
    return hits[0].point.y;
  };
  assert.ok(under(-1.2, .2) > under(0, 3.5) + .5, 'the medial arch rises above heel contact');
  assert.ok(under(-1.2, .2) > under(0, -2.8) + .5, 'the ball stays planted below the arch');
  assert.ok(under(-1.2, .2) > under(1.2, .2) + .15, 'the instep has an inner and outer edge');
  const bodyBounds = new Box3().setFromObject(body), widths = toes.map(toe => toe.scale.x);
  assert.ok(widths.every((width, i) => !i || width < widths[i - 1]));
  for (const toe of toes) {
    const tip = toe.children[0], bounds = new Box3().setFromObject(tip);
    assert.ok(bounds.intersectsBox(bodyBounds), 'toe roots remain seated inside the forefoot');
    assert.equal(toe.children.filter(child => child.name === 'cue-foot-nail').length, 1);
  }
  const colours = body.geometry.attributes.color.array;
  assert.ok(Math.max(...colours) - Math.min(...colours) > .2, 'baked soft light gives the unlit mesh form');
  const bounds = new Box3().setFromObject(foot);
  assert.ok(bounds.min.y >= .03 && bounds.max.y < 4.4);
  assert.ok(bounds.min.z > -6.1 && bounds.max.z < 5.8);
  giants.dispose();
});

test('feet share geometry, keep the crossing clear, freeze when paused and soften toe motion under Gentle', () => {
  const scene = new Scene(), giants = createCueGiants(scene);
  giants.update([row('a'), row('b')], { songS: 12.45 });
  const feet = giants.object.getObjectsByProperty('name', 'cue-foot');
  assert.equal(feet.length, 2);
  assert.equal(feet[0].getObjectByName('cue-foot-body').geometry, feet[1].getObjectByName('cue-foot-body').geometry);
  assert.equal(feet[0].userData.toes[0].children[0].geometry, feet[1].userData.toes[4].children[0].geometry);
  const angle = feet[0].userData.toes[0].rotation.x;
  assert.ok(angle > .8);
  giants.update([row('a'), row('b')], { songS: 12.8, paused: true });
  assert.equal(feet[0].userData.toes[0].rotation.x, angle);
  giants.update([row('a'), row('b')], { songS: 12.45, gentle: true });
  assert.ok(Math.abs(feet[0].userData.toes[0].rotation.x - angle * .4) < 1e-9);
  scene.updateMatrixWorld(true);
  for (const foot of feet) {
    assert.ok(new Box3().setFromObject(foot).min.y > 8, 'the full posed foot clears the road');
    const origin = foot.getWorldPosition(new Vector3());
    for (const toe of foot.userData.toes)
      assert.ok(toe.getWorldPosition(new Vector3()).z > origin.z + .5, 'all toes face the approaching driver');
  }
  const geometry = new Set(), disposal = new Map();
  giants.object.traverse(object => { if (object.geometry) geometry.add(object.geometry); });
  for (const g of geometry) g.addEventListener('dispose', () => disposal.set(g, (disposal.get(g) || 0) + 1));
  giants.clear(); assert.deepEqual(giants.stats, { heels: 0, feet: 0 });
  giants.dispose(); assert.equal(scene.children.length, 0);
  for (const g of geometry) assert.equal(disposal.get(g), 1, 'every shared geometry is disposed once');
});
