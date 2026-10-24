import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { padMediaSlots } from '../render/padMedia.js';
import { selectScrolllerSlots } from '../render/mediaSelection.js';
import { S } from '../sim/state.js';

test('all five courses have bounded inset media pads facing out of the road', () => {
  for (const id of ['threshold', 'keep', 'mainframe', 'boudoir', 'goonworks']) {
    const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url), 'utf8')));
    const slots = padMediaSlots(track);
    assert.ok(slots.length > 0 && slots.length <= 12, id);
    assert.deepEqual(new Set(selectScrolllerSlots(slots)), new Set(slots));
    for (const sl of slots) {
      const [a, b, c] = sl.X, [x, y, z] = sl.Y;
      const cross = [b*z-c*y, c*x-a*z, a*y-b*x];
      assert.ok(cross.reduce((s, v, i) => s + v * sl.Z[i], 0) > .999, id + ' front faces up');
      const pad = track.boosts[Number(sl.id.split('-').at(-1))];
      assert.ok(sl.w < pad.w && sl.h < pad.len, 'arrows remain around the picture');
    }
  }
});

test('Loom underlays follow a malus, fade out, ignore buffs and clear on reset', async t => {
  const hooks = registerHooks({ resolve(specifier, context, next) {
    const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
    return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
  } });
  t.after(() => hooks.deregister());
  const { Group } = await import('three/webgpu');
  const { createMalusSpirals } = await import('../render/items/malusSpirals.js');
  const previous = globalThis.OffscreenCanvas;
  const gradient = { addColorStop() {} }, ctx = new Proxy({}, { get: (o, k) => k in o ? o[k] : /Gradient/.test(k) ? () => gradient : () => {} });
  globalThis.OffscreenCanvas = class { constructor(w, h) { this.width = w; this.height = h; } getContext(kind) { return kind === '2d' ? ctx : null; } };
  t.after(() => { globalThis.OffscreenCanvas = previous; });
  const root = new Group(), fx = createMalusSpirals(root, { get: () => false });
  t.after(() => fx.dispose());
  const mesh = root.children[0], st = new Float64Array(100);
  st[S.posX] = 12; st[S.posY] = 2; st[S.quatW] = 1;
  const kart = { st, fxv: { effect: 'freeze' } };
  // Inspect the actual instance coverage input rather than a second lifecycle model.
  function coverage() {
    const seen = new Set(), pending = [mesh.material.opacityNode];
    while (pending.length) { const n = pending.pop(); if (seen.has(n)) continue; seen.add(n);
      if (n.value?.isInstancedBufferAttribute) return n.value.array[0];
      for (const child of n.getChildren()) pending.push(child);
    }
    throw new Error('missing instanced coverage');
  }
  fx.update([kart], .1); const rise = coverage(); assert.ok(rise > 0 && rise < 1);
  for (let i = 0; i < 5; i++) fx.update([kart], .1);
  assert.equal(coverage(), 1); assert.equal(mesh.instanceMatrix.array[12], 12);
  kart.fxv.effect = null; fx.update([kart], .1); assert.ok(coverage() > 0 && coverage() < 1);
  for (let i = 0; i < 5; i++) fx.update([kart], .1); assert.equal(coverage(), 0);
  kart.fxv.effect = 'golden'; fx.update([kart], .1); assert.equal(coverage(), 0);
  kart.fxv.effect = 'spiral'; fx.update([kart], .1); assert.ok(coverage() > 0);
  fx.clear(); assert.equal(coverage(), 0);
});
