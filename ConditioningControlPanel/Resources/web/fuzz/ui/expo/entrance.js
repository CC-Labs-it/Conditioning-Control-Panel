import { LITE } from '../perf.js';

const root=new URL('../../assets/expo/zones/',import.meta.url);
let batch=null;
const get=(name,signal)=>fetch(new URL(name,root),{signal}).then(response=>{
  if(!response.ok)throw Error('Expo entrance HTTP '+response.status);
  return response;
});
const textureFile=/^textures\/[a-f0-9]+(?:-mobile)?\.webp$/;

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
    return (JSON.parse(new TextDecoder().decode(data.subarray(20))).images||[]).map(image=>image.uri).filter(uri=>textureFile.test(uri));
  }finally{void reader.cancel().catch(()=>{});}
}

/** Begin only the first room's transfer while the accepted wormhole is crossing the screen. */
export function warmEntrance(){
  clearEntrance();
  const controller=new AbortController(), files=new Map(),current={controller,files,claimed:new Set(),timer:0};
  batch=current;
  function prepare(name){
    const pending=get(name,controller.signal);
    files.set(name,pending);
    pending.catch(()=>{if(files.get(name)===pending)files.delete(name);});
    return pending;
  }
  prepare('zones.json').then(async response=>{
    const plan=await response.clone().json();
    if(batch!==current || controller.signal.aborted)return;
    const def=plan.zones.find(zone=>zone.id===plan.start.id);
    if(!def)return;
    const id=def.id;
    const view=(LITE&&def.mobileFile)||def.file||id+'.glb';
    prepare(view).then(firstTextureNames).then(names=>{
      if(batch===current&&!controller.signal.aborted)for(const name of new Set(names))if(!current.claimed.has(name))prepare(name);
    }).catch(()=>{});
    for(const file of [def.collision||id+'-collision.glb',
      id+(LITE?'-light-mobile.webp':'-light.webp'),...['px','nx','py','ny','pz','nz'].map(side=>id+'-probe-'+side+'.webp')]){
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
  if(textureFile.test(name))return fetch(new URL(name,root),{signal,cache:'force-cache'}).then(response=>{
    if(!response.ok)throw Error('Expo material HTTP '+response.status);
    return response;
  });
  return get(name,signal);
}
