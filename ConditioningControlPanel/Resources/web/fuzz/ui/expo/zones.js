import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';
import { roomRequest as request, clearEntrance } from './entrance.js';

export const assetURL = path => new URL('../../assets/expo/' + path, import.meta.url).href;
const url = name => assetURL('zones/' + name);
const faces = ['px', 'nx', 'py', 'ny', 'pz', 'nz'];
// The inspection deck and the chamber share an open skylight across two storeys.
const sightlines = { dome:['catwalk','hall'], catwalk:['dome'] };

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
  let sharedHits=0;
  const anisotropy = Math.min(lite ? 4 : 8, renderer.capabilities.getMaxAnisotropy());
  let disposed = false, streamClock = 0;
  function syncDoors() {
    for (const zone of resident.values()) {
      for (const [object, id] of zone.doors) object.visible = !resident.has(id);
    }
  }
  function release(zone) {
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
  function zoneLoader(zone,signal) {
    const manager=new THREE.LoadingManager();
    // The exporter gives each shared PBR image its immutable content hash. Reuse its decoded pixels
    // and GPU Source across GLBs, while the glTF loader still owns UV channels and sampler settings.
    manager.addHandler(/^textures\/[a-f0-9]+(?:-mobile)?\.webp$/,{
      load(uri,done,_progress,fail) {
        if(signal.aborted){fail(new DOMException('Visit ended','AbortError'));return;}
        let entry=photoSources.get(uri);
        if (!entry) {
          entry={references:0,source:null,controller:new AbortController()};
          const item=entry;
          const relative=uri.slice(url('').length);
          item.promise=request(relative,item.controller.signal).then(response=>response.blob()).then(blob=>createImageBitmap(blob,{colorSpaceConversion:'none',premultiplyAlpha:'none'})).then(bitmap=>{
            if(item.controller.signal.aborted){bitmap.close();throw new DOMException('Visit ended','AbortError');}
            return item.source=new THREE.Source(bitmap);
          });
          photoSources.set(uri,item);
        } else sharedHits++;
        if(!zone.sources.has(uri)){entry.references++;zone.sources.set(uri,entry);}
        entry.promise.then(source=>{
          const texture=new THREE.Texture();
          texture.source=source;texture.userData.expoSharedSource=true;texture.needsUpdate=true;done(texture);
        }).catch(error=>{zone.materialError ||= error;fail(error);});
      }
    });
    return new GLTFLoader(manager).setMeshoptDecoder(MeshoptDecoder);
  }
  async function load(def, controller) {
    const { id } = def, zone = { id, doors: [], images: [], originalMaterials: new Set(),sources:new Map() };
    const { signal } = controller;
    const loader=zoneLoader(zone,signal);
    signal.addEventListener('abort',()=>releaseSources(zone),{once:true});
    const image = async name => {
      const response = await request(name, signal);
      const img = await createImageBitmap(await response.blob(), { colorSpaceConversion: 'none', premultiplyAlpha: 'none' });
      zone.images.push(img);
      return img;
    };
    const glb = async name => {
      const bytes = await (await request(name, signal)).arrayBuffer();
      return (await loader.parseAsync(bytes, url(''))).scene;
    };
    const jobs = [
      glb((lite && def.mobileFile) || def.file || id + '.glb').then(group => { zone.view = group; }),
      glb(def.collision || id + '-collision.glb').then(group => { zone.collider = group; }),
      image(id + (lite ? '-light-mobile.webp' : '-light.webp')).then(img => {
        const light = zone.light = new THREE.Texture(img);
        light.channel = 1; light.flipY = false; light.colorSpace = THREE.SRGBColorSpace;
        light.generateMipmaps = true; light.minFilter = THREE.LinearMipmapLinearFilter; light.needsUpdate = true;
      }),
      ...faces.map((face, i) => image(id + '-probe-' + face + '.webp').then(img => {
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
            if (m[key]) m[key].anisotropy = anisotropy;
          }
          if (!m.isMeshStandardMaterial) return m;
          m.envMap = zone.environment.texture;
          // A moving exhibit receives its diffuse and specular light from the room probe.
          // Static meshes have an additional diffuse bake and need only the smaller reflection contribution.
          m.envMapIntensity = def.dynamicProbeIntensity ?? 2.15;
          if (m.aoMap && m.aoMapIntensity === 1) m.aoMapIntensity = .32;
          if (mesh.userData.lightmap === false || !mesh.geometry.attributes.uv1) return m;
          if (!baked.has(m)) {
            const copy = m.clone(); copy.lightMap = zone.light;
            copy.envMapIntensity = def.reflectionIntensity ?? .93;
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
  function stream(dt) {
    streamClock += dt;
    if (streamClock < .18) return;
    streamClock = 0;
    let next = null, score = Infinity;
    for (const def of known.values()) {
      if (def.id === walker.zone) continue;
      const visibleFromHere = (known.get(walker.zone)?.sightlines || sightlines[walker.zone] || []).includes(def.id);
      const d = visibleFromHere ? 0 : distance(def.bounds, walker.feet);
      if (d > (lite ? 12 : 18)) unload(def.id);
      if (d < (lite ? 4 : 6) && !resident.has(def.id) && d < score && (retries.get(def.id) || 0) < performance.now()) {
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
    textureStats:()=>({residentSources:photoSources.size,sharedHits,decodedBytes:[...photoSources.values()].reduce((n,e)=>n+(e.source?e.source.data.width*e.source.data.height*4:0),0)}),
    dispose() {
    clearEntrance();
    disposed = true;
    for (const { controller } of pending.values()) controller.abort();
    for (const id of resident.keys()) unload(id);
    pmrem.dispose();
  } };
}
