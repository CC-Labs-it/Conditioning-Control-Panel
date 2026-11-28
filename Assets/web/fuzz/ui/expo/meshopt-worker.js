import {MeshoptDecoder} from '../../../vendor/three/addons/libs/meshopt_decoder.module.js';

// Some embedded hosts allow the worker script but disable WebAssembly inside its realm.
const available=MeshoptDecoder.supported && MeshoptDecoder.ready.then(()=>true,()=>false);
self.onmessage=async ({data})=>{
  if (!await available) { self.postMessage(null); return; }
  const transfer=[];
  const results=data.map(({count,stride,source,mode,filter})=>{
    try {
      const out=new Uint8Array(count*stride);
      MeshoptDecoder.decodeGltfBuffer(out,count,stride,source,mode,filter);
      transfer.push(out.buffer);
      return out;
    } catch(error) { return {error:error.message}; }
  });
  self.postMessage(results,transfer);
};
