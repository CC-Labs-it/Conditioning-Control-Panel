import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { WATER_VOLUME } from '../render/waterMath.js';
const hooks = registerHooks({ resolve(specifier, context, next) {
  const name = { 'three/tsl': 'three.tsl.min.js', three: 'three.webgpu.min.js', 'three/webgpu': 'three.webgpu.min.js' }[specifier];
  return name ? { url: new URL('../vendor/three/build/' + name, import.meta.url).href, shortCircuit: true } : next(specifier,context);
} });
const { createDecalSampler, clearWordLane } = await import('../render/surfaceDecals.js');
const { ambientWaterHeight } = await import('../render/waterMotion.js');
hooks.deregister();

test('words and status underlays follow curved canal support and the actual water clock', () => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url), 'utf8')));
  const canal = track.sections.find(s => s.id === 'coolant-canal');
  assert.ok(canal);
  const water = { field: { sample(s,x,out) { return Object.assign(out,{wet:true,height:.37}); } }, uniforms: {t:{value:7}} };
  const sample = createDecalSampler(track,water), s = (canal.fromS + canal.toS) / 2;
  for (const x of [-7,-3,0,3,7]) {
    const base = track.surfaceAt(s,x), decal = sample(s,x);
    const h = WATER_VOLUME.liftM + .37 + ambientWaterHeight(base.p,7);
    for (let k=0;k<3;k++) assert.ok(Math.abs(decal.p[k]-base.p[k]-base.n[k]*h)<1e-8);
  }
});

test('word lanes avoid boosts including lap wrap, while full-width pads keep a valid text position', () => {
  const track = {lengthM:100,frameAt:()=>({w:24}),boosts:[{s:97,len:7,x:0,w:4}]};
  const lane = clearWordLane(track,1,0,4,6);
  assert.ok(Math.abs(lane)>=4.4 && Math.abs(lane)<=9.55);
  assert.equal(clearWordLane(track,40,0,4,6),0);
  track.boosts[0].w=24;
  assert.ok(Number.isFinite(clearWordLane(track,1,0,4,6)));
});

test('every other boost has a conforming art footprint on every course, without changing physics', () => {
  for (const id of ['threshold','mainframe','keep','boudoir','goonworks']) {
    const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/'+id+'.json',import.meta.url),'utf8')));
    const mesh = track.meshes.pads, found = new Map();
    assert.equal(mesh.padDesign.length,mesh.positions.length/3*4);
    for (let v=0;v<mesh.padDesign.length;v+=4) if(mesh.padDesign[v]<0)found.set(mesh.padDesign[v+2]+':'+mesh.padDesign[v+3],[mesh.padDesign[v+2],mesh.padDesign[v+3]]);
    const expected=track.boosts.filter((_,i)=>i%2===0);
    assert.equal(found.size,expected.length,id);
    for(const p of expected)assert.ok([...found.values()].some(([s,x])=>Math.abs(s-p.s)<.002 && Math.abs(x-p.x)<.002),id+' float32 footprint');
  }
});
