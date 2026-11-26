import {test} from 'node:test';
import assert from 'node:assert/strict';
import {gzipSync} from 'node:zlib';
import {roomRequest,viewFile,roomAsset,warmEntrance,clearEntrance} from '../ui/expo/entrance.js';
import {setEdition,expoArrived} from '../ui/edition.js';

test('open visits retain lighting, collision and reflections from their own edition',()=>{
  const original={id:'atrium'};
  const updated={id:'atrium',assetStem:'atrium-c043'};
  for(const suffix of ['-collision.glb','-light.webp','-probe-px.webp']){
    assert.equal(roomAsset(original,suffix),'atrium'+suffix);
    assert.equal(roomAsset(updated,suffix),'atrium-c043'+suffix);
  }
  const relit={...updated,lightFile:'patched-light.webp'};
  assert.equal(roomAsset(relit,'-light.webp'),'patched-light.webp');
  assert.equal(roomAsset(relit,'-probe-px.webp'),'atrium-c043-probe-px.webp');
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

test('the entrance uses the desktop room in every profile and can fall back from gzip',t=>{
  const def={id:'atrium',file:'atrium.glb',arrivalGzip:'atrium-abcd.glb.gz'};
  assert.equal(viewFile(def),'atrium-abcd.glb.gz');
  const native=globalThis.DecompressionStream;
  t.after(()=>{globalThis.DecompressionStream=native;});
  globalThis.DecompressionStream=undefined;
  assert.equal(viewFile(def),'atrium.glb');
  assert.equal(viewFile({id:'hall',file:'hall-abcd.glb'}),'hall-abcd.glb');
});

test('the legacy preflight starts no asset request before a visitor accepts',async t=>{
  const calls=t.mock.method(globalThis,'fetch',()=>Promise.reject(new Error('unexpected request')));
  warmEntrance();warmEntrance(false);
  await Promise.resolve();
  assert.equal(calls.mock.callCount(),0);
});

test('changing the transmission directory never reuses an earlier warmed manifest',async t=>{
  setEdition({caps:{edition:'full',platform:'desktop',expo:{state:'absent'}}},{hosted:true});
  t.after(()=>{clearEntrance();setEdition(null,{noPage:true});});
  const requested=[];
  t.mock.method(globalThis,'fetch',async url=>{
    const host=new URL(url).host;requested.push(host);
    return Response.json({host,zones:[],start:{id:'atrium'}});
  });
  assert(expoArrived('https://first.example/edition/'));warmEntrance(true);
  assert(expoArrived('https://next.example/updated/'));
  const data=await (await roomRequest('zones.json',new AbortController().signal)).json();
  assert.equal(data.host,'next.example');
  assert.deepEqual(requested,['first.example','next.example']);
});
