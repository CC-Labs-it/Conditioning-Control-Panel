import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
import { evaluateColour } from './floor-colour-eval.mjs';
const hooks=registerHooks({resolve(s,c,n){
  if(s.startsWith('three/addons/'))return{url:new URL('../vendor/three/addons/'+s.slice(13),import.meta.url).href,shortCircuit:true};
  const files={three:'three.webgpu.min.js','three/webgpu':'three.webgpu.min.js','three/tsl':'three.tsl.min.js'};
  return files[s]?{url:new URL('../vendor/three/build/'+files[s],import.meta.url).href,shortCircuit:true}:n(s,c);
}});
const {Vector2}=await import('three/webgpu');
const {uniform}=await import('three/tsl');
const {keepMusicSeam}=await import('../render/keepDressing.js');hooks.deregister();
test('Keep artwork relief cannot erase full-slab music, and the real mortar stays unpainted',()=>{
  const p=uniform(new Vector2()),seam=keepMusicSeam(p),sample=(x,y)=>{p.value.set(x,y);return evaluateColour(seam)[0]};
  for(const offset of [-24,-12,0,12])for(const [x,y]of[[1,4],[4,4],[8,0],[11,0]])
    for(const [dx,dy]of[[0,0],[.3,2],[-.3,-2]])assert.equal(sample(x+offset+dx,y+dy),0,'full physical tile interiors remain available');
  for(const x of[0,2,6,10,12])assert.equal(sample(x,2),1,'vertical mortar');
  for(const [x,y]of[[1,0],[4,8],[8,4],[11,12]])assert.equal(sample(x,y),1,'staggered horizontal mortar');
});
