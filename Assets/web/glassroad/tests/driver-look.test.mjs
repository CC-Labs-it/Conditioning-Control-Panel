import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createLookAround, DRIVER_LOOK } from '../render/lookAround.js';
import { createCamRig, aimAtAnchor } from '../render/cameraMath.js';
import { createFeel } from '../core/feel.js';
import { createState } from '../sim/state.js';
import { createGamepad } from '../input/gamepad.js';
import { readPad } from '../input/padmap.js';
import { createDevices } from '../input/device.js';
import { createActionMap } from '../core/actions.js';
import { createPointerLook } from '../input/pointerLook.js';
import { RACER_FRAMING } from '../render/framing.js';
import { place, fakeTrack } from './sim-fake.mjs';

const near = (a, b, eps = 1e-8) => assert.ok(Math.abs(a - b) < eps, `${a} != ${b}`);
const DEG = Math.PI / 180;

test('neutral chase exactly matches the previous fully-up held stick, including grid snaps', () => {
  const feel = createFeel(JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8')));
  for (const aspect of [16 / 9, 390 / 844]) for (const rollDeg of [0, 50, 180]) {
    const track = fakeTrack({ rollDeg, flags: () => ['rail', 'magnet'] });
    const kart = place(track, { speed: 20 }), rig = createCamRig(feel);
    rig.snap(kart, aspect, {}, track);
    const before = [...rig.look];
    aimAtAnchor(before, rig.eye, rig.up, rig.anchor, rig.vFov, RACER_FRAMING.screenY);
    const oldGlance = createLookAround();
    for (let i = 0; i < 120; i++) oldGlance.update({ x: 0, y: -1 }, .1);
    oldGlance.apply(rig.eye, before, rig.up);
    rig.look.forEach((v, i) => near(v, before[i], 1e-10));
    assert.deepEqual(rig.glance.view, { yaw: 0, pitch: 0 }, 'manual controls remain relative to the new neutral');
    rig.snap(kart, aspect, {}, track); rig.look.forEach((v, i) => near(v, before[i], 1e-10));
  }
});
test('driver glance has bounded aim, leaves the eye in place and smoothly returns to forward', () => {
  for (const fps of [30, 60, 144]) {
    const glance = createLookAround(), eye = [2, 3, 4], look = [2, 3, -6];
    for (let i = 0; i < fps * 3; i++) glance.update({ x: 100, y: -100 }, 1 / fps);
    near(glance.view.yaw, DRIVER_LOOK.yawDeg * DEG); near(glance.view.pitch, DRIVER_LOOK.upDeg * DEG);
    glance.apply(eye, look, [0, 1, 0]);
    assert.deepEqual(eye, [2, 3, 4]); near(Math.hypot(...look.map((v, i) => v - eye[i])), 10);
    assert.ok(look[0] > eye[0] && look[1] > eye[1], 'right and up stay right and up');
    const held = glance.view.yaw; glance.update(null, 1 / fps);
    assert.ok(glance.view.yaw > 0 && glance.view.yaw < held, 'release begins a pan rather than a snap');
    for (let i = 0; i < fps * 4; i++) glance.update(null, 1 / fps);
    assert.deepEqual(glance.view, { yaw: 0, pitch: 0 });
  }
});

test('driver glance follows a banked camera basis and rejects invalid input without losing a finite pose', () => {
  const glance = createLookAround(), eye = [0, 0, 0], look = [0, 0, -10];
  for (let i = 0; i < 120; i++) glance.update({ x: 1, y: 0 }, 1 / 60, true);
  glance.apply(eye, look, [1, 0, 0]);
  assert.ok(look[1] < 0 && Math.abs(look[0]) < 1e-8, 'right uses the rolled road frame');
  glance.update({ x: NaN, y: Infinity }, NaN);
  assert.ok(Object.values(glance.view).every(Number.isFinite));
  glance.reset(); assert.deepEqual(glance.view, { yaw: 0, pitch: 0 });
});

test('chase glance affects aim only, holds at zero dt and resets on a new grid or rescue snap', () => {
  const feel = createFeel(JSON.parse(readFileSync(new URL('../feel.json', import.meta.url), 'utf8')));
  const a = createCamRig(feel), b = createCamRig(feel), kart = createState(), fx = {};
  a.snap(kart); b.snap(kart);
  for (let i = 0; i < 60; i++) { a.update(kart, fx, 1 / 60, 16 / 9, null, { x: .7, y: -.5 }); b.update(kart, fx, 1 / 60, 16 / 9); }
  assert.deepEqual(a.eye, b.eye); assert.notDeepEqual(a.look, b.look);
  const held = [...a.look]; a.update(kart, fx, 0, 16 / 9, null, { x: -1, y: 1 }); a.look.forEach((v, i) => near(v, held[i], 1e-12));
  for (let i = 0; i < 240; i++) a.update(kart, fx, 1 / 60, 16 / 9);
  a.look.forEach((v, i) => near(v, b.look[i]));
  a.snap(kart); assert.deepEqual(a.glance.view, { yaw: 0, pitch: 0 });
});

const buttons = () => Array.from({ length: 17 }, () => ({ pressed: false, value: 0 }));
test('right stick is independent of steering, menu navigation and Generic A trigger axes', () => {
  const g = { id: 'pad', index: 0, connected: true, mapping: 'standard', axes: [0, 0, .8, -.5], buttons: buttons() };
  const pad = createGamepad({ nav: { getGamepads: () => [g] }, devices: createDevices() });
  const map = createActionMap([pad]), a = map.sample();
  assert.equal(a.steer, 0); assert.equal(a.throttle, 0); assert.equal(a.hop, false); assert.equal(pad.takeNav(), null);
  assert.ok(pad.look.x > 0 && pad.look.y < 0);
  g.axes = [0, 0, .08, -.06]; map.sample(); assert.deepEqual(pad.look, { x: 0, y: 0 });
  g.connected = false; map.sample(); assert.deepEqual(pad.look, { x: 0, y: 0 });
  const raw = readPad({ axes: [0, 0, 1, -.7, .6, -1], buttons: buttons() }, 'xinput');
  assert.equal(raw.lookX, -.7); assert.equal(raw.lookY, .6); assert.equal(raw.lt, 1); assert.equal(raw.rt, 0);
  map.dispose();
});

function target() {
  const handlers = new Map();
  return { clientWidth: 1280, clientHeight: 720,
    addEventListener(k, f) { if (!handlers.has(k)) handlers.set(k, new Set()); handlers.get(k).add(f); },
    removeEventListener(k, f) { handlers.get(k)?.delete(f); },
    fire(k, values = {}) { const e = { pointerType: 'mouse', pointerId: 4, clientX: 100, clientY: 100, button: 2, buttons: 2, preventDefault() { this.prevented = true; }, ...values }; for (const f of handlers.get(k) || []) f(e); return e; },
    listeners() { return [...handlers.values()].reduce((n, h) => n + h.size, 0); },
  };
}
test('mouse glance is deliberate, bounded, ignores touch and cancels on release, blur, cards and disposal', () => {
  const el = target(), win = target(); let enabled = true;
  const pointer = createPointerLook({ element: el, target: win, enabled: () => enabled, devices: createDevices() });
  el.fire('pointerdown', { pointerType: 'touch' }); win.fire('pointermove', { clientX: 600 }); assert.equal(pointer.read().x, 0);
  el.fire('pointerdown', { button: 0 }); win.fire('pointermove', { clientX: 600 }); assert.equal(pointer.read().x, 0);
  const drag = () => { el.fire('pointerdown'); win.fire('pointermove', { clientX: 1000, clientY: -1000 }); assert.deepEqual(pointer.read(), { x: 1, y: -1 }); };
  drag(); win.fire('pointerup'); assert.deepEqual(pointer.read(), { x: 0, y: 0 });
  drag(); win.fire('blur'); assert.deepEqual(pointer.read(), { x: 0, y: 0 });
  drag(); enabled = false; assert.deepEqual(pointer.read(), { x: 0, y: 0 });
  assert.ok(!el.fire('contextmenu').prevented, 'menus keep their normal mouse behaviour');
  enabled = true; assert.ok(el.fire('contextmenu').prevented);
  pointer.dispose(); assert.equal(el.listeners() + win.listeners(), 0);
});
