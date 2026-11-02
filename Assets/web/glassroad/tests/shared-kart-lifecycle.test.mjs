import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {registerHooks} from 'node:module';
import {createBus} from '../core/events.js';
import {loadTrack} from '../track/load.js';
import {makeFeel} from './sim-fake.mjs';

const hooks=registerHooks({resolve(specifier,context,next){
  const files={'three':'build/three.webgpu.min.js','three/webgpu':'build/three.webgpu.min.js','three/tsl':'build/three.tsl.min.js'};
  const file=files[specifier]||(specifier.startsWith('three/addons/')?'addons/'+specifier.slice(13):null);
  return file?{url:new URL('../vendor/three/'+file,import.meta.url).href,shortCircuit:true}:next(specifier,context);
}});
const {buildKarts}=await import('../render/karts.js');
const {prepareWorldLayouts,buildWorld}=await import('../render/scene.js');
const {BoxGeometry,Group,Mesh,MeshBasicNodeMaterial,Sprite,SpriteNodeMaterial}=await import('three/webgpu');
hooks.deregister();

test('shared racers replace bus bindings and changed caps without replacing base meshes or bodies',()=>{
  const geometry=new BoxGeometry(),karts=buildKarts(null,2,geometry),first=createBus(),second=createBus();
  const identities=karts.list.map(k=>[k.root,k.body,k.model,k.model.geometry,k.model.material,k.propeller]);
  const detachFirst=karts.attach({bus:first,particles:{}});
  first.emit('countdown',{n:3});assert.ok(karts.list.every(k=>k.pend.length===1));
  karts.rebind({art:null,seed:7});assert.ok(karts.list.every(k=>k.pend.length===0));
  const detachSecond=karts.attach({bus:second,particles:{}});
  first.emit('go');assert.ok(karts.list.every(k=>k.pend.length===0),'departed course cannot animate racers');
  detachFirst();second.emit('go');assert.ok(karts.list.every(k=>k.pend.length===1),'late old cleanup cannot detach the new bus');
  for(const k of karts.list){const mount=new Group();mount.name='head_attach';k.model.add(mount);}
  const cap=()=>{const scene=new Group(),spin=new Mesh(new BoxGeometry(),new MeshBasicNodeMaterial());spin.name='spin';scene.add(spin);return{scene};};
  const sourceA=cap(),sourceB=cap(),art=source=>({theme:'keep',model:id=>id==='glide-keep-beanie'?source:null});
  karts.rebind({art:art(sourceA),seed:9});
  const caps=karts.list.map(k=>k.glideCap.cap);
  karts.rebind({art:art(sourceA),seed:10});
  assert.ok(karts.list.every((k,i)=>k.glideCap.cap===caps[i]),'unchanged source preserves cap identity');
  karts.rebind({art:art(sourceB),seed:11});
  for(const [i,k]of karts.list.entries()){
    assert.notEqual(k.glideCap.cap,caps[i]);assert.equal(caps[i].parent,null);
    assert.deepEqual([k.root,k.body,k.model,k.model.geometry,k.model.material,k.propeller],identities[i]);
  }
  detachSecond();second.emit('go');assert.ok(karts.list.every(k=>k.pend.length===0));
  karts.dispose();karts.dispose();geometry.dispose();
  for(const source of [sourceA,sourceB])source.scene.traverse(m=>{m.geometry?.dispose();m.material?.dispose();});
});

test('internal Mainframe selection prepares both authored layouts once without an initial Mainframe URL',async t=>{
  const previous=globalThis.fetch,requests=[];
  globalThis.fetch=async url=>{requests.push(String(url));return new Response(readFileSync(new URL(url),'utf8'));};
  t.after(()=>{globalThis.fetch=previous;});
  await Promise.all([prepareWorldLayouts('mainframe'),prepareWorldLayouts('mainframe')]);
  await prepareWorldLayouts('mainframe');await prepareWorldLayouts('keep');
  assert.equal(requests.filter(u=>u.endsWith('/mainframe.spirals.json')).length,1);
  assert.equal(requests.filter(u=>u.endsWith('/mainframe.media.json')).length,1);
  assert.equal(requests.length,2);
});

test('departing course disposal preserves pooled racers and their global Sprite quad without accumulating rings',t=>{
  const warnings=[];t.mock.method(console,'warn',message=>warnings.push(String(message)));
  const track=loadTrack(JSON.parse(readFileSync(new URL('../tracks/keep.json',import.meta.url),'utf8')));
  const pool={current:null},first=buildWorld(track,makeFeel(),{kartPool:pool,count:2});
  const karts=pool.current,root=karts.list[0].root,model=karts.list[0].model,children=root.children.length;
  // Real racer tags/stars use this same r186 singleton; greybox racers do not create them.
  const resident=new Sprite(new SpriteNodeMaterial());root.add(resident);
  const quad=resident.geometry,courseSprites=[];first.scene.traverse(o=>{if(o.isSprite&&o!==resident)courseSprites.push(o);});
  assert.ok(courseSprites.length>=2,'the actual course contains both particle and mote sprites');
  assert.ok(courseSprites.every(o=>o.geometry===quad),'course effects and retained racers borrow one quad');
  let quadDisposals=0;const onQuadDispose=()=>quadDisposals++;
  quad.addEventListener('dispose',onQuadDispose);t.after(()=>quad.removeEventListener('dispose',onQuadDispose));
  let disposals=0;model.material.addEventListener('dispose',()=>disposals++);
  first.dispose();assert.equal(root.parent,null);assert.equal(disposals,0);assert.equal(root.children.length,children);
  assert.equal(quadDisposals,0,'departing Sprite effects cannot invalidate retained vertex/index buffers');
  const second=buildWorld(track,makeFeel(),{kartPool:pool,count:2,seed:9});
  assert.equal(pool.current,karts);assert.equal(second.kart,root);assert.equal(karts.list[0].model,model);
  assert.equal(root.children.length,children+1,'the new course owns exactly one replacement ring');
  first.dispose();assert.equal(root.parent,second.scene,'late old disposal cannot detach current racers');
  second.dispose();assert.equal(root.parent,null);assert.equal(disposals,0);
  assert.equal(quadDisposals,0,'the singleton remains valid across repeated course disposal');
  karts.dispose();karts.dispose();assert.equal(disposals,1,'the final app owner disposes shared resources once');
  resident.removeFromParent();resident.material.dispose();assert.equal(quadDisposals,0,'Sprite quad ownership belongs to the library');
  assert.ok(warnings.every(w=>w.includes('document is not defined')),'only browser-only optional layers are absent in Node');
});
