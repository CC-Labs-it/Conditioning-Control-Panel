import test from 'node:test';
import assert from 'node:assert/strict';
import { register } from 'node:module';

const vendor = new URL('../../vendor/three/three.module.min.js', import.meta.url);
register('data:text/javascript,' + encodeURIComponent(`
  export async function resolve(id, context, next) {
    if (id === 'three') return { url: '${vendor}', shortCircuit: true };
    if (id.startsWith('three/addons/')) return { url: new URL(id.slice(6), '${vendor}').href, shortCircuit: true };
    return next(id, context);
  }`));
const THREE = await import('three');
const { createBoardTheme } = await import('../board/themes.js');
const { BOARD_THEME_IDS, boardThemeKey } = await import('../board/theme-ids.js');
const { setPresentation } = await import('../game/preferences.js');

function canvasDocument() {
  const gradient = { addColorStop() {} };
  const context = new Proxy({
    createLinearGradient: () => gradient, createRadialGradient: () => gradient,
  }, { get: (object, key) => object[key] || (() => {}) });
  return { createElement: () => ({ getContext: () => context }) };
}

function fixture() {
  const scene = new THREE.Scene(), boardGroup = new THREE.Group(), room = new THREE.Group();
  room.name = 'pbp-room';
  scene.background = new THREE.Color(0x34152a);
  scene.environment = new THREE.Texture();
  scene.fog = new THREE.Fog(0x423052, 14, 30);
  scene.add(boardGroup, room);
  const plinth = new THREE.Mesh(new THREE.BoxGeometry(9.4,.5,9.4),
    [new THREE.MeshPhysicalMaterial({color:0x543214}), new THREE.MeshPhysicalMaterial({color:0x341234})]);
  boardGroup.add(plinth);
  const squares = new Map();
  for (let f = 0; f < 8; f++) for (let rank = 0; rank < 8; rank++) {
    const material = new THREE.MeshPhysicalMaterial({color:(f+rank)%2 ? 0xeeeecc : 0x552233});
    const mesh = new THREE.Mesh(new THREE.BoxGeometry(1,.16,1),material);
    mesh.position.set(f-3.5,-.08,3.5-rank);
    mesh.userData.base = material.color.clone();
    squares.set('abcdefgh'[f]+(rank+1),mesh);
    boardGroup.add(mesh);
  }
  const lights = {hemi:new THREE.HemisphereLight(), key:new THREE.DirectionalLight(),
    fill:new THREE.DirectionalLight(0x393959),rim:new THREE.DirectionalLight(0x803264)};
  return {scene,boardGroup,squares,plinth,room,lights};
}

test('all board themes restore original materials, backdrop and lights and release their owned resources', t => {
  const savedDocument = globalThis.document;
  globalThis.document = canvasDocument();
  t.after(() => { globalThis.document = savedDocument; setPresentation({ reducedMotion:false }); });
  const view = fixture();
  const background = view.scene.background, originalFog = view.scene.fog.color.clone();
  const originals = [...view.squares.values()].map(mesh => mesh.material);
  const plinthMaterials = view.plinth.material;
  const lights = Object.values(view.lights).map(light => light.color.clone());
  const external = [...originals,...plinthMaterials,view.scene.environment];
  let externalDisposed = 0;
  for (const resource of external) resource.addEventListener('dispose',()=>externalDisposed++);
  const theme = createBoardTheme({view});
  for (const item of BOARD_THEME_IDS) {
    assert.equal(theme.setTheme(item),true);
    assert.equal(theme.debug().id,boardThemeKey(item));
    assert.equal(theme.setTheme(item),false);
    assert.equal(view.room.visible,false);
    const owned = new Set([view.scene.background,...view.plinth.material]);
    const decorated = view.boardGroup.getObjectByName('theme-'+boardThemeKey(item));
    assert.ok(decorated);
    decorated.traverse(object => {
      if (object.geometry) {
        owned.add(object.geometry);
        for (const value of object.geometry.attributes.position.array) assert.ok(Number.isFinite(value));
      }
      if (object.material) owned.add(object.material);
      const hits = [];
      object.raycast({},hits);
      assert.deepEqual(hits,[]);
    });
    for (const mesh of view.squares.values()) {
      owned.add(mesh.material);
      owned.add(mesh.material.map);
      assert.equal(mesh.position.y,-.08);
    }
    let disposed = 0;
    for (const resource of owned) resource.addEventListener('dispose',()=>disposed++);
    theme.react({kind:'capture'});
    theme.update(.1);
    assert.ok(theme.debug().elapsed > 0);
    const elapsed = theme.debug().elapsed;
    theme.update(0);
    assert.equal(theme.debug().elapsed,elapsed);
    setPresentation({reducedMotion:true});
    theme.update(.1);
    assert.equal(theme.debug().elapsed,elapsed);
    setPresentation({reducedMotion:false});
    assert.equal(theme.setTheme(null),true);
    assert.equal(disposed,owned.size,'every owned GPU resource is released once');
    assert.equal(view.scene.background,background);
    assert.ok(view.scene.fog.color.equals(originalFog));
    assert.equal(view.room.visible,true);
    assert.equal(view.plinth.material,plinthMaterials);
    assert.deepEqual([...view.squares.values()].map(mesh=>mesh.material),originals);
    assert.ok(Object.values(view.lights).every((light,index)=>light.color.equals(lights[index])));
    assert.equal(view.boardGroup.children.length,65);
  }
  assert.equal(externalDisposed,0);
  assert.equal(boardThemeKey('chess.board.unknown'),null);
  theme.setTheme('arcade');
  assert.equal(theme.setTheme('unknown'),true);
  theme.dispose();
  theme.dispose();
  assert.equal(theme.setTheme('theatre'),false);
  assert.equal(view.boardGroup.children.length,65);
});

test('tile finishes have distinct physical surfaces and share a clock that pauses without resetting', t => {
  const savedDocument = globalThis.document;
  globalThis.document = canvasDocument();
  setPresentation({reducedMotion:false,boardMotion:'full'});
  t.after(() => { globalThis.document = savedDocument; setPresentation({reducedMotion:false,boardMotion:'full'}); });
  const view = fixture(), theme = createBoardTheme({view});
  t.after(() => theme.dispose());
  const original = view.squares.get('a1').material;
  const profiles = new Set();
  for (const id of BOARD_THEME_IDS) {
    theme.setTheme(id);
    const dark = view.squares.get('a1').material, light = view.squares.get('a2').material;
    profiles.add(JSON.stringify([dark.roughness,dark.metalness,dark.clearcoat,dark.sheen,light.roughness,light.clearcoat]));
    assert.ok(Math.abs(dark.roughness-light.roughness) > .12,'the two tile materials have different responses to light');
    const clock = dark.userData.boardTileMotion.uniforms.uTileTime;
    const seeds = new Set();
    for (const mesh of view.squares.values()) {
      const {uniforms} = mesh.material.userData.boardTileMotion;
      assert.equal(uniforms.uTileTime,clock);
      seeds.add(uniforms.uTileSeed.value);
      assert.equal(mesh.material.transparent,false);
      assert.equal(mesh.material.transmission,0);
      // Build from the real stock physical shader to catch drift in hook names.
      const source = THREE.ShaderLib.physical;
      const shader = {uniforms:{},vertexShader:source.vertexShader,fragmentShader:source.fragmentShader};
      mesh.material.onBeforeCompile(shader,{});
      assert.equal(shader.uniforms.uTileTime,clock);
      assert.ok(shader.vertexShader.includes('vTileLocal=position.xz'));
      assert.ok(shader.fragmentShader.includes('roughnessFactor+tileRoughShift'));
      assert.ok(shader.fragmentShader.includes('totalEmissiveRadiance+=tileGlow'));
    }
    assert.equal(seeds.size,64,'tile pattern placement varies across the board');
    theme.update(.08);
    assert.equal(clock.value,.08);
    theme.update(0);
    theme.update(Number.NaN);
    assert.equal(clock.value,.08,'pause and invalid elapsed time do not advance surface motion');
    setPresentation({boardMotion:'still'});
    theme.react({kind:'capture'});
    theme.update(.1);
    assert.equal(clock.value,.08);
    setPresentation({boardMotion:'full',reducedMotion:true});
    theme.update(.1);
    assert.equal(clock.value,.08);
    setPresentation({reducedMotion:false});
    theme.update(.04);
    assert.equal(clock.value,.12,'resuming carries on from the frozen pattern');
    theme.setTheme(null);
    assert.equal(view.squares.get('a1').material,original);
    theme.update(.1);
    assert.equal(clock.value,.12,'released theme uniforms are no longer animated');
  }
  assert.equal(profiles.size,4);
});
