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
const { createBoardWaves } = await import('../board/board-waves.js');

function fixture(t, withPiece=false) {
  const scene=new THREE.Scene(),boardGroup=new THREE.Group(),squares=new Map();
  scene.add(boardGroup);
  const original=new THREE.BoxGeometry(1,.16,1),material=new THREE.MeshStandardMaterial();
  for(let x=0;x<8;x++) for(let z=0;z<8;z++) {
    const square=String.fromCharCode(97+x)+(8-z),mesh=new THREE.Mesh(original,material);
    mesh.position.set(x-3.5,-.08,z-3.5);mesh.userData.square=square;
    squares.set(square,mesh);boardGroup.add(mesh);
  }
  const piece=new THREE.Group();piece.position.set(.5,0,.5);
  piece.userData={square:'e4',type:'p',side:'w'};scene.add(piece);
  const pieces={pieces:new Map(withPiece?[['e4',piece]]:[])};
  const waves=Array.from({length:8},()=>new THREE.Vector4(0,0,-100,0));
  const surface=createBoardWaves({view:{scene,boardGroup,squares},pieces,waves,life:1.15});
  t.after(()=>{surface.dispose();original.dispose();material.dispose();});
  return {surface,squares,original,material,piece,waves,boardGroup};
}

function topVertices(squares) {
  const vertices=[];
  for(const mesh of squares.values()) {
    const position=mesh.geometry.getAttribute('position'),normal=mesh.geometry.getAttribute('normal');
    for(let i=0;i<position.count;i++) {
      if(normal.getY(i)<.5) continue;
      vertices.push({x:mesh.position.x+position.getX(i),y:mesh.position.y+position.getY(i),z:mesh.position.z+position.getZ(i),normal:[normal.getX(i),normal.getY(i),normal.getZ(i)]});
    }
  }
  return vertices;
}

test('a landing bends actual subdivided square tops with matching borders and normals', t => {
  const {surface,squares,waves}=fixture(t);
  waves[0].set(0,0,0,.8);surface.update(.25);
  const vertices=topVertices(squares);
  assert.ok(vertices.length>64*100,'each square has enough real vertices to bend');
  assert.ok(vertices.some(v=>Math.abs(v.y)>.001),'the board geometry itself bends');
  assert.ok(vertices.every(v=>Number.isFinite(v.y)&&v.normal.every(Number.isFinite)));
  const edges=new Map();let shared=0;
  for(const vertex of vertices) {
    const key=vertex.x.toFixed(6)+','+vertex.z.toFixed(6);
    const previous=edges.get(key);
    if(previous) {
      shared++;
      assert.ok(Math.abs(previous.y-vertex.y)<1e-6,'neighbouring square tops never split');
      vertex.normal.forEach((value,i)=>assert.ok(Math.abs(value-previous.normal[i])<1e-6,'shared borders use the same surface normal'));
    } else edges.set(key,vertex);
    if(Math.abs(vertex.x)>=3.99999||Math.abs(vertex.z)>=3.99999) assert.ok(Math.abs(vertex.y)<1e-7,'the perimeter stays fixed');
  }
  assert.ok(shared>500,'continuity was checked across the full tile grid');
  for(const mesh of squares.values()) {
    const p=mesh.geometry.getAttribute('position'),n=mesh.geometry.getAttribute('normal');
    for(let i=0;i<p.count;i++) if(n.getY(i)<-.9) assert.ok(Math.abs(p.getY(i)+.08)<1e-7,'the tile bottoms remain fixed');
  }
});

test('waves stay local and small, expire fully, and a capture bends slightly more', t => {
  const normal=fixture(t),capture=fixture(t);
  normal.waves[0].set(.5,-.5,0,.8);capture.waves[0].set(.5,-.5,0,1.25);
  let normalPeak=0,capturePeak=0;
  for(const age of [.06,.15,.25,.4,.6,.85,1.1]) {
    normal.surface.update(age);capture.surface.update(age);
    normalPeak=Math.max(normalPeak,...topVertices(normal.squares).map(v=>Math.abs(v.y)));
    capturePeak=Math.max(capturePeak,...topVertices(capture.squares).map(v=>Math.abs(v.y)));
    for(const [x,z] of [[-3.5,-3.5],[3.5,3.5],[-3.5,3.5],[3.5,-3.5]]) {
      assert.ok(Math.abs(normal.surface.sample(x,z))<1e-7,'a local footfall does not bend distant squares');
    }
  }
  assert.ok(normalPeak>.001 && normalPeak<.10,'ordinary steps use a small visible bend');
  assert.ok(capturePeak>normalPeak*1.15 && capturePeak<normalPeak*1.8,'capture gain is stronger without changing the whole effect');
  assert.ok(capturePeak<.10,'captures remain a small board bend');
  normal.surface.update(1.3);
  assert.ok(topVertices(normal.squares).every(v=>Math.abs(v.y)<1e-7),'an expired wave leaves no residual height');
});

test('grounded pieces retain flat support and all logical transforms remain untouched', t => {
  const planted=fixture(t,true),empty=fixture(t);
  planted.waves[0].set(.5,-.5,0,1.25);empty.waves[0].set(.5,-.5,0,1.25);
  const position=planted.piece.position.toArray(),rotation=planted.piece.quaternion.toArray();
  let freeHeight=0;
  for(const age of [.08,.16,.24,.36,.5]) {
    planted.surface.update(age);empty.surface.update(age);
    for(const [dx,dz] of [[0,0],[.16,0],[-.16,0],[0,.16],[0,-.16]]) {
      assert.ok(Math.abs(planted.surface.sample(.5+dx,.5+dz))<1e-7,'the planted footprint remains flat');
      freeHeight=Math.max(freeHeight,Math.abs(empty.surface.sample(.5+dx,.5+dz)));
    }
    assert.deepEqual(planted.piece.position.toArray(),position);
    assert.deepEqual(planted.piece.quaternion.toArray(),rotation);
  }
  assert.ok(freeHeight>.0005,'the support check sampled genuinely moving terrain');
  planted.piece.position.y=.6;planted.piece.userData.held=true;
  planted.surface.update(.24);empty.surface.update(.24);
  assert.ok(Math.abs(planted.surface.sample(.5,.5)-empty.surface.sample(.5,.5))<1e-7,'an airborne piece does not pin the terrain');
  assert.equal(planted.piece.position.y,.6,'drag lift remains owned by drag');
});

test('reset restores a flat board and disposal restores the original shared geometry exactly', t => {
  const {surface,squares,waves,original,material}=fixture(t);
  const transforms=[...squares.values()].map(mesh=>mesh.matrix.clone());
  let originalDisposals=0,ownedDisposals=0;
  original.addEventListener('dispose',()=>originalDisposals++);
  waves[0].set(0,0,0,1.25);surface.update(.25);
  const owned=new Set([...squares.values()].map(mesh=>mesh.geometry).filter(geometry=>geometry!==original));
  assert.ok(owned.size>0);
  for(const geometry of owned) geometry.addEventListener('dispose',()=>ownedDisposals++);
  surface.reset();
  assert.ok(topVertices(squares).every(v=>Math.abs(v.y)<1e-7&&Math.abs(v.normal[0])<1e-7&&Math.abs(v.normal[1]-1)<1e-7&&Math.abs(v.normal[2])<1e-7),'reset removes all height and slope');
  surface.dispose();
  [...squares.values()].forEach((mesh,index)=>{
    assert.equal(mesh.geometry,original);assert.equal(mesh.material,material);
    assert.deepEqual(mesh.matrix.elements,transforms[index].elements);
  });
  assert.equal(originalDisposals,0,'the shared original geometry belongs to the scene');
  assert.equal(ownedDisposals,owned.size,'each replacement geometry is released');
  surface.dispose();assert.equal(ownedDisposals,owned.size);
});

test('move markers follow the bent surface and release temporary geometry after settling', t => {
  const {surface,waves,boardGroup}=fixture(t);
  const markers=new THREE.Group();markers.name='pbp-markers';boardGroup.add(markers);
  const original=new THREE.PlaneGeometry(.92,.92).rotateX(-Math.PI/2);
  const material=new THREE.MeshBasicMaterial();
  const marker=new THREE.Mesh(original,material);marker.position.set(.5,.012,.5);markers.add(marker);
  t.after(()=>{original.dispose();material.dispose();});
  waves[0].set(0,0,0,.8);surface.update(.25);
  assert.notEqual(marker.geometry,original);
  const p=marker.geometry.attributes.position;
  assert.ok(p.count>100,'the ink also has enough subdivisions to follow a crease');
  for(let i=0;i<p.count;i++) assert.ok(Math.abs(p.getY(i)-surface.sample(p.getX(i)+.5,p.getZ(i)+.5))<1e-6);
  surface.update(1.3);
  assert.equal(marker.geometry,original);
});
