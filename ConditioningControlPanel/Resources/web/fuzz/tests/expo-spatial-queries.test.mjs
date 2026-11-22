import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
import test from 'node:test';

const vendor=new URL('../../vendor/three/',import.meta.url);
const hook=registerHooks({resolve(name,ctx,next){
  return name==='three'?{shortCircuit:true,url:new URL('three.module.min.js',vendor).href}:next(name,ctx);
}});
const T=await import('three');
const {createWalker}=await import('../ui/expo/collision.js');
const {createProjector}=await import('../ui/expo/projectors.js');
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
