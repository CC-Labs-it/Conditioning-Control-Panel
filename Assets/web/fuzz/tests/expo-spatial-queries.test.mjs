import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
import test from 'node:test';
import {readFile} from 'node:fs/promises';

const vendor=new URL('../../vendor/three/',import.meta.url);
const hook=registerHooks({resolve(name,ctx,next){
  return name==='three'?{shortCircuit:true,url:new URL('three.module.min.js',vendor).href}:next(name,ctx);
}});
const T=await import('three');
const {createWalker}=await import('../ui/expo/collision.js');
const {createProjector}=await import('../ui/expo/projectors.js');
const {GLTFLoader}=await import(new URL('addons/loaders/GLTFLoader.js',vendor).href);
const {MeshoptDecoder}=await import(new URL('addons/libs/meshopt_decoder.module.js',vendor).href);
hook.deregister();

function block(name,dimensions,position){
  const mesh=new T.Mesh(new T.BoxGeometry(...dimensions),new T.MeshStandardMaterial());
  mesh.name=name;mesh.position.set(...position);return mesh;
}

test('short rays cross sector and floor boundaries and preserve the nearest eligible surface',()=>{
  const walker=createWalker(new T.PerspectiveCamera());
  const west=new T.Group(),east=new T.Group(),above=new T.Group();
  west.add(block('floor',[4,.1,4],[-2,-.05,0]));
  east.add(block('floor',[4,.1,4],[2,-.05,0]),block('shop_shelves_work',[.1,1,2],[1,.6,0]));
  above.add(block('Walkway',[4,.1,4],[0,2.1,0]));
  walker.add('west',west);walker.add('east',east);walker.add('above',above);
  try{
    walker.place('west',[-.08,0,0]);walker.move(.2,0);assert.equal(walker.zone,'east');
    const ray=new T.Ray(new T.Vector3(-.2,.65,0),new T.Vector3(1,0,0));
    assert.equal(walker.trace(ray,1),null);
    assert(Math.abs(walker.trace(ray,2).distance-1.15)<.0001);
    assert(!walker.blocked(ray,2,true));assert(walker.blocked(ray,2));
    walker.setColliderEnabled('shop_shelves_work',false);assert.equal(walker.trace(ray,2),null);
    ray.origin.set(0,2,0);ray.direction.set(0,1,0);
    assert(walker.trace(ray,.06));assert.equal(walker.trace(ray,.04),null);
    walker.remove('above');assert.equal(walker.trace(ray,.06),null);
    assert(walker.jump());for(let i=0;i<70;i++)walker.step(1/90);
    assert(!walker.airborne);assert(Math.abs(walker.feet.y)<.0001);
  }finally{walker.dispose();}
});

// Hop up a flight at walking and sprinting pace, the way a visitor does, until the walker lands.
function hopUp(walker,from,heading,speed){
  walker.place('service',from,0);
  assert(walker.jump());
  for(let i=0;i<120&&(walker.airborne||i<3);i++){walker.move(heading[0]*speed/60,heading[1]*speed/60);walker.step(1/60);}
  const landed=walker.feet.clone();
  for(let i=0;i<8;i++){walker.move(-heading[0]*.06,-heading[1]*.06);walker.step(1/60);}
  return {landed,walkedBack:Math.hypot(walker.feet.x-landed.x,walker.feet.z-landed.z)};
}

test('a hop up a ramp lands on the ramp, never on the floor underneath it',()=>{
  const walker=createWalker(new T.PerspectiveCamera()),room=new T.Group();
  // One flight as a bare walkable plane, 1.7 m up over 2.8 m toward -x, with the floor running on under it.
  const geometry=new T.BufferGeometry();
  geometry.setAttribute('position',new T.Float32BufferAttribute([0,0,-.78, 0,0,.78, -2.8,1.7,.78, -2.8,1.7,-.78],3));
  geometry.setIndex([0,2,1,0,3,2]);
  const flight=new T.Mesh(geometry,new T.MeshStandardMaterial());flight.name='flight';
  room.add(block('floor',[8,.1,4],[0,-.05,0]),flight);
  walker.add('service',room);
  try{
    for(const speed of [4,6])for(const x of [-.3,-.6,-.9,-1.2]){
      const {landed,walkedBack}=hopUp(walker,[x,-x*1.7/2.8,0],[-1,0],speed);
      const ramp=Math.min(1.7,-landed.x*1.7/2.8);
      assert(landed.y>ramp-.05,`hop from x ${x} at ${speed} m/s left the feet at ${landed.y.toFixed(2)}, the ramp is at ${ramp.toFixed(2)}`);
      assert(walkedBack>.2,`the visitor cannot walk away after a hop from x ${x}`);
    }
  }finally{walker.dispose();}
});

test('the service stair keeps a visitor who hops up its first flight (7.4.0 tester report)',async()=>{
  const zones=new URL('../assets/expo/zones/',import.meta.url);
  const plan=JSON.parse(await readFile(new URL('zones.json',zones)));
  const def=plan.zones.find(zone=>zone.id==='service');
  await MeshoptDecoder.ready;
  const bytes=await readFile(new URL(def.collision,zones));
  const gltf=await new Promise((resolve,reject)=>new GLTFLoader().setMeshoptDecoder(MeshoptDecoder)
    .parse(bytes.buffer.slice(bytes.byteOffset,bytes.byteOffset+bytes.byteLength),'',resolve,reject));
  const walker=createWalker(new T.PerspectiveCamera());
  walker.add('service',gltf.scene,def.crossings);
  try{
    // The first flight climbs from x 26.2 (floor) to x 23.4 (half landing, 1.7 m) along z 22.03.
    for(const speed of [4,6])for(const x of [25.9,25.6,25.3,25])for(const z of [21.6,22.03,22.45]){
      const {landed,walkedBack}=hopUp(walker,[x,(26.2-x)*1.7/2.8,z],[-1,0],speed);
      assert(landed.y>.3,`hop from ${x}, ${z} at ${speed} m/s dropped the visitor to the floor under the flight`);
      assert(walkedBack>.2,`the visitor is stuck after a hop from ${x}, ${z}`);
    }
  }finally{walker.dispose();}
});

test('a dormant projector can wake, fade fully dark, and wake again',()=>{
  const room=new T.Group(),scene=new T.Group(),id='slot_01';
  const screen=block('expo_screen_'+id,[1,.7,.05],[0,1,0]),lens=block('expo_lens_'+id,[.2,.2,.2],[0,2,-1]);
  room.add(screen,lens);room.updateMatrixWorld(true);
  const atlas={texture:new T.Texture(),index:new Map()};
  const projector=createProjector({group:room,id,scene,assetURL:p=>p,atlas,lite:true});
  const beam=scene.children[0];
  try{
    projector.step(.1,false,4,undefined,0,undefined);
    assert.equal(projector.stats.lamp,0);assert.equal(screen.material.uniforms.hasProgram.value,0);assert(!beam.visible);
    projector.setActive(true);projector.step(.3,false,14,'slide',.5);
    assert(projector.stats.lamp>.1);assert.equal(screen.material.uniforms.hasProgram.value,1);
    projector.step(.1,false,4,undefined,.5);assert(projector.stats.zoom>0);
    projector.setActive(false);for(let i=0;i<100;i++)projector.step(.2,true,4);
    assert.equal(projector.stats.lamp,0);assert.equal(projector.stats.zoom,0);assert(!beam.visible);
    projector.setActive(true);projector.step(.1,false,14,'slide',.5);assert(projector.stats.lamp>.1);
  }finally{projector.dispose();atlas.texture.dispose();}
});
