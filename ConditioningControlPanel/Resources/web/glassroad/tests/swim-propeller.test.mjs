import test from 'node:test';
import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
import {readFileSync} from 'node:fs';
const hooks=registerHooks({resolve(s,c,next){const files={'three/webgpu':'three.webgpu.min.js','three/tsl':'three.tsl.min.js'};
  return files[s]?{url:new URL('../vendor/three/build/'+files[s],import.meta.url).href,shortCircuit:true}:next(s,c);}});
const {createSwimPropeller,createPropellerMotion,stepPropellerMotion}=await import('../render/swimPropeller.js');
const {Group,Mesh,BoxGeometry,MeshBasicNodeMaterial,Vector3}=await import('three/webgpu');hooks.deregister();

test('marine drive extends shaft, unfolds exactly three blades, then spins; exit reverses order',t=>{
  const p=createSwimPropeller(new Group());t.after(()=>p.dispose());
  assert.equal(p.hinges.length,3);assert.equal(p.motion.progress,0);
  p.update(1,.4);assert.ok(p.motion.extension>0);assert.equal(p.motion.unfold,0);assert.equal(p.motion.rpm,0);
  p.update(1,.4);assert.equal(p.motion.extension,1);assert.ok(p.motion.unfold>0);assert.equal(p.motion.rpm,0);
  p.update(1,.4);assert.equal(p.motion.unfold,1);assert.ok(p.motion.rpm>0);
  p.update(1,.4);assert.equal(p.motion.progress,1);assert.equal(p.motion.rpm,320);
  const rear=new Vector3();p.rotor.getWorldPosition(rear);assert.ok(rear.z>1.2,'rotor clears the rear chassis');
  p.update(0,.2);assert.ok(p.motion.rpm<320&&p.motion.rpm>0);assert.equal(p.motion.unfold,1);
  p.update(0,.25);assert.equal(p.motion.rpm,0);assert.ok(p.motion.unfold<1);assert.equal(p.motion.extension,1);
  p.update(0,.6);assert.equal(p.motion.unfold,0);assert.ok(p.motion.extension<1);
  p.update(0,2);assert.equal(p.motion.extension,0);assert.equal(p.motion.progress,0);
  assert.ok(p.hinges.every(h=>Math.abs(h.rotation.y-Math.PI/2)<1e-9));
});

test('waterline reversals preserve mechanical travel, paused freezes, Gentle halves motion',()=>{
  const m=createPropellerMotion();stepPropellerMotion(m,1,.7);
  const before={...m};stepPropellerMotion(m,0,.01);
  assert.ok(m.progress<before.progress&&before.progress-m.progress<.007);
  assert.ok(Math.abs(m.unfold-before.unfold)<.04,'no hinge pop at reversal');
  stepPropellerMotion(m,1,.01);assert.ok(m.progress>before.progress);
  const held={...m};stepPropellerMotion(m,0,10,{paused:true});assert.deepEqual(m,held);
  stepPropellerMotion(m,1,0);assert.deepEqual(m,held);
  const a=createPropellerMotion(),b=createPropellerMotion();stepPropellerMotion(a,1,.4);stepPropellerMotion(b,1,.4,{gentle:true});
  assert.equal(b.progress,a.progress/2);
  stepPropellerMotion(a,1,5);stepPropellerMotion(b,1,5,{gentle:true});assert.equal(b.rpm,a.rpm/2);
});

test('prebuilt real foils fit variant bounds, inherit kart scale, reset and dispose without source mutation',t=>{
  const body=new Group();body.scale.setScalar(2);
  const model=new Mesh(new BoxGeometry(1.8,1,2.1),new MeshBasicNodeMaterial());
  const sourcePositions=model.geometry.attributes.position.array.slice();
  const p=createSwimPropeller(body,model),other=createSwimPropeller(new Group());
  t.after(()=>{model.geometry.dispose();model.material.dispose();other.dispose();});
  assert.equal(p.object.parent,body);assert.ok(p.object.position.z>1);
  const meshes=[];p.object.traverse(o=>{if(o.isMesh)meshes.push(o);});
  assert.equal(meshes.filter(o=>o.name.startsWith('hydrofoil-')).length,3);
  assert.ok(meshes.every(o=>o.geometry.attributes.normal&&o.material.isNodeMaterial));
  assert.ok(p.hinges.every(h=>h.children[0].geometry.attributes.position.count>50),'curved solid foils, not cards');
  const geometries=new Set(meshes.map(m=>m.geometry)),materials=new Set(meshes.map(m=>m.material));
  const world=new Vector3();p.object.getWorldPosition(world);assert.ok(Math.abs(world.z-p.object.position.z*2)<1e-9);
  for(let i=0;i<1000;i++)p.update(i%2,.016);
  const after=[];p.object.traverse(o=>{if(o.isMesh)after.push(o);});assert.deepEqual(after,meshes);
  assert.deepEqual(model.geometry.attributes.position.array,sourcePositions);assert.equal(other.motion.progress,0);
  p.update(1,3);p.reset();assert.deepEqual(p.motion,createPropellerMotion());
  let gd=0,md=0;for(const g of geometries)g.addEventListener('dispose',()=>gd++);for(const m of materials)m.addEventListener('dispose',()=>md++);
  p.dispose();assert.equal(body.children.length,0);assert.equal(gd,geometries.size);assert.equal(md,materials.size);
});

test('all kart poses expose diagnostics and own propeller lifecycle',()=>{
  const code=readFileSync(new URL('../render/karts.js',import.meta.url),'utf8');
  assert.match(code,/root\.userData\.swimPropeller = propeller\.motion/);
  assert.match(code,/k\.propeller\.update\([^;]+underwater[^;]+\{gentle,paused\}/);
  assert.match(code,/k\.propeller\.reset\(\)/);assert.match(code,/k\.propeller\.dispose\(\)/);
  assert.match(code,/k\.propeller\.fit\(/);
});

test('refitting ignores parent world motion and stows every folded foil inside the rear housing',t=>{
  const body=new Group(),model=new Group();body.add(model);
  const shell=new Mesh(new BoxGeometry(1.4,1,1.8),new MeshBasicNodeMaterial());
  shell.position.z=.1;model.rotation.y=.15;model.add(shell);
  const p=createSwimPropeller(body,model);t.after(()=>{p.dispose();shell.geometry.dispose();shell.material.dispose();});
  const mount=p.object.position.clone(),scale=p.object.scale.clone();
  body.position.set(150,80,-240);body.rotation.set(.3,1.7,.2);body.scale.setScalar(2.1);
  p.fit(model);assert.ok(p.object.position.distanceTo(mount)<1e-7);assert.ok(p.object.scale.distanceTo(scale)<1e-7);
  const tip=new Vector3();p.object.updateWorldMatrix(true,true);
  for(const hinge of p.hinges) {
    const foil=hinge.children[0],points=foil.geometry.attributes.position;
    for(let i=0;i<points.count;i++) {
      tip.fromBufferAttribute(points,i);foil.localToWorld(tip);p.object.worldToLocal(tip);
      assert.ok(tip.z<.065,'folded foil is behind the housing opening');
      assert.ok(Math.hypot(tip.x,tip.y)<.145,'folded foil fits inside the housing radius');
    }
  }
});
