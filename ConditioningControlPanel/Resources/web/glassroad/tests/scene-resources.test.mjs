import test from 'node:test';
import assert from 'node:assert/strict';
import {releaseSceneResources,trackSceneResources} from '../render/sceneResources.js';

test('retiring a scene releases old racer bindings and unique node arrays while preserving garage/shared geometry',()=>{
  const scene={},garage={},position={},index={},particle={},shared={},other={};
  const geometry={attributes:{position},index};
  const rows=new Set(),disposed=[],deleted=[];
  const row=(name,owner,attributes)=>{const result={scene:owner,geometry,attributes,
    getAttributes(){return attributes;},dispose(){disposed.push(name);rows.delete(result);}};rows.add(result);return result;};
  row('old-world',scene,[position,particle,shared]);
  row('old-water-variant',scene,[position,particle]);
  row('pooled-racer-old-light',scene,[position]);
  row('garage',garage,[position,shared,other]);
  const cold={scene,geometry,getAttributes(){throw Error('unused render state must not build a graph during teardown');},dispose(){disposed.push('cold');rows.delete(cold);}};rows.add(cold);
  const three={_objects:{_renderObjects:rows},_attributes:{delete(attribute){assert.equal(rows.size,1,'release binding owners first');deleted.push(attribute);}}};
  releaseSceneResources(three,scene);
  assert.deepEqual(disposed,['old-world','old-water-variant','pooled-racer-old-light','cold']);
  assert.deepEqual(deleted,[particle]);assert.equal(rows.size,1);
  releaseSceneResources(three,scene);assert.equal(deleted.length,1,'a second retirement is inert');
  assert.ok(!deleted.includes(position)&&!deleted.includes(index)&&!deleted.includes(shared));
});

test('interleaved node arrays remain alive when a different attribute view uses their buffer',()=>{
  const scene={},otherScene={},data={},first={isInterleavedBufferAttribute:true,data},second={isInterleavedBufferAttribute:true,data};
  const rows=new Set(),geometry={attributes:{}};
  for(const [owner,attribute] of [[scene,first],[otherScene,second]]) {
    const row={scene:owner,geometry,attributes:[attribute],getAttributes(){return this.attributes;},dispose(){rows.delete(this);}};rows.add(row);
  }
  const deleted=[];releaseSceneResources({_objects:{_renderObjects:rows},_attributes:{delete:a=>deleted.push(a)}},scene);
  assert.deepEqual(deleted,[]);assert.equal(rows.size,1);
});

test('course objects rendered by a separate shadow/capture scene retire with their owning world',()=>{
  const kart={},garageKart={},scene={traverse:visit=>visit(kart)},captureScene={};
  const rows=new Set(),geometry={attributes:{}},deleted=[];
  for(const object of [kart,garageKart]) {
    const attribute={},row={object,scene:captureScene,geometry,attributes:[attribute],getAttributes(){return this.attributes;},dispose(){rows.delete(this);}};
    rows.add(row);
  }
  const garageRow=[...rows][1];
  releaseSceneResources({_objects:{_renderObjects:rows},_attributes:{delete:a=>deleted.push(a)}},scene);
  assert.deepEqual([...rows],[garageRow]);assert.equal(deleted.length,1);
  assert.ok(!deleted.includes(garageRow.attributes[0]));
});

test('shadow-only node arrays are captured before lamp disposal removes their render variants',()=>{
  const object={},attribute={},scene={traverse:visit=>visit(object)},rows=new Set(),events=[];
  const shadow={object,scene:{},geometry:{attributes:{}},attributes:[attribute],getAttributes(){events.push('collect');return this.attributes;},dispose(){events.push('dispose-shadow');rows.delete(this);}};
  rows.add(shadow);
  releaseSceneResources({_objects:{_renderObjects:rows},_attributes:{delete:a=>{assert.equal(a,attribute);events.push('free-array');}}},scene,()=>{
    events.push('light-event');shadow.dispose();
  });
  assert.deepEqual(events,['collect','light-event','dispose-shadow','free-array']);
  assert.equal(rows.size,0);
});

test('arrays uploaded by replaced warm variants still release when the course closes',()=>{
  const scene={},garage={},object={},garageObject={},position={},first={},last={},shared={},deleted=[];
  const rows=new Set(),three={_objects:{_renderObjects:rows},_attributes:{delete:a=>deleted.push(a)},_geometries:{updateAttributes(){}}};
  trackSceneResources(three);const tracked=three._geometries.updateAttributes;trackSceneResources(three);
  assert.equal(three._geometries.updateAttributes,tracked);
  const row=(owner,target,attributes)=>({scene:owner,object:target,geometry:{attributes:{position}},attributes,getAttributes(){return this.attributes;},dispose(){rows.delete(this);}});
  three._geometries.updateAttributes(row(scene,object,[position,first,shared]));
  const current=row(scene,object,[position,last]);rows.add(current);three._geometries.updateAttributes(current);
  three._geometries.updateAttributes(row(garage,garageObject,[position,shared]));
  releaseSceneResources(three,scene);
  assert.deepEqual(new Set(deleted),new Set([first,last]));assert.ok(!deleted.includes(shared));
  releaseSceneResources(three,scene);assert.equal(deleted.length,2);
  releaseSceneResources(three,garage);assert.deepEqual(new Set(deleted),new Set([first,last,shared]));
});

test('every retired interleaved view releases its distinct renderer bookkeeping entry',()=>{
  const scene={},data={},a={isInterleavedBufferAttribute:true,data},b={isInterleavedBufferAttribute:true,data};
  const rows=new Set(),deleted=[],row={scene,geometry:{attributes:{}},attributes:[a,b],getAttributes(){return this.attributes;},dispose(){rows.delete(this);}};
  rows.add(row);
  releaseSceneResources({_objects:{_renderObjects:rows},_attributes:{delete:a=>deleted.push(a)}},scene);
  assert.deepEqual(deleted,[a,b]);
});
