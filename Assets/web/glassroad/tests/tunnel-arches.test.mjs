import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { tunnelLayout, mediaLook, archAt } from '../render/mediaMath.js';
import { helixBodyData } from '../render/helixBody.js';
import { HELIX_LOOK } from '../render/helixMath.js';

const hooks = registerHooks({ resolve(specifier, context, next) {
  const files = { 'three/webgpu': 'three.webgpu.min.js', 'three/tsl': 'three.tsl.min.js' };
  return files[specifier] ? { url: new URL('../vendor/three/build/' + files[specifier], import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { tunnelArchGeometry, tunnelArchMaterial } = await import('../render/tunnelArches.js');
const { createHelix } = await import('../render/helix.js');
const { uniform } = await import('three/tsl');
hooks.deregister();

test('screen tunnel ribs have solid cross-sections outside the accepted inner aperture', t => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url))));
  const spec = JSON.parse(readFileSync(new URL('../tracks/mainframe.media.json', import.meta.url))).tunnel;
  const tunnel = tunnelLayout(spec, track, mediaLook());
  const geometry = tunnelArchGeometry(tunnel, track, spec, 32); t.after(() => geometry.dispose());
  const positions = geometry.getAttribute('position'), normals = geometry.getAttribute('normal');
  assert.ok(positions.count > tunnel.ribs.positions.length / 3, 'inner, outer and edge faces replace flat strips');
  assert.ok([...positions.array, ...normals.array].every(Number.isFinite));
  const firstStation = tunnel.rings[0] - (tunnel.rings[1] - tunnel.rings[0]) / 2;
  const frame = track.frameAt(firstStation - 1.15 / 2);
  const original = archAt(frame, 0, frame.w / 2 + (spec.padM ?? 3.5) - .15, tunnel.heightM - .15, 1).P;
  for (let axis = 0; axis < 3; axis++) assert.ok(Math.abs(positions.array[axis] - original[axis]) < .0001, 'inner edge does not narrow the driving aperture');
  const inner = [positions.getX(0), positions.getY(0), positions.getZ(0)];
  const outerIndex = 4;
  const outer = [positions.getX(outerIndex), positions.getY(outerIndex), positions.getZ(outerIndex)];
  assert.ok(Math.abs(Math.hypot(...outer.map((v, i) => v - inner[i])) - .72) < .0001, 'rib has physical depth');
  assert.ok(geometry.index.count < 50000, 'bounded prewarmed static mesh');
});

test('arch skin is opaque textured material with shared paused/Gentle clock and contrast inputs', t => {
  const time = uniform(0), contrast = uniform(1), material = tunnelArchMaterial(time, contrast, 'mainframe');
  t.after(() => material.dispose());
  const nodes = new Set(), pending = [material.colorNode];
  while (pending.length) {
    const node = pending.pop(); if (nodes.has(node)) continue; nodes.add(node);
    for (const child of node.getChildren()) pending.push(child);
  }
  assert.equal(material.transparent, false); assert.equal(material.depthWrite, true);
  assert.ok(nodes.has(time)); assert.ok(nodes.has(contrast));
  assert.equal(material.positionNode, null, 'moving inlay never moves the solid rib');
});

test('music helix has a thick six-sided body separate from its glow and leaves road clearance', t => {
  const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url))));
  const data = helixBodyData(track, HELIX_LOOK), helix = createHelix(track, null);
  t.after(() => helix.dispose());
  assert.equal(data.sides, 6); assert.ok(data.radius >= .5);
  assert.ok(Object.values(data.attrs).every(values => values.every(Number.isFinite)));
  assert.ok(data.count / track.lengthM < 6.1, 'bounded six vertices per metre across both strands');
  for (let i = 0; i < data.count; i += 6) {
    const frame = track.frameAt(data.attrs.aP[i * 4] % track.lengthM), radial = data.attrs.aP[i * 4 + 3];
    assert.ok(Math.sqrt((radial - data.radius) ** 2 - HELIX_LOOK.liftM ** 2) > frame.w / 2 + 1.5, 'physical cable stays beyond road shoulder');
  }
  const body = helix.object.getObjectByName('music-helix-textured-body'), glow = helix.object.getObjectByName('music-helix-song-glow');
  assert.ok(body && glow); assert.notEqual(body.material.blending, glow.material.blending);
  assert.ok(Object.keys(body.geometry.attributes).length <= 8, 'WebGPU vertex buffer budget');
  helix.update({ on: true, helix: 1, helixPhase: 1, kick: .8, barIndex: 3, barPhase: .5 }, { kartS: 60 });
  assert.equal(helix.object.visible, true);
  helix.update(null); assert.equal(helix.object.visible, false, 'no song hides both body and glow');
});
