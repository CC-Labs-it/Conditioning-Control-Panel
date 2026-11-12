import test from 'node:test';
import assert from 'node:assert/strict';
import { addImpulse, IMPULSE_LIFE, waveEnergy } from '../ui/kinetic-waves.js';

test('tap cadence builds energy and stronger impulses, then decays to rest', () => {
  const isolated=[], rapid=[];
  addImpulse(isolated,1,.5,()=>.5);
  for(let i=0;i<6;i++)addImpulse(rapid,1+i*.10,.5,()=>.5);
  assert.ok(waveEnergy(rapid,1.5)>2*waveEnergy(isolated,1));
  assert.ok(rapid.at(-1).power>rapid[0].power);
  assert.ok(waveEnergy(rapid,2.5)<waveEnergy(rapid,1.5));
  assert.equal(waveEnergy(rapid,1.5+IMPULSE_LIFE+.001),0);
});

test('each contact samples its own throw once and rapid tapping stays bounded', () => {
  const hits=[];
  addImpulse(hits,0,.2,()=>.22);
  const first={...hits[0]};
  addImpulse(hits,.08,.2,()=>.83);
  assert.notEqual(hits[0].polarity,hits[1].polarity);
  assert.notEqual(hits[0].speed,hits[1].speed);
  assert.notEqual(hits[0].seed,hits[1].seed);
  waveEnergy(hits,.45);
  assert.deepEqual(hits[0],first,'sampling a frame cannot reroll the event');
  for(let i=1;i<500;i++) addImpulse(hits,1+i*.005,1.5,()=>.57);
  assert.equal(hits.length,16);
  assert.ok(waveEnergy(hits,3.5)<=1);
  assert.equal(hits.at(-1).u,1);
  addImpulse(hits,10,-2,()=>.5);
  assert.equal(hits.length,1);assert.equal(hits[0].u,0);
});
