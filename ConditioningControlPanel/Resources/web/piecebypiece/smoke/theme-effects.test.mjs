import test from 'node:test';
import assert from 'node:assert/strict';
import { register } from 'node:module';

const three = new URL('../../vendor/three/three.module.min.js', import.meta.url).href;
register('data:text/javascript,' + encodeURIComponent(`
  export async function resolve(id,context,next) {
    if(id==='three') return {url:'${three}',shortCircuit:true};
    return next(id,context);
  }`));
const THREE = await import('three');
const { createThemeEffects } = await import('../board/theme-effects.js');
const { createBus } = await import('../game/events.js');
const { setPresentation } = await import('../game/preferences.js');

function fixture() {
  const bus = createBus(), scene = new THREE.Scene();
  const king = new THREE.Group();
  king.userData = { square:'e1',type:'k',side:'w' };
  king.position.set(.5,0,3.5);
  const pieces = { pieces:new Map([['e1',king]]) };
  const effects = createThemeEffects({ view:{scene},bus,pieces });
  effects.setTheme('chess.board.observatory');
  return {bus,scene,king,effects};
}
function advance(effects,seconds) { for (let i=0;i<seconds*20;i++) effects.update(.05); }

test('move takeoff, intermediate hops and actual landing keep their own cadence', () => {
  setPresentation({reducedMotion:false});
  const {bus,king,effects,scene} = fixture();
  const beats = [];
  bus.on('theme-react',event=>beats.push(event));
  effects.update(.05);
  king.userData.square='e3';
  effects.update(.05);
  assert.equal(beats[0].kind,'move');
  assert.deepEqual(beats[0].world,{x:.5,y:0,z:3.5});
  advance(effects,.3);
  bus.emit('hopLand',{world:{x:.5,y:0,z:2.5},small:false});
  bus.emit('hopLand',{world:{x:.5,y:0,z:2.5},small:false});
  assert.equal(beats.length,2,'one contact cannot double its burst');
  advance(effects,.4);
  bus.emit('land',{world:{x:.5,y:0,z:1.5},side:'w',capture:false});
  assert.equal(beats.length,3);
  assert.equal(beats[2].strength,.8);
  assert.deepEqual(king.position.toArray(),[.5,0,3.5],'no cosmetic lift or input displacement');
  assert.ok(effects.debug().active>0);
  advance(effects,2);
  assert.equal(effects.debug().active,0);
  assert.equal(effects.debug().waves,0);
  assert.equal(effects.debug().light,0);
  effects.dispose();
  assert.equal(scene.children.length,0);
});

test('theme switches, skipped landings, pause and reduced motion leave no delayed burst', () => {
  const {bus,effects,scene,king} = fixture();
  for (const [id,index] of [['observatory',0],['conservatory',1],['theatre',2],['arcade',3]]) {
    effects.setTheme('chess.board.'+id);
    assert.equal(effects.debug().theme,id);
    assert.equal(scene.children[0].children[0].material.uniforms.uStyle.value,index);
    effects.update(.05);
    bus.emit('land',{world:{x:1,y:0,z:1},skipped:true});
    bus.emit('land',{world:{x:1,y:0,z:1},refused:true});
    assert.equal(effects.debug().active,0);
    effects.update(0);
    const before=effects.debug().bursts;
    king.userData.square = king.userData.square==='e1' ? 'e2' : 'e1';
    effects.update(0);
    bus.emit('land',{world:{x:1,y:0,z:1}});
    advance(effects,.2);
    assert.equal(effects.debug().bursts,before);
    bus.emit('hit',{world:{x:1,y:1,z:1}});
    effects.update(.01);
    assert.ok(effects.debug().active>0);
    setPresentation({reducedMotion:true});
    assert.equal(effects.debug().active,0);
    const clock=effects.debug().clock;
    advance(effects,1);
    bus.emit('land',{world:{x:1,y:0,z:1}});
    assert.equal(effects.debug().clock,clock);
    assert.equal(effects.debug().active,0);
    setPresentation({reducedMotion:false});
  }
  effects.setTheme(null);
  bus.emit('land',{world:{x:1,y:0,z:1}});
  assert.equal(scene.children[0].visible,false);
  assert.equal(effects.debug().active,0);
  effects.dispose();
});

test('pool saturation and repeated disposal release resources and listeners', () => {
  const {bus,effects,scene} = fixture();
  let disposals=0;
  scene.traverse(o=>{
    o.geometry?.addEventListener('dispose',()=>disposals++);
    o.material?.addEventListener('dispose',()=>disposals++);
  });
  for(let n=0;n<100;n++) {
    effects.update(.1);
    bus.emit('land',{world:{x:(n%7)-3,y:0,z:1},capture:true});
  }
  assert.ok(effects.debug().active<=effects.debug().pool);
  assert.ok(effects.debug().waves<=8);
  const bursts=effects.debug().bursts;
  effects.dispose(); effects.dispose();
  bus.emit('land',{world:{x:0,y:0,z:0}});
  assert.equal(effects.debug().bursts,bursts);
  assert.equal(disposals,2);
  assert.equal(scene.children.length,0);
});

test('hidden contacts and Still preference cannot queue a response before the next frame', () => {
  setPresentation({reducedMotion:false,boardMotion:'full'});
  const {bus,effects,king} = fixture();
  const originalDocument=globalThis.document;
  try {
    effects.update(.05);
    const before=effects.debug().bursts;
    globalThis.document={hidden:true};
    king.userData.square='e2';
    bus.emit('land',{world:{x:.5,y:0,z:2.5}});
    globalThis.document.hidden=false;
    effects.update(.05);
    assert.equal(effects.debug().bursts,before,'hidden network landing is not replayed on return');
    setPresentation({boardMotion:'still'});
    bus.emit('land',{world:{x:.5,y:0,z:1.5}});
    assert.equal(effects.debug().bursts,before,'Still applies before a frame can set dt to zero');
    setPresentation({boardMotion:'full'});
    effects.update(.05);
    assert.equal(effects.debug().bursts,before);
    bus.emit('land',{world:{x:.5,y:0,z:1.5}});
    assert.equal(effects.debug().bursts,before+1,'fresh real contact resumes normally');
  } finally {
    globalThis.document=originalDocument;
    setPresentation({reducedMotion:false,boardMotion:'full'});
    effects.dispose();
  }
});
