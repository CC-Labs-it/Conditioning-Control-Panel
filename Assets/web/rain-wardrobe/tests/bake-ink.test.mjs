// The ink look (2026-10-30): render-time inverted hulls + readable fishnets, then the shared post
// pass (sharpen + pop) after the 2x -> 1x box filter. Deterministic, no WebGL.
import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from '../../vendor/three/three.module.min.js';
import {
  INK, INK_LAYER, TOON, sharpenPass, popPass, tubeFrame, boxDownscale, unpremultiply, inkWidth, inkMaterial, addInkHulls, inkFishnet, installBaker
} from '../bake.js';

const chroma = (r, g, b) => Math.max(r, g, b) - Math.min(r, g, b);
const px = (rgba, a = 255) => ({ data: new Uint8ClampedArray(rgba.length ? [...rgba, a] : []), width: 1, height: 1 });

test('sharpen: flat colour stays flat, an edge gains contrast, alpha untouched, colour never above alpha', () => {
  const W = 12, H = 8, data = new Uint8ClampedArray(W * H * 4);
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    const i = (y * W + x) * 4, v = x < 6 ? 80 : 180, a = x >= 10 ? 128 : 255;
    data[i] = data[i + 1] = data[i + 2] = Math.round(v * a / 255); data[i + 3] = a;
  }
  const out = sharpenPass({ data, width: W, height: H }).data;
  const at = (x, y, c = 0) => out[(y * W + x) * 4 + c];
  for (let i = 3; i < data.length; i += 4) assert.equal(out[i], data[i]);
  for (let i = 0; i < data.length; i += 4) assert.ok(out[i] <= out[i + 3]);
  assert.equal(at(2, 4), 80);                     // flat on the dark side
  assert.ok(at(5, 4) < 80 && at(6, 4) > 180, `edge ${at(5, 4)} ${at(6, 4)}`);
  assert.deepEqual([...sharpenPass({ data, width: W, height: H }, { sharpen: 0 }).data], [...data]);
});

test('pop: contrast S-curve, saturation raises chroma, greys stay grey, the pink stays pink and capped, skin gets less', () => {
  const run = (r, g, b) => [...popPass(px([r, g, b])).data];
  const dress = run(219, 79, 156), out = popPass(px([219, 79, 156])).data;
  assert.ok(chroma(...dress) > chroma(219, 79, 156));
  assert.ok(dress[0] > dress[2] && dress[2] > dress[1], `still pink ${dress}`);
  assert.ok(dress[1] > 0 && dress[2] < 255, `capped, not neon: ${dress}`);   // at most the lead channel reaches 255, the hue holds
  const grey = run(120, 120, 120);
  assert.ok(grey[0] === grey[1] && grey[1] === grey[2], 'greys stay grey');
  // Contrast: an S-curve on luma, deeper shadows and brighter highlights, mid grey fixed.
  assert.ok(grey[0] < 120 && run(190, 190, 190)[0] > 190, `${grey} ${run(190, 190, 190)}`);
  assert.equal(run(128, 128, 128)[0], 128);
  assert.equal(popPass(px([120, 120, 120]), { contrast: 0 }).data[0], 120);
  assert.equal(out[3], 255);
  assert.equal(popPass(px([200, 100, 50], 0)).data[0], 200);   // clear pixels untouched
  // A pale skin tone gains less than a pale violet of the same saturation (vibrance skips skin).
  const gain = rgb => chroma(...run(...rgb)) / chroma(...rgb);
  assert.ok(gain([230, 190, 170]) < gain([200, 170, 230]), `${gain([230, 190, 170])} ${gain([200, 170, 230])}`);
  assert.ok(gain([230, 190, 170]) > 1.1);
});

test('tubeFrame: toon = box down + sharpen + pop; toon false = the plain downscale', () => {
  const W = 8, H = 6, raw = new Uint8ClampedArray(W * 2 * H * 2 * 4);
  for (let i = 0; i < raw.length; i += 4) { const x = (i / 4) % (W * 2); raw[i] = x < W ? 60 : 200; raw[i + 1] = 40; raw[i + 2] = x < W ? 120 : 90; raw[i + 3] = 255; }
  const frame = { data: raw, width: W * 2, height: H * 2 };
  const plain = tubeFrame(frame, W, H, { toon: false });
  assert.deepEqual([...plain.data], [...unpremultiply(boxDownscale(raw, W * 2, H * 2, 2).data)]);
  const toon = tubeFrame(frame, W, H);
  assert.equal(toon.width, W); assert.equal(toon.height, H);
  assert.notDeepEqual([...toon.data], [...plain.data]);
  for (let i = 3; i < toon.data.length; i += 4) assert.equal(toon.data[i], 255);
});

test('inkWidth: body / dress / boots full width, hair thinner, head thinnest, tiny parts none', () => {
  const mesh = (name, mat = 'x') => ({ name, material: { name: mat } });
  assert.equal(inkWidth(mesh('GEO-rain-body', 'Companion.Complexion.Stockings')), INK.width);
  assert.equal(inkWidth(mesh('Companion.Wardrobe.Dress', 'Companion.Wardrobe.Orchid satin')), INK.width);
  assert.equal(inkWidth(mesh('Companion.HairV9.Foundation', 'Companion.HairV2.Champagne strands')), INK.hairWidth);
  assert.equal(inkWidth(mesh('mesh_12', 'Companion.HairV9.Warm cream')), INK.hairWidth);
  assert.equal(inkWidth(mesh('GEO-rain-head', 'Companion.MakeupV10.Skin')), INK.headWidth);
  for (const name of ['GEO-rain-eyes', 'GEO-rain-eyelashes', 'Companion.BootV3.Zip tooth 3.L', 'GEO-rain-tongue', 'Companion.Wardrobe.Hoop.L'])
    assert.equal(inkWidth(mesh(name)), 0, name);
  assert.ok(INK.hairWidth < INK.width && INK.headWidth < INK.hairWidth);
  // About 2-2.5 px of the 384x618 tube for the silhouette, about 1.5 px on the hair (3.74 mm a pixel).
  assert.ok(INK.width / 0.00374 >= 2 && INK.width / 0.00374 <= 2.5);
  assert.ok(INK.hairWidth / 0.00374 > 1.3 && INK.hairWidth / 0.00374 < 1.7);
});

test('ink hulls: one back-face child per outlined mesh on the bake layer, pushed along the normal, removed after', () => {
  const root = new THREE.Group();
  const geo = new THREE.BoxGeometry(1, 1, 1);
  const body = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ name: 'Companion.Wardrobe.Orchid satin' })); body.name = 'Companion.Wardrobe.Dress';
  const eyes = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ name: 'Companion.EyesV3.Ivory' })); eyes.name = 'GEO-rain-eyes';
  const ground = new THREE.Mesh(geo, new THREE.MeshBasicMaterial()); ground.name = 'ground';
  const hair = new THREE.Mesh(geo, new THREE.MeshStandardMaterial({ name: 'Companion.HairV9.Warm cream' })); hair.name = 'lock'; hair.scale.setScalar(2);
  root.add(body, eyes, ground, hair);
  let priorRan = 0;
  body.material.onBeforeCompile = () => { priorRan++; };
  const hulls = addInkHulls(THREE, root, { skip: [ground] });
  assert.equal(hulls.count, 2);
  const hull = body.children[0];
  assert.equal(hull.material.side, THREE.BackSide);
  assert.ok(hull.layers.isEnabled(INK_LAYER) && !hull.layers.isEnabled(0), 'the visible view never sees it');
  assert.equal(hull.geometry, body.geometry);
  assert.equal(eyes.children.length, 0); assert.equal(ground.children.length, 0);
  const shader = { uniforms: {}, vertexShader: 'void main(){\n#include <skinning_vertex>\n}', fragmentShader: 'void main(){\n#include <dithering_fragment>\n}' };
  hull.material.onBeforeCompile(shader, null);
  assert.equal(priorRan, 1, 'the wardrobe discards still run on the hull');
  assert.equal(shader.uniforms.rainInkWidth.value, INK.width);
  assert.match(shader.vertexShader, /transformed \+= normalize\(objectNormal\) \* rainInkWidth;/);
  assert.match(shader.fragmentShader, /gl_FragColor\.rgb = vec3\(/);
  assert.equal(hair.children[0].material.customProgramCacheKey().includes((INK.hairWidth / 2).toFixed(5)), true, 'width in object units');
  hulls.remove();
  assert.equal(body.children.length, 0); assert.equal(hair.children.length, 0);
});

test('inkMaterial keeps a base program key apart from the plain one', () => {
  const base = new THREE.MeshStandardMaterial({ name: 'm' });
  base.customProgramCacheKey = () => 'rain-legwear-v4-true';
  const ink = inkMaterial(THREE, base, 0.005);
  assert.match(ink.customProgramCacheKey(), /^rain-legwear-v4-true\|rain-ink-/);
  assert.equal(ink.name, 'm.ink');
});

test('fishnets read during the bake: darker, more opaque, wider strands, restored after; other legwear untouched', () => {
  const garment = { mode: { value: 1 }, color: { value: new THREE.Color(0.075, 0.012, 0.038) }, opacity: { value: 0.78 }, strand: { value: 0.092 } };
  const undo = inkFishnet(garment);
  assert.ok(Math.abs(garment.color.value.r - 0.075 * INK.fishnetDarken) < 1e-6);
  assert.equal(garment.opacity.value, INK.fishnetOpacity);
  assert.equal(garment.strand.value, INK.fishnetStrand);
  undo();
  assert.ok(Math.abs(garment.color.value.r - 0.075) < 1e-6);
  assert.equal(garment.opacity.value, 0.78); assert.equal(garment.strand.value, 0.092);
  const tights = { mode: { value: 2 }, color: { value: new THREE.Color(0.1, 0.1, 0.1) }, opacity: { value: 0.42 } };
  inkFishnet(tights)();
  assert.equal(tights.opacity.value, 0.42);
});

test('the bake asks for ink; the October pixel gate does not', async () => {
  const seen = [];
  const baker = installBaker({ post: () => {} }, { webview: null, makeSource: (ctx, w, h, options) => { seen.push(options); throw new Error('stop'); } });
  await baker.start({ lookHash: 'a', clips: ['idle'] }, () => {});
  await baker.start({ lookHash: 'b', clips: ['idle'], pixel: true }, () => {});
  await baker.start({ lookHash: 'c', clips: ['idle'], palette: 'shipped' }, () => {});
  assert.notEqual(seen[0].ink, false);
  assert.equal(seen[1].ink, false); assert.equal(seen[2].ink, false);
  assert.ok(TOON.saturation > 0.25 && TOON.sharpen >= 0.8 && TOON.sharpen <= 1.2 && TOON.sharpenSigma >= 0.8 && TOON.sharpenSigma <= 1);
});
