import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import * as T from '../vendor/three/build/three.webgpu.min.js';
const three=new URL('../vendor/three/build/three.webgpu.min.js',import.meta.url).href;
const tsl=readFileSync(new URL('../vendor/three/build/three.tsl.min.js',import.meta.url),'utf8').replaceAll('"three/webgpu"',JSON.stringify(three));
const source=readFileSync(new URL('../render/garageStyle.js',import.meta.url),'utf8')
  .replace("'three/webgpu'",JSON.stringify(three)).replace("'three/tsl'",JSON.stringify('data:text/javascript;base64,'+Buffer.from(tsl).toString('base64')));
const {createGarageStyle}=await import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));

test('garage paint and outlines leave shared source materials and vertex buffers intact',()=>{
  const style=createGarageStyle(),geo=new T.BoxGeometry(),material=new T.MeshStandardNodeMaterial({color:0xf479b0}),mesh=new T.Mesh(geo,material);
  const attributes=Object.keys(geo.attributes);style.apply(mesh);style.apply(mesh);
  assert.notEqual(mesh.material,material);assert.ok(mesh.material.isMeshToonNodeMaterial);
  assert.equal(material.color.getHex(),0xf479b0);assert.deepEqual(Object.keys(geo.attributes),attributes);
  assert.equal(mesh.children.length,1);assert.equal(mesh.children[0].geometry,geo);
  const hits=[];mesh.children[0].raycast(new T.Raycaster(),hits);assert.equal(hits.length,0);
  style.dispose();geo.dispose();material.dispose();
});

test('outline shares the exact accepted rig and bind through a moving bone',()=>{
  const style=createGarageStyle(),geo=new T.BoxGeometry(),n=geo.getAttribute('position').count;
  geo.setAttribute('skinIndex',new T.Uint16BufferAttribute(new Array(n*4).fill(0),4));
  geo.setAttribute('skinWeight',new T.Float32BufferAttribute(Array.from({length:n*4},(_,i)=>i%4===0?1:0),4));
  const bone=new T.Bone(),mesh=new T.SkinnedMesh(geo,new T.MeshStandardNodeMaterial());mesh.add(bone);mesh.bind(new T.Skeleton([bone]));
  style.apply(mesh);const hull=mesh.children.find(m=>m.userData.garageInk);assert.equal(hull.skeleton,mesh.skeleton);
  assert.deepEqual(hull.bindMatrix.elements,mesh.bindMatrix.elements);bone.rotation.z=.85;bone.position.y=.4;mesh.updateMatrixWorld(true);
  const a=new T.Vector3(),b=new T.Vector3();mesh.getVertexPosition(3,a);hull.getVertexPosition(3,b);assert.ok(a.distanceTo(b)<1e-8);
  style.dispose();geo.dispose();mesh.material.dispose();
});

test('face pixels, transparent lights and locked silhouettes retain their original materials',()=>{
  const style=createGarageStyle(),root=new T.Group(),geo=new T.BoxGeometry();
  for(const name of ['emi_screen','glass']){const material=new T.MeshStandardNodeMaterial({name,transparent:name==='glass'}),mesh=new T.Mesh(geo,material);root.add(mesh);}
  const materials=root.children.map(m=>m.material);style.apply(root);
  root.children.forEach((m,i)=>{assert.equal(m.material,materials[i]);assert.equal(m.children.length,0);});
  const silhouette=new T.Mesh(geo,new T.MeshBasicNodeMaterial({color:0x211e24})),original=silhouette.material;
  style.apply(silhouette,{locked:true});assert.equal(silhouette.material,original);style.dispose();geo.dispose();
});
