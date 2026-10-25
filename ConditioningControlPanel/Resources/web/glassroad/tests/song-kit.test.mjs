import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
const hooks = registerHooks({ resolve(s, c, next) {
  const files = { 'three': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js', 'three/webgpu': 'three.webgpu.min.js' };
  if (s.startsWith('three/addons/')) return { url: new URL('../vendor/' + s, import.meta.url).href, shortCircuit: true };
  return files[s] ? { url: new URL('../vendor/three/build/' + files[s], import.meta.url).href, shortCircuit: true } : next(s, c);
} });
const { createCueShift, shiftAmount, SHIFT_PRESETS } = await import('../render/cueShift.js');
const { createCueBar, barValue } = await import('../render/cueBar.js');
const { createCueParticles } = await import('../render/cueParticles.js');
const { createCueGantries } = await import('../render/cueGantry.js');
const { Scene, FogExp2, Color } = await import('three/webgpu');
const { createState, S, setPos } = await import('../sim/state.js');
hooks.deregister();

const frameAt = (s, out = {}) => Object.assign(out, { p: [0, 0, s], b: [1, 0, 0], n: [0, 1, 0], t: [0, 0, 1], w: 20 });
const track = { id: 'goonworks', lengthM: 5000, frameAt };
const canvas = () => { const ctx = new Proxy({}, { get: (o, k) => k in o ? o[k] : (k === 'measureText' ? () => ({ width: 10 }) : k === 'createLinearGradient' || k === 'createRadialGradient' ? () => ({ addColorStop() {} }) : () => {}), set: (o, k, v) => (o[k] = v, true) });
  return { width: 0, height: 0, getContext: () => ctx }; };
const kart = s => { const st = createState(); setPos(st, [0, 0, s]); st[S.s] = s; return st; };
const row = (kind, extra = {}) => ({ id: kind + (extra.id || ''), kind, mode: 'WATCH', startS: 10, endS: 20, alpha: 1, locked: true, s: 300,
  frame: frameAt(300), params: {}, joltTimes: [], ...extra });

test('a SHIFT eases in over its ramp, holds, eases out, and leaves the fog where it found it', () => {
  const r = row('SHIFT', { params: { preset: 'pink', transitionBars: 2 } });
  assert.equal(shiftAmount(r, 9.9, { bpm: 120 }), 0);
  assert.ok(shiftAmount(r, 11, { bpm: 120 }) > 0 && shiftAmount(r, 11, { bpm: 120 }) < 1);
  assert.equal(shiftAmount(r, 15, { bpm: 120 }), 1);
  assert.equal(shiftAmount(r, 22, { bpm: 120 }), 0);
  assert.equal(shiftAmount({ ...r, params: { ...r.params, holdToEnd: true } }, 99, { bpm: 120 }), 1);
  // Gentle never shortens the rise below 1.2 s.
  assert.ok(shiftAmount({ ...r, params: { preset: 'dim' } }, 10.8, { gentle: true }) < 1);
  const scene = new Scene(); scene.background = new Color('#f7d4ee');
  const shift = createCueShift(scene);
  assert.ok(scene.fog instanceof FogExp2, 'fog exists from the start so warmed shaders carry it');
  shift.update([r], 15, { bpm: 120 });
  assert.equal(scene.fog.density, SHIFT_PRESETS.pink.density); assert.ok(shift.stats.dome);
  shift.update([], 30, {}); assert.equal(scene.fog.density, 0); assert.equal(scene.background.getHexString(), 'f7d4ee');
  shift.dispose(); assert.equal(scene.children.length, 0);
});

test('a course that drives its own fog keeps driving it once the shift ends', () => {
  const scene = new Scene(); scene.fog = new FogExp2('#063e49', .004);
  const shift = createCueShift(scene);
  shift.update([row('SHIFT', { params: { preset: 'green' } })], 15, {});
  assert.ok(scene.fog.density > .004);
  shift.update([], 30, {}); assert.equal(scene.fog.color.getHexString(), '063e49');
});

test('the install bar steps on its blips; the reboot bar fills steadily and completes at its end', () => {
  const rows = [row('BAR', { id: 'a', startS: 10, endS: 50, params: { variant: 'install', value: 0 } }),
    row('BAR', { id: 'b', startS: 20, endS: 24, params: { variant: 'install', value: .5 } }),
    row('BAR', { id: 'c', startS: 60, endS: 80, params: { variant: 'reboot', value: 0 } }),
    row('BAR', { id: 'd', startS: 70, endS: 80, params: { variant: 'reboot', value: .5 } })];
  assert.equal(barValue(rows, 5, 'install'), null);
  assert.equal(barValue(rows, 15, 'install'), 0);
  assert.equal(barValue(rows, 21, 'install'), .5);
  assert.equal(barValue(rows, 65, 'reboot'), .25);
  assert.equal(barValue(rows, 75, 'reboot'), .75);
  assert.equal(barValue(rows, 90, 'reboot'), 1);
  const scene = new Scene(), bar = createCueBar(scene, track, { make: canvas });
  bar.update([rows[0]], rows, { songS: 21, kart: kart(100), dt: .5 });
  assert.equal(bar.stats.value, 50); assert.ok(bar.object.visible);
  for (let i = 0; i < 30; i++) bar.update([], rows, { songS: 60, kart: kart(100), dt: .1 });
  assert.equal(bar.object.visible, false);
  bar.dispose();
});

test('particles are pooled, halve under Gentle, fall and expire', () => {
  const scene = new Scene(), fx = createCueParticles(scene);
  assert.equal(fx.burst('hearts', [0, 0, 0], { count: 20 }), 20);
  assert.equal(fx.burst('hearts', [0, 0, 0], { count: 20, gentle: true }), 10);
  assert.equal(fx.burst('nope', [0, 0, 0]), 0);
  fx.update(.1); assert.equal(fx.stats.hearts, 30);
  for (let i = 0; i < 60; i++) fx.update(.1);
  assert.equal(fx.stats.hearts, 0);
  fx.burst('shards', [0, 0, 0], { count: 999 }); fx.update(.1); assert.ok(fx.stats.shards <= 220);
  fx.clear(); fx.dispose(); assert.equal(scene.children.length, 0);
});

test('arches light on the beat, go dark once crossed, and stand outside the road', () => {
  const scene = new Scene(), arches = createCueGantries(scene, track, { make: canvas });
  const r = row('GANTRY', { mode: 'CROSS', params: { label: '5' }, hitS: null });
  arches.update([r], { songS: 9, song: { bpm: 120, downbeatS: 0 } });
  assert.equal(arches.stats.active, 1);
  const root = arches.object.children.find(c => c.visible);
  for (const post of root.children.slice(0, 2)) assert.ok(Math.abs(post.position.x) > 10);
  arches.update([{ ...r, hitS: 10 }], { songS: 11, song: { bpm: 120 } });
  arches.update([], { songS: 12 }); assert.equal(arches.stats.active, 0);
  arches.dispose();
});
