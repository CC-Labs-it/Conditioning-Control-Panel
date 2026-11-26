/* A retired cap takes the room's base ring with it (ui/expo/cap-ring.js): the Habitat glass and plaque caps left in
 * #5601, but their dark rings stayed on the room mesh in front of the keeper's desk. */
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import test from 'node:test';
const T=await import(new URL('../../vendor/three/three.module.min.js',import.meta.url).href);
const {liftCapRing}=await import('../ui/expo/cap-ring.js');

// A flat ring (two triangles) on the room's Architecture mesh in front of a wall cap, plus one far triangle.
function room(){
  const scene=new T.Group(),architecture=new T.Group();architecture.name='Architecture';scene.add(architecture);
  const geom=new T.BufferGeometry();
  geom.setAttribute('position',new T.Float32BufferAttribute([
    19.34,1.15,9.52, 19.56,1.15,9.52, 19.56,1.37,9.52,     // ring, under the cap
    19.34,1.15,9.52, 19.56,1.37,9.52, 19.34,1.37,9.52,
    10,0,0, 11,0,0, 11,1,0,                              // some other cap's ring, elsewhere
  ],3));
  geom.setIndex([0,1,2,3,4,5,6,7,8]);
  const ink=new T.MeshBasicMaterial();ink.name='action_ink';
  const ring=new T.Mesh(geom,ink);ring.name='Architecture_11';architecture.add(ring);
  const wall=new T.Mesh(new T.BoxGeometry(1,1,1),new T.MeshBasicMaterial());wall.name='Architecture_1';wall.material.name='tile';
  architecture.add(wall);
  const cap=new T.Group();cap.name='expo_press_habitat_glass';
  const face=new T.Mesh(new T.CylinderGeometry(.095,.095,.026),ink);face.name='expo_press_habitat_glass_1';
  face.rotation.x=Math.PI/2;cap.add(face);cap.position.set(19.45,1.26,9.5);scene.add(cap);
  return {scene,ring,cap,wall};
}

test('the ring a retired cap sat on leaves with it, and comes back on unmount', () => {
  const {scene,ring,cap,wall}=room();
  const wallIndex=wall.geometry.index;
  const restore=liftCapRing(T,scene,cap);
  assert.deepEqual([...ring.geometry.index.array],[6,7,8],'only the triangles inside the cap box go');
  assert.equal(wall.geometry.index,wallIndex,'other materials are never touched');
  assert.equal(cap.children[0].geometry.index.count>0,true,'the cap itself is not cut');
  restore();
  assert.deepEqual([...ring.geometry.index.array],[0,1,2,3,4,5,6,7,8]);
});

test('no cap, no change', () => {
  const {scene,ring}=room();
  liftCapRing(T,scene,null)();
  assert.equal(ring.geometry.index.count,9);
});

test('the Habitat lifts the rings of both caps it retires', () => {
  const src=readFileSync(new URL('../ui/expo/habitat.js',import.meta.url),'utf8');
  assert.ok(src.includes("import {liftCapRing} from './cap-ring.js';"));
  assert.ok(src.includes("undo.push(liftCapRing(T,group,obj('expo_press_'+name)));"));
});
