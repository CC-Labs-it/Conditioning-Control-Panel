import {test} from 'node:test';
import assert from 'node:assert/strict';
import {gzipSync} from 'node:zlib';
import {roomRequest,phoneMap,viewFile,roomAsset} from '../ui/expo/entrance.js';

test('open visits retain lighting, collision and reflections from their own edition',()=>{
  const original={id:'atrium'};
  const updated={id:'atrium',assetStem:'atrium-c043'};
  for(const suffix of ['-collision.glb','-light.webp','-light-mobile.webp','-probe-px.webp']){
    assert.equal(roomAsset(original,suffix),'atrium'+suffix);
    assert.equal(roomAsset(updated,suffix),'atrium-c043'+suffix);
  }
});

test('the entrance decodes a gzip asset once across ordinary file and HTTP-encoded hosts',async t=>{
  const body=Buffer.from('glTF\u0002\u0000test');
  const fetchMock=t.mock.method(globalThis,'fetch');
  for(const wire of [gzipSync(body),body]){
    fetchMock.mock.mockImplementation(async()=>new Response(wire));
    const response=await roomRequest('atrium-test.glb.gz',new AbortController().signal);
    assert.deepEqual(Buffer.from(await response.arrayBuffer()),body);
  }
});

test('the entrance shares geometry only when the mobile images have a verified mapping',()=>{
  const def={id:'atrium',file:'atrium.glb',mobileFile:'atrium-mobile.glb',arrivalGzip:'atrium-abcd.glb.gz'};
  assert.equal(viewFile(def,true),'atrium-mobile.glb');
  def.arrivalMobileMaps={'textures/title.webp':'textures/new-title-mobile.webp'};
  assert.equal(viewFile(def,true),viewFile(def,false));
  assert.equal(phoneMap('textures/title.webp',def),'textures/new-title-mobile.webp');
  assert.equal(phoneMap('textures/fern.webp',def),'textures/fern-mobile.webp');
  assert.equal(phoneMap('textures/fern-mobile.webp',def),'textures/fern-mobile.webp');
});
