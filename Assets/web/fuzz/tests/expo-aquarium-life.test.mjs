import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../../vendor/three/three.module.min.js';
import {swimPose,mountAquariumLife} from '../ui/expo/aquarium-life.js';

test('nose follows the real three-dimensional orbit on both halves and through each turn',()=>{
  const motion=[1.23,.085,.39,18,.65],a=new THREE.Vector3(),b=a.clone(),dir=a.clone(),spare=a.clone();
  for(let t=0;t<18;t+=.1){
    swimPose(motion,t-.001,a,spare);swimPose(motion,t+.001,b,spare);swimPose(motion,t,spare,dir);
    assert(b.sub(a).normalize().dot(dir)>.999999);
    assert(Math.abs(dir.dot(new THREE.Vector3(0,1,0)))<.25);
  }
});

test('rendered animal batches preserve native position, size, direction, and the observed Gentle pose',()=>{
  const room=new THREE.Group(),reference=new THREE.BoxGeometry(1,.3,.2),paint=new THREE.MeshStandardMaterial();
  const sources=[];
  for(let i=0;i<2;i++){
    const head=i*.92,origin=[4+i,2,-1+i],scale=.5+i*.3;
    const node=new THREE.Mesh(reference,paint);node.name='fish_'+i;
    node.position.fromArray(origin);node.rotation.y=head;node.scale.setScalar(scale);
    node.userData={aquariumSpecies:'silverfish',aquariumSize:scale,aquariumHeading:head,aquariumOrigin:origin,aquariumSwim:[1.15,.06,.42,17,.3+i*2]};
    room.add(node);sources.push(node);
  }
  const life=mountAquariumLife(THREE,room,false),batch=room.getObjectByName('Swimming_silverfish_fish_0');
  assert.equal(batch.count,2);assert(sources.every(s=>!s.visible));life.tick(2.1,false);
  const m=new THREE.Matrix4(),centre=new THREE.Vector3(),direction=new THREE.Vector3();
  sources.forEach((source,i)=>{
    const expected=new THREE.Vector3(),tangent=new THREE.Vector3();
    swimPose(source.userData.aquariumSwim,2.1,expected,tangent);expected.add(source.position);
    batch.getMatrixAt(i,m);assert(centre.setFromMatrixPosition(m).distanceTo(expected)<1e-5);
    assert(direction.set(1,0,0).transformDirection(m).dot(tangent)>.999999);
  });
  const before=Array.from(batch.instanceMatrix.array);life.tick(30,true);assert.deepEqual(Array.from(batch.instanceMatrix.array),before);
  life.dispose();assert(sources.every(s=>s.visible));assert.equal(room.children.length,2);reference.dispose();paint.dispose();
});
