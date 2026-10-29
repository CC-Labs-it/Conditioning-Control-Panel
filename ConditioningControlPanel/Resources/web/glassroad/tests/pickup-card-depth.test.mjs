import test from 'node:test';
import assert from 'node:assert/strict';
import { registerHooks } from 'node:module';
const hooks = registerHooks({ resolve(s, c, next) {
  const names = {'three/webgpu':'three.webgpu.min.js','three/tsl':'three.tsl.min.js'};
  return names[s] ? {url:new URL('../vendor/three/build/'+names[s],import.meta.url).href,shortCircuit:true}:next(s,c);
} });
const {pickupCardEdgeGeometry,PICKUP_CARD}=await import('../render/items/pickupCards.js');
const {Mesh,MeshBasicNodeMaterial,Raycaster,Vector3,DoubleSide}=await import('three/webgpu');
hooks.deregister();
test('card edge retains solid rim but submits no hidden face beneath either artwork plane',()=>{
  const geometry=pickupCardEdgeGeometry(),material=new MeshBasicNodeMaterial({side:DoubleSide}),mesh=new Mesh(geometry,material);
  const ray=new Raycaster(); mesh.updateMatrixWorld();
  for(const distance of [2,100,1000])for(const side of [-1,1]){
    ray.set(new Vector3(0,PICKUP_CARD.height/2,side*distance),new Vector3(0,0,-side));
    assert.equal(ray.intersectObject(mesh).length,0,'no competing face at '+distance);
  }
  ray.set(new Vector3(2,PICKUP_CARD.height/2,0),new Vector3(-1,0,0));
  assert.ok(ray.intersectObject(mesh).length>0,'physical gold rim remains');
  geometry.dispose();material.dispose();
});
