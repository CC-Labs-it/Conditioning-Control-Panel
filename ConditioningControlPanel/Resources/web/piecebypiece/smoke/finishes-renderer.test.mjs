import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { register } from 'node:module';

// Resolve the same local Three modules as the game's browser import map.
const vendor = new URL('../../vendor/three/', import.meta.url);
register('data:text/javascript,' + encodeURIComponent(`
  export async function resolve(id, context, next) {
    if (id === 'three') return { url: '${vendor}three.module.min.js', shortCircuit: true };
    if (id.startsWith('three/addons/')) return { url: '${vendor}' + id.slice(6), shortCircuit: true };
    return next(id, context);
  }`));

const THREE = await import('three');
const { createPieces, TYPES } = await import('../board/pieces.js');
const { createJiggle } = await import('../board/jiggle.js');
const { FINISHES, setFinishTime } = await import('../board/finishes.js');
const { FINISH_IDS, finishKey } = await import('../skins/finish-ids.js');
const shaderOf = mat => {
  const { vertexShader, fragmentShader } = THREE.ShaderLib.physical;
  const shader = { vertexShader, fragmentShader, uniforms: {} };
  mat.onBeforeCompile(shader, null);
  return shader;
};

test('approved sculpts take, switch and remove their finish while retaining the spring and jewellery', async t => {
  // Only a contact shadow needs a canvas. Feed the actual twelve GLBs to the game's GLTFLoader.
  t.mock.method(globalThis, 'fetch', async (input, init) => {
    if (init?.method === 'HEAD') return new Response(null);
    const name = new URL(input.url ?? input).pathname.split('/').pop();
    const body = await readFile(new URL('../assets/pieces/' + name, import.meta.url));
    return new Response(body);
  });
  const savedDocument = globalThis.document;
  const savedProgressEvent = globalThis.ProgressEvent;
  globalThis.document = { createElement: () => ({ getContext: () => ({
    createRadialGradient: () => ({ addColorStop() {} }), fillRect() {},
  }) }) };
  globalThis.ProgressEvent ??= class { constructor(type, data) { Object.assign(this, { type }, data); } };
  t.after(() => {
    if (savedDocument === undefined) delete globalThis.document; else globalThis.document = savedDocument;
    if (savedProgressEvent === undefined) delete globalThis.ProgressEvent; else globalThis.ProgressEvent = savedProgressEvent;
  });
  const pieces = createPieces({ group: new THREE.Group(), jiggle: createJiggle(), assetsBase: 'https://chess.invalid/pieces/' });
  const position = {};
  TYPES.forEach((type, i) => { for (const side of ['w', 'b']) position['abcdef'[i] + (side === 'w' ? '1' : '8')] = { type, side }; });
  pieces.setPosition(position);
  const item = id => ({ finish: 'chess.finish.' + id });
  await pieces.setLooks({ w: item('polka'), b: item('opal') });
  await pieces.tryLoadGlb();
  for (const piece of pieces.pieces.values()) {
    assert.match(piece.userData.artSource, /\.glb/);
    assert.equal(piece.userData.finish, piece.userData.side === 'w' ? 'polka' : 'opal');
    assert.ok(piece.children[0].geometry.getAttribute('finishSurface'));
    const shader = shaderOf(piece.userData.material);
    assert.equal(shader.uniforms.uBend, piece.userData.jiggleUniforms.uBend);
    assert.ok(shader.fragmentShader.includes('finishPaint(vFinishPosition)'));
    for (const jewel of piece.userData.materials.slice(1)) {
      assert.equal(shaderOf(jewel).uniforms.uFinishKind, undefined);
    }
  }
  assert.deepEqual(FINISH_IDS.map(finishKey), FINISHES.map(f => f.id));
  const white = pieces.pieceAt('a1'), black = pieces.pieceAt('a8');
  const body = white.userData.material;
  body.transparent = true;
  body.opacity = .54;
  for (const finish of FINISHES) {
    await pieces.setLooks({ w: item(finish.id), b: item('opal') });
    assert.equal(pieces.pieceAt('a1'), white);
    assert.equal(pieces.pieceAt('a8'), black);
    assert.equal(shaderOf(body).uniforms.uFinishKind.value, FINISHES.indexOf(finish));
  }
  const clock = shaderOf(body).uniforms.uFinishClock;
  setFinishTime(10, true); setFinishTime(10.1, true);
  const moving = clock.value;
  assert.ok(moving > 0);
  setFinishTime(10.2, false); setFinishTime(12, true);
  assert.equal(clock.value, moving);
  setFinishTime(12.1, .35);
  assert.ok(Math.abs(clock.value - moving - .1 * .5 * .35) < 1e-7);

  const promoted = { ...position, a1: { type: 'q', side: 'w' } };
  pieces.setPosition(promoted);
  assert.equal(pieces.pieceAt('a1').userData.finish, 'kintsugi');
  await pieces.setLooks({ w: item('unknown'), b: item('contour') });
  assert.equal(pieces.pieceAt('a1').userData.finish, null);
  assert.equal(shaderOf(pieces.pieceAt('a1').userData.material).uniforms.uFinishKind, undefined);
  const blackBody = black.userData.material;
  blackBody.opacity = .36;
  await pieces.setLooks({ w: null, b: null });
  assert.equal(pieces.pieceAt('a8'), black);
  assert.equal(blackBody.opacity, .36);
  assert.equal(shaderOf(blackBody).uniforms.uBend, black.userData.jiggleUniforms.uBend);
  assert.equal(shaderOf(blackBody).uniforms.uFinishKind, undefined);
  assert.ok(!blackBody.customProgramCacheKey().includes('finish'));

  // Like Three's renderer, a material reuses a linked program without calling
  // onBeforeCompile again. Disposing the material clears that program map.
  function gpu(material) {
    const programs = new Map();
    let uniforms;
    material.addEventListener('dispose', () => programs.clear());
    return () => {
      const key = material.customProgramCacheKey();
      if (!programs.has(key)) {
        programs.set(key, true);
        uniforms = shaderOf(material).uniforms;
      }
      return uniforms;
    };
  }
  const whiteQueen = pieces.pieceAt('a1'), whiteGpu = gpu(whiteQueen.userData.material), blackGpu = gpu(blackBody);
  for (let cycle = 0; cycle < 2; cycle++) {
    assert.equal(whiteGpu().uFinishBase, undefined);
    assert.equal(blackGpu().uFinishBase, undefined);
    await pieces.setLooks({ w: item('polka'), b: item('polka') });
    const w = whiteGpu(), b = blackGpu();
    assert.equal(w.uFinishBase.value.getHexString(), 'bd085c');
    assert.equal(b.uFinishBase.value.getHexString(), '123fb4');
    assert.equal(w.uFinishClock, b.uFinishClock);
    assert.equal(w.uBend, whiteQueen.userData.jiggleUniforms.uBend);
    assert.equal(b.uBend, black.userData.jiggleUniforms.uBend);
    assert.notEqual(w.uBend, b.uBend);
    await pieces.setLooks({ w: null, b: null });
  }
  assert.equal(whiteGpu().uFinishBase, undefined);
  assert.equal(blackGpu().uFinishBase, undefined);
});
