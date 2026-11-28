import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const threeUrl = new URL('../../vendor/three/three.module.min.js', import.meta.url).href;
const THREE = await import(threeUrl);
const lookUrl = new URL('../skins/look.js', import.meta.url).href;
const moduleUrl = (source) => 'data:text/javascript;base64,' + Buffer.from(source).toString('base64');
const source = (file) => readFileSync(new URL(file, import.meta.url), 'utf8')
  .replace("from 'three'", `from '${threeUrl}'`).replace("from '../skins/look.js'", `from '${lookUrl}'`);
const { createBoardOrnaments, CORNERS } = await import(moduleUrl(source('../board/ornaments.js')));
const crest = 'chess.banner.crest';
const pink = 'chess.banner.house-pink';
const look = (banner) => ({ banner });
const wait = () => { let resolve; const promise = new Promise((r) => { resolve = r; }); return { promise, resolve }; };

function fixture(pending = new Map()) {
  const scene = new THREE.Scene();
  const geometry = new THREE.BoxGeometry(0.8, 1.2, 0.2);
  geometry.translate(0.6, 0.6, -0.1); // The original attachment sits beside a piece.
  const material = new THREE.MeshStandardMaterial();
  const attachments = {
    prepare: (pick) => pending.get(pick.banner)?.promise || Promise.resolve(),
    boardPartsFor: (pick) => pick.banner ? [{ geometry, material }] : [],
  };
  return { scene, geometry, material, ornaments: createBoardOrnaments({ scene, attachments }) };
}

test('banners occupy two outer corners per side and never cover a playable square', async () => {
  const { scene, ornaments, geometry, material } = fixture();
  await ornaments.setLooks({ w: look(pink), b: look(crest) });
  assert.deepEqual(ornaments.debug().corners, { w: 2, b: 2 });
  scene.updateMatrixWorld(true);
  for (const prop of ornaments.group.children) {
    const box = new THREE.Box3().setFromObject(prop);
    const size = box.getSize(new THREE.Vector3());
    assert.ok(box.max.x < -4 || box.min.x > 4, 'the whole prop stays outside files a-h');
    assert.ok(box.max.z < -4 || box.min.z > 4, 'the whole prop stays outside ranks 1-8');
    assert.ok(Math.abs(box.min.y - CORNERS.floor) < 1e-6, 'the foot rests on the plinth');
    assert.ok(size.x <= CORNERS.footprint + 1e-6 && size.y <= CORNERS.height + 1e-6);
    assert.equal(Math.sign(prop.position.z), prop.userData.side === 'w' ? 1 : -1);
    const hits = [];
    prop.children[0].children[0].raycast({}, hits);
    assert.equal(hits.length, 0, 'ornaments never intercept a move');
  }
  let disposed = 0;
  ornaments.group.traverse((o) => { if (o.isMesh) o.material.addEventListener('dispose', () => disposed++); });
  await ornaments.setLooks({ w: null, b: look(crest) });
  assert.deepEqual(ornaments.debug().corners, { w: 0, b: 2 });
  assert.equal(disposed, 2, 'only the unequipped side is released');
  let geometryDisposed = false, originalDisposed = false;
  geometry.addEventListener('dispose', () => { geometryDisposed = true; });
  material.addEventListener('dispose', () => { originalDisposed = true; });
  ornaments.dispose();
  assert.equal(disposed, 4);
  assert.equal(scene.children.length, 0);
  assert.equal(geometryDisposed, false, 'the shared asset cache still owns geometry');
  assert.equal(originalDisposed, false, 'only material copies are released');
});

test('a slow old selection and a disposed board cannot put banners back', async () => {
  const slow = wait();
  const { ornaments, scene } = fixture(new Map([[crest, slow]]));
  const old = ornaments.setLooks({ w: look(crest) });
  await ornaments.setLooks({ b: look(pink) });
  slow.resolve();
  assert.equal(await old, false);
  assert.deepEqual(ornaments.debug().shown, { w: null, b: pink });
  const later = wait();
  const second = fixture(new Map([[crest, later]]));
  const request = second.ornaments.setLooks({ w: look(crest) });
  second.ornaments.dispose();
  later.resolve();
  assert.equal(await request, false);
  assert.equal(second.scene.children.length, 0);
  ornaments.dispose();
  assert.equal(scene.children.length, 0);
});

test('the attachment cache loads one banner model and gives it only to the board', async () => {
  const registry = JSON.parse(readFileSync(new URL('../assets/skins/registry.json', import.meta.url), 'utf8'));
  const originalFetch = globalThis.fetch;
  globalThis.fetch = async () => ({ ok: true, json: async () => registry });
  const loader = moduleUrl(`import * as THREE from '${threeUrl}';
    export class GLTFLoader { async loadAsync() { const scene = new THREE.Group();
      scene.add(new THREE.Mesh(new THREE.BoxGeometry(1, 1, 1), new THREE.MeshStandardMaterial()));
      return { scene }; } }`);
  const merge = moduleUrl('export const mergeGeometries = () => null;');
  try {
    const src = source('../board/attachments.js')
      .replace("import('three/addons/loaders/GLTFLoader.js')", `import('${loader}')`)
      .replace("import('three/addons/utils/BufferGeometryUtils.js')", `import('${merge}')`);
    const { createAttachments } = await import(moduleUrl(src));
    const attachments = createAttachments();
    const picked = { banner: crest, insignia: 'chess.insignia.star' };
    await attachments.prepare(picked);
    assert.deepEqual(attachments.debug().loaded.filter((id) => id.startsWith(crest)), [crest + '|pawn']);
    for (const type of ['p', 'n', 'b', 'r', 'q', 'k']) {
      const worn = attachments.partsFor(type, picked);
      assert.equal(worn.length, 1);
      assert.equal(worn[0].slot, 'insignia', 'badges still dress every piece');
    }
    assert.equal(attachments.boardPartsFor(picked).length, 1);
    assert.deepEqual(attachments.boardPartsFor({ banner: 'unknown' }), []);
  } finally { globalThis.fetch = originalFetch; }
});
