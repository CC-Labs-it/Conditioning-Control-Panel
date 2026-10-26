import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {createWaterField} from '../render/waterField.js';
import {loadTrack} from '../track/load.js';
const fixture=()=>({lengthM:120,nodeS:[0,30,40,70,80,120],sections:[],frameAt(s){return {w:20,n:[0,1,0],surface:s<30||s>=40&&s<70?'stream':'glass',flags:[]};}});
const energy=f=>{let e=0;for(let s=0;s<30;s+=.5)for(let x=-9;x<=9;x+=.5)e+=f.sample(s,x).height**2;return e;};
test('field atlas has full-width distinct runs and isolated texel-centre UVs',()=>{
  const f=createWaterField(fixture());assert.equal(f.runs.length,2);assert.equal(f.width,64);
  for(const r of f.runs){assert.equal(r.nx,64);assert.ok(r.widths.every(w=>Math.abs(w-19.92)<1e-5));
    const left=f.atlasUV(r.fromS,-r.widths[0]/2),right=f.atlasUV(r.fromS,r.widths[0]/2);
    assert.ok(Math.abs(left.u-.5/64)<1e-7);assert.ok(Math.abs(right.u-63.5/64)<1e-7);
    assert.ok(Math.abs(left.v-(r.rowOffset+.5)/f.height)<1e-7);
  }
  assert.equal(f.atlasUV(35,0).wet,false);assert.equal(f.sample(10,11).wet,false);
  assert.equal(f.disturb({s:35,x:0}),false);assert.equal(f.stats.disturbances,0);
});
test('a contact depresses its core and displaces a raised rim with local volume balance',()=>{
  const f=createWaterField(fixture()),r=f.runs[0],s=r.fromS+r.ds*10,x=r.widths[10]*(32/63-.5);
  assert.ok(f.disturb({s,x,radius:3,strength:.3}));assert.ok(f.sample(s,x).height<-.29);
  let positive=0,negative=0,total=0;
  for(let row=0;row<r.ny;row++)for(let col=0;col<r.nx;col++){
    const h=f.sample(r.fromS+row*r.ds,(col/63-.5)*r.widths[row]).height;
    positive=Math.max(positive,h);negative=Math.min(negative,h);total+=h;
  }
  assert.ok(positive>.02);assert.ok(negative<-.29);assert.ok(Math.abs(total)<1e-5);
  assert.equal(f.sample(s+6,x).height,0,'compact footprint');
  assert.ok(f.sample(s,x+1).slopeX>0,'normal responds before the next texture upload');
  f.step(0);assert.ok(f.sample(s,x+1).slopeX>0);assert.ok(f.sample(s,x+2).foam>0);
});
test('signed disturbances invert the displaced surface and directional contact strengthens bow',()=>{
  const a=createWaterField(fixture()),b=createWaterField(fixture());
  a.disturb({s:15,x:0,radius:4,strength:.3,dirS:1});b.disturb({s:15,x:0,radius:4,strength:-.3,dirS:1});
  assert.ok(a.sample(18,0).height>a.sample(12,0).height*1.5);
  for(const s of [12,15,18])assert.ok(Math.abs(a.sample(s,0).height+b.sample(s,0).height)<1e-6);
});
test('waves persist, propagate after contact leaves, decay and never reach an unrelated overpass run',()=>{
  const f=createWaterField(fixture());f.disturb({s:15,x:0,radius:2,strength:.4});f.step(0);
  const start=energy(f);assert.ok(start>0);assert.equal(f.sample(15,4).height,0);
  for(let i=0;i<90;i++)f.step(1/60);
  assert.ok(Math.abs(f.sample(15,4).height)>.0001,'wavefront travels beyond injection');
  assert.ok(energy(f)>0,'surface retains a wake after kart leaves');
  assert.equal(f.sample(55,0).height,0);assert.equal(f.sample(55,0).foam,0);
  for(let i=0;i<1200;i++)f.step(1/60);
  assert.ok(energy(f)<start*.003,'damped water settles');assert.ok(f.stats.maxHeight<.002);
});
test('fixed steps are partition-independent, pause freezes bytes, Gentle slows instead of clearing',()=>{
  const a=createWaterField(fixture()),b=createWaterField(fixture()),g=createWaterField(fixture());
  for(const f of [a,b,g])f.disturb({s:15,x:0});
  for(let i=0;i<60;i++)a.step(1/60);for(let i=0;i<30;i++)b.step(1/30);
  assert.deepEqual(a.pixels,b.pixels);
  for(let i=0;i<120;i++)g.step(1/60,{gentle:true});assert.deepEqual(g.pixels,a.pixels);
  const saved=a.pixels.slice(),steps=a.stats.steps,version=a.stats.version;
  assert.equal(a.step(4,{paused:true}),false);assert.deepEqual(a.pixels,saved);assert.equal(a.stats.steps,steps);assert.equal(a.stats.version,version);
});
test('large frame gaps, repeated contacts, invalid inputs and restart remain finite and bounded',()=>{
  const f=createWaterField(fixture(),{quality:'phone'});assert.equal(f.width,32);
  for(let i=0;i<100;i++){f.disturb({s:15,x:0,strength:20,radius:3});f.step(.1);}
  assert.ok(f.stats.maxHeight<=.800001);assert.ok(Number.isFinite(f.stats.energy));
  const previous=f.stats.steps;f.step(400);assert.ok(f.stats.steps-previous<=6);assert.ok(f.stats.droppedTimeS>399);
  assert.equal(f.disturb({s:NaN,x:0}),false);assert.equal(f.disturb({s:10,x:Infinity}),false);
  f.step(Infinity);f.reset();assert.equal(f.stats.maxHeight,0);assert.equal(f.stats.energy,0);assert.equal(f.stats.steps,0);
  for(let i=0;i<f.pixels.length;i+=4)assert.deepEqual(Array.from(f.pixels.slice(i,i+4)),[128,128,128,0]);
});
for(const id of ['threshold','keep','mainframe'])test(id+' builds a bounded full-width atlas from the real course',()=>{
  const tr=loadTrack(JSON.parse(readFileSync(new URL('../tracks/'+id+'.json',import.meta.url)))),f=createWaterField(tr);
  assert.ok(f.runs.length>0);assert.ok(f.height<=2050);assert.ok(f.stats.cells<64*2050);
  if(id==='mainframe'){
    const canal=tr.sections.find(s=>s.id==='coolant-canal'),s=(canal.fromS+canal.toS)/2;
    assert.equal(f.sample(s,14).wet,true,'curved bank is wet, not just shallow centre');
    assert.ok(f.disturb({s,x:14,radius:2}));f.step(1/60);assert.ok(f.sample(s,14).height<0);
  }
});
test('dry course produces one neutral row with no solver work',()=>{
  const tr=fixture();tr.frameAt=()=>({w:20,n:[0,1,0],surface:'glass',flags:[]});const f=createWaterField(tr);
  assert.equal(f.runs.length,0);assert.equal(f.height,1);assert.equal(f.sample(20,0).wet,false);
  assert.equal(f.disturb({s:20,x:0}),false);f.step(.1);assert.equal(f.stats.cells,0);
});
test('idle fields sleep, disturbances wake them and reset publishes neutral pixels once',()=>{
  const f=createWaterField(fixture()),initial=f.stats.version;
  for(let i=0;i<120;i++)assert.equal(f.step(1/60),false);
  assert.equal(f.stats.steps,0);assert.equal(f.stats.version,initial);
  f.disturb({s:15,x:0});assert.equal(f.step(0),true);assert.ok(f.stats.version>initial);
  for(let i=0;i<2400;i++)f.step(1/60);
  const settled=f.stats.version,steps=f.stats.steps;
  assert.equal(f.step(1/60),false);assert.equal(f.stats.version,settled);assert.equal(f.stats.steps,steps);
  assert.equal(f.stats.maxHeight,0);assert.equal(f.sample(15,0).foam,0);
  f.disturb({s:15,x:0});f.step(1/60);const activeVersion=f.stats.version;
  f.reset();assert.ok(f.stats.version>activeVersion);assert.equal(f.sample(15,0).height,0);
  assert.equal(f.step(1/60),false);
});
test('table-driven solver keeps the reference bytes on the real course (pinned before the perf pass)',()=>{
  const tr=loadTrack(JSON.parse(readFileSync(new URL('../tracks/threshold.json',import.meta.url))),{moving:true}),f=createWaterField(tr,{quality:'phone'});
  let sum=0;
  for(let k=0;k<300;k++){
    f.runs.forEach((r,j)=>{if((k+j)%2===0)f.disturb({s:r.fromS+((k*7.3+j*11)%Math.max(1,r.toS-r.fromS)),x:((k%5)-2)*.4,strength:.3,dirX:1,dirS:.5});});
    f.step(1/60*(1+k%3));const q=f.sample(f.runs[0].fromS+3,.2);sum+=q.height+q.slopeX+q.slopeS+q.foam;
  }
  let h=0;for(const v of f.pixels)h=(h*31+v)>>>0;
  assert.equal(h,1647520444);assert.equal(sum,30.647941466131503);assert.equal(f.stats.activeCells,15785);
});
test('a settled run rests on its own while another run keeps moving',()=>{
  const f=createWaterField(fixture()),[a,b]=f.runs;
  f.disturb({s:15,x:0,radius:3,strength:.4});
  for(let i=0;i<1500;i++){f.disturb({s:60,x:0,radius:2,strength:.3});f.step(1/60);}
  const neutral=(r)=>{for(let p=(r.rowOffset-1)*f.width*4;p<(r.rowOffset+r.ny+1)*f.width*4;p+=4)
    if(f.pixels[p]!==128||f.pixels[p+1]!==128||f.pixels[p+2]!==128||f.pixels[p+3]!==0)return false;return true;};
  assert.equal(a.live,false);assert.ok(neutral(a),'quiet run packs neutral texels');assert.equal(f.sample(15,0).height,0);
  assert.equal(b.live,true);assert.ok(!neutral(b));assert.ok(f.stats.activeCells>0);
  f.disturb({s:15,x:0,radius:3,strength:.4});assert.equal(a.live,true);f.step(1/60);assert.ok(!neutral(a),'a contact wakes it');
});
