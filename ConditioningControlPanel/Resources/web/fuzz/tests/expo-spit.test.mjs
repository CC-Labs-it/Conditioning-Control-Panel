import test from 'node:test';
import assert from 'node:assert/strict';
import {spitVelocity} from '../ui/expo/spit.js';

test('the judge can measure a charged human throw near three metres, and hooves double its reach',()=>{
  const groundRange=v=>v.along*(v.up+Math.sqrt(v.up**2+2*9.81*1.525))/9.81;
  const distances=Array.from({length:100},(_,i)=>groundRange(spitVelocity(1,i/100-.29,false)));
  const best=Math.max(...distances);
  assert.ok(best>=2.95&&best<=3.15);
  const human=groundRange(spitVelocity(1,.31,false)),hoof=groundRange(spitVelocity(1,.31,true));
  assert.equal(hoof,2*human);assert.ok(human>groundRange(spitVelocity(.1,.31,false)));
  assert.ok(spitVelocity(.1,-1.40,false).up<0);
});
