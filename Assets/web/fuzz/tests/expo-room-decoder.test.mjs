import assert from 'node:assert/strict';
import test from 'node:test';
import {createRoomDecoder} from '../ui/expo/room-decoder.js';

function fixture(start) {
  const calls=[],messages=[];
  const local={supported:true,decodeGltfBufferAsync(count,stride,source,mode,filter){
    calls.push({count,stride,source,mode,filter});
    return Promise.resolve(new Uint8Array(count*stride).fill(source[0]));
  }};
  const worker={terminated:false,postMessage(payload,transfer){messages.push(structuredClone(payload,{transfer}));},
    terminate(){this.terminated=true;},reply(results){this.onmessage({data:results});}};
  const decoder=createRoomDecoder(local,start??(()=>worker));
  const buffer=new Uint8Array([2,5,8,13]);
  const large=(source=buffer.subarray(1,3))=>decoder.decodeGltfBufferAsync(8192,4,source,'ATTRIBUTES','NONE');
  return {calls,messages,decoder,buffer,worker,large};
}

test('large views are batched once, preserve their shared source and resolve in request order',async()=>{
  const {decoder,calls,messages,worker,buffer,large}=fixture();
  try {
    const one=large(),two=large(buffer.subarray(2));
    const small=await decoder.decodeGltfBufferAsync(1,4,buffer,'ATTRIBUTES','NONE');
    assert.deepEqual([...small],[2,2,2,2]);assert.equal(calls.length,1);
    assert.equal(messages.length,1);assert.deepEqual(messages[0].map(job=>[...job.source]),[[5,8],[8,13]]);
    assert.deepEqual([...buffer],[2,5,8,13]);
    const three=large();await Promise.resolve();assert.equal(messages.length,1);
    worker.reply([new Uint8Array([21]),new Uint8Array([34])]);assert.equal(messages.length,2);
    worker.reply([new Uint8Array([55])]);
    assert.deepEqual((await Promise.all([one,two,three])).map(v=>v[0]),[21,34,55]);
  } finally { decoder.dispose(); }
});

test('unavailable and failed workers decode outstanding and future requests from their original views',async()=>{
  const denied=fixture(()=>{throw new Error('Workers disabled');});
  assert.equal((await denied.large())[0],5);assert.equal(denied.calls.length,1);denied.decoder.dispose();
  const {large,decoder,worker,calls}=fixture();
  const first=large();await Promise.resolve();const second=large();
  worker.onerror({preventDefault(){}});assert(worker.terminated);
  assert.deepEqual((await Promise.all([first,second,large()])).map(v=>v[0]),[5,5,5]);
  assert.equal(calls.length,3);decoder.dispose();
});

test('a worker that loads but cannot initialize WebAssembly returns all views to the local decoder',async()=>{
  const {large,decoder,worker,calls}=fixture();
  const pending=large();await Promise.resolve();
  worker.reply(null);
  assert(worker.terminated);assert.equal((await pending)[0],5);
  assert.equal((await large())[0],5);assert.equal(calls.length,2);decoder.dispose();
});

test('a malformed mesh rejects only its view and closing the scene settles queued and in-flight views',async()=>{
  const {large,decoder,worker}=fixture();
  const bad=large(),good=large();await Promise.resolve();
  worker.reply([{error:'Malformed mesh'},new Uint8Array([89])]);
  await assert.rejects(bad,/Malformed mesh/);assert.equal((await good)[0],89);
  const first=large();await Promise.resolve();const second=large();
  decoder.dispose();assert(worker.terminated);
  for(const promise of [first,second,large()])await assert.rejects(promise,{name:'AbortError'});
});
