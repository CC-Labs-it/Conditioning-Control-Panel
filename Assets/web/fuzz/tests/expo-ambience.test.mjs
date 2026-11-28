import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
import test from 'node:test';

const library=new URL('../../vendor/three/three.module.min.js',import.meta.url);
const hooks=registerHooks({resolve(name,context,next){
  return name==='three'?{shortCircuit:true,url:library.href}:next(name,context);
}});
const {Group}=await import('three');
const {createAmbience}=await import('../ui/expo/ambience.js');
hooks.deregister();

function installation(ctx={},lite=false){
  const renderer={domElement:{height:768}};
  return createAmbience({ctx,renderer,lite,meter:m=>{m.voices=1;}});
}

test('aquarium bubbles remain outside the acrylic and produce the same field after restreaming',()=>{
  const a=installation(),room=new Group(),samples=()=>{
    const g=room.getObjectByName('bubbles').geometry;
    const start=g.getAttribute('position'),run=g.getAttribute('run');
    for(let i=0;i<start.count;i++){
      const z=start.getZ(i),end=z+run.getZ(i);
      assert(Math.min(Math.abs(z-45.50),Math.abs(end-45.50))>3.34+.085);
    }
    return Array.from(start.array);
  };
  a.mount('aquarium',room);const first=samples();
  let resourceReleased=0;
  for(const m of room.children[0].children){
    m.material.addEventListener('dispose',()=>resourceReleased++);
    m.geometry.addEventListener('dispose',()=>resourceReleased++);
  }
  a.unmount('aquarium');assert.equal(room.children.length,0);assert.equal(resourceReleased,4);
  a.mount('aquarium',room);assert.deepEqual(samples(),first);a.dispose();
});

test('Gentle and reduced motion hold the visible light pattern and reduce the touch particle field',()=>{
  const ctx={},a=installation(ctx),lite=installation({},true);
  a.mount('forum',new Group());lite.mount('forum',new Group());
  assert(lite.debug().rooms.forum.particles<a.debug().rooms.forum.particles);
  a.tick(1);const initial=a.debug().clock;
  ctx.reduced=true;a.tick(2);assert.equal(a.debug().clock,initial);assert.equal(a.debug().voice,1);
  ctx.reduced=false;ctx.gentle=true;a.tick(1);assert.equal(a.debug().clock,initial);
  ctx.gentle=false;a.tick(2);assert.equal(a.debug().clock,initial+2);a.dispose();lite.dispose();
});

test('vitrine and teapot effects live under their native assembly and dispose once on unload or panic',()=>{
  const a=installation(),cabinet=new Group(),lamp=new Group(),tea=new Group();
  lamp.name='collection_case_light';tea.name='collection_tea';cabinet.add(lamp);
  const releaseCabinet=a.fitCollection(cabinet),releaseTea=a.fitCollection(tea);
  assert.equal(lamp.getObjectByName('collection-cabinet-wash').parent,lamp);
  const wisp=tea.getObjectByName('teapot-spout-steam');let releases=0;
  wisp.geometry.addEventListener('dispose',()=>releases++);
  a.debug(false);assert(!wisp.visible);a.debug(true);assert(wisp.visible);
  releaseCabinet();assert.deepEqual(a.debug().fittings,['teapot-spout-steam']);
  a.dispose();releaseTea();assert.equal(releases,1);assert.equal(tea.children.length,0);
});
