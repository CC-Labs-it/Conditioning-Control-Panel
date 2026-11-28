import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { register } from 'node:module';

const vendor = new URL('../../vendor/three/', import.meta.url);
register('data:text/javascript,' + encodeURIComponent(`
  export async function resolve(id,context,next) {
    if(id==='three') return {url:'${vendor}three.module.min.js',shortCircuit:true};
    if(id.startsWith('three/addons/')) return {url:'${vendor}'+id.slice(6),shortCircuit:true};
    return next(id,context);
  }`));
const THREE = await import('three');
const { GLTFLoader } = await import('three/addons/loaders/GLTFLoader.js');
const { createCheerleaders } = await import('../board/cheerleader.js');
const { createBus } = await import('../game/events.js');
const { setPresentation } = await import('../game/preferences.js');
const nodes = root => { const result=[];root.traverse(node=>result.push(node));return result; };
const bones = root => nodes(root).filter(node=>node.isBone);
const skinMeshes = root => nodes(root).filter(node=>node.isSkinnedMesh);
const rotations = root => bones(root).map(bone=>bone.quaternion.toArray());
const advance = (cheer, seconds) => { for(let t=0;t<seconds;t+=1/60) cheer.update(1/60); };

function footBounds(root,name) {
  root.updateWorldMatrix(true,true);
  const bounds=new THREE.Box3(), point=new THREE.Vector3();
  for(const mesh of skinMeshes(root)) {
    mesh.skeleton.update();
    const joint=mesh.skeleton.bones.findIndex(bone=>bone.name===name);
    const index=mesh.geometry.getAttribute('skinIndex'),weight=mesh.geometry.getAttribute('skinWeight');
    for(let i=0;i<index.count;i++) {
      if(![0,1,2,3].some(k=>index.getComponent(i,k)===joint && weight.getComponent(i,k)>.5)) continue;
      mesh.getVertexPosition(i,point).applyMatrix4(mesh.matrixWorld);bounds.expandByPoint(point);
    }
  }
  return bounds;
}

test('Racing Emi retains its short native rig, independent seats and real animation gates', async t => {
  const bytes=await readFile(new URL('../assets/cheerleader/emi-racing.glb',import.meta.url));
  const jsonLength=bytes.readUInt32LE(12),json=JSON.parse(bytes.subarray(20,20+jsonLength));
  const binOffset=20+jsonLength;
  json.buffers[0].uri='data:application/octet-stream;base64,'+bytes.subarray(binOffset+8,binOffset+8+bytes.readUInt32LE(binOffset)).toString('base64');
  // Remove only image dependencies for Node. Geometry, skin weights and clips stay exact.
  function stripMaps(value) {
    if(!value || typeof value!=='object') return;
    for(const key of Object.keys(value)) {
      if(key.endsWith('Texture')) delete value[key];else stripMaps(value[key]);
    }
  }
  json.materials.forEach(stripMaps);
  let source;
  t.mock.method(GLTFLoader.prototype,'loadAsync',async function(url) {
    assert.match(url,/emi-racing\.glb$/);
    source=await this.parseAsync(JSON.stringify(json),'');return source;
  });
  const saved=Object.fromEntries(['document','window','Image','ProgressEvent'].map(key=>[key,globalThis[key]]));
  let paused=false;
  globalThis.document={hidden:false};
  globalThis.window={PBP:{isPaused:()=>paused,settings:{}}};
  globalThis.Image=class {set src(value) {assert.match(value,/emi-racing-faces\.png$/);queueMicrotask(()=>this.onload());}};
  globalThis.ProgressEvent??=class {constructor(type,data){Object.assign(this,{type},data);}};
  const scene=new THREE.Scene(),bus=createBus(),listeners=new Map();
  const canvas={getBoundingClientRect:()=>({left:0,top:0,width:800,height:800}),addEventListener:(name,fn)=>listeners.set(name,fn),removeEventListener:(name,fn)=>{if(listeners.get(name)===fn)listeners.delete(name);}};
  const camera=new THREE.PerspectiveCamera(45,1,.01,30);camera.position.set(-2.2,.8,3);camera.lookAt(-2.2,.4,4.45);camera.updateMatrixWorld(true);
  let checkedSide=null;
  const game={rules:{inCheck:()=>!!checkedSide,turn:()=>checkedSide||'w'}};
  const cheer=createCheerleaders({view:{scene,camera,renderer:{domElement:canvas}},bus,game});
  t.after(()=>{
    cheer.dispose();setPresentation({reducedMotion:false,boardMotion:'full'});
    for(const [key,value] of Object.entries(saved)){if(value===undefined)delete globalThis[key];else globalThis[key]=value;}
  });
  setPresentation({reducedMotion:false,boardMotion:'full'});
  await cheer.setLooks({w:{companion:'chess.companion.emi'},b:{companion:'chess.companion.emi'}});
  assert.equal(cheer.debug().seats.length,2);
  assert.equal(source.animations.length,26);
  assert.equal(new Set(skinMeshes(source.scene).flatMap(mesh=>mesh.skeleton.bones)).size,15);
  const white=scene.getObjectByName('chess-emi-w'),black=scene.getObjectByName('chess-emi-b');
  const originals=nodes(source.scene),whiteModel=white.children[0],blackModel=black.children[0];
  for(const root of [white,black]) {
    assert.ok(root.scale.x>0);
    assert.deepEqual(root.scale.toArray(),[root.scale.x,root.scale.x,root.scale.x],'only uniform whole-model scaling');
    const copy=nodes(root.children[0]);
    assert.equal(copy.length,originals.length,'no procedural limbs added to the native rig');
    assert.deepEqual(root.children.slice(1).map(node=>node.name),['emi-seat-cushion','emi-pet-hearts'],'only the cushion and local hearts accompany the unchanged model');
    copy.forEach((node,index)=>{
      assert.equal(node.name,originals[index].name);
      assert.deepEqual(node.scale.toArray(),originals[index].scale.toArray(),'authored local proportions remain exact');
      if(node.isMesh) assert.equal(node.geometry,originals[index].geometry,'original mesh/accessors are shared unchanged');
    });
  }
  const forward=new THREE.Vector3(0,0,-1);
  assert.ok(forward.clone().applyQuaternion(white.quaternion).z<-.99,'white faces inward toward -Z');
  assert.ok(forward.clone().applyQuaternion(black.quaternion).z>.99,'black faces inward toward +Z');
  const whiteSkins=skinMeshes(whiteModel),blackSkins=skinMeshes(blackModel);
  whiteSkins.forEach((mesh,i)=>{
    assert.notEqual(mesh.skeleton,blackSkins[i].skeleton);
    assert.notEqual(mesh.skeleton,skinMeshes(source.scene)[i].skeleton);
    mesh.skeleton.bones.forEach((bone,j)=>assert.notEqual(bone,blackSkins[i].skeleton.bones[j]));
  });
  const screen=root=>nodes(root).find(node=>node.material?.name==='emi_screen').material.map;
  assert.notEqual(screen(white),screen(black));
  assert.deepEqual(screen(white).repeat.toArray(),[.25,.25]);
  const rest=rotations(whiteModel);advance(cheer,.4);
  assert.notDeepEqual(rotations(whiteModel),rest,'native idle clip changes authored bones');
  const idle=rotations(whiteModel),whitePositions=bones(whiteModel).filter(b=>!b.name.startsWith('foot')).map(b=>b.position.toArray());
  bus.emit('capture',{by:'w',ply:1});advance(cheer,.8);
  assert.equal(cheer.debug().seats[0].clip,'cheer');
  assert.equal(cheer.debug().seats[1].clip,'sulk');
  assert.notDeepEqual(rotations(whiteModel),idle,'native cheer changes authored bone rotations');
  assert.notDeepEqual(screen(white).offset.toArray(),screen(black).offset.toArray(),'opponents keep independent expressions');
  assert.deepEqual(bones(whiteModel).filter(b=>!b.name.startsWith('foot')).map(b=>b.position.toArray()),whitePositions,'animation cannot stretch limbs or shift the seated support');
  const feet=[];
  for(const root of [white,black]) for(const name of ['footL','footR']) {
    const bounds=footBounds(root,name);assert.ok(!bounds.isEmpty());
    feet.push({side:root.name,name,min:bounds.min.toArray(),max:bounds.max.toArray()});
    assert.ok(bounds.min.y>-.10 && bounds.max.y<.35,'the original short boots stay by the rim');
    assert.ok(Math.min(Math.abs(bounds.min.z),Math.abs(bounds.max.z))>4 && Math.max(Math.abs(bounds.min.z),Math.abs(bounds.max.z))<4.74,'boots sit on the rim clear of playable squares');
    assert.ok(bounds.getSize(new THREE.Vector3()).length()<.4,'boots retain native compact dimensions');
  }
  t.diagnostic(JSON.stringify({nativeClips:source.animations.length,feet}));
  bus.emit('newgame');advance(cheer,.3);
  let gameActions=0;bus.on('move',()=>gameActions++);bus.on('pet',()=>gameActions++);
  const faceMesh=nodes(white).find(node=>node.material?.name==='emi_screen');
  scene.updateMatrixWorld(true);faceMesh.skeleton.update();
  const faceCentre=new THREE.Box3().setFromObject(faceMesh).getCenter(new THREE.Vector3()).project(camera);
  let stopped=0;
  const pointer={button:0,pointerId:7,clientX:(faceCentre.x+1)*400,clientY:(1-faceCentre.y)*400,preventDefault(){},stopImmediatePropagation(){stopped++;}};
  listeners.get('pointerdown')(pointer);cheer.update(.1);
  assert.ok(stopped>0,'a direct ray hit on Emi owns that pointer press');
  assert.equal(cheer.debug().seats[0].action,'pet');assert.ok(cheer.debug().seats[0].hearts>0);
  listeners.get('pointerup')(pointer);listeners.get('click')(pointer);
  const stoppedBefore=stopped;
  listeners.get('pointerdown')({...pointer,clientX:-1000,clientY:-1000});
  listeners.get('click')({...pointer,clientX:700,clientY:700});
  assert.equal(stopped,stoppedBefore,'board presses and nearby later clicks are not consumed');
  assert.equal(gameActions,0,'petting never emits a chess action');
  checkedSide='w';bus.emit('check',{side:'w'});bus.emit('turn',{side:'w'});advance(cheer,5);
  assert.equal(cheer.debug().seats[0].action,'scared');assert.equal(cheer.debug().seats[0].face,15);
  assert.ok(cheer.debug().seats[0].fearBlend>.99);assert.equal(cheer.debug().seats[0].hearts,0);
  const fear=cheer.debug().seats[0],armBounds=footBounds(white,'shoulderR');
  white.updateMatrixWorld(true);faceMesh.skeleton.update();
  const screenBounds=new THREE.Box3().setFromObject(faceMesh);
  t.diagnostic(JSON.stringify({handReach:fear.handReach,mouthTarget:fear.mouthTarget,handMin:armBounds.min.toArray(),handMax:armBounds.max.toArray(),screenMin:screenBounds.min.toArray(),screenMax:screenBounds.max.toArray()}));
  assert.ok(fear.handReach>.08&&fear.handReach<.35,'arm reach is measured from correctly bound skinned geometry');
  assert.ok(armBounds.min.z<screenBounds.min.z+.015,'the raised hand reaches the front of the real screen');
  assert.equal(cheer.pet('w'),false,'petting the checked side is forbidden');
  assert.equal(cheer.pet('b'),true,'the other seat remains independent');cheer.update(.1);
  assert.ok(cheer.debug().seats[1].hearts>0);
  const scared=rotations(whiteModel);advance(cheer,.1);assert.notDeepEqual(rotations(whiteModel),scared,'the scared pose has a slight tremble');
  checkedSide=null;bus.emit('turn',{side:'b'});cheer.update(.1);assert.equal(cheer.debug().seats[0].checked,false);
  bus.emit('newgame');const idles=new Set();let hopHeight=0,sideStep=0;
  for(let i=0;i<1080;i++){cheer.update(1/60);idles.add(cheer.debug().seats[0].idle);hopHeight=Math.max(hopHeight,whiteModel.position.y+.4265);sideStep=Math.max(sideStep,Math.abs(whiteModel.position.x));}
  assert.deepEqual([...idles].sort(),['bob','hop','shuffle','wobble']);
  assert.ok(hopHeight>.055&&hopHeight<.075,'the two small hops are visible without stretching the model');
  assert.ok(sideStep>.03&&sideStep<.04,'the brief sidestep remains on the cushion');
  paused=true;assert.equal(cheer.react('gameover',{winner:'w',ply:2}),false);
  const held=rotations(whiteModel),clock=cheer.debug().seats[0].time;advance(cheer,.2);
  assert.deepEqual(rotations(whiteModel),held);assert.equal(cheer.debug().seats[0].time,clock);
  paused=false;cheer.update(.1);assert.equal(cheer.debug().seats[0].action,'idle');
  globalThis.document.hidden=true;assert.equal(cheer.react('capture',{by:'w',ply:3}),false);
  globalThis.document.hidden=false;cheer.update(.1);assert.equal(cheer.debug().seats[0].action,'idle');
  for(const setting of [{reducedMotion:true},{reducedMotion:false,boardMotion:'still'}]) {
    setPresentation(setting);cheer.update(.1);
    const stopped=rotations(whiteModel),time=cheer.debug().seats[0].time;
    assert.equal(cheer.react('gameover',{winner:'w',ply:4}),false);advance(cheer,.3);
    assert.deepEqual(rotations(whiteModel),stopped);assert.equal(cheer.debug().seats[0].time,time);
  }
  setPresentation({reducedMotion:false,boardMotion:'full'});cheer.update(.1);
  cheer.react('capture',{by:'w',ply:5});cheer.update(.1);assert.equal(cheer.debug().seats[0].action,'cheer');
  bus.emit('newgame');advance(cheer,.5);
  const beforeWave=rotations(whiteModel);
  cheer.react('gameover',{winner:null,ply:6});advance(cheer,.5);
  assert.equal(cheer.debug().seats[0].clip,'wave');
  assert.notDeepEqual(rotations(whiteModel),beforeWave,'native wave changes bone rotations');
  let disposedMaterials=0,disposedGeometry=0,disposedFaces=0;
  const skeletonDisposals=new Map();
  for(const skeleton of new Set([...whiteSkins,...blackSkins,...skinMeshes(source.scene)].map(mesh=>mesh.skeleton))) {
    const dispose=skeleton.dispose;
    skeletonDisposals.set(skeleton,0);
    t.mock.method(skeleton,'dispose',function(){skeletonDisposals.set(skeleton,skeletonDisposals.get(skeleton)+1);return dispose.call(this);});
  }
  for(const root of [white,black]) {
    nodes(root).filter(node=>node.material).forEach(node=>node.material.addEventListener('dispose',()=>disposedMaterials++));
    root.getObjectByName('emi-seat-cushion').geometry.addEventListener('dispose',()=>disposedGeometry++);
    screen(root).addEventListener('dispose',()=>disposedFaces++);
  }
  cheer.dispose();const counts=[disposedMaterials,disposedGeometry,disposedFaces];cheer.dispose();
  assert.equal(scene.children.length,0);assert.ok(disposedMaterials>0);assert.equal(disposedGeometry,2);assert.equal(disposedFaces,2);
  assert.equal(listeners.size,0,'disposing removes every canvas interaction listener');
  assert.deepEqual([disposedMaterials,disposedGeometry,disposedFaces],counts,'disposal is idempotent');
  for(const count of skeletonDisposals.values()) assert.equal(count,1,'each original or cloned skeleton is released exactly once');
  bus.emit('capture',{by:'w',ply:6});assert.equal(cheer.react('capture',{by:'w'}),false);
  await cheer.setLooks({w:{companion:'chess.companion.emi'}});assert.equal(cheer.debug().seats.length,0);
});
