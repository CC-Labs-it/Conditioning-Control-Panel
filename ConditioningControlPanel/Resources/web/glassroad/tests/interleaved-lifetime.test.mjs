import test from 'node:test';
import assert from 'node:assert/strict';
import { WebGPURenderer, InstancedInterleavedBuffer, InterleavedBufferAttribute } from '../vendor/three/build/three.webgpu.min.js';
import { releaseSceneResources } from '../render/sceneResources.js';

test('retiring node matrix views clears the real backend canonical buffer before a course reuses it', t => {
  const previous=globalThis.GPUBufferUsage;
  globalThis.GPUBufferUsage={VERTEX:32,COPY_SRC:4,COPY_DST:8};
  t.after(()=>{if(previous===undefined)delete globalThis.GPUBufferUsage;else globalThis.GPUBufferUsage=previous;});
  const three=new WebGPURenderer({canvas:{width:10,height:10,style:{}}}), backend=three.backend;
  let builds=0;
  backend.device={createBuffer:desc=>{const size=desc.size;return{id:++builds,destroyed:false,getMappedRange:()=>new ArrayBuffer(size),unmap(){},destroy(){this.destroyed=true;}};}};
  const data=new InstancedInterleavedBuffer(new Float32Array(128),16);
  const a=new InterleavedBufferAttribute(data,4,0), b=new InterleavedBufferAttribute(data,4,4);
  backend.createAttribute(a);backend.createAttribute(b);
  const first=backend.get(data).buffer;
  const scene={},rows=new Set();
  rows.add({scene,geometry:{attributes:{}},attributes:[a,b],getAttributes(){return this.attributes;},dispose(){rows.delete(this);}});
  three._objects={_renderObjects:rows};
  three._attributes={delete:attribute=>backend.destroyAttribute(attribute)};
  releaseSceneResources(three,scene);
  assert.equal(first.destroyed,true);
  backend.createAttribute(a);
  const next=backend.get(data).buffer;
  assert.notEqual(next,first,'the next course needs a fresh live buffer');
  assert.equal(next.destroyed,false);
  assert.equal(builds,2);
});
