import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createImmersion } from '../track/immersion.js';
import { loadTrack } from '../track/load.js';

test('immersion follows physical depth inside the reservoir and excludes crossing road levels', () => {
  const at=createImmersion({lengthM:1000,reservoir:{waterY:4},sections:[{id:'submerged',fromS:100,toS:200},{id:'emerge',fromS:200,toS:250}]});
  assert.equal(at(150,1),1);assert.equal(at(150,6),0);assert.equal(at(400,1),0);
  assert.equal(at(1150,1),1);assert.equal(at(100,1),0);assert.equal(at(250,1),0);
  assert.ok(at(150,3.4)>0&&at(150,3.4)<1);
  assert.ok(at(102,1)>0&&at(102,1)<1);
  assert.equal(at(NaN,0),0);assert.equal(at(150,NaN),0);
  assert.equal(createImmersion({sections:[]})(10,-50),0);
});

test('Mainframe enters and leaves water continuously along its actual reservoir route', () => {
  const track=loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json',import.meta.url))));
  const at=createImmersion(track), section=track.sections.find(s=>s.id==='submerged'),exit=track.sections.find(s=>s.id==='emerge');
  let max=0,last=0,largestStep=0;
  for(let s=section.fromS-2;s<exit.toS+2;s+=.2){const wet=at(s,track.frameAt(s).p[1]+.65);max=Math.max(max,wet);largestStep=Math.max(largestStep,Math.abs(wet-last));last=wet;}
  assert.equal(max,1);assert.equal(last,0);assert.ok(largestStep<.1);
  const canal=track.sections.find(s=>s.id==='coolant-canal');assert.equal(at(canal.fromS+20,-100),0);
});
