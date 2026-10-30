import test from 'node:test';
import assert from 'node:assert/strict';
import { createCoinCount, COIN_HUD } from '../hud/wordCount.js';

function fixture() {
  const calls = [], animations = [], number = {}, listeners = new Map();
  const element = { style: {}, classList: { toggle() {} }, setAttribute() {}, querySelector: () => number, remove() {},
    animate(frames, options) { const animation = { playState: 'running', cancel() { this.playState = 'idle'; } };
      calls.push({ frames, options }); animations.push(animation); return animation; } };
  const doc = { getElementById: () => true, createElement: () => element };
  const bus = { on(name, callback) { listeners.set(name, callback); return () => listeners.delete(name); } };
  const dispose = createCoinCount({ appendChild() {} }, bus, { doc, gentle: () => true });
  const emit = delta => listeners.get('word-coins')({ count: 3, cap: 10, show: true, delta });
  return { emit, calls, animations, dispose };
}

test('actual Gentle HUD animation keeps both brightness and opacity legs at least 300 ms', () => {
  for (const delta of [1, -1]) {
    const { emit, calls, dispose } = fixture();
    emit(delta);
    const { frames, options } = calls[0];
    assert.equal(options.easing, 'linear', 'global acceleration must not shorten the first leg');
    assert.ok(options.duration >= (delta > 0 ? COIN_HUD.gainMs : COIN_HUD.lossMs));
    const peak = frames[1].offset;
    assert.ok(options.duration * peak >= 300, 'minimum time to the brightness or opacity peak');
    assert.ok(options.duration * (1 - peak) >= 300, 'minimum return time');
    dispose();
  }
});

test('dense Gentle pickups cannot repeatedly restart the brightness rise', () => {
  const { emit, calls, animations, dispose } = fixture();
  emit(1); emit(1); emit(-1);
  assert.equal(calls.length, 1, 'updates keep the current soft envelope');
  animations[0].playState = 'finished'; emit(1);
  assert.equal(calls.length, 2, 'the next completed envelope may start normally');
  dispose(); assert.equal(animations[1].playState, 'idle');
});
