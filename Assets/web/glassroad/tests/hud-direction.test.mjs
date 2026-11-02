import test from 'node:test';
import assert from 'node:assert/strict';
import { createDirectionWarning } from '../hud/direction.js';
import { S, RESPAWN, createState, setVel } from '../sim/state.js';

function setup() {
  const st = createState(); st[S.grounded] = 1; setVel(st, [0, 0, -15]);
  const warning = createDirectionWarning({ frameAt: () => ({ t: [0, 0, 1] }) });
  const run = (seconds = 1) => { let visible; for (let i = 0; i < seconds * 60; i++) visible = warning.update(st, 1 / 60); return visible; };
  return { st, warning, run };
}
test('wrong way waits for sustained reverse travel and clears as soon as travel is forward', () => {
  const { st, warning, run } = setup();
  assert.equal(run(0.5), false);
  assert.equal(run(0.5), true);
  setVel(st, [0, 0, 15]);
  assert.equal(warning.update(st, 1 / 60), false);
  setVel(st, [0, 0, -15]);
  assert.equal(run(0.5), false, 'a new reversal needs a fresh grace period');
});
test('wrong way ignores stops, sideways skids, jumps, stalls, rescue and non-racing phases', () => {
  for (const configure of [
    st => setVel(st, [0, 0, -2]), st => setVel(st, [20, 0, -4]),
    st => { st[S.grounded] = 0; }, st => { st[S.stalled] = 1; },
    st => { st[S.respawnPhase] = RESPAWN.GRACE; },
  ]) { const { st, run } = setup(); configure(st); assert.equal(run(), false); }
  const { st, warning, run } = setup();
  assert.equal(run(), true);
  assert.equal(warning.update(st, 1, false), false);
  assert.equal(run(0.5), false, 'restart clears the old warning');
});
test('wrong way freezes its clock while paused and never counts a long frame as sustained travel', () => {
  const { st, warning, run } = setup();
  assert.equal(warning.update(st, 10), false);
  assert.equal(warning.update(st, 0), false);
  assert.equal(run(), true);
  assert.equal(warning.update(st, 0), true);
});
test('wrong way uses the physical fork tangent and supports inverted roads and the lap seam', () => {
  const st = createState(); st[S.grounded] = 1; st[S.seg] = 999; st[S.s] = 0;
  setVel(st, [-15, 0, 0]);
  const warning = createDirectionWarning({ frameAt() { assert.fail('mapped main road'); }, pathFrameAt(s, seg) {
    assert.equal(seg, 999); return { t: [1, 0, 0], n: [0, -1, 0] };
  } });
  let visible;
  for (let i = 0; i < 60; i++) { st[S.s] = i % 2 ? 0 : 9999; visible = warning.update(st, 1 / 60); }
  assert.equal(visible, true);
});
