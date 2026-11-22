import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {registerHooks} from 'node:module';
import test from 'node:test';
import * as THREE from '../../vendor/three/three.module.min.js';
import {MeshoptDecoder} from '../../vendor/three/addons/libs/meshopt_decoder.module.js';
import {firstPress} from '../ui/expo/first-press.js';

const library=new URL('../../vendor/three/',import.meta.url);
const hook=registerHooks({resolve(name,context,next){
  if(name==='three')return {shortCircuit:true,url:new URL('three.module.min.js',library).href};
  return next(name,context);
}});
const {GLTFLoader}=await import(new URL('addons/loaders/GLTFLoader.js',library));
hook.deregister();
const zones=new URL('../assets/expo/zones/',import.meta.url);
const plan=JSON.parse(await readFile(new URL('zones.json',zones)));
const hall=plan.zones.find(zone=>zone.id==='hall');

async function loadHall(file){
  const bytes=await readFile(new URL(file,zones));
  const loader=new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
  // Preserve the shipped geometry, transforms and finish names. Room textures require a browser.
  loader.register(parser=>({name:'geometryOnly',beforeRoot(){
    parser.json.materials=parser.json.materials.map(({name})=>({name}));
  }}));
  return (await loader.parseAsync(bytes.buffer.slice(bytes.byteOffset,bytes.byteOffset+bytes.byteLength),'')).scene;
}

for(const [profile,field] of [['desktop','file'],['Gentle','mobileFile']]){
  test('the First Press moves its original cover and keeps the register clear on '+profile,async()=>{
    const room=await loadHall(hall[field]);
    const camera=new THREE.PerspectiveCamera();
    camera.position.set(0,1.6,-12);camera.updateMatrixWorld();
    const state={file:{taps:0},presses:0,ticket:{stamp(){},book(){}},pressRecord:()=>''};
    const lines=[],books=[];state.ticket.book=(...row)=>books.push(row);
    const api={THREE,camera,props:()=>()=>{},cue(id){lines.push(id);},say(){},play(){},setColliderEnabled(){}};
    const press=firstPress(api,state),clean=[];
    const register=room.getObjectByName('press_readout'),registerStart=new THREE.Vector3();
    room.updateMatrixWorld(true);register.getWorldPosition(registerStart);
    const glass=room.getObjectByName('Surface_glass');
    const ray=new THREE.Raycaster(new THREE.Vector3(.17,2.5,-9),new THREE.Vector3(0,-1,0));
    function height(group){return ray.intersectObject(group,true)[0]?.point.y??-Infinity;}
    const originalHeight=height(glass);assert.ok(originalHeight>1.3);
    press.mount('hall',room,name=>room.getObjectByName(name),()=>null,clean);
    const cover=room.getObjectByName('Hand-hole cover');
    room.updateMatrixWorld(true);
    assert.ok(Math.abs(height(cover)-originalHeight)<.001);
    assert.ok(height(glass)<0,'no second dome remains in the architectural glass');
    assert.ok(register.getWorldPosition(new THREE.Vector3()).x>registerStart.x+1);
    press.press();press.press();press.press();
    assert.equal(state.pressWatching,true);press.tick(.6,'hall');room.updateMatrixWorld(true);
    assert.ok(height(cover)<originalHeight && height(cover)>0,'the lift descends in sight');
    press.tick(1.4,'hall');room.updateMatrixWorld(true);assert.equal(state.pressWatching,false);
    assert.ok(height(cover)<0,'the glass descends below floor level with the plinth');
    for(let i=0;i<4;i++){
      press.press(false,i);
      if(i<3){press.press(false,i);assert.equal(state.presses,i+4,'a loan is recorded only once per location');}
      press.tick(.2,'atrium');
    }
    press.press();assert.equal(state.presses,7);
    camera.position.set(-11.48,1.6,-2);press.tick(9.9,'hall');assert.equal(state.presses,7);
    press.press(true);assert.equal(press.sealed,false);
    press.tick(.2,'hall');press.press(true);press.tick(.1,'hall');room.updateMatrixWorld(true);
    assert.equal(state.presses,8);assert.ok(books.some(([id])=>id==='press-sealed'));
    assert.deepEqual(lines,['pa_hall1_press','v4_press_rope','v4_press_moved','v4_press_mezz','v4_press_found','v4_press_price','v4_press_invert','v4_press_still','v4_press_seal']);
    assert.ok(press.sealed);assert.equal(cover.visible,false);
    assert.ok(Math.abs(height(cover)-originalHeight)<.001,'the lift returns for sealing');
    for(const dispose of clean.reverse())dispose();
    room.updateMatrixWorld(true);
    assert.ok(Math.abs(height(glass)-originalHeight)<.001);
    assert.ok(register.getWorldPosition(new THREE.Vector3()).distanceTo(registerStart)<.001);
  });
}
