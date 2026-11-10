const ROOT = new URL('../../assets/documentary/textures/', import.meta.url);

/** Shared scanned maps, with a finite texture set for the complete ride. */
export function createMuseumMaterials(THREE, { lite = false } = {}) {
  const textures = new Set();
  const maps = new Map();
  const loader = new THREE.TextureLoader();
  const pending = [];
  let disposed = false;
  function texture(name, kind) {
    const key = `${name}-${kind}`;
    if (maps.has(key)) return maps.get(key);
    let finish;
    pending.push(new Promise(resolve => { finish = resolve; }));
    const map = loader.load(new URL(`${key}.webp`, ROOT).href, finish, undefined, finish);
    map.wrapS = map.wrapT = THREE.RepeatWrapping;
    map.colorSpace = kind === 'color' ? THREE.SRGBColorSpace : THREE.NoColorSpace;
    map.anisotropy = lite ? 2 : 8;
    textures.add(map);
    maps.set(key, map);
    return map;
  }
  function scanned(name, color, roughness = 0.8, normalScale = 0.45) {
    return new THREE.MeshStandardMaterial({
      color, map: texture(name, 'color'), normalMap: texture(name, 'normal'),
      roughnessMap: texture(name, 'roughness'), roughness,
      normalScale: new THREE.Vector2(normalScale, normalScale),
    });
  }
  const materials = {
    stone: scanned('stone', '#fff4df', 0.92, 0.22),
    plaster: scanned('plaster', '#d0d3b8', 1, 0.4),
    plasterBlue: scanned('plaster', '#8faabd', 1, 0.4),
    wood: scanned('wood', '#e8c49f', 0.77, 0.2),
    woodDark: scanned('wood', '#bf9474', 0.8, 0.2),
    brick: scanned('brick', '#ddc4b0', 0.98, 0.65),
    floor: scanned('floor', '#fff3dc', 0.87, 0.35),
    brass: new THREE.MeshStandardMaterial({ color: '#b89955', metalness: 0.72, roughness: 0.38 }),
    iron: new THREE.MeshStandardMaterial({ color: '#393d3e', metalness: 0.65, roughness: 0.52 }),
    velvet: new THREE.MeshStandardMaterial({ color: '#6b2733', roughness: 0.95 }),
    glass: new THREE.MeshPhysicalMaterial({ color: '#b9d0c4', transparent: true, opacity: 0.12,
      metalness: 0, roughness: 0.16, depthWrite: false, side: THREE.DoubleSide }),
    paper: new THREE.MeshStandardMaterial({ color: '#eee1c3', roughness: 0.96 }),
    lamp: new THREE.MeshStandardMaterial({ color: '#fff0c7', emissive: '#ffbf65', emissiveIntensity: 2.5, roughness: 0.6 }),
    dark: new THREE.MeshStandardMaterial({ color: '#1f1c1a', roughness: 0.9 }),
  };
  let finishEnvironment;
  pending.push(new Promise(resolve => { finishEnvironment = resolve; }));
  const environment = loader.load(new URL('studio-environment.webp', ROOT).href, finishEnvironment, undefined, finishEnvironment);
  environment.mapping = THREE.EquirectangularReflectionMapping;
  environment.colorSpace = THREE.SRGBColorSpace;
  textures.add(environment);
  return {
    materials, environment,
    ready: Promise.all(pending),
    dispose() {
      if (disposed) return;
      disposed = true;
      for (const value of Object.values(materials)) value.dispose();
      for (const map of textures) map.dispose();
    },
  };
}
