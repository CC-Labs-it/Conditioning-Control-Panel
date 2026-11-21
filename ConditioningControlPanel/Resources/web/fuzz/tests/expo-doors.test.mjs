import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
import test from 'node:test';

const library=new URL('../../vendor/three/',import.meta.url);
const hook=registerHooks({resolve(name,context,next){
  if(name==='three')return {shortCircuit:true,url:new URL('three.module.min.js',library).href};
  if(name.startsWith('three/addons/'))return {shortCircuit:true,url:new URL(name.slice(6),library).href};
  return next(name,context);
}});
const {createDoors,streamingPriority}=await import('../ui/expo/doors.js');
const {BoxGeometry,Group,Mesh,MeshStandardMaterial,PerspectiveCamera,Ray,Vector3}=await import('three');
hook.deregister();

function room(){return {view:new Group(),environment:{texture:null}};}
function installation(gentle=false){
  const camera=new PerspectiveCamera(),walker={feet:new Vector3(7,0,32)},parts=new Map();
  for(const kind of ['staff','civic'])for(const part of ['Hinge','Leaf']){
    const source=new Mesh(undefined,new MeshStandardMaterial());
    source.userData.finish='oak';parts.set(part+'_'+kind,[source]);
  }
  parts.set('CrownShutter',[new Mesh(undefined,new MeshStandardMaterial())]);
  const sounds=[],scene=new Group(),doors=createDoors(scene,walker,{gentle},camera,(key)=>sounds.push(key));
  doors.setHardware({parts,dispose(){}});
  doors.mount('atrium',room());
  const state=()=>doors.debug().find(d=>d.id==='staff-entrance');
  const origin=new Vector3(10,1.2,31.94),ray=new Ray(origin,new Vector3(1,0,0));
  function approach(x){walker.feet.set(x,0,32);camera.position.copy(walker.feet);camera.lookAt(12,0,32);}
  function wait(seconds){for(let i=0;i<Math.ceil(seconds*60);i++)doors.tick(1/60);}
  return {doors,state,approach,wait,ray,sounds,walker,camera,scene};
}

test('the unloaded staff passage stays physically shut until its counterpart arrives',()=>{
  const {doors,state,approach,wait,ray,walker}=installation();
  approach(10);wait(2);
  assert.equal(state().ready,false);assert.equal(state().progress,0);assert(doors.blocked(ray,4));
  const trial=new Vector3(11.99,0,31.94);
  doors.pushOut(trial,.24,1.6,walker.feet);assert(trial.x<11.72);
  doors.mount('service',room());wait(.2);
  assert(state().progress>0&&state().progress<1);
  wait(.6);assert.equal(state().progress,1);assert(!doors.blocked(ray,4));
  doors.dispose();
});

for(const gentle of [false,true])test('jambs stay fixed, reverse approach opens away and the occupied sweep stays clear in '+(gentle?'Gentle':'Standard'),()=>{
  const {doors,state,approach,wait,sounds}=installation(gentle);
  doors.mount('service',room());const hinges=state().leaves.map(leaf=>leaf.hinge);
  approach(9);wait(.18);
  assert(state().progress>0&&state().progress<1);
  wait(1);assert(state().leaves.every(leaf=>leaf.tip[0]>12.8));
  approach(12.2);wait(2.4);assert.equal(state().progress,1);
  assert(doors.holds('service'));assert.deepEqual(state().leaves.map(leaf=>leaf.hinge),hinges);
  approach(18);wait(2.9);assert.equal(state().progress,0);
  approach(15);wait(1.2);assert(state().leaves.every(leaf=>leaf.tip[0]<11.2));
  assert.deepEqual(state().leaves.map(leaf=>leaf.hinge),hinges);assert.equal(sounds.length,2);
  doors.dispose();
});

test('streaming distinguishes stacked levels, view direction and the two annex doors',()=>{
  const priority=(a,b,p,f=[0,0,-1])=>streamingPriority(a,b,new Vector3(...p),false,new Vector3(...f));
  const wants=(a,b,p,f)=>{const s=priority(a,b,p,f);return s.distance<s.near;};
  for(const wing of ['chronology','aquarium'])assert(!wants('atrium',wing,[.22,0,36.21]));
  assert(wants('atrium','chronology',[4.2,0,38.8]));assert(!wants('atrium','aquarium',[4.2,0,38.8]));
  assert(wants('service','atrium',[24,5.1,23.2]));assert(!wants('service','atrium',[29.1,0,41]));
  assert(wants('catwalk','dome',[-22,11.95,-5]));assert(wants('hall','twelve',[0,0,14]));
  assert(!wants('hall','twelve',[0,0,14],[0,0,1]));
  assert(!wants('service','aquarium',[0,0,0]));
});

test('all three gallery elbows have a doorway before the neighboring assets arrive',()=>{
  const {doors}=installation();
  const pairs=[['chronology','forum','archive-to-forum'],['forum','commons','forum-to-commons'],['commons','aquarium','aquarium-climate-doors']];
  for(const [inside,outside,id] of pairs){
    doors.mount(inside,room());const sealed=doors.debug().find(d=>d.id===id);
    assert(sealed);assert.equal(sealed.ready,false);assert.equal(sealed.progress,0);
    const a=new Vector3().fromArray(sealed.leaves[0].hinge),b=new Vector3().fromArray(sealed.leaves[1].hinge);
    assert(Math.abs(a.distanceTo(b)-6.60)<1e-8);
    doors.mount(outside,room());assert(doors.debug().find(d=>d.id===id).ready);
    doors.unmount(inside);doors.unmount(outside);
  }
  doors.dispose();
});

test('the small viewing hatch stays sealed from the atrium while the catwalk is missing',()=>{
  const {doors,walker,camera,wait}=installation(),ray=new Ray(new Vector3(-10,8.16,28),new Vector3(-1,0,0));
  assert(!doors.debug().find(d=>d.id==='inspection-shutter').ready);
  assert(doors.blocked(ray,2.8));
  doors.mount('catwalk',room());walker.feet.set(-9.9,6.8,28.3);camera.position.copy(walker.feet);camera.lookAt(-12,6.8,28);
  wait(1);const lifted=doors.debug().find(d=>d.id==='inspection-shutter');
  assert.equal(lifted.leaves[0].degrees,0);assert(Math.abs(lifted.leaves[0].hinge[1]-8.68)<1e-8);
  assert(!doors.blocked(ray,2.8));
  doors.dispose();
});

test('the aquarium return leaves the near-side right turn clear when arriving from the tunnel',()=>{
  const {doors,camera,walker,wait}=installation();
  doors.mount('aquarium',room());
  walker.feet.set(-7.8,0,43.1);camera.position.copy(walker.feet);camera.lookAt(-7.8,0,41);wait(1);
  const pair=doors.debug().find(d=>d.id==='aquarium-return');assert.equal(pair.progress,1);
  assert(pair.leaves.every(leaf=>leaf.tip[2]>42.4));
  const aisle=new Ray(new Vector3(-7.8,1.2,39.99),new Vector3(1,0,0));assert(!doors.blocked(aisle,4.2));
  doors.dispose();
});

test('the upper inspection wall and its collision follow the lifetime of its shared atrium wall',()=>{
  const {doors,scene}=installation(),corridor=room();
  const material=new MeshStandardMaterial();material.name='pale_shell';
  const wall=new Mesh(new BoxGeometry(.15,3.07,2.49),material);
  wall.name='catwalk_corridor_endcap';wall.userData.solidStairEnd=true;corridor.view.add(wall);
  const lining=()=>scene.getObjectByName('catwalk_inspection_wall_return');
  const side=new Ray(new Vector3(-13.3,8.40,26.55),new Vector3(1,0,0));
  doors.mount('catwalk',corridor);assert.equal(lining(),undefined);
  for(let load=0;load<2;load++){
    doors.unmount('atrium');assert.equal(lining().count,5);assert.equal(lining().material,material);
    assert(doors.blocked(side,2));const visitor=new Vector3(-12,6.8,26.55);
    doors.pushOut(visitor,.24,1.6,new Vector3(-12.4,6.8,26.55));assert(visitor.x<-12.24);
    doors.mount('atrium',room());assert.equal(lining(),undefined);assert(!doors.blocked(side,2));
  }
  doors.unmount('atrium');doors.unmount('catwalk');assert.equal(lining(),undefined);assert(!doors.blocked(side,2));
  doors.dispose();wall.geometry.dispose();material.dispose();
});

test('the crown aperture retains its chamber until the moving shades have closed',()=>{
  const {doors,walker,wait}=installation(true),shade=()=>doors.debug().find(d=>d.id==='crown-shutter');
  walker.zone='catwalk';walker.feet.set(-22,11.95,-5);doors.mount('catwalk',room());wait(1);
  assert.equal(shade().progress,0);
  doors.mount('dome',room());wait(.2);
  assert(shade().progress>0&&shade().progress<1);wait(1);assert.equal(shade().progress,1);
  walker.feet.set(-13,6.8,21);wait(.15);assert(doors.holds('dome'));
  wait(1.2);assert.equal(shade().progress,0);assert(!doors.holds('dome'));
  doors.dispose();
});
