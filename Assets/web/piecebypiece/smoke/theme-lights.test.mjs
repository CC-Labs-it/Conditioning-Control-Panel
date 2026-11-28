import test from 'node:test';
import assert from 'node:assert/strict';
import { register } from 'node:module';
const three=new URL('../../vendor/three/three.module.min.js',import.meta.url).href;
register('data:text/javascript,'+encodeURIComponent(`
  export async function resolve(id,context,next) {
    if(id==='three') return {url:'${three}',shortCircuit:true};
    return next(id,context);
  }`));
const THREE=await import('three');
const {createThemeLights}=await import('../board/theme-lights.js');
const {createBus}=await import('../game/events.js');
const {setPresentation}=await import('../game/preferences.js');

function fixture() {
  setPresentation({reducedMotion:false,boardMotion:'full'});
  const scene=new THREE.Scene(),boardGroup=new THREE.Group(),bus=createBus();
  scene.add(boardGroup);
  const trim=new THREE.Group(); trim.name='theme-arcade'; boardGroup.add(trim);
  const neon=new THREE.MeshStandardMaterial({color:0x11ffff,emissive:0x11ffff,emissiveIntensity:1.2});
  neon.userData.themeLamp='cyan';
  const bulb=new THREE.MeshBasicMaterial({color:0xffeebb}); bulb.userData.themeLamp='bulb';
  const face=new THREE.MeshBasicMaterial({color:0xeebbee});
  const shape=new THREE.BoxGeometry(.1,.1,.1);
  trim.add(new THREE.Mesh(shape,neon),new THREE.Mesh(shape,neon),new THREE.Mesh(shape,bulb));
  // Emi is a separate scene subtree, even if one of her materials acquires a similar tag.
  face.userData.themeLamp='bulb'; boardGroup.add(new THREE.Mesh(shape,face));
  const lights=createThemeLights({view:{scene,boardGroup},bus}); lights.setTheme('chess.board.arcade');
  return {scene,boardGroup,bus,trim,neon,bulb,face,lights};
}
function advance(lights,seconds) { for(let i=0;i<Math.ceil(seconds*20);i++) lights.update(.05); }
const peak=lights=>Math.max(...lights.debug().intensities);

test('four fixed actual lights distinguish themes and never cast shadows or allocate extra geometry',()=>{
  const {boardGroup,lights}=fixture();
  const rig=boardGroup.getObjectByName('pbp-theme-lights'),objects=[...rig.children],signatures=[];
  for(const id of ['observatory','conservatory','theatre','arcade']) {
    lights.setTheme('chess.board.'+id);
    assert.equal(lights.debug().theme,id);
    assert.equal(rig.children.length,4);
    assert.ok(rig.children.every((light,i)=>light===objects[i] && light.isPointLight && !light.castShadow && light.distance<4));
    assert.ok(rig.children.every(light=>Math.abs(light.position.x)>4 || Math.abs(light.position.z)>4));
    signatures.push(rig.children.map(light=>light.color.getHex()+':'+light.intensity).join());
  }
  assert.equal(new Set(signatures).size,4);
  lights.setTheme('chess.board.unknown');
  assert.equal(rig.visible,false); assert.ok(rig.children.every(light=>light.intensity===0));
  lights.dispose();
});

test('physical land cadence brightens real lights and existing source materials, capture is stronger and settles',()=>{
  const {bus,lights,neon,bulb,face}=fixture(),faceColor=face.color.clone(),bulbColor=bulb.color.clone();
  assert.equal(lights.debug().sources,2,'merged trim can share a single material');
  const baseline=peak(lights);
  bus.emit('land',{world:{x:0,z:3.5}});
  bus.emit('theme-react',{kind:'land',world:{x:0,z:3.5},strength:.8});
  assert.equal(lights.debug().reactions,1,'the forwarded contact is not a second pulse');
  advance(lights,.5); const land=peak(lights);
  assert.ok(land>baseline+.5);
  assert.ok(neon.emissiveIntensity>1.2 && bulb.color.r>bulbColor.r);
  assert.ok(face.color.equals(faceColor),'Emi face and screens are untouched');
  advance(lights,2); assert.equal(peak(lights),baseline);
  assert.equal(neon.emissiveIntensity,1.2); assert.ok(bulb.color.equals(bulbColor));
  bus.emit('hit',{world:{x:0,z:3.5}}); advance(lights,.5);
  assert.ok(peak(lights)>land);
  advance(lights,3); assert.equal(peak(lights),baseline);
  const count=lights.debug().reactions;
  bus.emit('land',{world:{x:1,z:2},skipped:true}); bus.emit('land',{world:{x:9,z:2}});
  assert.equal(lights.debug().reactions,count);
  lights.dispose();
});

test('check suspense and result swells remain bounded and fully return to steady light',()=>{
  const {bus,lights}=fixture(),baseline=peak(lights);
  bus.emit('check',{side:'b'}); advance(lights,.5);
  assert.ok(peak(lights)>baseline);
  advance(lights,3); assert.equal(peak(lights),baseline);
  bus.emit('gameover',{winner:'w'}); advance(lights,.5);
  assert.ok(lights.debug().intensities.every(value=>value>baseline));
  assert.ok(peak(lights)<=baseline+3.2);
  advance(lights,4); assert.equal(peak(lights),baseline);
  lights.dispose();
});

test('zero dt, hidden, Still and reduced motion clear pending pulses while retaining steady baseline',()=>{
  const {bus,lights,neon}=fixture(),originalDocument=globalThis.document;
  try {
    for(const mode of ['pause','hidden','still','reduced']) {
      lights.update(.05);
      bus.emit('land',{world:{x:0,z:3.5}}); advance(lights,.4);
      assert.ok(peak(lights)>lights.debug().baseline);
      const count=lights.debug().reactions;
      if(mode==='hidden') globalThis.document={hidden:true};
      if(mode==='still') setPresentation({boardMotion:'still'});
      if(mode==='reduced') setPresentation({reducedMotion:true});
      lights.update(0);
      const time=lights.debug().clock;
      bus.emit('hit',{world:{x:0,z:3.5}});
      assert.equal(lights.debug().active,0); assert.equal(peak(lights),lights.debug().baseline);
      assert.equal(neon.emissiveIntensity,1.2);
      globalThis.document=originalDocument;
      setPresentation({boardMotion:'full',reducedMotion:false});
      advance(lights,2);
      assert.equal(lights.debug().reactions,count,'no suppressed contact is queued for resume');
      assert.ok(lights.debug().clock>time);
    }
  } finally { globalThis.document=originalDocument; setPresentation({boardMotion:'full',reducedMotion:false}); lights.dispose(); }
});

test('theme rebinding restores borrowed materials and disposal removes all listeners and owned lights',()=>{
  const {bus,lights,neon,bulb,trim,boardGroup}=fixture(),bulbColor=bulb.color.clone();
  let disposed=0,borrowedDisposed=0;
  for(const light of boardGroup.getObjectByName('pbp-theme-lights').children) {
    const release=light.dispose.bind(light);
    light.dispose=()=>{ disposed++; release(); };
  }
  for(const material of [neon,bulb]) material.addEventListener('dispose',()=>borrowedDisposed++);
  bus.emit('land',{world:{x:0,z:3.5}}); advance(lights,.5);
  trim.removeFromParent();
  const replacement=new THREE.Group(); replacement.name='theme-arcade'; boardGroup.add(replacement);
  lights.update(.05);
  assert.equal(neon.emissiveIntensity,1.2); assert.ok(bulb.color.equals(bulbColor));
  assert.equal(lights.debug().sources,0);
  const count=lights.debug().reactions;
  lights.dispose(); lights.dispose();
  bus.emit('land',{world:{x:0,z:3.5}}); setPresentation({reducedMotion:true});
  assert.equal(lights.debug().reactions,count);
  assert.equal(disposed,4); assert.equal(borrowedDisposed,0);
  assert.equal(boardGroup.getObjectByName('pbp-theme-lights'),undefined);
  setPresentation({reducedMotion:false});
});
