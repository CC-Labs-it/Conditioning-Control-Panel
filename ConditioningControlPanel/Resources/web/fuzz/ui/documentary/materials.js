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
  // Subtle finish variation prevents flat, perfect plastic without adding image requests.
  function finishMaps(brushed = false) {
    const size = 128, rough = new Uint8Array(size * size * 4), normal = new Uint8Array(size * size * 4);
    let seed = brushed ? 4921 : 8711;
    const random = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed / 4294967296; };
    const rows = Array.from({ length: size }, () => random());
    for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) {
      const i = (y * size + x) * 4, grain = random() - .5;
      const value = brushed ? 166 + rows[y] * 55 + grain * 12 : 188 + grain * 32;
      rough[i] = rough[i + 1] = rough[i + 2] = value; rough[i + 3] = 255;
      normal[i] = 128 + grain * (brushed ? 2 : 12);
      normal[i + 1] = 128 + (brushed ? (rows[y] - .5) * 28 : (random() - .5) * 12);
      normal[i + 2] = 255; normal[i + 3] = 255;
    }
    const make = data => {
      const map = new THREE.DataTexture(data, size, size, THREE.RGBAFormat);
      map.wrapS = map.wrapT = THREE.RepeatWrapping;
      map.minFilter = THREE.LinearMipmapLinearFilter; map.magFilter = THREE.LinearFilter;
      map.generateMipmaps = true; map.repeat.set(brushed ? 3 : 5, brushed ? 3 : 5);
      map.anisotropy = lite ? 2 : 8; map.needsUpdate = true;
      textures.add(map); return map;
    };
    return { roughnessMap: make(rough), normalMap: make(normal), normalScale: new THREE.Vector2(.2, .2) };
  }
  const paint = finishMaps();
  const metal = finishMaps(true);
  const enamel = color => new THREE.MeshPhysicalMaterial({
    color, ...paint, roughness: .73, metalness: .12,
    clearcoat: .24, clearcoatRoughness: .55,
  });
  const materials = {
    stone: scanned('stone', '#fff4df', 0.92, 0.22),
    plaster: scanned('plaster', '#d0d3b8', 1, 0.4),
    plasterBlue: scanned('plaster', '#8faabd', 1, 0.4),
    wood: scanned('wood', '#e8c49f', 0.77, 0.2),
    woodDark: scanned('wood', '#bf9474', 0.8, 0.2),
    brick: scanned('brick', '#ddc4b0', 0.98, 0.65),
    floor: scanned('terrazzo', '#eddae6', 0.86, 0.13),
    brass: new THREE.MeshStandardMaterial({ color: '#b89955', ...metal, metalness: 0.82, roughness: 0.58 }),
    iron: new THREE.MeshStandardMaterial({ color: '#393d3e', metalness: 0.65, roughness: 0.52 }),
    velvet: new THREE.MeshPhysicalMaterial({ color: '#6d2647', map: texture('velour', 'color'),
      normalMap: texture('velour', 'normal'), roughnessMap: texture('velour', 'roughness'),
      normalScale: new THREE.Vector2(.18, .18), roughness: 1, sheen: 1, sheenColor: '#cf729d', sheenRoughness: .75 }),
    leather: scanned('leather', '#302b37', .91, .4),
    rubber: scanned('rubber', '#434450', 1, .22),
    glass: new THREE.MeshPhysicalMaterial({ color: '#b9d0c4', transparent: true, opacity: 0.12,
      metalness: 0, roughness: 0.16, depthWrite: false, side: THREE.DoubleSide }),
    paper: new THREE.MeshStandardMaterial({ color: '#eee1c3', roughness: 0.96 }),
    lamp: new THREE.MeshStandardMaterial({ color: '#fff0c7', emissive: '#ffbf65', emissiveIntensity: 2.5, roughness: 0.6 }),
    dark: new THREE.MeshStandardMaterial({ color: '#1f1c1a', roughness: 0.9 }),
    lacquerRose: enamel('#a9436d'),
    lacquerPlum: enamel('#46303f'),
    lacquerBlack: enamel('#252934'),
    chrome: new THREE.MeshStandardMaterial({ color: '#c5d2dd', ...metal, roughness: 0.47, metalness: 0.92 }),
    pearl: enamel('#e8e6df'),
    mint: enamel('#87b6a1'),
    cyanPanel: enamel('#285b6e'),
    amberPanel: enamel('#a88151'),
    pinkPanel: enamel('#98486e'),
    pinkLight: new THREE.MeshStandardMaterial({ color: '#ffc4e7', emissive: '#ff4ea9', emissiveIntensity: 2.6, roughness: 0.4 }),
    cyanLight: new THREE.MeshStandardMaterial({ color: '#b8f7ff', emissive: '#32d5ed', emissiveIntensity: 2.2, roughness: 0.4 }),
    amberLight: new THREE.MeshStandardMaterial({ color: '#ffe5ac', emissive: '#ffb449', emissiveIntensity: 2.4, roughness: 0.4 }),
    mintLight: new THREE.MeshStandardMaterial({ color: '#e4ffe8', emissive: '#8bffb6', emissiveIntensity: 1.5, roughness: 0.4 }),
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
