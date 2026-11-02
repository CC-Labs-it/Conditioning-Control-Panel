// FF60 shadows lane: the shadow pass skips what cannot reach the map (render/shadows.js).
import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const file = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' }[specifier];
  return file ? { url: new URL('../vendor/three/build/' + file, import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const S = await import('../render/shadows.js');
const T = await import('three/webgpu');
hooks.deregister();

const box = (s = 1) => new T.Mesh(new T.BoxGeometry(s, s, s), new T.MeshBasicNodeMaterial());

test('kart parts under KART_CAST_MIN_M stop casting, bodies keep casting (scales included)', () => {
  const kart = new T.Group(), body = box(1.6), cup = box(0.2), scaled = new T.Group(), part = box(0.2);
  scaled.scale.setScalar(4); scaled.add(part);   // 0.2 m box at 4x = 0.69 m radius: a real part, casts
  kart.add(body, cup, scaled);
  const scene = new T.Scene(); scene.add(kart);
  const shadows = S.buildShadows({ three: { shadowMap: {} } }, scene, null, { name: 'high', shadows: 'map' }, { karts: [kart] });
  assert.equal(body.castShadow, true);
  assert.equal(cup.castShadow, false, 'a 0.17 m part casts nothing through the VSM light-bleed cut');
  assert.equal(part.castShadow, true);
  assert.ok(S.castRadius(cup, kart) < S.KART_CAST_MIN_M && S.castRadius(part, kart) > S.KART_CAST_MIN_M);
  // a part grown later with the body's flag (vehicleLife mechanisms) is trimmed by the periodic pass
  const late = box(0.1); late.castShadow = true; body.add(late);
  for (let i = 0; i < S.PRUNE_EVERY; i++) shadows.update([0, 0, 0]);
  assert.equal(late.castShadow, false);
  assert.equal(body.castShadow, true, 'the trim pass only ever turns a part off');
  const off = box(2); off.castShadow = false; kart.add(off);
  S.trimSmallCasters(kart, S.KART_CAST_MIN_M);
  assert.equal(off.castShadow, false);
  shadows.dispose();
});

test('projection skips exactly the meshes the VSM shadow pass rejects', () => {
  const m = box(); assert.equal(S.shadowProjectionSkips(m), true);
  m.castShadow = true; assert.equal(S.shadowProjectionSkips(m), false);
  m.castShadow = false; m.receiveShadow = true; assert.equal(S.shadowProjectionSkips(m), false);
  assert.equal(S.shadowProjectionSkips(new T.Group()), false, 'groups set the render order, never skipped');
  assert.equal(S.shadowProjectionSkips(new T.DirectionalLight()), false);
  assert.equal(S.shadowProjectionSkips(new T.LOD()), false);
});

test('the filter runs only for registered shadow cameras and steps over pruned subtrees', () => {
  // a stand-in renderer with r186's shape: render() projects through this._projectObject, which recurses through it
  const three = {
    _projectObject(o, camera, order, list) {
      if (o.visible === false) return;
      if (o.isMesh) list.push(o.name);
      for (const c of o.children) this._projectObject(c, camera, order, list);
    },
    render(scene, camera) { const list = []; this._projectObject(scene, camera, 0, list); return list; },
  };
  const scene = new T.Scene(), casters = new T.Group(), dressing = new T.Group(), lit = new T.Group();
  const caster = box(); caster.name = 'caster'; caster.castShadow = true;
  const plain = box(); plain.name = 'plain';
  const under = box(); under.name = 'under-plain'; under.castShadow = true; plain.add(under);   // a caster below a non-caster
  const deco = box(); deco.name = 'deco'; dressing.add(deco);
  const lamp = new T.PointLight(); lit.add(lamp, box());
  casters.add(caster, plain); scene.add(casters, dressing, lit);
  const rec = S.installShadowProjection(three), shadowCam = new T.OrthographicCamera(), view = new T.PerspectiveCamera();
  assert.equal(S.installShadowProjection(three), rec, 'installed once per renderer');
  rec.cameras.add(shadowCam);
  assert.deepEqual(three.render(scene, view).sort(), ['', 'caster', 'deco', 'plain', 'under-plain'].sort());
  assert.deepEqual(three.render(scene, shadowCam).sort(), ['caster', 'under-plain'].sort());
  rec.refresh(scene);
  assert.equal(dressing._glassroadShadowPrune, rec.epoch, 'a subtree with nothing for the map is stamped');
  assert.notEqual(casters._glassroadShadowPrune, rec.epoch);
  assert.notEqual(lit._glassroadShadowPrune, rec.epoch, 'a light keeps its subtree in the walk');
  // a pruned subtree is skipped whole (a stamp is at most PRUNE_EVERY frames old); the next refresh sees the caster
  deco.castShadow = true;
  assert.equal(three.render(scene, shadowCam).includes('deco'), false);
  rec.refresh(scene);
  assert.ok(three.render(scene, shadowCam).includes('deco'));
  assert.notEqual(dressing._glassroadShadowPrune, rec.epoch);
  rec.cameras.delete(shadowCam);
  assert.ok(three.render(scene, shadowCam).includes('plain'), 'an unregistered camera gets the stock projection');
});

test('a shadow map never renders while the renderer has shadows switched off (hit cam inset, water probes)', () => {
  assert.equal(T.ShadowNode.prototype.updateBefore._glassroadSkipsDisabled, true, 'installed when shadows.js loads');
  assert.equal(S.skipShadowsWhileDisabled(), false, 'installed once');
  // a stand-in class with the stock method: the patch forwards only while shadowMap.enabled is on
  let calls = 0;
  class Stand { updateBefore() { calls++; } }
  assert.equal(S.skipShadowsWhileDisabled(Stand), true);
  const node = new Stand(), renderer = { shadowMap: { enabled: false } };
  node.updateBefore({ renderer });
  assert.equal(calls, 0, 'the inset and probe captures switch shadows off: no second shadow pass, no relinks');
  renderer.shadowMap.enabled = true;
  node.updateBefore({ renderer });
  assert.equal(calls, 1);
});
