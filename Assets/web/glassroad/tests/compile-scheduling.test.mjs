import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {runInNewContext} from 'node:vm';

const core=readFileSync(new URL('../vendor/three/build/three.core.js',import.meta.url),'utf8');
const start=core.indexOf('let yieldDeadline=0;');
const end=core.indexOf('function probeAsync',start);
assert.ok(start>=0&&end>start,'the pinned r186 scheduling helper is present');
const source=core.slice(start,end)+';yieldToMain';

test('shader preparation shares one short work budget instead of scheduling every small step',async()=>{
  let time=100,yields=0;
  const next=runInNewContext(source,{performance:{now:()=>time},self:{scheduler:{yield:async()=>{yields++;time+=20;}}}});
  for(let step=0;step<100;step++){await next();time+=.02;}
  assert.equal(yields,1,'small steps share the budget after resuming');
  time+=9;await next();
  assert.equal(yields,2,'work gives the main thread another turn once the budget expires');
});

test('Firefox fallback yields a frame and starts its budget after the frame resumes',async()=>{
  let time=100,frames=0,resume;
  const next=runInNewContext(source,{performance:{now:()=>time},self:{},requestAnimationFrame:fn=>{frames++;resume=fn;}});
  const first=next();time+=50;resume();await first;
  for(let step=0;step<20;step++){time+=.1;await next();}
  assert.equal(frames,1,'waiting for the frame does not consume the next work budget');
  time+=9;const last=next();assert.equal(frames,2);resume();await last;
});
