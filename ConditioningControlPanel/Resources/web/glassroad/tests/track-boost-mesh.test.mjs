import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';

const read = id => JSON.parse(readFileSync(new URL(`../tracks/${id}.json`, import.meta.url)));
const dot = (a, b) => a.reduce((s, v, i) => s + v * b[i], 0);
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const sub = (a, b) => a.map((v, i) => v - b[i]);
const blend = (rows, weights) => rows[0].map((_, k) => rows.reduce((s, r, i) => s + r[k] * weights[i], 0));
const weightsAt = (p, a, b, c) => {
  const det = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1]);
  if (Math.abs(det) < 1e-9) return null;
  const u = ((b[1] - c[1]) * (p[0] - c[0]) + (c[0] - b[0]) * (p[1] - c[1])) / det;
  const v = ((c[1] - a[1]) * (p[0] - c[0]) + (a[0] - c[0]) * (p[1] - c[1])) / det;
  return [u, v, 1 - u - v];
};
const vertex = (mesh, i) => ({ p: Array.from(mesh.positions.subarray(i * 3, i * 3 + 3)),
  n: Array.from(mesh.normals.subarray(i * 3, i * 3 + 3)), uv: Array.from(mesh.uvs.subarray(i * 2, i * 2 + 2)) });

function checkCurvedPad(json, row, options = {}) {
  const track = loadTrack({ ...json, boosts: [row] }, options), pad = track.boosts[0], { road, pads } = track.meshes;
  const roadFaces = [];
  for (let i = 0; i < road.indices.length; i += 3) {
    const v = Array.from(road.indices.subarray(i, i + 3), index => vertex(road, index));
    const meanS = v.reduce((sum, row) => sum + row.uv[1], 0) / 3;
    const shift = Math.round((pad.s + pad.len / 2 - meanS) / track.lengthM) * track.lengthM;
    for (const row of v) row.uv[1] += shift;
    if (Math.max(...v.map(v => v.uv[1])) < pad.s || Math.min(...v.map(v => v.uv[1])) > pad.s + pad.len ||
      Math.max(...v.map(v => v.uv[0])) < pad.x - pad.w / 2 || Math.min(...v.map(v => v.uv[0])) > pad.x + pad.w / 2) continue;
    if (dot(v[0].n, track.frameAt(v[0].uv[1]).n) < .5) continue;
    roadFaces.push(v);
  }
  let area = 0, maxPhysicalGap = 0, samples = 0;
  for (let i = 0; i < pads.indices.length; i += 3) {
    const v = Array.from(pads.indices.subarray(i, i + 3), index => vertex(pads, index));
    const normal = cross(sub(v[1].p, v[0].p), sub(v[2].p, v[0].p));
    // Float32 road UVs can leave valid submillimetre corners at an exact pad boundary.
    assert.ok(Math.hypot(...normal) > 1e-10 && dot(normal, v[0].n) > 0, 'nondegenerate upward triangle');
    area += Math.abs((v[1].uv[0] - v[0].uv[0]) * (v[2].uv[1] - v[0].uv[1]) -
      (v[2].uv[0] - v[0].uv[0]) * (v[1].uv[1] - v[0].uv[1])) / 2;
    for (const weights of [[1 / 3, 1 / 3, 1 / 3], [.6, .2, .2], [.2, .6, .2], [.2, .2, .6]]) {
      const p = blend(v.map(v => v.p), weights), uv = blend(v.map(v => v.uv), weights);
      assert.ok(Math.abs(uv[0]) <= pad.w / 2 + 1e-5 && uv[1] >= -1e-5 && uv[1] <= pad.len + 1e-5);
      const globalUV = [pad.x + uv[0], pad.s + uv[1]], support = track.surfaceAt(globalUV[1], globalUV[0]);
      const physicalGap = dot(sub(p, support.p), support.n);
      assert.ok(physicalGap >= .018 && physicalGap < .065, `decal follows physical support: ${physicalGap}`);
      maxPhysicalGap = Math.max(maxPhysicalGap, physicalGap);
      const face = roadFaces.map(v => ({ v, w: weightsAt(globalUV, ...v.map(v => v.uv)) }))
        .find(({ w }) => w && w.every(k => k >= -1e-5));
      assert.ok(face, 'pad has a rendered road triangle beneath it');
      const roadP = blend(face.v.map(v => v.p), face.w), roadN = blend(face.v.map(v => v.n), face.w);
      const gap = dot(sub(p, roadP), roadN) / Math.hypot(...roadN);
      assert.ok(gap > .018 && gap < .022, `decal clears the visible road: ${gap}`);
      samples++;
    }
  }
  assert.ok(samples > 20);
  assert.ok(Math.abs(area - pad.w * pad.len) < .002, 'complete pad footprint without doubled or missing triangles');
  return { triangles: pads.indices.length / 3, maxPhysicalGap, area };
}

test('Boudoir wet boost triangle interiors follow physical and rendered canal support', () => {
  const json = read('boudoir'), track = loadTrack(json);
  const wet = json.boosts.filter(p => track.canalProfile(track.nodeS[p.node], p.x).blend > 0);
  assert.equal(wet.length, 12);
  for (const row of wet) checkCurvedPad(json, row);
});

test('Mainframe curved and blending canal pads retain complete UV footprints above the visible road', () => {
  const json = read('mainframe'), track = loadTrack(json);
  const wet = json.boosts.filter(p => track.canalProfile(track.nodeS[p.node], p.x).blend > 0);
  assert.ok(wet.length > 0);
  for (const row of wet) checkCurvedPad(json, row);
  const section = json.sections.find(s => s.id === 'coolant-canal');
  checkCurvedPad(json, { node: section.from + 1, x: 8, w: 5, len: 8 });
});

test('A banked curved pad crossing the lap seam retains its complete footprint and winding', () => {
  const json = read('boudoir');
  json.sections = json.sections.map((s, i) => ({ ...s, id: i === 0 ? 'coolant-canal' : s.id === 'coolant-canal' ? 'bath-stream' : s.id }));
  json.nodes = json.nodes.map(n => ({ ...n, roll: 12 }));
  checkCurvedPad(json, { node: json.nodes.length - 1, x: 4, w: 5, len: 20 });
});

test('Dry pads keep their original two-edge strips, metre UVs and vertex count', () => {
  const json = read('boudoir'), row = json.boosts[0], track = loadTrack({ ...json, boosts: [row] });
  const pad = track.boosts[0], mesh = track.meshes.pads, rows = Math.ceil(pad.len);
  assert.equal(mesh.positions.length / 3, (rows + 1) * 2);
  assert.equal(mesh.indices.length, rows * 6);
  for (let k = 0; k <= rows; k++) for (let side = 0; side < 2; side++) {
    const v = vertex(mesh, k * 2 + side), u = (side ? 1 : -1) * pad.w / 2, s = pad.len * k / rows;
    assert.deepEqual(v.uv, [u, s]);
    const support = track.surfaceAt(pad.s + s, pad.x + u);
    assert.ok(Math.abs(dot(sub(v.p, support.p), support.n) - .02) < .0001);
  }
});

test('Wrapped Float32 road UVs retain the preceding face when a pad edge rounds forward', () => {
  const json = read('mainframe'), track = loadTrack({ ...json, boosts: [] }, { aerials: false });
  const uv = track.meshes.road.uvs;
  // Changing the lap length moves Float32 rounding boundaries, so locate a real wet edge.
  const node = json.nodes.findIndex((_, node) => {
    const s = track.nodeS[node];
    if (track.canalProfile(s, 0).blend <= 0) return false;
    for (let i = 1; i < uv.length; i += 2) {
      const raw = uv[i], shift = Math.round((s - raw) / track.lengthM) * track.lengthM;
      if (raw > track.lengthM && raw + shift > s && raw + shift - s < .001) return true;
    }
    return false;
  });
  assert.ok(node >= 0, 'fixture must exercise the wrapped Float32 edge crossing');
  const row = { node, x: 0, w: 7, len: 6 };
  const result = checkCurvedPad(json, row, { aerials: false });
  assert.ok(Math.abs(result.area - row.w * row.len) < 1e-5, 'the submillimetre edge strip has complete coverage');
});
