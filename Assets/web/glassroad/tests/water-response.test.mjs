import { test } from 'node:test';
import assert from 'node:assert/strict';
import { waterResponse } from '../render/waterResponse.js';

test('fast travel sheds denser, taller spray more often than slow travel', () => {
  const slow=waterResponse({speed:3}),fast=waterResponse({speed:36});
  assert.ok(fast.count>slow.count*2 && fast.interval<slow.interval/2);
  assert.ok(fast.strength>slow.strength*2 && fast.lift>slow.lift);
});
test('drift sprays toward the displaced side; reversing drift reverses that pressure', () => {
  const right=waterResponse({speed:25,lateral:7,steer:.8,drifting:true});
  const left=waterResponse({speed:25,lateral:-7,steer:-.8,drifting:true});
  assert.ok(right.pressure(1)>right.pressure(-1));
  assert.equal(right.pressure(1),left.pressure(-1));
  assert.ok(right.count>waterResponse({speed:25}).count);
});
test('braking displaces more water, with the same response at different frame rates', () => {
  const a=waterResponse({speed:20,previousSpeed:20.2,dt:1/60});
  const b=waterResponse({speed:20,previousSpeed:20.4,dt:1/30});
  assert.ok(Math.abs(a.brake-b.brake)<1e-12);
  assert.ok(a.strength>waterResponse({speed:20}).strength);
});
