import test from 'node:test';
import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
import {readFileSync} from 'node:fs';
const hooks=registerHooks({resolve(s,c,next){const files={'three/webgpu':'three.webgpu.min.js','three/tsl':'three.tsl.min.js'};
  return files[s]?{url:new URL('../vendor/three/build/'+files[s],import.meta.url).href,shortCircuit:true}:next(s,c);}});
const {createSwimPropeller,createPropellerFleet}=await import('../render/swimPropeller.js');
const {Group,Scene}=await import('three/webgpu');hooks.deregister();

test('perf: every kart drive draws through one instanced mesh per part, placeholders on no layer',()=>{
  const scene=new Scene(),fleet=createPropellerFleet(8),bodies=[],drives=[];
  scene.add(fleet.group);
  for(let i=0;i<8;i++){const root=new Group(),body=new Group();root.position.set(i*10,0,-i*3);root.add(body);scene.add(root);bodies.push(root);drives.push(createSwimPropeller(body,null,fleet));}
  assert.equal(fleet.group.children.length,12,'twelve parts, one draw each for all karts');
  assert.ok(fleet.group.children.every(m=>m.isInstancedMesh&&m.frustumCulled===false));
  const placeholders=[];for(const r of bodies)r.traverse(o=>{if(o.isMesh)placeholders.push(o);});
  assert.equal(placeholders.length,96);assert.ok(placeholders.every(o=>o.layers.mask===0),'no camera ever draws a placeholder');
  // shadows.js castFrom marks the karts' trees: the fleet casts when they do
  assert.ok(fleet.group.children.every(m=>!m.castShadow));
  for(const o of placeholders)o.castShadow=true;
  assert.ok(fleet.group.children.every(m=>m.castShadow));
  for(const d of drives)d.update(1,.5);
  bodies[3].visible=false;scene.updateMatrixWorld(true);
  const hub=fleet.group.getObjectByName('rotor-hub');hub.onBeforeRender();
  assert.equal(hub.count,7,'a hidden kart drops out of the instances');
  const shown=[];for(const r of bodies)if(r.visible)r.traverse(o=>{if(o.name==='rotor-hub')shown.push(o);});
  shown.forEach((o,i)=>{for(let j=0;j<16;j++)assert.ok(Math.abs(hub.instanceMatrix.array[i*16+j]-o.matrixWorld.elements[j])<1e-4);});
  drives[0].dispose();bodies[3].visible=true;hub.onBeforeRender();assert.equal(hub.count,7,'a disposed drive leaves the fleet');
  for(const d of drives.slice(1))d.dispose();fleet.dispose();assert.equal(fleet.group.parent,null);
});

test('perf: karts and the world wire the fleet; collectible cards keep frustum culling',()=>{
  const karts=readFileSync(new URL('../render/karts.js',import.meta.url),'utf8');
  assert.match(karts,/createPropellerFleet\(count\)/);assert.match(karts,/createSwimPropeller\([^;]+,\s*propellers\)/);
  assert.match(karts,/propellers\.dispose\(\)/);
  const scene=readFileSync(new URL('../render/scene.js',import.meta.url),'utf8');
  assert.match(scene,/karts\.propellers\.group/);
  const cards=readFileSync(new URL('../render/items/pickupCards.js',import.meta.url),'utf8');
  assert.doesNotMatch(cards,/frustumCulled\s*=\s*false/);
});
