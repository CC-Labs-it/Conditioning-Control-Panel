// Bake the Backroom's Daily Daze machine into transparent layers for the clicker's 2D dock.
// Run from the fuzz directory: node dev/bake-prize-wheel.mjs
import { createRequire } from 'node:module';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
const game = resolve(process.argv[2] || fileURLToPath(new URL('..', import.meta.url)));
const out = resolve(process.argv[3] || resolve(game, 'assets/prize-wheel'));
const web = resolve(game, '..');
const {
  chromium
} = createRequire('C:/wt-fe/e2e/package.json')('playwright');
const browser = await chromium.launch({
  channel: 'chrome',
  headless: true,
  args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader']
});
try {
  const page = await browser.newPage();
  await page.route('http://wheel.local/**', async route => {
    const path = new URL(route.request().url()).pathname;
    if (path === '/') return route.fulfill({
      contentType: 'text/html',
      body: '<script type="importmap">{"imports":{"three":"/vendor/three/three.module.min.js"}}</script>'
    });
    const file = resolve(web, '.' + path);
    if (!file.startsWith(web + sep)) return route.abort();
    const contentType = file.endsWith('.js') ? 'application/javascript' : file.endsWith('.png') ? 'image/png' : 'model/gltf-binary';
    await route.fulfill({
      contentType,
      body: await readFile(file)
    });
  });
  await page.goto('http://wheel.local/');
  const art = await page.evaluate(async () => {
    const T = await import('three');
    const {
      GLTFLoader
    } = await import('/vendor/three/addons/loaders/GLTFLoader.js');
    const source = '/backroom/stations/wheel/';
    const [{
      scene: model
    }, atlas] = await Promise.all([new GLTFLoader().loadAsync(source + 'assets/wheel.glb'), new T.TextureLoader().loadAsync(source + 'assets/emi-faces-slot.png')]);
    const W = 272,
      H = 324,
      scene = new T.Scene();
    const renderer = new T.WebGLRenderer({
      alpha: true,
      antialias: true,
      preserveDrawingBuffer: true
    });
    renderer.setPixelRatio(3);
    renderer.setSize(W, H, false);
    renderer.outputColorSpace = T.SRGBColorSpace;
    renderer.toneMapping = T.NeutralToneMapping;
    renderer.toneMappingExposure = 1.6;
    renderer.setClearColor(0, 0);
    scene.add(model, new T.HemisphereLight(0xfbd7f4, 0x36243e, 1.2));
    for (const [xyz, color, energy] of [[[-3, 4, 5], 0xffd5eb, 2.1], [[3, 2, 3], 0xa5b6ff, 1.4], [[1, 4, -3], 0xff75c1, 2.2]]) {
      const light = new T.DirectionalLight(color, energy);
      light.position.set(...xyz);
      scene.add(light);
    }
    const get = name => model.getObjectByName(name);
    for (const name of ['layout_sectors', 'hub_spiral', 'title_letters', 'status_letters']) get(name).visible = false;
    model.traverse(n => {
      if (/^(peg_|glyph_)/.test(n.name)) n.visible = false;
    });
    const face = document.createElement('canvas');
    face.width = 152;
    face.height = 137;
    face.getContext('2d').drawImage(atlas.image, 3 * 152, 0, 152, 137, 0, 0, 152, 137);
    const map = new T.CanvasTexture(face);
    map.flipY = false;
    map.colorSpace = T.SRGBColorSpace;
    get('EMI_glass').material = new T.MeshBasicMaterial({
      map
    });
    const lamps = [];
    model.traverse(n => {
      if (/^bulb_\d+$/.test(n.name)) {
        n.scale.multiplyScalar(1.2);
        lamps.push(n);
      }
    });
    for (const n of lamps) {
      n.material = n.material.clone();
      n.material.color.set('#8d567d');
      n.material.emissive.set('#412d42');
      n.material.emissiveIntensity = .2;
    }
    const camera = new T.OrthographicCamera(-.985, .985, .985 * H / W, -.985 * H / W, .1, 20);
    camera.position.set(0, 1.58, 7);
    camera.lookAt(0, 1.096, 0);
    camera.updateMatrixWorld();
    scene.updateMatrixWorld(true);
    const project = p => {
      const v = p.clone().project(camera);
      return [Math.round((v.x + 1) * W * 500) / 1000, Math.round((1 - v.y) * H * 500) / 1000];
    };
    const point = (name, x = 0, y = 0, z = 0) => project(get(name).localToWorld(new T.Vector3(x, y, z)));
    const center = point('wheel_rotor', 0, 0, .10);
    const faceRadius = point('wheel_rotor', .711, 0, .10)[0] - center[0];
    const geometry = {
      width: W,
      height: H,
      center,
      faceRadius,
      hubRadius: point('wheel_rotor', .164, 0, .175)[0] - center[0],
      bulbs: lamps.sort((a, b) => a.name.localeCompare(b.name)).map(n => point(n.name)),
      title: point('title_screen', 0, 0, .02),
      screen: point('status_screen', 0, 0, .02)
    };
    const read = () => {
      renderer.render(scene, camera);
      return renderer.domElement.toDataURL('image/webp', .995).split(',')[1];
    };
    const front = new Set();
    for (const name of ['pointer', 'pointer_bracket', 'star_mount', 'star_bracket']) get(name)?.traverse(n => front.add(n));
    const meshes = [];
    model.traverseVisible(n => {
      if (n.isMesh) meshes.push(n);
    });
    for (const n of meshes) n.visible = !front.has(n);
    const cabinet = read();
    for (const n of meshes) n.visible = front.has(n);
    const trim = read();
    renderer.dispose();
    return {
      cabinet,
      trim,
      geometry
    };
  });
  await mkdir(out, {
    recursive: true
  });
  for (const layer of ['cabinet', 'trim']) await writeFile(resolve(out, layer + '.webp'), Buffer.from(art[layer], 'base64'));
  await writeFile(resolve(out, 'geometry.js'), '// Projected from the Daily Daze model by dev/bake-prize-wheel.mjs.\nexport const machine = ' + JSON.stringify(art.geometry, null, 2) + ';\n');
  console.log(JSON.stringify(art.geometry));
} finally {
  await browser.close();
}
