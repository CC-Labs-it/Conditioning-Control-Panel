import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../../vendor/three/three.module.min.js';
import {createOcclusionQuery} from '../ui/documentary/occlusion.js';

test('accelerated occlusion matches double-sided mesh rays with near/far clipping', () => {
 const geometry=new THREE.BoxGeometry(8,4,12,16,1,48);
 geometry.rotateY(.4); geometry.translate(3,2,-8);
 const mesh=new THREE.Mesh(geometry,new THREE.MeshBasicMaterial({side:THREE.DoubleSide}));
 mesh.updateMatrixWorld(true);
 const query=createOcclusionQuery(THREE,geometry), caster=new THREE.Raycaster();
 for(const origin of [new THREE.Vector3(),new THREE.Vector3(3,2,-8),new THREE.Vector3(8,6,4)]) {
  for(let x=-10;x<=10;x++) for(let y=-2;y<=6;y+=2) for(const far of [.02,3,10,40]) {
   caster.set(origin,new THREE.Vector3(x,y,-12).sub(origin).normalize());
   caster.near=.05;caster.far=far;
   assert.equal(query(caster.ray,caster.near,far),caster.intersectObject(mesh,false).length>0);
  }
 }
 geometry.dispose(); mesh.material.dispose();
});

test('non-indexed triangles and a target before the wall stay exact', () => {
 const geometry=new THREE.PlaneGeometry(4,4,5,5).toNonIndexed();
 const query=createOcclusionQuery(THREE,geometry);
 const ray=new THREE.Ray(new THREE.Vector3(0,0,2),new THREE.Vector3(0,0,-1));
 assert.equal(query(ray,.05,1.9),false);
 assert.equal(query(ray,.05,2),true);
 assert.equal(query(ray,2.1,4),false);
 geometry.dispose();
});
