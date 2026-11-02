import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
const hooks = registerHooks({ resolve(specifier, context, next) {
  return specifier === 'three/webgpu' ? { url: new URL('../vendor/three/build/three.webgpu.min.js', import.meta.url).href, shortCircuit: true } : next(specifier, context);
} });
const { buildBoudoirForks, buildForkMeshData, forkPortalDepth, forkSignPlan, FORK_RENDER } = await import('../render/boudoirForks.js');
const { MeshBasicNodeMaterial, Group } = await import('three/webgpu'); hooks.deregister();
const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/boudoir.json', import.meta.url))));
const close = (a, b, tolerance = 2e-4) => assert.ok(Math.abs(a - b) < tolerance, `${a} != ${b}`);
function trianglesAreValid(data) {
  const { positions: p, normals: n, indices } = data;
  assert.ok([...p, ...n, ...data.uvs].every(Number.isFinite));
  for (let i = 0; i < indices.length; i += 3) {
    const [a, b, c] = Array.from(indices.slice(i, i + 3), v => v * 3);
    assert.ok(Math.max(a, b, c) < p.length);
    const u = [0, 1, 2].map(k => p[b + k] - p[a + k]), v = [0, 1, 2].map(k => p[c + k] - p[a + k]);
    const cross = [u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0]];
    assert.ok(Math.hypot(...cross) > 1e-5, 'every face has real area');
    assert.ok(cross.reduce((sum, value, k) => sum + value * n[a + k], 0) >= -1e-5, `winding faces its declared normal at triangle ${i / 3}, vertex ${a / 3}`);
  }
}

test('fork decks follow real support width, thickness and mapped main progress at every sample', () => {
  const data = buildForkMeshData(track.forks);
  assert.equal(data.summaries.length, 3); trianglesAreValid(data.road); trianglesAreValid(data.rails);
  let first = 0;
  for (const fork of track.forks) {
    const samples = fork.samples(FORK_RENDER.sampleM);
    samples.forEach((f, row) => {
      const depth = forkPortalDepth(f.localS, fork.lengthM);
      for (let col = 0; col <= 4; col++) {
        const top = first + row * 14 + col * 2, bottom = top + 1, x = (col / 4 - .5) * f.w;
        close(data.road.uvs[top * 2], x); close(data.road.uvs[top * 2 + 1], f.s, .001);
        close(data.road.forkWidth[top], f.w / 2);
        for (let k = 0; k < 3; k++) {
          close(data.road.positions[top * 3 + k], f.p[k] + f.b[k] * x + f.n[k] * depth);
          close(data.road.positions[bottom * 3 + k], f.p[k] + f.b[k] * x + f.n[k] * (depth - FORK_RENDER.thicknessM));
        }
      }
    });
    first += samples.length * 14 + 8;
  }
  assert.equal(first, data.road.positions.length / 3);
  assert.ok(data.road.indices.length / 3 < 22000, 'all three solid roads stay within a modest primitive budget');
});

test('visual rails use the engine rail openings and the minimap includes exact endpoints', () => {
  const data = buildForkMeshData(track.forks);
  for (const [i, fork] of track.forks.entries()) {
    const frames = fork.samples(FORK_RENDER.sampleM); let expected = 0, skipped = 0;
    for (let j = 1; j < frames.length; j++) {
      const f = fork.frameAt((frames[j - 1].localS + frames[j].localS) / 2);
      for (const flag of ['railL', 'railR']) if (f.flags.includes('rail') || f.flags.includes(flag)) expected++; else skipped++;
    }
    assert.equal(data.summaries[i].railSpans, expected); assert.ok(skipped > 0, fork.id + ' has real open junction edges');
    assert.deepEqual(data.minimap[i].points[0], [frames[0].p[0], frames[0].p[2]]);
    assert.deepEqual(data.minimap[i].points.at(-1), [frames.at(-1).p[0], frames.at(-1).p[2]]);
  }
  close(forkPortalDepth(0, 300), -.025); close(forkPortalDepth(300, 300), -.025);
  close(forkPortalDepth(24, 300), 0); close(forkPortalDepth(150, 300), 0);
});

test('route signs preserve real labels and reflect left or right physical choices', () => {
  const signs = forkSignPlan(track);
  for (const [i, sign] of signs.entries()) {
    assert.equal(sign.choices[1].label, track.forks[i].label);
    assert.equal(sign.choices[0].arrow, 'straight'); assert.equal(sign.choices[0].side, -sign.choices[1].side);
    const portal = track.forkPortals.find(p => p.id === sign.id);
    assert.equal(sign.s, portal.s0 - 28); assert.equal(sign.direction, portal.side);
  }
  for (const side of [-1, 1]) {
    const plan = forkSignPlan({ frameAt: s => ({ p: [0, 0, s], b: [1, 0, 0] }), forks: [{ id: 'choice', label: 'Shortcut', fromS: 100, lengthM: 200,
      mainS: s => s + 100, frameAt: s => ({ p: [side * 10, 0, s + 100] }) }] });
    assert.equal(plan[0].choices[1].arrow, side < 0 ? 'left' : 'right');
  }
});

test('five draw batches keep shader attributes bounded and preserve shared materials on dispose', () => {
  const roadMaterial = new MeshBasicNodeMaterial(), railMaterial = new MeshBasicNodeMaterial(); let sharedDisposes = 0, geometryDisposes = 0;
  for (const material of [roadMaterial, railMaterial]) material.addEventListener('dispose', () => sharedDisposes++);
  const view = buildBoudoirForks(track, { roadMaterial, railMaterial }), parent = new Group(); parent.add(view.group);
  assert.equal(view.diagnostics.drawCalls, 5);
  const road = view.group.getObjectByName('fork-road'); assert.equal(road.material, roadMaterial); assert.equal(Object.keys(road.geometry.attributes).length, 7);
  const seen = new Set(); view.group.traverse(mesh => { if (mesh.geometry && !seen.has(mesh.geometry)) { seen.add(mesh.geometry); mesh.geometry.addEventListener('dispose', () => geometryDisposes++); }
    if (mesh.geometry && !mesh.isInstancedMesh) trianglesAreValid({ positions: mesh.geometry.attributes.position.array, normals: mesh.geometry.attributes.normal.array, uvs: mesh.geometry.attributes.uv.array, indices: mesh.geometry.index.array }); });
  const before = view.group.children.map(mesh => Array.from(mesh.matrix.elements)); view.update(10, { paused: true });
  assert.deepEqual(view.group.children.map(mesh => Array.from(mesh.matrix.elements)), before);
  view.dispose(); view.dispose(); assert.equal(sharedDisposes, 0); assert.equal(geometryDisposes, seen.size); assert.equal(parent.children.length, 0);
  const phone = buildBoudoirForks(track, { roadMaterial, railMaterial, phone: true });
  assert.ok(phone.diagnostics.vertices < view.diagnostics.vertices * .7); assert.equal(phone.diagnostics.drawCalls, 5); phone.dispose();
  roadMaterial.dispose(); railMaterial.dispose();
});

test('shared apron edges suppress buried skirts and gutter marks while real open edges remain visible', () => {
  const data = buildForkMeshData(track.forks); let first = 0, expectedTriangles = 0;
  for (const [i, fork] of track.forks.entries()) {
    const samples = fork.samples(FORK_RENDER.sampleM), summary = data.summaries[i];
    assert.ok(summary.buriedSkirts > 0, fork.id + ' omits internal apron skirts');
    expectedTriangles += (samples.length - 1) * 20 + 4 - summary.buriedSkirts * 2;
    for (const [row, frame] of samples.entries()) for (const [side, col] of [[-1, 0], [1, 4]]) {
      const exposed = frame.flags.includes('rail') || frame.flags.includes(side < 0 ? 'railL' : 'railR');
      assert.equal(data.road.roadEdge[first + row * 14 + col * 2], exposed ? 1 : 0);
      assert.equal(data.road.roadEdge[first + row * 14 + (col === 0 ? 1 : 3) * 2], exposed ? 1 : 0);
    }
    first += samples.length * 14 + 8;
  }
  assert.equal(data.road.indices.length / 3, expectedTriangles);
  const frameAt = s => ({ p: [0, 0, s], t: [0, 0, 1], b: [-1, 0, 0], n: [0, 1, 0], w: 12, flags: ['open'], surface: 'grip', flow: [0, 0], s, localS: s, seg: 0 });
  const open = buildForkMeshData([{ id: 'real-open', lengthM: 100, baseSeg: 0, points: [{ flags: ['open'] }], frameAt, samples: () => [frameAt(0), frameAt(50), frameAt(100)] }]);
  assert.equal(open.summaries[0].buriedSkirts, 0); assert.ok(open.road.roadEdge.every(value => value === 1));
});
