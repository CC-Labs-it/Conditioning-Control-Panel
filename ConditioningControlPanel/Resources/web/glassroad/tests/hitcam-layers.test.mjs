import {test} from 'node:test';
import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
const hooks=registerHooks({resolve(specifier,context,next){
  const files={'three':'build/three.webgpu.min.js','three/webgpu':'build/three.webgpu.min.js','three/tsl':'build/three.tsl.min.js'};
  const path=files[specifier]||(specifier.startsWith('three/addons/')?specifier.slice(6):null);
  return path?{url:new URL('../vendor/three/'+path,import.meta.url).href,shortCircuit:true}:next(specifier,context);
}});
const {createHitCam}=await import('../render/hitCam.js');
const {Scene,Group,PerspectiveCamera}=await import('three/webgpu');
const {CATCH_LAYER}=await import('../render/shadows.js');
const {createState,S}=await import('../sim/state.js');
hooks.deregister();

test('hit-camera preparation and first live hit share every main layer except shadow catchers',async()=>{
  const mainCamera=new PerspectiveCamera(),handlers=new Map();
  mainCamera.layers.enable(CATCH_LAYER);mainCamera.layers.enable(3);
  const originalMask=mainCamera.layers.mask,expected=originalMask&~(1<<CATCH_LAYER);
  let preparedMask,inset;
  const renderer={warmInset(scene,camera){preparedMask=camera.layers.mask;},setInset(value){inset=value;}};
  const bus={on(name,handler){handlers.set(name,handler);return()=>handlers.delete(name);}};
  const player=createState(),victim=createState();victim[S.posZ]=50;
  const world={scene:new Scene(),karts:[new Group(),new Group()]};
  const cam=createHitCam({bus,feel:null,renderer,world,doc:null});
  await cam.warm(mainCamera);
  assert.equal(preparedMask,expected,'prepare the same custom layers the first hit will use');
  handlers.get('item-hit')({by:0,victim:1,verdict:'hit',kind:'pendulum'});
  cam.update(.016,{racing:true,paused:false,card:false,finished:false,gentle:false,
    racers:[{view:player,meta:{i:0}},{view:victim,meta:{i:1}}],player,mainCamera,viewW:1280,viewH:720});
  assert.ok(inset,'the first distant hit activates the inset');
  assert.equal(inset.camera.layers.mask,expected);
  assert.equal(inset.camera.layers.mask,preparedMask);
  assert.equal(mainCamera.layers.mask,originalMask,'the race camera keeps its shadow receivers');
  cam.dispose();
});
