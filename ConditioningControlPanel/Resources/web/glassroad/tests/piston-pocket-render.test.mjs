import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { loadTrack } from '../track/load.js';
import { movingPoseAt } from '../track/movingHazards.js';
import { pistonPocketContains } from '../track/pistonPocket.js';
const ids = ['threshold', 'mainframe', 'keep', 'boudoir', 'goonworks'];
const tracks = ids.map(id => loadTrack(JSON.parse(readFileSync(new URL('../tracks/' + id + '.json', import.meta.url))), { moving: true }));
const inTriangle = (p, a, b, c) => {
  const cross = (u, v, w) => (v[0]-u[0])*(w[1]-u[1])-(v[1]-u[1])*(w[0]-u[0]);
  const signs = [cross(a,b,p),cross(b,c,p),cross(c,a,p)];
  return !signs.some(x => x > 1e-4) || !signs.some(x => x < -1e-4);
};
for (const track of tracks) test(track.id + ': road cuts match centered physical pockets on both faces', () => {
  const mesh = track.meshes.road;
  for (const row of track.moving.traps) {
    const pocket = row.pocket, faces = [];
    for (let i = 0; i < mesh.indices.length; i += 3) {
      const face = Array.from(mesh.indices.subarray(i,i+3), j => [mesh.uvs[j*2],mesh.uvs[j*2+1]]);
      const shift = Math.round((pocket.s-face.reduce((n,p)=>n+p[1],0)/3)/track.lengthM)*track.lengthM;
      for (const point of face) point[1] += shift;
      if (Math.max(...face.map(p=>p[1])) < pocket.s-pocket.len/2-.1 || Math.min(...face.map(p=>p[1])) > pocket.s+pocket.len/2+.1) continue;
      const area = Math.abs((face[1][0]-face[0][0])*(face[2][1]-face[0][1])-(face[1][1]-face[0][1])*(face[2][0]-face[0][0]));
      if (area > 1e-5) faces.push(face);
      const vertices = Array.from(mesh.indices.subarray(i,i+3), j => {
        const d = row.p.map((v,k)=>mesh.positions[j*3+k]-v);
        return [row.b,row.n,row.t].map(axis=>axis.reduce((n,v,k)=>n+v*d[k],0));
      });
      if (vertices.every(v=>v[0]*row.side>row.roadWidth/2-.6) && vertices.every(v=>v[0]*row.side<row.roadWidth/2+.6)) {
        const sideArea = Math.abs((vertices[1][1]-vertices[0][1])*(vertices[2][2]-vertices[0][2])-(vertices[2][1]-vertices[0][1])*(vertices[1][2]-vertices[0][2]));
        if (sideArea>1e-5) assert.ok(!inTriangle([-.4,0],...vertices.map(v=>v.slice(1))), `${row.id}: recessed head entry has no outer road skirt`);
      }
    }
    for (const along of [-.4,0,.4]) for (const lateral of [-.4,0,.4]) {
      const s = pocket.s + along*pocket.len, x = pocket.x + lateral*pocket.w;
      assert.ok(pistonPocketContains(pocket,s,x,track.lengthM));
      assert.ok(!faces.some(([a,b,c])=>inTriangle([x,s],a,b,c)), `${row.id}: no permanent top or underside spans the physical cut`);
    }
    const rails = track.meshes.rails;
    for (let i = 0; i < rails.indices.length; i += 3) {
      const vertices = Array.from(rails.indices.subarray(i,i+3), j => {
        const d = row.p.map((v,k)=>rails.positions[j*3+k]-v);
        return [row.b,row.n,row.t].map(axis=>axis.reduce((n,v,k)=>n+v*d[k],0));
      });
      if (vertices.every(v=>v[0]*row.side>row.roadWidth/2-1) && vertices.every(v=>v[0]*row.side<row.roadWidth/2+1))
        assert.ok(!inTriangle([.6,0],...vertices.map(v=>v.slice(1))), `${row.id}: the rail leaves a real mouth for the piston`);
    }
    for (const sign of [-1,1]) {
      const s = pocket.s + sign*(pocket.len/2+.02), x = pocket.x;
      assert.ok(!pistonPocketContains(pocket,s,x,track.lengthM));
      assert.ok(faces.some(([a,b,c])=>inTriangle([x,s],a,b,c)), `${row.id}: road closes exactly outside the pocket`);
    }
  }
});

test('one shared cover draw follows physical slide and recess, holds still, and releases only owned geometry', async () => {
  const hooks = registerHooks({ resolve(id, ctx, next) {
    const path = id === 'three/webgpu' ? 'three.webgpu.min.js' : id === 'three/tsl' ? 'three.tsl.min.js' : null;
    return path ? { url:new URL('../vendor/three/build/'+path,import.meta.url).href, shortCircuit:true } : next(id,ctx);
  } });
  try {
    const { Scene, MeshBasicNodeMaterial } = await import('three/webgpu');
    const { createMovingHazardView } = await import('../render/movingHazards.js');
    for (const track of tracks) {
      const material = new MeshBasicNodeMaterial(), scene = new Scene(); let materialDisposed = false;
      material.addEventListener('dispose',()=>materialDisposed=true);
      const view = createMovingHazardView(scene,track,{},null,material), cover = view.group.getObjectByName('piston-sliding-road-covers');
      assert.equal(cover.material,material); assert.equal(view.inspect().pockets,6); assert.ok(view.inspect().draws<=7);
      const position = cover.geometry.getAttribute('position'), rest = position.array.slice();
      const uv = cover.geometry.getAttribute('uv'), row = track.moving.traps[0], pocket = row.pocket;
      assert.ok(Math.abs(uv.getY(0)-(pocket.s-pocket.len/2))<.001);
      let moved = false;
      for (let t = 0; t < 12; t += .05) {
        view.update(t); const pose = movingPoseAt(track,row,t);
        for (let k=0;k<3;k++) assert.ok(Math.abs(position.array[k]-rest[k]-row.b[k]*pose.panelShift-row.n[k]*(pose.panelY||0))<.002);
        moved ||= Math.abs(pose.panelShift)>.1;
        assert.equal(view.group.getObjectByName(row.id).getObjectByName('recessed-piston-head').position.y,pocket.baseM);
      }
      assert.ok(moved); const version=position.version; view.update(11.95,{paused:true}); assert.equal(position.version,version);
      view.update(12); const held=position.version; view.update(12); assert.equal(position.version,held);
      view.dispose(); assert.equal(scene.children.length,0); assert.equal(materialDisposed,false); material.dispose();
    }
  } finally { hooks.deregister(); }
});
