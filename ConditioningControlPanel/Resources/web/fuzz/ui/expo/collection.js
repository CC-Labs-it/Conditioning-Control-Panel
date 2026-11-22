import { BoxGeometry, Group, Mesh } from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { MeshoptDecoder } from 'three/addons/libs/meshopt_decoder.module.js';

const rooms = new Set(['chronology', 'forum', 'commons', 'hall', 'hoof', 'habitat', 'shop', 'service']);

function releaseTemplate(scene) {
  const resources = new Set(), images = new Set();
  scene.traverse(node => {
    if (!node.isMesh) return;
    resources.add(node.geometry);
    for (const material of [node.material].flat()) {
      resources.add(material);
      for (const value of Object.values(material)) if (value?.isTexture) {
        resources.add(value);
        images.add(value.source?.data);
      }
    }
  });
  for (const resource of resources) resource.dispose();
  for (const image of images) image?.close?.();
}

function roomProbe(room) {
  let probe;
  room.traverse(node => {
    if (!probe && node.isMesh) {
      const material = [node.material].flat().find(value => value.envMap);
      if (material) probe = material.envMap;
    }
  });
  return probe;
}

/** Shared geometry with room-owned materials and walk surfaces. Admission makes no collection request. */
export function createCollection({ assetURL, walker, register, onAnchor, onAssembly, onLabel }) {
  const loader = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
  const request = new AbortController(), mounted = new Map();
  let template, loading, gearAngle = 0, turns = 0, rotorAngle = 0;

  async function fetchAsset(path, kind) {
    const response = await fetch(assetURL('collection/' + path), {signal:request.signal});
    if (!response.ok) throw Error(path + ' HTTP ' + response.status);
    return response[kind]();
  }

  function load() {
    if (!loading) loading = (async () => {
      const manifest = await fetchAsset('collection.json', 'json');
      const data = await fetchAsset(manifest.model, 'arrayBuffer');
      const {scene} = await loader.parseAsync(data, '');
      if (request.signal.aborted) { releaseTemplate(scene); return null; }
      template = {scene, manifest};
      return template;
    })().catch(error => {
      loading = null;
      if (!request.signal.aborted) console.warn('Expo collection', error);
      return null;
    });
    return loading;
  }

  function addWalkSurfaces(state, rows) {
    if (!rows.length) return;
    const proxy = new Group();
    for (const row of rows) {
      const [x,y,z,w,h,d] = row.collision;
      const mount = new Group();
      mount.position.fromArray(row.center);
      mount.rotation.y = row.yaw * Math.PI / 180;
      const box = new Mesh(new BoxGeometry(w,h,d));
      box.position.set(x,y,z);
      box.name = 'collection_support_' + row.id;
      box.userData.surface = row.id === 'weather' ? 'metal' : 'wood';
      mount.add(box); proxy.add(mount);
    }
    const key = 'collection_' + state.id;
    walker.add(key, proxy, [], state.id);
    proxy.traverse(node => { if (node.isMesh) { node.geometry.dispose(); node.material.dispose(); } });
    state.cleanup.push(() => walker.remove(key));
  }

  function install(state, asset) {
    if (!asset || mounted.get(state.id) !== state) return;
    const probe = roomProbe(state.room), byMaterial = new Map();
    const finish = original => {
      if (!byMaterial.has(original)) {
        const material = original.clone();
        if (material.isMeshStandardMaterial && probe) {
          material.envMap = probe;
          material.envMapIntensity = 2.15;
          material.aoMapIntensity = .38;
        }
        byMaterial.set(original, material);
      }
      return byMaterial.get(original);
    };
    state.cleanup.push(() => { for (const material of byMaterial.values()) material.dispose(); });
    for (const master of asset.scene.children) {
      const layout=asset.manifest.pieces.find(row=>row.id===master.userData.collectionPiece);
      if ((layout?.zone||master.userData.collectionRoom) !== state.id) continue;
      const root = master.clone(true);
      if(layout){root.position.fromArray(layout.center);root.rotation.y=layout.yaw*Math.PI/180;}
      root.traverse(node => {
        if (node.isMesh) node.material = Array.isArray(node.material) ? node.material.map(finish) : finish(node.material);
      });
      state.room.add(root);
      root.updateWorldMatrix(true,true);
      state.cleanup.push(() => root.removeFromParent());
      root.traverse(node => {
        if (!node.userData.collectionLabel) return;
        const remove = onLabel?.(node,node.userData.collectionLabel);
        if (remove) state.cleanup.push(remove);
      });
      const removeLighting = onAssembly?.(root);
      if (removeLighting) state.cleanup.push(removeLighting);
      for (const anchor of root.children) if (anchor.userData.guestSlot) {
        anchor.userData.collectionEnvironment = probe;
        const remove = onAnchor?.(anchor.userData.guestSlot,anchor);
        if (remove) state.cleanup.push(remove);
      }
      if (root.userData.collectionPiece === 'weather') state.rotor = root.getObjectByName('weather_rotor');
      if (root.userData.collectionPiece === 'drive') {
        state.wheel = root.getObjectByName('drive_handwheel');
        state.gears = [0,1,2].map(i => root.getObjectByName('drive_gear_' + i));
        state.cleanup.push(register(state.wheel, 'Turn the handwheel', () => { turns += Math.PI * 2; }, 3));
      }
    }
    addWalkSurfaces(state, asset.manifest.pieces.filter(row => row.zone === state.id));
  }

  function unmount(id) {
    const state = mounted.get(id);
    if (!state) return;
    mounted.delete(id);
    for (const remove of state.cleanup.reverse()) remove();
  }

  return {
    mount(id, room) {
      if (!rooms.has(id) || mounted.has(id) || request.signal.aborted) return;
      const state = {id, room, cleanup:[]};
      mounted.set(id,state);
      load().then(asset => install(state,asset));
    },
    unmount,
    tick(dt, calm) {
      const weather = mounted.get('commons')?.rotor;
      if (weather && !calm) {
        rotorAngle = (rotorAngle + dt * .49) % (Math.PI * 2);
        weather.rotation.y = rotorAngle;
      }
      const rig = mounted.get('hall');
      if (!rig?.wheel) return;
      gearAngle += (turns - gearAngle) * (calm ? 1 : 1 - Math.exp(-2.35 * dt));
      rig.wheel.rotation.z = gearAngle;
      rig.gears[0].rotation.z = gearAngle * 26/16;
      rig.gears[1].rotation.z = -gearAngle * 26/24;
      rig.gears[2].rotation.z = gearAngle;
    },
    dispose() {
      request.abort();
      for (const id of mounted.keys()) unmount(id);
      if (template) releaseTemplate(template.scene);
      template = null;
    }
  };
}
