import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';
import { roomRequest as request, clearEntrance, firstSurfaceMap, viewFile, phoneMap, roomAsset } from './entrance.js';

export const assetURL = path => new URL('../../assets/expo/' + path, import.meta.url).href;
const url = name => assetURL('zones/' + name);
const faces = ['px', 'nx', 'py', 'ny', 'pz', 'nz'];
// Both the inspection skylight and the rotunda stair reveal rooms beyond their floor bounds.
const sightlines = { dome:['catwalk','hall'], catwalk:['dome'], twelve:['hall'] };
const galleryDoors = [
  ['atrium','chronology',7.8,41],['chronology','forum',3.4,103],
  ['forum','commons',-66.2,98.6],['commons','aquarium',-61.8,45.5],['aquarium','atrium',-7.8,41]
];
const gallerySections=new Set(['chronology','forum','commons','aquarium']);
function galleryDoorDistance(from,to,p){
  if(!gallerySections.has(from)&&!gallerySections.has(to))return null;
  const door=galleryDoors.find(([a,b])=>(from===a&&to===b)||(from===b&&to===a));
  return door?Math.hypot(p.x-door[2],p.z-door[3],Math.max(0,Math.abs(p.y)-.8)):Infinity;
}

export async function loadPlan(signal) {
  return (await request('zones.json', signal)).json();
}

function disposeGroup(group) {
  const textures = new Set(), materials = new Set(), geometries = new Set(), images = new Set();
  group?.traverse(mesh => {
    if (!mesh.isMesh) return;
    geometries.add(mesh.geometry);
    for (const mat of [mesh.material].flat()) materials.add(mat);
  });
  for (const mat of materials) {
    for (const [key, value] of Object.entries(mat)) {
      if (value?.isTexture && key !== 'envMap' && key !== 'lightMap') textures.add(value);
    }
    mat.dispose();
  }
  for (const tex of textures) {
    if (!tex.userData.expoSharedSource) images.add(tex.source?.data);
    tex.dispose();
  }
  for (const image of images) image?.close?.();
  for (const geometry of geometries) geometry.dispose();
  group?.removeFromParent();
}

/** A zone becomes walkable in the same frame that its visible scene and interaction targets mount. */
export function createZones({ scene, renderer, walker, exhibits, plan, lite, report }) {
  const pmrem = new THREE.PMREMGenerator(renderer);
  const known = new Map(plan.zones.map(z => [z.id, z]));
  const resident = new Map(), pending = new Map(), retries = new Map();
  const photoSources = new Map();
  const earlySources=new WeakMap();
  let sharedHits=0,fullDetailMaps=0,firstRoom=true,promotionTimer=0;
  const anisotropy = Math.min(lite ? 4 : 8, renderer.capabilities.getMaxAnisotropy());
  let disposed = false, streamClock = 0;
  function syncDoors() {
    for (const zone of resident.values()) {
      for (const [object, id] of zone.doors) object.visible = !resident.has(id);
    }
  }
  function release(zone) {
    zone.released=true;zone.lightController?.abort();
    zone.originalMaterials?.forEach(m => m.dispose());
    disposeGroup(zone.view); disposeGroup(zone.collider);
    zone.light?.dispose(); zone.probe?.dispose(); zone.environment?.dispose();
    for (const image of zone.images) image.close?.();
    releaseSources(zone);
  }
  function releaseSources(zone) {
    for (const [uri,entry] of zone.sources) {
      if (--entry.references) continue;
      photoSources.delete(uri);
      entry.controller.abort(); entry.source?.data?.close?.();
    }
    zone.sources.clear();
  }
  async function pixels(name,signal){
    const response=await request(name,signal);
    return createImageBitmap(await response.blob(),{colorSpaceConversion:'none',premultiplyAlpha:'none'});
  }
  function useCurrentSource(texture){
    const entry=earlySources.get(texture.source);
    if(!entry)return;
    if(texture.source!==entry.source){
      // WebGL immutable storage keeps its initial dimensions until the old allocation is released.
      texture.dispose();texture.source=entry.source;texture.needsUpdate=true;
    }else if(entry.preview)entry.users.add(texture);
  }
  async function sharpenEntrance(entries){
    const queue=entries.filter(entry=>entry.preview);
    async function worker(){
      while(queue.length&&!disposed){
        const entry=queue.shift(),signal=entry.controller.signal;
        if(signal.aborted)continue;
        try{
          const image=await pixels(entry.file,signal);
          if(signal.aborted || disposed){image.close();continue;}
          const old=entry.source;entry.source=new THREE.Source(image);entry.preview=false;
          for(const texture of entry.users)useCurrentSource(texture);
          entry.users.clear();old.data.close();fullDetailMaps++;
        }catch(error){if(!signal.aborted&&!disposed)report(error);}
      }
    }
    await Promise.all([worker(),worker()]);
  }
  async function sharpenLighting(zone){
    if(!zone.previewLight||zone.released)return;
    const controller=zone.lightController=new AbortController();
    try{
      const full=await pixels(roomAsset(zone.definition,'-light.webp'),controller.signal);
      if(disposed||controller.signal.aborted){full.close();return;}
      const previous=zone.light.image;
      zone.light.dispose();zone.light.image=full;zone.light.needsUpdate=true;
      zone.images[zone.images.indexOf(previous)]=full;
      previous.close();fullDetailMaps++;
    }catch(error){if(!controller.signal.aborted&&!disposed)report(error);}
    finally{zone.previewLight=false;}
  }
  function zoneLoader(zone,signal) {
    const manager=new THREE.LoadingManager();
    // The exporter gives each shared PBR image its immutable content hash. Reuse its decoded pixels
    // and GPU Source across GLBs, while the glTF loader still owns UV channels and sampler settings.
    manager.addHandler(/^textures\/[a-f0-9]+(?:-mobile)?\.webp$/,{
      load(uri,done,_progress,fail) {
        if(signal.aborted){fail(new DOMException('Visit ended','AbortError'));return;}
        if(zone.mobileTransfer)uri=url(phoneMap(uri.slice(url('').length),zone.definition));
        let entry=photoSources.get(uri);
        if (!entry) {
          const relative=uri.slice(url('').length),file=zone.entrance?firstSurfaceMap(relative):relative;
          entry={references:0,source:null,controller:new AbortController(),file:relative,preview:file!==relative};
          const item=entry;
          item.promise=pixels(file,item.controller.signal).catch(error=>{
            if(!item.preview||item.controller.signal.aborted)throw error;
            item.preview=false;return pixels(relative,item.controller.signal);
          }).then(bitmap=>{
            if(item.controller.signal.aborted){bitmap.close();throw new DOMException('Visit ended','AbortError');}
            item.source=new THREE.Source(bitmap);
            if(item.preview){item.users=new Set();earlySources.set(item.source,item);}
          });
          photoSources.set(uri,item);
        } else sharedHits++;
        if(!zone.sources.has(uri)){entry.references++;zone.sources.set(uri,entry);}
        entry.promise.then(()=>{
          const texture=new THREE.Texture();
          texture.source=entry.source;texture.userData.expoSharedSource=true;texture.needsUpdate=true;done(texture);
        }).catch(error=>{zone.materialError ||= error;fail(error);});
      }
    });
    return new GLTFLoader(manager).setMeshoptDecoder(MeshoptDecoder);
  }
  async function load(def, controller) {
    const { id } = def, zone = { id, doors: [], images: [], originalMaterials: new Set(),sources:new Map(),entrance:firstRoom&&id===plan.start.id };
    const primary=viewFile(def,lite);
    zone.definition=def;zone.mobileTransfer=lite&&primary===def.arrivalGzip;
    zone.previewLight=!lite&&zone.entrance&&!!def.mobileFile;
    const { signal } = controller;
    const loader=zoneLoader(zone,signal);
    signal.addEventListener('abort',()=>releaseSources(zone),{once:true});
    const image = async name => {
      const response = await request(name, signal);
      const img = await createImageBitmap(await response.blob(), { colorSpaceConversion: 'none', premultiplyAlpha: 'none' });
      zone.images.push(img);
      return img;
    };
    const bytes=async name=>(await request(name,signal)).arrayBuffer();
    const parse=async data=>(await loader.parseAsync(data,url(''))).scene;
    async function view(){
      let data;
      try{data=await bytes(primary);}
      catch(error){
        if(primary!==def.arrivalGzip||signal.aborted)throw error;
        data=await bytes((lite&&def.mobileFile)||def.file||id+'.glb');
      }
      return parse(data);
    }
    const jobs = [
      view().then(group => { zone.view = group; }),
      bytes(def.collision||roomAsset(def,'-collision.glb')).then(parse).then(group=>{zone.collider=group;}),
      image(roomAsset(def,lite||zone.previewLight ? '-light-mobile.webp' : '-light.webp')).then(img => {
        const light = zone.light = new THREE.Texture(img);
        light.channel = 1; light.flipY = false; light.colorSpace = THREE.SRGBColorSpace;
        light.generateMipmaps = true; light.minFilter = THREE.LinearMipmapLinearFilter; light.needsUpdate = true;
      }),
      ...faces.map((face, i) => image(roomAsset(def,'-probe-'+face+'.webp')).then(img => {
        (zone.faces ||= [])[i] = img;
      })),
    ];
    try {
      const results = await Promise.allSettled(jobs);
      if (disposed || signal.aborted) throw new DOMException('Visit ended', 'AbortError');
      const failure = results.find(r => r.status === 'rejected');
      if (failure) throw failure.reason;
      if (zone.materialError) throw zone.materialError;
      zone.probe = new THREE.CubeTexture(zone.faces);
      zone.probe.colorSpace = THREE.SRGBColorSpace; zone.probe.needsUpdate = true;
      zone.view.userData.reflectionProbe = zone.probe;
      zone.environment = pmrem.fromCubemap(zone.probe);
      zone.view.updateMatrixWorld(true);
      const baked = new Map();
      zone.view.traverse(mesh => {
        const portal = mesh.userData.portal || /^door_([a-z]+)$/.exec(mesh.name)?.[1];
        if (portal) zone.doors.push([mesh, portal]);
        if (!mesh.isMesh) return;
        const apply = m => {
          for (const key of ['map', 'normalMap', 'roughnessMap', 'metalnessMap', 'aoMap', 'emissiveMap']) {
            if (m[key]) {useCurrentSource(m[key]);m[key].anisotropy=anisotropy;}
          }
          if (!m.isMeshStandardMaterial) return m;
          if (m.alphaTest>0 && (m.name==='garden_fern' || m.name==='garden_calathea')) {
            // On two samples, the fern's fine leaflets flicker between coverage levels during a walk.
            m.alphaToCoverage=m.name==='garden_calathea' && renderer.capabilities.maxSamples>=2;
            m.normalScale.set(Math.sign(m.normalScale.x)*.27,Math.sign(m.normalScale.y)*.27);
          }
          if(m.name==='garden_soil')m.normalScale.set(Math.sign(m.normalScale.x)*.23,Math.sign(m.normalScale.y)*.23);
          m.envMap = zone.environment.texture;
          // A moving exhibit receives its diffuse and specular light from the room probe.
          // Static meshes have an additional diffuse bake and need only the smaller reflection contribution.
          m.envMapIntensity = def.dynamicProbeIntensity ?? 2.15;
          if (m.aoMap && m.aoMapIntensity === 1) m.aoMapIntensity = .32;
          if (mesh.userData.lightmap === false || !mesh.geometry.attributes.uv1) return m;
          if (!baked.has(m)) {
            const copy = m.clone(); copy.lightMap = zone.light;
            copy.envMapIntensity = m.name==='alloy'||m.name==='utility_silver' ? 1.25 : def.reflectionIntensity ?? .93;
            // Cycles' colour-free diffuse pass stores reflected radiance, while Three's lightMap input is irradiance.
            copy.lightMapIntensity = def.lightIntensity ?? Math.PI;
            zone.originalMaterials.add(m); baked.set(m, copy);
          }
          return baked.get(m);
        };
        mesh.material = Array.isArray(mesh.material) ? mesh.material.map(apply) : apply(mesh.material);
      });
      if(id==='catwalk'){
        // The exterior maintenance enclosure meets the rounded plaster drum near its lower edge.
        // Trim that concealed join to the crown so no underside projects through the curved ceiling.
        const metalwork=new Set();
        zone.view.traverse(mesh=>{if(mesh.isMesh)for(const m of [mesh.material].flat())metalwork.add(m);});
        for(const m of metalwork) {
          m.onBeforeCompile=shader=>{
            shader.vertexShader='varying vec3 crownLocal;\n'+shader.vertexShader.replace('#include <project_vertex>',
              'crownLocal=(modelMatrix*vec4(transformed,1.)).xyz-vec3(-22.,2.75,-5.);\n#include <project_vertex>');
            shader.fragmentShader='varying vec3 crownLocal;\n'+shader.fragmentShader.replace('#include <clipping_planes_fragment>',
              '#include <clipping_planes_fragment>\nif(crownLocal.y>0. && dot(crownLocal,crownLocal)<75.0) discard;');
          };
          m.customProgramCacheKey=()=>'expo-crown-cut';
        }
      }
      walker.add(id, zone.collider, def.crossings);
      scene.add(zone.view); resident.set(id, zone);
      exhibits.mount?.(id, zone.view);
      syncDoors();
      if(zone.entrance){
        firstRoom=false;
        promotionTimer=setTimeout(()=>{void sharpenLighting(zone);void sharpenEntrance([...zone.sources.values()]);},250);
      }
      return zone;
    } catch (error) {
      if (resident.get(id) === zone) {
        exhibits.unmount?.(id, zone.view);
        resident.delete(id);
      }
      walker.remove(id); release(zone); throw error;
    }
  }
  function ensure(id) {
    if (disposed) return Promise.reject(new DOMException('Visit ended', 'AbortError'));
    if (resident.has(id)) return Promise.resolve(resident.get(id));
    if (pending.has(id)) return pending.get(id).promise;
    if (!known.has(id)) return Promise.reject(new Error('Unknown Expo zone: ' + id));
    const controller = new AbortController();
    const promise = load(known.get(id), controller).finally(() => pending.delete(id));
    pending.set(id, { promise, controller }); return promise;
  }
  function unload(id) {
    const zone = resident.get(id);
    if (!zone) return;
    exhibits.unmount?.(id, zone.view); walker.remove(id); resident.delete(id); release(zone); syncDoors();
  }
  function distance(bounds, p) {
    return Math.hypot(Math.max(bounds[0] - p.x, 0, p.x - bounds[3]),
      Math.max(bounds[1] - p.y, 0, p.y - bounds[4]), Math.max(bounds[2] - p.z, 0, p.z - bounds[5]));
  }
  function looksIntoAtrium(zone,p){
    if(zone==='service'&&p.x<=21.5){
      const ground=p.y<1.5&&Math.abs(p.z-32)<=1.6;
      const gallery=p.y>5.9&&p.y<7.7&&Math.abs(p.z-23)<=1.6;
      return ground||gallery;
    }
    return zone==='catwalk'&&p.x>=-16&&p.y>5.9&&p.y<7.7&&Math.abs(p.z-28)<=2.1;
  }
  function stream(dt) {
    streamClock += dt;
    if (streamClock < .18) return;
    streamClock = 0;
    let next = null, score = Infinity;
    for (const def of known.values()) {
      if (def.id === walker.zone) continue;
      const visibleFromHere = (known.get(walker.zone)?.sightlines || sightlines[walker.zone] || []).includes(def.id);
      const opening=def.id==='atrium'&&looksIntoAtrium(walker.zone,walker.feet);
      const passage=galleryDoorDistance(walker.zone,def.id,walker.feet);
      const d = passage??(opening ? -1 : visibleFromHere ? 0 : distance(def.bounds, walker.feet));
      const insideGallery=passage!=null&&gallerySections.has(walker.zone);
      const p=walker.feet;
      const southApproach=walker.zone==='atrium'&&p.y<1.2&&p.z>=36.95&&
        ((def.id==='chronology'&&p.x>=0)||(def.id==='aquarium'&&p.x<-.5));
      // The far wall of the next gallery becomes visible around a bend before the floor seam.
      if (d > (insideGallery ? (lite?20:24) : (lite?12:18))) unload(def.id);
      const near=insideGallery?(lite?13:15):southApproach?10:(lite?4:6);
      if (d < near && !resident.has(def.id) && d < score && (retries.get(def.id) || 0) < performance.now()) {
        next = def; score = d;
      }
    }
    if (next && !pending.size) ensure(next.id).catch(error => {
      if (disposed || error.name === 'AbortError') return;
      retries.set(next.id, performance.now() + 6000);
      report(error);
    });
  }
  return { ensure, stream, resident,
    textureStats:()=>({residentSources:photoSources.size,sharedHits,fullDetailMaps,pendingDetailMaps:[...photoSources.values()].filter(e=>e.preview).length+[...resident.values()].filter(z=>z.previewLight).length,
      decodedBytes:[...photoSources.values()].reduce((n,e)=>n+(e.source?e.source.data.width*e.source.data.height*4:0),0)}),
    dispose() {
    clearTimeout(promotionTimer);clearEntrance();
    disposed = true;
    for (const { controller } of pending.values()) controller.abort();
    for (const id of resident.keys()) unload(id);
    pmrem.dispose();
  } };
}
