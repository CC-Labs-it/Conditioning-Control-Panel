import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as THREE from '../vendor/three/build/three.core.js';
import { MeshoptDecoder } from '../vendor/three/addons/libs/meshopt_decoder.module.js';
import { springStep, motionState, stepMechanism, partitionLifeTriangles } from '../render/vehicleLifeMath.js';
import {meterBar,meterScale,METER_HEIGHTS,createFeedbackMeter,advanceMeterPhase} from '../render/feedbackMeter.js';

const tslSource=readFileSync(new URL('../vendor/three/build/three.tsl.min.js',import.meta.url),'utf8')
  .replaceAll('"three/webgpu"',JSON.stringify(new URL('../vendor/three/build/three.webgpu.min.js',import.meta.url).href));
const tslModule='data:text/javascript;base64,'+Buffer.from(tslSource+'\n//# sourceURL=tsl.test-runtime.js').toString('base64');
const accentSource=readFileSync(new URL('../render/vehicleAccents.js',import.meta.url),'utf8')
  .replace("'three/webgpu'",JSON.stringify(new URL('../vendor/three/build/three.webgpu.min.js',import.meta.url).href))
  .replace("'three/tsl'",JSON.stringify(tslModule));
const accentModule='data:text/javascript;base64,'+Buffer.from(accentSource+'\n//# sourceURL=vehicleAccents.test-runtime.js').toString('base64');

const runtimeSource = readFileSync(new URL('../render/vehicleLife.js', import.meta.url), 'utf8')
  .replace("'three/webgpu'", JSON.stringify(new URL('../vendor/three/build/three.core.js', import.meta.url).href))
  .replace("'./vehicleLifeMath.js'", JSON.stringify(new URL('../render/vehicleLifeMath.js', import.meta.url).href))
  .replace("'./feedbackMeter.js'",JSON.stringify(new URL('../render/feedbackMeter.js',import.meta.url).href))
  .replace("'./vehicleAccents.js'",JSON.stringify(accentModule));
const { createVehicleLife } = await import('data:text/javascript;base64,' + Buffer.from(runtimeSource + '\n//# sourceURL=vehicleLife.test-runtime.js').toString('base64'));
const {rearShellSurface}=await import(accentModule);

function rigidFixture() {
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.Float32BufferAttribute([2,0,0, 3,0,0, 2,1,0, 2,0,1, 3,0,1, 2,1,1], 3));
  geometry.setAttribute('_life', new THREE.Uint8BufferAttribute([1,1,1,0,0,0], 1));
  geometry.setAttribute('skinIndex', new THREE.Uint16BufferAttribute(new Array(24).fill(0), 4));
  geometry.setAttribute('skinWeight', new THREE.Float32BufferAttribute(Array.from({length:24}, (_,i) => i % 4 === 0 ? 1 : 0), 4));
  geometry.setIndex([0,1,2,3,4,5]); geometry.computeVertexNormals();
  const bone = new THREE.Bone(); bone.position.x = 2; bone.updateMatrixWorld(true);
  const material = new THREE.MeshBasicMaterial(), skeleton = new THREE.Skeleton([bone]);
  const mesh = new THREE.SkinnedMesh(geometry, material); mesh.bind(skeleton, new THREE.Matrix4());
  return { geometry, bone, material, mesh, definition: {groups:[{id:1,name:'test',mode:'spin',axis:[0,1,0],pivot:[2,0,0],rate:2}]} };
}

test('rigid extraction uses inverse binds and disposal restores shared geometry safely', () => {
  const f = rigidFixture(), other = new THREE.SkinnedMesh(f.geometry, f.material);
  other.bind(f.mesh.skeleton, new THREE.Matrix4());
  let sourceDisposals = 0; f.geometry.addEventListener('dispose', () => sourceDisposals++);
  const a = createVehicleLife([f.mesh], f.definition), b = createVehicleLife([other], f.definition);
  assert.equal(a.metrics.draws, 1); assert.equal(a.metrics.movingTriangles, 1);
  assert.equal(f.mesh.geometry, other.geometry);
  assert.notEqual(f.mesh.geometry.getAttribute('position'), f.geometry.getAttribute('position'));
  assert.notEqual(f.mesh.geometry.getAttribute('position').array, f.geometry.getAttribute('position').array);
  assert.deepEqual([...f.mesh.geometry.index.array], [3,4,5]);
  const mechanism = f.bone.children[0], extracted = mechanism.children[0];
  assert.deepEqual([...extracted.geometry.getAttribute('position').array], [0,0,0,1,0,0,0,1,0]);
  assert.equal(extracted.material, f.material);
  a.update(1/60, {speed:1}); assert.notEqual(mechanism.quaternion.y, 0);
  a.update(1/60, {motionOff:true}); assert.equal(mechanism.quaternion.y, 0);
  a.dispose(); a.dispose();
  assert.equal(f.mesh.geometry, f.geometry); assert.notEqual(other.geometry, f.geometry);
  b.update(1/60, {speed:1}); b.dispose();
  assert.equal(other.geometry, f.geometry); assert.equal(sourceDisposals, 0); assert.equal(f.bone.children.length, 0);
});

test('mixed rigid weights fail closed and preserve the original mesh', () => {
  const f = rigidFixture(); f.geometry.getAttribute('skinWeight').setX(0, .5);
  const life = createVehicleLife([f.mesh], f.definition);
  assert.equal(life.metrics.rejected, true); assert.equal(f.mesh.geometry, f.geometry); assert.equal(f.bone.children.length, 0);
});

test('analytic spring converges to rest and agrees at 30, 60 and 144 fps', () => {
  const samples = [30, 60, 144].map(fps => {
    const s = { value: -.7, velocity: 2 };
    for (let i = 0; i < fps * 2; i++) springStep(s, .4, 12, 1 / fps);
    assert.ok(Math.abs(s.value - .4) < 1e-8);
    return s.value;
  });
  assert.ok(Math.max(...samples) - Math.min(...samples) < 1e-12);
});

test('all mechanism modes remain finite and bounded under impacts and bad inputs', () => {
  for (const mode of ['spin', 'swing', 'pulse', 'press']) {
    const state = motionState(), definition = { mode, amplitude: .3, rate: 2, travel: .06 };
    for (let i = 0; i < 4000; i++) {
      const value = stepMechanism(state, definition, i % 11 ? 1 / 60 : Infinity,
        { speed: 10, steer: -10, boost: NaN, land: i % 7 ? 0 : 1, hit: 1, time: i / 60 });
      assert.ok(Number.isFinite(value));
      assert.ok(Math.abs(value) <= (mode === 'spin' ? Math.PI * 2 : mode === 'press' ? .06 : .3) + 1e-10);
    }
  }
});

test('motion off resets pose and velocity, resumed swing is continuous', () => {
  const s = motionState(), d = { mode: 'swing', amplitude: .5, rate: 1 };
  for (let i = 0; i < 60; i++) stepMechanism(s, d, 1 / 60, { speed: 1, boost: 1 });
  assert.equal(stepMechanism(s, d, 1 / 60, { motionOff: true }), 0);
  assert.equal(s.velocity, 0);
  assert.ok(Math.abs(stepMechanism(s, d, 1 / 60, { speed: 1 })) < .02);
});

test('distance culling freezes the current mechanism pose without a rest-pose pop', () => {
  const state = motionState(), definition = {mode:'spin',rate:2};
  stepMechanism(state, definition, .1, {speed:1});
  const before = state.value;
  for (let i = 0; i < 100; i++) assert.equal(stepMechanism(state, definition, .1, {near:false}), before);
  assert.ok(stepMechanism(state, definition, 1/60, {speed:1}) > before);
});

test('gentle motion reduces amplitude and landing settles without an instantaneous jump', () => {
  const d = { mode: 'swing', amplitude: .4, rate: 0 };
  const normal = motionState(), gentle = motionState();
  for (let i = 0; i < 120; i++) {
    stepMechanism(normal, d, 1 / 60, { steer: 1 });
    stepMechanism(gentle, d, 1 / 60, { steer: 1, gentle: true });
  }
  assert.ok(gentle.value < normal.value * .5);
  const before = normal.value;
  stepMechanism(normal, d, 1 / 144, { land: 1 });
  assert.ok(Math.abs(normal.value - before) < .01);
  for (let i = 0; i < 180; i++) stepMechanism(normal, d, 1 / 60, {});
  assert.ok(Math.abs(normal.value) < 1e-8);
});

test('spin is frame rate independent, menu moves slowly and boost accelerates', () => {
  const d = { mode: 'spin', rate: 2 };
  const run = (fps, signals) => {
    const s = motionState();
    for (let i = 0; i < fps; i++) stepMechanism(s, d, 1 / fps, signals);
    return s.value;
  };
  assert.ok(Math.abs(run(30, { speed: 1 }) - run(144, { speed: 1 })) < 1e-12);
  assert.ok(run(60, { menu: true, selected: true }) > run(60, { menu: true }));
  assert.ok(run(60, { speed: 1, boost: 1 }) > run(60, { speed: 1 }));
});

test('triangle cuts conserve triangles, reject mixed groups and leave static geometry in place', () => {
  const ids = [0,0,0,1,1,1,2,2,2], index = [0,1,2,3,4,5,6,7,8];
  const p = partitionLifeTriangles(index, ids, new Set([1,2]));
  assert.deepEqual(p.keep, [0,1,2]);
  assert.equal(p.keep.length + [...p.groups.values()].reduce((n,a) => n+a.length, 0), index.length);
  assert.equal(partitionLifeTriangles([0,1,3], ids, new Set([1])), null);
  assert.equal(partitionLifeTriangles(index, ids, new Set([1])), null);
  assert.equal(partitionLifeTriangles([0,1,99], ids, new Set([1,2])), null);
});

test('Feedback meter tags nine source-space bars, rejects shell, and independently moves seated heights',()=>{
  const values=[];
  for(let i=0;i<9;i++){
    assert.equal(meterBar((i-4)*.088,.264+METER_HEIGHTS[i],-.683),i);
    values.push(meterScale(i,1.25));
    assert.ok(values[i]>.2&&values[i]<1.05);
  }
  assert.equal(new Set(values.map(v=>v.toFixed(4))).size,9);
  assert.equal(meterBar(0,.35,-.658),-1);assert.equal(meterBar(.58,.34,-.683),-1);
  const g=new THREE.BufferGeometry();g.setAttribute('position',new THREE.Float32BufferAttribute([0,.264,-.683,0,.319,-.683,.58,.34,-.683],3));
  const mesh=new THREE.Mesh(g),meter=createFeedbackMeter([mesh]);
  meter.update(.016,{time:1.25});assert.equal(mesh.geometry.getAttribute('position').getY(0),Math.fround(.264));
  assert.equal(mesh.geometry.getAttribute('position').getY(2),Math.fround(.34));
  assert.notEqual(mesh.geometry.getAttribute('position').getY(1),Math.fround(.319));
  meter.update(.016,{motionOff:true});assert.equal(mesh.geometry.getAttribute('position').getY(1),Math.fround(.319));
  meter.dispose();assert.equal(mesh.geometry,g);
});

test('Feedback boost accelerates a continuous bounded phase, with frame-independent timing and exact freeze/reset',()=>{
  const phase=3.2,dt=1/144;
  const idle=advanceMeterPhase(phase,dt,{}),boosted=advanceMeterPhase(phase,dt,{boost:1});
  assert.ok(boosted>idle&&boosted-phase<.014,'boost changes derivative without jumping phase');
  assert.equal(advanceMeterPhase(phase,dt,{near:false,boost:1}),phase);
  assert.equal(advanceMeterPhase(phase,dt,{motionOff:true}),0);
  assert.ok(advanceMeterPhase(phase,dt,{gentle:true})<idle);
  const run=fps=>{let p=0;for(let i=0;i<fps*2;i++)p=advanceMeterPhase(p,1/fps,{boost:i>=fps?1:0});return p;};
  assert.ok(Math.abs(run(30)-run(144))<1e-12);
  assert.ok(advanceMeterPhase(Math.PI*200-.001,.1,{boost:1})<1,'bounded wrap');
});

test('all shipped mechanisms preserve rest world vertices and pivots through two actual R4 clips', async t => {
  const core = new URL('../vendor/three/build/three.core.js', import.meta.url).href;
  const asModule = source => 'data:text/javascript;base64,' + Buffer.from(source + '\n//# sourceURL=vehicleLife.test-addon.js').toString('base64');
  const addon = path => readFileSync(new URL('../vendor/three/addons/' + path, import.meta.url), 'utf8')
    .replace("from 'three';", 'from ' + JSON.stringify(core) + ';');
  const loaderSource = addon('loaders/GLTFLoader.js')
    .replace("'../utils/BufferGeometryUtils.js'", JSON.stringify(asModule(addon('utils/BufferGeometryUtils.js'))))
    .replace("'../utils/SkeletonUtils.js'", JSON.stringify(asModule(addon('utils/SkeletonUtils.js'))));
  const { GLTFLoader } = await import(asModule(loaderSource));
  await MeshoptDecoder.ready;
  const loader = new GLTFLoader().setMeshoptDecoder(MeshoptDecoder);
  // The base rider includes its face atlas. This geometry/rig test deliberately omits image decoding.
  loader.register(parser => ({name:'geometryOnlyTest', beforeRoot() {
    const stripTextures = value => {
      if (!value || typeof value !== 'object') return;
      for (const key of Object.keys(value)) {
        if (key.endsWith('Texture')) delete value[key];
        else stripTextures(value[key]);
      }
    };
    for (const material of parser.json.materials || []) stripTextures(material);
  }}));
  const load = async name => {
    const bytes = readFileSync(new URL('../art/models/' + name + '.glb', import.meta.url));
    return loader.parseAsync(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength), '');
  };
  const base = await load('emi-teacup-kart');
  const clips = ['hop_squash', 'lean_left'].map(name => {
    const clip = base.animations.find(a => a.name === name);
    assert.ok(clip, 'accepted R4 clip ' + name); return clip;
  });
  const catalog = JSON.parse(readFileSync(new URL('../art/models/vehicle-mechanisms.json', import.meta.url)));
  let compared = 0;
  for (const [id, definition] of Object.entries(catalog.vehicles)) {
    const gltf = await load('emi-vehicle-' + id), scene = gltf.scene, meshes = [];
    scene.updateMatrixWorld(true); scene.traverse(o => { if (o.isSkinnedMesh) meshes.push(o); });
    const originals = meshes.map(mesh => ({mesh, geometry:mesh.geometry}));
    const life = createVehicleLife(meshes, definition);
    assert.equal(life.metrics.rejected, false, id); assert.ok(life.metrics.draws > 0 && life.metrics.draws <= 10, id);
    t.diagnostic(id + ': ' + life.metrics.groups + ' groups, ' + life.metrics.draws + ' moving material draws, ' + life.metrics.movingTriangles + ' moving triangles');
    const children = [];
    scene.traverse(o => { if (o.name.startsWith('mechanism-')) children.push(o); });
    const mixer = new THREE.AnimationMixer(scene), expected = new THREE.Vector3(), actual = new THREE.Vector3();
    for (const clip of clips) {
      mixer.stopAllAction(); mixer.clipAction(clip).reset().play(); mixer.setTime(Math.min(clip.duration * .46, .25));
      life.update(0, {motionOff:true}); scene.updateMatrixWorld(true);
      for (const {mesh, geometry} of originals) {
        const ids = geometry.getAttribute('_life') || geometry.getAttribute('_LIFE');
        if (!ids) continue;
        for (const group of definition.groups) {
          const indices = [];
          for (const index of geometry.index.array) if (ids.getX(index) === group.id) indices.push(index);
          if (!indices.length) continue;
          const matches = children.filter(child => child.name === 'mechanism-' + group.id && child.material === mesh.material);
          assert.equal(matches.length, 1, id + ' material ownership');
          const child = matches[0], position = child.geometry.getAttribute('position');
          assert.equal(position.count, indices.length);
          for (let i = 0; i < indices.length; i++) {
            mesh.getVertexPosition(indices[i], expected).applyMatrix4(mesh.matrixWorld);
            actual.fromBufferAttribute(position, i).applyMatrix4(child.matrixWorld);
            assert.ok(expected.distanceTo(actual) < 2e-6, id + '/' + clip.name + ' rigid world vertex'); compared++;
          }
          const boneIndex = mesh.skeleton.bones.findIndex(bone => bone.name === group.bone);
          expected.fromArray(group.pivot).applyMatrix4(mesh.bindMatrix).applyMatrix4(mesh.skeleton.boneInverses[boneIndex])
            .applyMatrix4(mesh.skeleton.bones[boneIndex].matrixWorld);
          actual.setFromMatrixPosition(child.parent.matrixWorld);
          assert.ok(expected.distanceTo(actual) < 2e-6, id + '/' + clip.name + ' pivot attachment');
          // Check a nonzero local mechanism deformation under the same animated bone pose.
          const signals = {time:1.375,speed:1,boost:1,steer:.4};
          life.update(0, {motionOff:true}); life.update(.05, signals); scene.updateMatrixWorld(true);
          const state = motionState(), phase = (group.id * .137 % 1) * Math.PI * 2;
          const value = stepMechanism(state, group, .05, signals, phase);
          const inverse = mesh.skeleton.boneInverses[boneIndex].clone().multiply(mesh.bindMatrix);
          const pivot = new THREE.Vector3().fromArray(group.pivot).applyMatrix4(inverse);
          const axis = new THREE.Vector3().fromArray(group.axis).transformDirection(inverse);
          for (let i = 0; i < indices.length; i++) {
            expected.fromBufferAttribute(geometry.getAttribute('position'), indices[i]).applyMatrix4(inverse).sub(pivot);
            if (group.mode === 'spin' || group.mode === 'swing') expected.applyAxisAngle(axis, value);
            else if (group.mode === 'pulse') expected.addScaledVector(axis, expected.dot(axis) * value);
            else if (group.mode === 'press') expected.addScaledVector(axis, value);
            expected.add(pivot).applyMatrix4(mesh.skeleton.bones[boneIndex].matrixWorld);
            actual.fromBufferAttribute(position, i).applyMatrix4(child.matrixWorld);
            assert.ok(expected.distanceTo(actual) < 2e-6, id + '/' + clip.name + ' deformed mechanism axis'); compared++;
          }
          life.update(0, {motionOff:true}); scene.updateMatrixWorld(true);
        }
      }
    }
    life.dispose(); mixer.stopAllAction(); mixer.uncacheRoot(scene);
    for (const {mesh, geometry} of originals) assert.equal(mesh.geometry, geometry);
    const mask=new THREE.Texture();
    const lit=createVehicleLife(meshes,{...definition,vehicleId:id},{art:{tex:()=>mask}});
    assert.equal(lit.metrics.accentDraws,4,id+' has physical Loom/light fittings');
    assert.ok(lit.metrics.accentTriangles<1000,id+' accent triangle budget');
    const mount=lit.metrics.accentMount;
    assert.ok(Number.isFinite(mount.shellZ),id+' exact static-shell support');
    for(let i=0;i<32;i++) {
      const a=i*Math.PI/16,r=mount.radius-.008;
      assert.ok(Number.isFinite(rearShellSurface(meshes,mount.x+Math.cos(a)*r,mount.y+Math.sin(a)*r)),id+' full rotor excursion remains over shell '+(mount.x+Math.cos(a)*r).toFixed(3)+','+(mount.y+Math.sin(a)*r).toFixed(3));
    }
    if(id==='pendulum'||id==='velvet-lock') {
      const group=definition.groups[0],pivot=new THREE.Vector3(...group.pivot),axis=new THREE.Vector3(...group.axis);
      let maxX=-Infinity;
      for(const {geometry}of originals) {
        const positions=geometry.getAttribute('position'),ids=geometry.getAttribute('_life')||geometry.getAttribute('_LIFE');
        for(let i=0;i<positions.count;i++)if(ids.getX(i)===group.id)for(let step=0;step<=32;step++) {
          expected.fromBufferAttribute(positions,i).sub(pivot).applyAxisAngle(axis,(step/16-1)*group.amplitude).add(pivot);
          maxX=Math.max(maxX,expected.x);
        }
      }
      assert.ok(mount.x-mount.radius>maxX+.005,id+' accent clears complete bob/heart sweep');
    }
    if(id==='feedback')assert.equal(lit.metrics.meterBars,9,'all9actualexportedbars tagged');
    lit.update(.016,{time:2,speed:.7,boost:1});lit.update(.016,{motionOff:true});lit.dispose();
    for(const {mesh,geometry}of originals)assert.equal(mesh.geometry,geometry,id+' accent/meter restoration');
  }
  assert.ok(compared > 1000, 'actual exported moving vertices sampled');
  t.diagnostic(compared + ' world vertex comparisons across rest and active mechanisms, hop_squash and lean_left; tolerance 0.002 mm');
});
