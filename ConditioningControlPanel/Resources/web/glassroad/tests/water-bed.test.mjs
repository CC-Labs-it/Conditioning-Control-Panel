import { test } from 'node:test';
import assert from 'node:assert/strict';
import { waterBedPositions } from '../render/waterBed.js';

test('submerged render bed exposes troughs without changing physical support or dry road', () => {
  const positions = new Float32Array([1, 2, 3, 4, 5, 6]);
  const uvs = new Float32Array([7, 100, 0, 200]);
  const field = { atlasUV: (s, x) => ({ wet: s === 100 && x === 0 }) };
  const track = { frameAt: () => ({p:[0,0,0],b:[1,0,0],n:[0,1,0]}), surfaceAt: () => ({n:[.6,.8,0]}) };
  const bed = waterBedPositions(positions, uvs, field, track);
  assert.deepEqual([...positions], [1, 2, 3, 4, 5, 6]);
  assert.deepEqual([...bed.slice(3)], [4, 5, 6]);
  assert.ok(Math.abs(bed[0] - 0.37) < 1e-6);
  assert.ok(Math.abs(bed[1] - 1.16) < 1e-6);
  assert.equal(bed[2], 3);
});

test('whole slab sinks, including underside and edge faces with opposite shading normals', () => {
  const positions = new Float32Array([0,0,0, 0,-.25,0, 8,0,0, 8,-.25,0]);
  const uvs = new Float32Array([0,10, 0,10, 0,10, .25,10]);
  const field = {atlasUV:()=>({wet:true})};
  const track = {frameAt:()=>({p:[0,0,0],b:[1,0,0],n:[0,1,0]})};
  const bed = waterBedPositions(positions,uvs,field,track);
  for(let i=0;i<4;i++) {
    assert.ok(Math.abs(bed[i*3+1]-positions[i*3+1]+1.05)<1e-6);
    assert.equal(bed[i*3],positions[i*3]);
  }
});
