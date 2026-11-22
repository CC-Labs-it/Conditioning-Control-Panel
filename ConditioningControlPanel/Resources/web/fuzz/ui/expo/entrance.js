import {expoAllowed} from '../edition.js';
import {assetURL} from './asset-base.js';

const zoneURL=name=>assetURL('zones/'+name);
let batch=null;
async function get(name,signal){
  const packed=name.endsWith('.glb.gz');
  const response=await fetch(zoneURL(name),{signal,cache:packed?'force-cache':'default'});
  if(!response.ok)throw Error('Expo entrance HTTP '+response.status);
  if(!packed)return response;
  // Some hosts already apply Content-Encoding to packaged .gz files. Inspect the decoded response's signature.
  const peek=response.clone().body.getReader(),prefix=[];
  try{
    while(prefix.length<2){
      const {done,value}=await peek.read();if(done)break;
      for(let i=0;i<value.length&&prefix.length<2;i++)prefix.push(value[i]);
    }
  }finally{void peek.cancel().catch(()=>{});}
  if(prefix[0]===0x67&&prefix[1]===0x6c)return response;
  if(prefix[0]!==31||prefix[1]!==139)throw Error('Invalid compressed Expo geometry');
  return new Response(response.body.pipeThrough(new DecompressionStream('gzip')),{headers:{'Content-Type':'model/gltf-binary'}});
}
const textureFile=/^textures\/[a-f0-9]+\.webp$/;

export function viewFile(def){
  if(def.arrivalGzip&&typeof DecompressionStream==='function')return def.arrivalGzip;
  return def.file||def.id+'.glb';
}
export function roomAsset(def,suffix){
  if(suffix==='-light.webp'&&def.lightFile)return def.lightFile;
  return (def.assetStem||def.id)+suffix;
}

async function firstTextureNames(response){
  const reader=response.clone().body.getReader(),chunks=[];
  let bytes=0,end=20;
  try{
    while(bytes<end){
      const {done,value}=await reader.read();
      if(done)break;
      chunks.push(value);bytes+=value.byteLength;
      if(end===20 && bytes>=20){
        const header=new Uint8Array(20);let offset=0;
        for(const chunk of chunks){const count=Math.min(20-offset,chunk.length);header.set(chunk.subarray(0,count),offset);offset+=count;}
        const v=new DataView(header.buffer);
        if(v.getUint32(0,true)!==0x46546c67 || v.getUint32(16,true)!==0x4e4f534a)return [];
        end=20+v.getUint32(12,true);if(end>2097152)return [];
      }
    }
    if(bytes<end)return [];
    const data=new Uint8Array(end);let offset=0;
    for(const chunk of chunks){const count=Math.min(end-offset,chunk.length);data.set(chunk.subarray(0,count),offset);offset+=count;}
    const gltf=JSON.parse(new TextDecoder().decode(data.subarray(20)));
    return (gltf.images||[]).map(image=>image.uri).filter(uri=>textureFile.test(uri));
  }finally{void reader.cancel().catch(()=>{});}
}

/** The accepted transmission can start its transfer while the arrival transition plays. */
export function warmEntrance(accepted){
  if(!expoAllowed()){clearEntrance();return;}
  if(accepted!==true)return;
  const base=zoneURL('');
  if(batch&&batch.base!==base)clearEntrance();
  if(batch)return;
  const controller=new AbortController(), files=new Map(),current={base,controller,files,claimed:new Set(),timer:0};
  batch=current;
  function prepare(name){
    const pending=get(name,controller.signal);
    files.set(name,pending);
    pending.catch(()=>{if(files.get(name)===pending)files.delete(name);});
    return pending;
  }
  prepare('zones.json').then(async response=>{
    const plan=await response.clone().json();
    if(batch!==current||controller.signal.aborted||base!==zoneURL(''))return;
    const def=plan.zones.find(zone=>zone.id===plan.start.id);
    if(!def)return;
    const view=viewFile(def);
    prepare(view).then(firstTextureNames).then(names=>{
      if(batch===current&&!controller.signal.aborted&&base===zoneURL(''))for(const name of new Set(names)){
        if(!current.claimed.has(name))prepare(name);
      }
    }).catch(()=>{});
    for(const file of [def.collision||roomAsset(def,'-collision.glb'),
      roomAsset(def,'-light.webp'),...['px','nx','py','ny','pz','nz'].map(side=>roomAsset(def,'-probe-'+side+'.webp'))]){
      prepare(file);
    }
  }).catch(()=>{});
  current.timer=setTimeout(()=>{if(batch===current)clearEntrance();},45000);
}

export function clearEntrance(){
  if(!batch)return;
  clearTimeout(batch.timer);batch.controller.abort();batch.files.clear();batch=null;
}

export async function roomRequest(name,signal){
  if(batch&&batch.base!==zoneURL(''))clearEntrance();
  batch?.claimed.add(name);
  if(name==='zones.json'&&batch)clearTimeout(batch.timer);
  const existing=batch?.files.get(name);
  if(existing){
    batch.files.delete(name);
    try{
      const response=await existing;
      if(signal.aborted)throw new DOMException('Visit ended','AbortError');
      return response;
    }catch(error){
      if(signal.aborted)throw error;
    }
  }
  if(textureFile.test(name))return fetch(zoneURL(name),{signal,cache:'force-cache'}).then(response=>{
    if(!response.ok)throw Error('Expo material HTTP '+response.status);
    return response;
  });
  return get(name,signal);
}
