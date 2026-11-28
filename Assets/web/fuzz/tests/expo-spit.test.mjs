import test from 'node:test';
import assert from 'node:assert/strict';
import {spitVelocity} from '../ui/expo/spit.js';
import {atRematchStart,flagDistances,rangeOrigin,rematchHint} from '../ui/expo/rematch.js';

test('the rematch hint follows the actual judge and three flags, including the far plate',()=>{
  const [x,y,z]=rangeOrigin,judge={x,y,z};
  assert.ok(atRematchStart(judge));
  assert.equal(rematchHint('commons',judge,'keyboard'),'Hold Q and release to spit.');
  for(const distance of flagDistances){
    const flag={x:x-.46,y,z:z-distance};
    assert.equal(atRematchStart(flag),false);
    assert.equal(rematchHint('commons',flag,'gamepad'),'From the judge, hold RT / R2 and release to spit.');
    assert.equal(rematchHint('commons',flag,'mouse'),'From the judge, hold Q and release to spit.');
  }
  for(const p of [{x:x+5,y,z:z-3},{x,y,z:z+4},{x,y,z:z-10.5},{x,y:3,z}]){
    assert.equal(rematchHint('commons',p,'keyboard'),'');
  }
  assert.equal(rematchHint('hall',judge,'touch'),'');
});

test('the mobile nudge tells the visitor where Spit is when carrying offers a different action',()=>{
  const p={x:-66.74,y:0,z:90.6};
  assert.equal(rematchHint('commons',p,'touch',false),'From the judge, hold Spit, then release.');
  assert.equal(rematchHint('commons',p,'touch',true),'From the judge, open Actions, hold Spit, then release.');
  assert.equal(rematchHint('commons',{...p,z:94},'touch',true),'Open Actions, hold Spit, then release.');
});

test('the judge can measure a charged human throw near three metres, and hooves double its reach',()=>{
  const groundRange=v=>v.along*(v.up+Math.sqrt(v.up**2+2*9.81*1.525))/9.81;
  const distances=Array.from({length:100},(_,i)=>groundRange(spitVelocity(1,i/100-.29,false)));
  const best=Math.max(...distances);
  assert.ok(best>=2.95&&best<=3.15);
  const human=groundRange(spitVelocity(1,.31,false)),hoof=groundRange(spitVelocity(1,.31,true));
  assert.equal(hoof,2*human);assert.ok(human>groundRange(spitVelocity(.1,.31,false)));
  assert.ok(spitVelocity(.1,-1.40,false).up<0);
});
