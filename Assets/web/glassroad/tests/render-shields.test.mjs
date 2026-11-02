import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { ITEM_LOOK as L, AURA_LOOK as A } from '../render/items/look.js';
import { selectShieldShells, shieldLook, auraStyle } from '../render/items/shieldMath.js';
import { createItems } from '../sim/items.js';
import { S } from '../sim/state.js';
import { BUBBLE_RAM } from '../core/bubble.js';
import { fakeTrack, makeFeel, recorder, place, acts, DT } from './sim-fake.mjs';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const builds = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return builds[specifier] ? { url: new URL('../vendor/three/build/' + builds[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { shieldCloud } = await import('../render/items/materials.js');
const { Matrix4, Quaternion, Vector3 } = await import('three/webgpu'); hooks.deregister();

test('real Glass and Golden item overlap draws one shell and preserves Glass after Golden ends', () => {
  const track = fakeTrack(), feel = makeFeel(), events = recorder(), items = createItems(track, feel, events);
  const racer = { i: 0, st: place(track), input: acts(), item: {}, mods: {}, fxv: {}, finished: false };
  const standings = { order: [0], progressOf: () => racer.st[S.s], placeOf: () => 1, gapToLeader: () => 0 };
  const update = dt => items.update(dt, [racer], standings, { phase: 'racing' });
  function fire(id) { racer.item.id = id; racer.input.item = true; update(DT); racer.input.item = false; }
  fire('glass'); update(1);
  const presentation = rows => {
    const row = selectShieldShells(rows)[0], glass = row.kind === 'golden' ? rows.find(r => r.kind === 'glass') : null;
    return shieldLook(row, feel.get(row.kind === 'golden' ? 'items.goldenS' : 'items.glassMaxS'), true, false, {},
      glass ? shieldLook(glass, feel.get('items.glassMaxS'), true) : null);
  };
  let previous = presentation(items.view().shields); fire('golden');
  const rows = structuredClone(items.view().shields); assert.equal(rows.length, 2);
  const selected = selectShieldShells(rows); assert.equal(selected.length, 1); assert.equal(selected[0].kind, 'golden');
  assert.deepEqual(rows, items.view().shields, 'visual selection cannot remove the saved shield');
  const reversed = selectShieldShells(rows.slice().reverse()); assert.equal(reversed[0].kind, 'golden');
  for (let t = 0; t < feel.get('items.goldenS') + .1; t += DT) {
    const current = presentation(items.view().shields);
    assert.ok(current.alpha >= L.playerShieldAlpha - .00001, 'continuous protection keeps the existing shell visible');
    assert.ok(Math.abs(current.alpha - previous.alpha) < .01);
    assert.ok(Math.abs(current.gold - previous.gold) < .11, 'Golden tint joins and leaves without a colour cut');
    assert.ok(Math.abs(current.warning - previous.warning) < .06);
    previous = current; update(DT);
  }
  assert.equal(racer.fxv.shield, 'glass');
  assert.equal(selectShieldShells(items.view().shields)[0].kind, 'glass');
  items.reset(); assert.deepEqual(selectShieldShells(items.view().shields), []);
});

test('shield warning enters and expires smoothly, is clock deterministic and never flashes brighter', () => {
  for (const duration of [4, 20]) for (const gentle of [false, true]) {
    let previous = null, previousWarning = 0;
    for (let t = 0; t <= duration + .01; t += .001) {
      const row = { kind: duration === 4 ? 'golden' : 'glass', t, k: Math.max(0, 1 - t / duration) };
      const look = shieldLook(row, duration, true, gentle);
      assert.ok(Number.isFinite(look.alpha) && look.alpha >= 0 && look.alpha <= L.playerShieldAlpha);
      assert.ok(look.warning >= previousWarning - 1e-10 && look.warning <= 1);
      if (previous) { assert.ok(Math.abs(look.alpha - previous.alpha) < .003); assert.ok(Math.abs(look.warning - previous.warning) < .002); }
      previous = look; previousWarning = look.warning;
    }
    assert.equal(shieldLook({ t: 0, k: 1 }, duration, true, gentle).alpha, 0);
    assert.equal(shieldLook({ t: duration, k: 0 }, duration, true, gentle).alpha, 0);
    const paused = { kind: 'glass', t: duration - 1, k: 1 / duration };
    assert.deepEqual(shieldLook(paused, duration, true, gentle), shieldLook(paused, duration, true, gentle));
  }
  const a = shieldLook({ kind: 'glass', t: 18.6, k: .05 }, 20, true, true);
  const b = shieldLook({ kind: 'glass', t: 18.9, k: .05 }, 20, true, true);
  assert.equal(a.alpha, b.alpha, 'Gentle has no periodic warning pulse');
  assert.ok(L.shieldPulseHz < .5 && L.shieldPulseDepth <= .06);
});

test('shield shell stays at the kart and has a clear center with no art model dependency', () => {
  const cloud = shieldCloud(2), rotation = new Quaternion().setFromAxisAngle(new Vector3(0, 0, 1), .3);
  const look = shieldLook({ kind: 'glass', t: 1, k: .95 }, 20, true);
  cloud.begin(); assert.equal(cloud.push(10, 3, -7, rotation, look.radius, look.alpha, look.gold, look.warning), true); cloud.end();
  const matrix = new Matrix4(), p = new Vector3(), q = new Quaternion(), scale = new Vector3(); cloud.mesh.getMatrixAt(0, matrix); matrix.decompose(p, q, scale);
  assert.deepEqual(p.toArray(), [10, 3, -7]);
  for (let i = 0; i < 3; i++) assert.ok(Math.abs(scale.toArray()[i] - L.shieldShape[i] * look.radius) < 1e-6);
  assert.equal(cloud.mesh.material.positionNode, null, 'no pull towards camera');
  assert.ok(L.shieldBody * L.playerShieldAlpha < .01, 'front center leaves more than 99% of the kart visible');
  assert.equal(cloud.mesh.material.depthWrite, false); assert.equal(cloud.mesh.material.transparent, true);
  const camDistance = Math.hypot(3.4, 1.8 - L.shieldUpM), radius = L.playerShieldR * Math.max(...L.shieldShape);
  const viewShare = radius / Math.sqrt(camDistance ** 2 - radius ** 2) / Math.tan(Math.PI / 6);
  assert.ok(viewShare < .55, 'shell stays around kart instead of filling the chase view');
  assert.ok(L.shieldR < BUBBLE_RAM.radiusM); assert.equal(BUBBLE_RAM.radiusM, 1.55); assert.equal(BUBBLE_RAM.pushMps, 18);
  let geometryDisposals = 0, materialDisposals = 0; cloud.mesh.geometry.addEventListener('dispose', () => geometryDisposals++);
  cloud.mesh.material.addEventListener('dispose', () => materialDisposals++);
  cloud.begin(); cloud.end(); assert.equal(cloud.mesh.count, 0); assert.equal(cloud.mesh.visible, false);
  cloud.dispose(); assert.equal(geometryDisposals, 1); assert.equal(materialDisposals, 1);
});

test('duplicate shield auras are hidden and Surge retains slow low-contrast feedback', () => {
  assert.equal(auraStyle('glass').visible, false); assert.equal(auraStyle('golden').visible, false); assert.equal(auraStyle(null).visible, false);
  const surge = auraStyle('surge', true), gentle = auraStyle('surge', true, true), echo = auraStyle('echo', true);
  assert.equal(surge.visible, true); assert.ok(surge.alpha < A.playerAlpha / 2 && surge.bandHz <= .25);
  assert.equal(surge.bandPower, 1); assert.ok(surge.bandAlpha <= .1 && surge.bandColour <= .14);
  assert.equal(gentle.bandAlpha, 0); assert.equal(gentle.bandColour, 0);
  assert.equal(echo.visible, true); assert.equal(echo.alpha, A.playerAlpha); assert.equal(echo.bandHz, A.bandClimbHz); assert.equal(echo.bandPower, 3);
});
