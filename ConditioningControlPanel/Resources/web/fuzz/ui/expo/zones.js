import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';
import { roomRequest as request, clearEntrance, viewFile, roomAsset } from './entrance.js';
import {createDoors,loadDoorHardware,streamingPriority} from './doors.js';
import {batchArchiveHousings,fitArchiveBays} from './archive-furniture.js';
import {createRoomVisibility} from './room-visibility.js';
import {createRoomDecoder} from './room-decoder.js';

import {assetURL} from './asset-base.js';
const url = name => assetURL('zones/' + name);
const faces = ['px', 'nx', 'py', 'ny', 'pz', 'nz'];

export async function loadPlan(signal) {
  const [plan,archiveRoute]=await Promise.all([
    request('zones.json',signal).then(r=>r.json()),
    fetch(assetURL('station-route.json'),{signal}).then(r=>{
      if(!r.ok)throw Error('Expo tour route HTTP '+r.status);
      return r.json();
    })
  ]);
  return {...plan,archiveRoute};
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
export function createZones({ scene, renderer, walker, exhibits, plan, lite, report, ctx={},camera,compileRoom,play }) {
  const pmrem = new THREE.PMREMGenerator(renderer);
  const geometryDecoder=createRoomDecoder(MeshoptDecoder);
  const known = new Map(plan.zones.map(z => [z.id, z]));
  const resident = new Map(), pending = new Map(), retries = new Map();
  const photoSources = new Map();
  let sharedHits=0,firstRoom=true;
  const anisotropy = Math.min(lite ? 4 : 8, renderer.capabilities.getMaxAnisotropy());
  let disposed = false, streamClock = 0;
  const direction=new THREE.Vector3(),doorController = new AbortController(), doors=createDoors(scene,walker,ctx,camera,play);
  const visibility = createRoomVisibility(resident,doors,walker);
  walker.setDoors?.(doors);
  const hardware=loadDoorHardware(assetURL('door-hardware.glb'),doorController.signal).then(asset=>{
    if(disposed){asset.dispose();return;}
    doors.setHardware(asset);
  });
  hardware.catch(()=>{});
  function release(zone) {
    zone.releaseHousings?.();
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
  function zoneLoader(zone,signal) {
    const manager=new THREE.LoadingManager();
    // The exporter gives each shared PBR image its immutable content hash. Reuse its decoded pixels
    // and GPU Source across GLBs, while the glTF loader still owns UV channels and sampler settings.
    manager.addHandler(/^textures\/[a-f0-9]+\.webp$/,{
      load(uri,done,_progress,fail) {
        if(signal.aborted){fail(new DOMException('Visit ended','AbortError'));return;}
        let entry=photoSources.get(uri);
        if (!entry) {
          const relative=uri.slice(url('').length);
          entry={references:0,source:null,controller:new AbortController()};
          const item=entry;
          item.promise=pixels(relative,item.controller.signal).then(bitmap=>{
            if(item.controller.signal.aborted){bitmap.close();throw new DOMException('Visit ended','AbortError');}
            item.source=new THREE.Source(bitmap);
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
    return new GLTFLoader(manager).setMeshoptDecoder(geometryDecoder);
  }
  async function warmGraphics(zone,signal){
    if(zone.entrance||!camera)return;
    const maps=new Set([zone.light]);
    zone.view.traverse(node=>{
      if(node.isMesh)for(const material of [node.material].flat()){
        for(const key of ['map','normalMap','roughnessMap','metalnessMap','aoMap','emissiveMap']){
          if(material[key])maps.add(material[key]);
        }
      }
    });
    const nextFrame=()=>new Promise(resolve=>document.hidden?setTimeout(resolve,20):requestAnimationFrame(resolve));
    // One large full-resolution map can fill a frame's upload budget by itself.
    let started=performance.now(),count=0,pixels=0;
    for(const texture of maps){
      if(signal.aborted||disposed)throw new DOMException('Visit ended','AbortError');
      renderer.initTexture(texture);
      pixels+=(texture.image?.width||0)*(texture.image?.height||0);
      if(++count>=8||pixels>=512*1024||performance.now()-started>2){
        await nextFrame();started=performance.now();count=0;pixels=0;
      }
    }
    await compileRoom(zone.view,camera,scene);
  }
  async function load(def, controller) {
    const { id } = def, zone = { id, images: [], originalMaterials: new Set(),sources:new Map(),entrance:firstRoom&&id===plan.start.id };
    const primary=viewFile(def);
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
        data=await bytes(def.file||id+'.glb');
      }
      return parse(data);
    }
    const jobs = [
      hardware,
      view().then(group => { zone.view = group; }),
      bytes(def.collision||roomAsset(def,'-collision.glb')).then(parse).then(group=>{zone.collider=group;}),
      image(roomAsset(def,'-light.webp')).then(img => {
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
      fitArchiveBays(zone,plan.archiveRoute);
      zone.view.updateMatrixWorld(true);
      const baked = new Map();
      zone.view.traverse(mesh => {
        if (!mesh.isMesh) return;
        const apply = m => {
          for (const key of ['map', 'normalMap', 'roughnessMap', 'metalnessMap', 'aoMap', 'emissiveMap']) {
            if (m[key]) m[key].anisotropy=anisotropy;
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
      await warmGraphics(zone,signal);
      if(signal.aborted||disposed)throw new DOMException('Visit ended','AbortError');
      walker.add(id, zone.collider, def.crossings);
      scene.add(zone.view); resident.set(id, zone);
      doors.mount(id,zone);
      exhibits.mount?.(id, zone.view);
      zone.releaseHousings=batchArchiveHousings(zone.view);
      if(zone.entrance)firstRoom=false;
      return zone;
    } catch (error) {
      if (resident.get(id) === zone) {
        exhibits.unmount?.(id, zone.view);
        doors.unmount(id);
        resident.delete(id);
      }
      walker.remove(id); release(zone); throw error;
    }
  }
  function ensure(id,speculative=false) {
    if (disposed) return Promise.reject(new DOMException('Visit ended', 'AbortError'));
    if (resident.has(id)) return Promise.resolve(resident.get(id));
    const existing=pending.get(id);
    if(existing){
      if(!speculative){
        if(existing.controller.signal.aborted)return existing.promise.catch(()=>ensure(id));
        existing.speculative=false;
      }
      return existing.promise;
    }
    if (!known.has(id)) return Promise.reject(new Error('Unknown Expo zone: ' + id));
    const controller = new AbortController();
    const promise = load(known.get(id), controller).finally(() => pending.delete(id));
    pending.set(id,{promise,controller,speculative,outsideSince:0});return promise;
  }
  function unload(id) {
    const zone = resident.get(id);
    if (!zone) return;
    exhibits.unmount?.(id, zone.view);walker.remove(id);resident.delete(id);doors.unmount(id);release(zone);
  }
  function stream(dt) {
    doors.tick(dt);
    streamClock += dt;
    if (streamClock < .18) return;
    streamClock = 0;
    camera?.getWorldDirection(direction);
    let next = null, score = Infinity;
    const now=performance.now();
    for (const def of known.values()) {
      if (def.id === walker.zone) continue;
      const {distance:d,near,keep}=streamingPriority(walker.zone,def.id,walker.feet,lite,direction);
      if (d>keep && !doors.holds(def.id))unload(def.id);
      const job=pending.get(def.id);
      if(job?.speculative){
        if(d<near+2.5)job.outsideSince=0;
        else if(!job.outsideSince)job.outsideSince=now;
        else if(d>keep&&now-job.outsideSince>800)job.controller.abort();
      }
      if (d < near && !resident.has(def.id) && !job && d < score && (retries.get(def.id) || 0) < now) {
        next = def; score = d;
      }
    }
    // Turning away from an entrance must not keep a different, approaching room waiting on its download.
    if(next)for(const job of pending.values()){
      if(job.speculative&&job.outsideSince&&now-job.outsideSince>1200)job.controller.abort();
    }
    if (next && (!pending.size||score<3.1&&pending.size<2)) ensure(next.id,true).catch(error => {
      if (disposed || error.name === 'AbortError') return;
      retries.set(next.id, performance.now() + 6000);
      report(error);
    });
  }
  return { ensure, stream, resident, visibility, doors:doors.debug,registerDoorLeaf:doors.registerLeaf,
    textureStats:()=>({residentSources:photoSources.size,sharedHits,fullDetailMaps:[...photoSources.values()].filter(e=>e.source).length,pendingDetailMaps:0,
      decodedBytes:[...photoSources.values()].reduce((n,e)=>n+(e.source?e.source.data.width*e.source.data.height*4:0),0)}),
    dispose() {
    clearEntrance();doorController.abort();geometryDecoder.dispose();
    disposed = true;
    for (const { controller } of pending.values()) controller.abort();
    for (const id of resident.keys()) unload(id);
    walker.setDoors?.(null);doors.dispose();
    pmrem.dispose();
  } };
}
