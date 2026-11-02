import {test} from 'node:test';
import assert from 'node:assert/strict';
import {warmGarageWardrobe} from '../render/garageWarm.js';

test('garage warms all variants together, measures their union once and restores only after compilation', async () => {
  const active=new Set(),events=[],bounds={min:[-1,-2,-3],max:[3,4,5]};
  let finish;
  const task=warmGarageWardrobe({
    outfits:['coat','hat','swim'],
    attach(id){active.add(id);events.push('attach:'+id);return{dispose(){active.delete(id);events.push('dispose:'+id);}};},
    measure(){assert.equal(active.size,3);events.push('measure');return bounds;},
    compile(){assert.equal(active.size,3);events.push('compile');return new Promise(resolve=>{finish=resolve;});},
    restore(){assert.equal(active.size,0);events.push('restore');},
  });
  assert.deepEqual(events,['attach:coat','attach:hat','attach:swim','measure','compile']);
  finish();assert.equal(await task,bounds);
  assert.deepEqual(events.slice(-4),['dispose:coat','dispose:hat','dispose:swim','restore']);
});

test('failed garage preparation frees every attached binding and restores the accepted look', async () => {
  for(const failure of ['attach','measure','compile']) {
    const active=new Set();let restored=0;
    await assert.rejects(warmGarageWardrobe({
      outfits:['one','two'],
      attach(id){if(failure==='attach'&&id==='two')throw Error(failure);active.add(id);return{dispose(){active.delete(id);}};},
      measure(){if(failure==='measure')throw Error(failure);return {};},
      async compile(){if(failure==='compile')throw Error(failure);},
      restore(){restored++;},
    }),new RegExp(failure));
    assert.equal(active.size,0,failure);assert.equal(restored,1,failure);
  }
});

test('one failed wardrobe disposal cannot strand later bindings or prevent appearance restoration', async () => {
  for (const compileFailure of [false, true]) {
    const disposed=[],failure=Error(compileFailure?'compile failed':'dispose failed');let restored=0;
    await assert.rejects(warmGarageWardrobe({
      outfits:['one','two','three'],
      attach:id=>({dispose(){disposed.push(id);if(id==='one')throw compileFailure?Error('cleanup failed'):failure;}}),
      measure:()=>({}),async compile(){if(compileFailure)throw failure;},restore(){restored++;},
    }),error=>error===failure);
    assert.deepEqual(disposed,['one','two','three']);assert.equal(restored,1);
  }
});
