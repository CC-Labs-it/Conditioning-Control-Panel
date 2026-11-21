import {GLTFLoader} from 'three/addons/loaders/GLTFLoader.js';
import {JOKE_ADS} from '../../data/content/ads.js';

const cases = new Map([
  ['guest_anchor_chess', 'guest-chess.glb'],
  ['guest_anchor_classifieds', 'guest-classifieds.glb']
]);

export function guestInscription(mesh, label) {
  if (mesh.name !== 'classifieds_label_ink') return label;
  const lamp = JOKE_ADS.find(ad => ad.id === 'j_lamp');
  const stairs = JOKE_ADS.find(ad => ad.id === 'j_stairs');
  return {...label,section:label.body,body:lamp.sub+'\n\n'+stairs.headline+'\n'+stairs.sub};
}

function release(model) {
  const resources = new Set();
  model.traverse(node => {
    if (!node.isMesh) return;
    resources.add(node.geometry);
    for (const material of [node.material].flat()) {
      resources.add(material);
      for (const value of Object.values(material)) if (value?.isTexture) resources.add(value);
    }
  });
  for (const resource of resources) {
    resource.dispose();
    if (resource.isTexture) resource.source?.data?.close?.();
  }
}

export function createGuestCollection(assetURL) {
  const mounts = new Map(), models = new Map(), loader = new GLTFLoader();
  const request = new AbortController();
  function load(file) {
    if (!models.has(file)) {
      const pending = (async () => {
        const response = await fetch(assetURL(file), {signal:request.signal});
        if (!response.ok) throw Error(file + ' HTTP ' + response.status);
        const {scene} = await loader.parseAsync(await response.arrayBuffer(), '');
        if (request.signal.aborted) { release(scene); return null; }
        return scene;
      })().catch(error => {
        models.delete(file);
        if (!request.signal.aborted) console.warn('Expo collection', error);
        return null;
      });
      models.set(file,pending);
    }
    return models.get(file);
  }
  function unmount(anchor) {
    const mount = mounts.get(anchor);
    mount?.object?.removeFromParent();
    for (const material of mount?.materials.values() || []) material.dispose();
    mounts.delete(anchor);
  }
  return {
    mount(name,anchor) {
      const file = cases.get(name);
      if (!file || request.signal.aborted || mounts.has(anchor)) return;
      const mount = {materials:new Map()};
      mounts.set(anchor,mount);
      load(file).then(template => {
        if (!template || mounts.get(anchor) !== mount) return;
        mount.object = template.clone(true);
        function roomFinish(original) {
          if (!mount.materials.has(original)) {
            const surface = original.clone();
            surface.envMap = anchor.userData.collectionEnvironment;
            surface.envMapIntensity = 1.35;
            if (surface.map && surface.name === 'Original classified advertisements') {
              surface.emissiveMap = surface.map;
              surface.emissive.setHex(0xffffff);
              surface.emissiveIntensity = .44;
            }
            mount.materials.set(original,surface);
          }
          return mount.materials.get(original);
        }
        mount.object.traverse(node => {
          if (node.isMesh) node.material = Array.isArray(node.material) ? node.material.map(roomFinish) : roomFinish(node.material);
        });
        anchor.add(mount.object);
      });
    },
    unmount,
    dispose() {
      request.abort();
      for (const anchor of mounts.keys()) unmount(anchor);
      for (const pending of models.values()) pending.then(model => { if (model) release(model); });
      models.clear();
    }
  };
}
