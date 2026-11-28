import test from 'node:test';
import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
import {pickExhibits} from '../ui/expo/exhibit-picking.js';
import {createRoomVisibility} from '../ui/expo/room-visibility.js';
import {createExpoPaintClock} from '../ui/expo/frame-pacing.js';

test('Gentle draws each second vsync despite clock rounding and skips missed frames after a stall', () => {
  const due=createExpoPaintClock(true);
  let previous=0;
  for(let frame=1;frame<=600;frame++){
    const now=Math.floor(frame*1000/60)/1000;
    assert.equal(due(now-previous),frame%2===0,'frame '+frame);
    previous=now;
  }
  assert.equal(due(.4),true);
  assert.equal(due(.001),false);
  assert.equal(due(.003,true),true);
  assert.equal(due(.016),false);
  assert.equal(createExpoPaintClock(false)(.002),true);
});

test('shared projector cases keep their transforms while labels, lenses and switches stay independent', async () => {
  const url=new URL('../../vendor/three/three.module.min.js',import.meta.url).href;
  const hook=registerHooks({resolve(name,ctx,next){return name==='three'?{shortCircuit:true,url}:next(name,ctx);}});
  try {
    const {batchArchiveHousings}=await import('../ui/expo/archive-furniture.js');
    const {Group,Mesh,BoxGeometry,MeshBasicMaterial,Matrix4}=await import('three');
    const room=new Group(),geometry=new BoxGeometry(),material=new MeshBasicMaterial(),cases=[],controls=[];
    room.position.set(1,0,2);
    for (let i=1;i<=2;i++){
      const bay=new Group();bay.name='archive_bay_slot_'+i;bay.position.x=i*3;bay.rotation.y=i*.2;room.add(bay);
      const casing=new Mesh(geometry,material);casing.name='expo_fixture_slot_'+i;bay.add(casing);cases.push(casing);
      const key=new Mesh(geometry,material);key.name='expo_replay_slot_'+i;bay.add(key);controls.push(key);
    }
    room.updateMatrixWorld(true);
    const undo=batchArchiveHousings(room),batch=room.getObjectByName('archive_bay_housings');
    assert.equal(batch.count,2);
    for(let i=0;i<2;i++){
      const local=new Matrix4();batch.getMatrixAt(i,local);
      const world=local.premultiply(room.matrixWorld);
      assert(world.elements.every((v,j)=>Math.abs(v-cases[i].matrixWorld.elements[j])<1e-6));
    }
    assert(cases.every(n=>!n.visible));assert(controls.every(n=>n.visible));
    undo();assert(cases.every(n=>n.visible));assert.equal(batch.parent,null);
    geometry.dispose();material.dispose();
  } finally {hook.deregister();}
});

test('streaming warms the same target as the scene and immediately restores the caller target', async () => {
  const url=new URL('../../vendor/three/three.module.min.js',import.meta.url).href;
  const hook=registerHooks({resolve(name,ctx,next){return name==='three'?{shortCircuit:true,url}:next(name,ctx);}});
  try {
    const {createPost}=await import('../ui/expo/post.js');
    const original={name:'monitor'},room={},camera={},scene={};
    let target=original,face=3,level=1,prepared;
    const renderer={
      extensions:{has:()=>true},capabilities:{maxSamples:2},
      getRenderTarget:()=>target,getActiveCubeFace:()=>face,getActiveMipmapLevel:()=>level,
      setRenderTarget(t,f=0,l=0){target=t;face=f;level=l;},
      compileAsync(...args){assert.deepEqual(args,[room,camera,scene]);prepared=target;return Promise.resolve(room);}
    };
    const post=createPost(renderer,false);
    try {
      const compiled=post.compileRoom(room,camera,scene);
      assert(prepared?.isWebGLRenderTarget);assert.deepEqual([target,face,level],[original,3,1]);
      assert.equal(await compiled,room);
      renderer.compileAsync=()=>{throw Error('compile failed');};
      assert.throws(()=>post.compileRoom(room,camera,scene),/compile failed/);
      assert.deepEqual([target,face,level],[original,3,1]);
    } finally {post.dispose();}
  } finally {hook.deregister();}
});

function node(name, parent, visible = true) {
  const item = {name, parent, visible, userData:{}, children:[], layers:{test:()=>true}};
  parent?.children.push(item);
  return item;
}

test('nested exhibit targets are hit once and hidden exhibits do no triangle work', () => {
  const scene=node('scene'), shelf=node('shelf',scene), cap=node('cap',shelf);
  const glass=node('glass',scene,false), hidden=node('cap',glass), released=node('closed loan',shelf,false);
  const targets=new Map([shelf,cap,hidden].map(x=>[x,true]));
  const checked=[];
  for(const [n,d] of [[shelf,2],[cap,1],[hidden,0],[released,0]]) n.raycast=(_,hits)=>{checked.push(n);hits.push({distance:d});};
  const hits=[{}];pickExhibits(targets,{layers:{}},hits);
  assert.deepEqual(checked,[shelf,cap]);assert.deepEqual(hits.map(h=>h.distance),[1,2]);
  glass.visible=true;pickExhibits(targets,{layers:{}},hits);assert.deepEqual(hits.map(h=>h.distance),[0,1,2]);
});

test('sealed rooms keep their shell and lights, revealing furnishings before the threshold', () => {
  const hall=node('hall'), hoof=node('hoof'), architecture=node('Architecture',hoof);
  const board=node('hoof_display',hoof),retracted=node('hoof_staff',hoof,false);
  const light=node('hoof_light',hoof);light.isLight=true;
  const resident=new Map([['hall',{view:hall}],['hoof',{view:hoof}]]);
  const walker={zone:'hall',feet:{x:0,y:0,z:3}};
  let open=false;
  const view=createRoomVisibility(resident,{connectVisibleRooms(set){if(open)set.add('hoof');}},walker);
  view.prepare();assert.equal(board.visible,false);assert.equal(architecture.visible,true);assert.equal(light.visible,true);
  view.restore();assert.equal(board.visible,true);assert.equal(retracted.visible,false);
  walker.feet={x:-10,y:0,z:7};view.prepare();assert.equal(board.visible,true);view.restore();
  walker.feet={x:0,y:0,z:0};open=true;view.prepare();assert.equal(board.visible,true);view.restore();
});
