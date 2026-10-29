import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createGateScreen } from '../render/gateScreen.js';

function gate() {
  const body = { clientWidth: 1280, clientHeight: 720, appendChild() {} };
  const doc = { body, hidden: false, createElement: () => ({ style: {}, setAttribute() {}, addEventListener() {} }) };
  return createGateScreen(body, { doc, makeField: () => ({ render() {} }) });
}
const screen = (kind, gentle = false, prevKind = null, prevK = 0) => ({ kind, k: 1, strength: 1, prevKind, prevK, params: { gentle, seed: 1 } });

test('a passing gate cannot brighten blackout; reset restores its continuing envelope', () => {
  const s = gate();
  s.play({}); s.update(0.7, false, screen('blackout'));
  assert.equal(s.level, 0);
  assert.equal(s.canvas.style.opacity, '0.000');
  s.update(0.1, false, null);
  assert.equal(s.level, 1, 'no stale attenuation after reset');
  s.update(0.1, false, screen('melt', false, 'blackout', 0.5));
  assert.equal(s.level, 0.5, 'replaced blackout fades continuously');
});

test('fog ingress keeps progressing with no active gate and attenuates later gate takeovers', () => {
  const s = gate(), p = screen('pink');
  s.update(4, false, p);
  s.play({}); s.update(0.7, false, p);
  assert.ok(s.level <= 0.051, 'settled fog covers a new gate overlay');
  const opacity = s.canvas.style.opacity;
  s.update(10, true, null);
  assert.equal(s.canvas.style.opacity, opacity, 'pause freezes presentation');
});

test('Gentle retains the softer screen amount and absent DOM remains supported', () => {
  const s = gate();
  s.play({}); s.update(0.7, false, screen('blackout', true));
  assert.equal(s.level, 0.5, 'Gentle blackout only halves gate visibility');
  assert.equal(createGateScreen(null, { doc: null }).update(1, false, null), undefined);
});
