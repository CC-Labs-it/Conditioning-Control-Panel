import { LITE } from '../perf.js';

const root=new URL('../../assets/expo/zones/',import.meta.url);
let batch=null;
async function get(name,signal){
  const packed=name.endsWith('.glb.gz');
  const response=await fetch(new URL(name,root),{signal,cache:packed?'force-cache':'default'});
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
const textureFile=/^textures\/[a-f0-9]+(?:-mobile)?\.webp$/;

export function viewFile(def,lite){
  if(def.arrivalGzip&&typeof DecompressionStream==='function'&&(!lite||def.arrivalMobileMaps))return def.arrivalGzip;
  return (lite&&def.mobileFile)||def.file||def.id+'.glb';
}
export function phoneMap(file,def){
  if(!def.arrivalMobileMaps||file.endsWith('-mobile.webp'))return file;
  return def.arrivalMobileMaps[file]||file.replace(/\.webp$/,'-mobile.webp');
}
export function roomAsset(def,suffix){
  return (def.assetStem||def.id)+suffix;
}

async function firstTextureNames(response,details){
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
    const imageFor=slot=>{
      const texture=gltf.textures?.[slot?.index];
      return gltf.images?.[texture?.extensions?.EXT_texture_webp?.source ?? texture?.source]?.uri;
    };
    if(details){
      const printed=new Set();
      for(const mat of gltf.materials||[]){
        const pbr=mat.pbrMetallicRoughness||{};
        const colour=imageFor(pbr.baseColorTexture);
        if(/^(art_|.*readout)/i.test(mat.name||''))printed.add(colour);
        else if(colour&&textureFile.test(colour))details.add(colour);
        printed.add(imageFor(mat.emissiveTexture));
        for(const slot of [mat.normalTexture,pbr.metallicRoughnessTexture]){
          const name=imageFor(slot);if(name&&textureFile.test(name))details.add(name);
        }
      }
      for(const name of printed)details.delete(name);
    }
    return (gltf.images||[]).map(image=>image.uri).filter(uri=>textureFile.test(uri));
  }finally{void reader.cancel().catch(()=>{});}
}

/** Buying the ending starts its first room's transfer while the visitor reads the wormhole prompt. */
export function warmEntrance(){
  if(batch)return;
  const controller=new AbortController(), files=new Map(),current={controller,files,claimed:new Set(),details:new Set(),timer:0};
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
    const view=viewFile(def,LITE);
    prepare(view).then(file=>firstTextureNames(file,!LITE&&def.mobileFile?current.details:null)).then(names=>{
      if(batch===current&&!controller.signal.aborted)for(const file of new Set(names)){
        const name=LITE&&view===def.arrivalGzip?phoneMap(file,def):firstSurfaceMap(file);
        if(!current.claimed.has(name))prepare(name);
      }
    }).catch(()=>{});
    for(const file of [def.collision||roomAsset(def,'-collision.glb'),
      roomAsset(def,LITE||def.mobileFile?'-light-mobile.webp':'-light.webp'),...['px','nx','py','ny','pz','nz'].map(side=>roomAsset(def,'-probe-'+side+'.webp'))]){
      prepare(file);
    }
  }).catch(()=>{});
  current.timer=setTimeout(()=>{if(batch===current)clearEntrance();},45000);
}

// Printed lettering and projected pictures keep full detail during the first desktop frame.
export function firstSurfaceMap(name){
  return batch?.details.has(name)?name.replace(/\.webp$/,'-mobile.webp'):name;
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
