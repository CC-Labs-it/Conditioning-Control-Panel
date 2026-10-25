// Shared shallow water bodies and gravity-fed spill curtains. All shaders exist before warm-up.
import { Group, Mesh, BufferGeometry, Float32BufferAttribute, InterleavedBuffer, InterleavedBufferAttribute, DataTexture, RGBAFormat, UnsignedByteType, LinearFilter, ClampToEdgeWrapping, NoColorSpace, MeshBasicNodeMaterial, DoubleSide } from 'three/webgpu';
import { uniform, uv, vec2, vec3, float, sin, mix, smoothstep, texture, cubeTexture, positionWorld, cameraPosition, normalize, pow, attribute, positionLocal, dot } from 'three/tsl';
import { ambientWaterNodes } from './waterMotion.js';
import { WATER_VOLUME as L, waterSpills } from './waterMath.js';
import { createWaterSurface } from './materials/water.js';
import { createWaterField } from './waterField.js';
import { createWaterFoam } from './waterFoam.js';
import { createWaterReflections } from './waterReflections.js';
import { Q } from './quality.js';
import { createAudioReactiveUniforms, reactiveWaterNodes } from './audioReactive.js';
import { buildWaterTransitions, waterJoinWeight, transitionSupport, transitionOutlets } from './waterTransitions.js';

const data = () => ({ p: [], uv: [], edge: [], flow: [], surface: [], field: [], normal: [], index: [] });
function vertex(g, p, u, v, edge = 0, flow = [0, 0], surface = 3, frame = null, atlas = null, join = [1, 1, 0]) {
  const i = g.p.length / 3; g.p.push(...p); g.uv.push(u, v); g.edge.push(edge); g.flow.push(...flow); g.surface.push(surface);
  const N = frame?.n || [0,1,0];
  g.field.push(atlas?.u || 0, atlas?.v || 0, ...(frame?.b || [1,0,0]), ...(frame?.t || [0,0,-1]), ...N, ...join);
  g.normal.push(...N); return i;
}
function quad(g, a, b, c, d) { g.index.push(a, b, c, b, d, c); }
function geometry(g) {
  const out = new BufferGeometry();
  for (const [name, values, size] of [['position', g.p, 3], ['uv', g.uv, 2], ['aWaterEdge', g.edge, 1], ['flow', g.flow, 2], ['surface', g.surface, 1]]) out.setAttribute(name, new Float32BufferAttribute(values, size));
  // All transported-frame attributes share one buffer, staying below WebGPU's eight-buffer floor.
  const packed = new InterleavedBuffer(new Float32Array(g.field), 14);
  for (const [name, size, offset] of [['aWaterUV',2,0], ['aWaterB',3,2], ['aWaterT',3,5], ['aWaterN',3,8], ['aWaterJoin',3,11]]) out.setAttribute(name, new InterleavedBufferAttribute(packed, size, offset));
  out.setAttribute('normal', new Float32BufferAttribute(g.normal, 3));
  out.setIndex(g.index); out.computeBoundingSphere(); out.boundingSphere.radius += 0.85; return out;
}
function edgePoint(track, s, side, lift = L.liftM) {
  const frame = track.frameAt(s), x = Math.max(0.1, frame.w / 2 - 0.04) * side;
  const f = track.surfaceAt ? track.surfaceAt(s, x) : { ...frame, p: frame.p.map((v,k) => v + frame.b[k] * x) };
  return { f, x, p: f.p.map((v, k) => v + f.n[k] * lift) };
}
function runUV(field, run, s, lateral) {
  const along = Math.max(0, Math.min(1, (s - run.fromS) / (run.toS - run.fromS)));
  return { u: (lateral * (run.nx - 1) + 0.5) / field.width,
    v: (run.rowOffset + along * (run.ny - 1) + 0.5) / field.height };
}

export function buildWaterVolume(track, pal, art = null, { quality = Q.name, scene = null, renderer = null, reactive = createAudioReactiveUniforms() } = {}) {
  const object = new Group(); object.name = 'water-volume';
  const field = createWaterField(track, { quality });
  const runs = field.runs.map(r => ({ fromS: r.fromS, toS: r.toS })), spills = waterSpills(track, runs), top = data(), wall = data(), fall = data();
  const transitions = buildWaterTransitions(track, field.runs);
  // Short end outlets join the same folded curtain batch, with lower-road clearance checks.
  spills.push(...transitionOutlets(track, transitions.shallowEnds, spills));
  const heightTexture = new DataTexture(field.pixels, field.width, field.height, RGBAFormat, UnsignedByteType);
  heightTexture.name = 'water-height-slope-foam'; heightTexture.colorSpace = NoColorSpace;
  heightTexture.minFilter = heightTexture.magFilter = LinearFilter; heightTexture.wrapS = heightTexture.wrapT = ClampToEdgeWrapping;
  heightTexture.generateMipmaps = false; heightTexture.flipY = false; heightTexture.needsUpdate = true;
  const heightSample = texture(heightTexture, attribute('aWaterUV', 'vec2')).level(0);
  const fieldNormal = normalize(attribute('aWaterN', 'vec3'));
  const u = { t: uniform(0), motion: uniform(1), reflectionReady: uniform(0), reactive };
  const wind=ambientWaterNodes(positionLocal,u.t);
  const join = attribute('aWaterJoin', 'vec3');
  const fieldHeight = heightSample.r.mul(2).sub(1).clamp(-0.8, 0.8).add(wind.height).mul(join.x);
  const reflections = createWaterReflections({ scene, renderer, exclude: object, quality, track });
  const froth = createWaterFoam(track, field, spills, { quality });
  const tex = id => art?.detail?.(id) || art?.tex?.(id + (Q.name === 'phone' ? '-phone' : '')) || null;
  const normal = tex('water-normal'), foam = tex('water-foam-mask'), sheet = tex('waterfall-sheet'), phase = tex('waterfall-phase-mask');
  for (const run of field.runs) {
    const resolution = quality === 'phone' ? 2 : 1.25;
    const widest = Math.max(...run.widths);
    const rows = Math.max(1, Math.ceil((run.toS - run.fromS) / resolution)), columns = Math.max(4, Math.min(quality === 'phone' ? 32 : 72, Math.ceil(widest / resolution)));
    let previous = null;
    for (let row = 0; row <= rows; row++) {
      const s = run.fromS + (run.toS - run.fromS) * row / rows, f = track.frameAt(s), half = Math.max(0.1, f.w / 2 - 0.04), ids = [];
      const weight = waterJoinWeight(run, s, track.lengthM, field.runs), lift = .015 + (L.liftM - .015) * weight;
      const flow = f.flow || (track.flowAt ? track.flowAt(f.seg, f.segT || 0, [0, 0]) : [0, 0]);
      for (let column = 0; column <= columns; column++) {
        const x = -half + 2 * half * column / columns;
        const contact = track.surfaceAt ? track.surfaceAt(s, x) : { ...f, p: f.p.map((v,k) => v + f.b[k] * x) };
        const p = contact.p.map((v,k) => v + contact.n[k] * lift);
        const atlas = runUV(field, run, s, column / columns);
        ids.push(vertex(top, p, x, s, half - Math.abs(x), flow, f.surface === 'pool' ? 4 : 3, contact, atlas, [weight, 1, 0]));
        if (previous && column) quad(top, previous[column - 1], previous[column], ids[column - 1], ids[column]);
      }
      previous = ids;
      if (!row) continue;
      const lastS = s - (run.toS - run.fromS) / rows;
      for (const side of [-1, 1]) {
        const lastWeight = waterJoinWeight(run, lastS, track.lengthM, field.runs);
        const a = edgePoint(track, lastS, side, .015 + (L.liftM - .015) * lastWeight), b = edgePoint(track, s, side, lift), base = wall.p.length / 3;
        for (const e of [a, b]) {
          const distance = e === a ? lastS : s, atlas = runUV(field, run, distance, (side + 1) / 2);
          const j = [waterJoinWeight(run, distance, track.lengthM, field.runs), 1, 0];
          vertex(wall, e.p, e.x, distance, 0, [0,0], 3, e.f, atlas, j);
          vertex(wall, [e.p[0], e.p[1] - L.depthM * j[0], e.p[2]], e.x, distance, 1, [0,0], 3, e.f, atlas, j);
        }
        quad(wall, base, base + 1, base + 2, base + 3);
      }
    }
    // Close the two ends so the visible depth reads as a body rather than a pair of curtains.
    for (const s of [run.fromS, run.toS]) {
      if (transitions.ends.some(e => e.run === run && e.s === s)) continue;
      const weight = waterJoinWeight(run, s, track.lengthM, field.runs);
      if (weight <= 0) continue;
      const lift = .015 + (L.liftM - .015) * weight;
      const a = edgePoint(track, s, -1, lift), b = edgePoint(track, s, 1, lift), base = wall.p.length / 3;
      for (const e of [a, b]) {
        const atlas = runUV(field, run, s, e === a ? 0 : 1);
        vertex(wall, e.p, e.x, s, 0, [0,0], 3, e.f, atlas, [weight, 1, 0]);
        vertex(wall, [e.p[0], e.p[1] - L.depthM * weight, e.p[2]], e.x, s, 1, [0,0], 3, e.f, atlas, [weight, 1, 0]);
      }
      quad(wall, base, base + 1, base + 2, base + 3);
    }
  }
  // A shallow supported skirt narrows into broken wet patches on dry road. It shares
  // the existing liquid shader and atlas boundary, so there is no second water clock.
  for (const end of transitions.ends) {
    const rows = Math.max(2, Math.ceil(end.dryM / .8)), columns = quality === 'phone' ? 12 : 24;
    let previous = null;
    for (let row = 0; row <= rows; row++) {
      const q = row / rows, s = end.s + end.dir * end.dryM * q, f = track.frameAt(s);
      const fade = 1 - q * q * (3 - 2 * q), half = Math.max(.1, f.w / 2 - .04) * (1 - .58 * q);
      const contacts = Array.from({ length: columns + 1 }, (_, i) => transitionSupport(track, s, -half + 2 * half * i / columns));
      if (contacts.some(p => !p)) { previous = null; continue; }
      const ids = contacts.map((contact, i) => {
        const x = -half + 2 * half * i / columns, p = contact.p.map((v, k) => v + contact.n[k] * (.008 + .007 * fade));
        const atlas = end.reservoir ? { u: .5 / field.width, v: .5 / field.height } : runUV(field, end.run, end.s, i / columns);
        return vertex(top, p, x, s, half - Math.abs(x), [0,0], 4, contact, atlas, [0, fade, q]);
      });
      if (previous) for (let i = 1; i <= columns; i++) quad(top, previous[i - 1], previous[i], ids[i - 1], ids[i]);
      previous = ids;
    }
  }
  for (const spill of spills) {
    // Authored spills are clipped to a wet run. Address its edge directly: interpolated
    // Float32 widths can classify the exact geometric edge as outside the simulation.
    const middleS = (spill.fromS + spill.toS) / 2;
    const run = field.runs.find(r => middleS >= r.fromS && middleS <= r.toS);
    const rows = Math.max(2, Math.ceil((spill.toS - spill.fromS) / 1.5)), drops = 12;
    for (let layer = 0; layer < 2; layer++) {
      let previous = null;
      for (let row = 0; row <= rows; row++) {
        const t = row / rows, e = edgePoint(track, spill.fromS + (spill.toS - spill.fromS) * t, spill.side), ids = [];
        for (let d = 0; d <= drops; d++) {
          const depth = d / drops;
          const bow = Math.sin(depth * Math.PI * 0.6) * 2.6 + layer * 0.65 * depth;
          const fold = Math.sin(t * 43 + depth * 8 + layer * 2) * 0.42 * depth;
          const bridge = Math.min(1, depth * 12) * 1.4;
          const outward = (bridge + bow + fold) * spill.side;
          ids.push(vertex(fall, [e.p[0] + e.f.b[0] * outward, e.p[1] - spill.dropM * depth,
            e.p[2] + e.f.b[2] * outward], t, depth * spill.dropM, depth, [0,0], 3, e.f,
            runUV(field, run, spill.fromS + (spill.toS - spill.fromS) * t, (spill.side + 1) / 2)));
          if (previous && d) quad(fall, previous[d - 1], previous[d], ids[d - 1], ids[d]);
        }
        previous = ids;
      }
    }
  }
  function add(g, mat, name, order) { if (!g.index.length) { mat.dispose(); return; } const mesh = new Mesh(geometry(g), mat); mesh.name = name; mesh.renderOrder = order; object.add(mesh); }
  // The displaced surface owns depth even when clear. Later road shadow catchers must
  // stay below it; otherwise their bed triangles composite over the visible waves.
  const skin = new MeshBasicNodeMaterial({ transparent: true, depthWrite: true, side: DoubleSide });
  const fields = { ...(normal ? { slope: q => texture(normal, q.mul(0.25)).rg.mul(2).sub(1) } : {}), ...(foam ? { foam: q => texture(foam, q.mul(0.25)).r } : {}) };
  const windSlope=vec2(dot(wind.gradient,attribute('aWaterB','vec3').xz),dot(wind.gradient,attribute('aWaterT','vec3').xz));
  const deformation = { slope: heightSample.gb.mul(2).sub(1).add(windSlope), foam: heightSample.a, height: fieldHeight,
    reflection: direction => cubeTexture(reflections.texture, direction).rgb, reflectionReady: u.reflectionReady,
    B: normalize(attribute('aWaterB','vec3')), T: normalize(attribute('aWaterT','vec3')) };
  const surface = createWaterSurface({ surface: attribute('surface', 'float'), flow: attribute('flow', 'vec2'), p: uv(), trueEdge: attribute('aWaterEdge', 'float'), V: normalize(cameraPosition.sub(positionWorld)), N0: fieldNormal, pal, u, fields, deformation, reactive });
  skin.colorNode = surface.colour;
  const patch = sin(uv().x.mul(2.4).add(sin(uv().y.mul(.7)).mul(1.7))).mul(.5).add(.5);
  const wetEdge = mix(float(1), smoothstep(.18, .65, patch), smoothstep(.2, .8, join.z));
  skin.opacityNode = surface.alpha.mul(join.y).mul(wetEdge);
  skin.positionNode = positionLocal.add(fieldNormal.mul(fieldHeight));
  // Water follows the road (0); later wakes and spray depth-test against the real wave surface.
  add(top, skin, 'water-surface-raised', 0.5);
  const body = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide });
  const depth = attribute('aWaterEdge', 'float'), rim = float(1).sub(smoothstep(0, 0.2, depth));
  const ribbon = sin(uv().y.mul(1.3).sub(u.t.mul(2))).mul(0.18).add(0.82);
  const response = reactiveWaterNodes(uv(), reactive);
  body.colorNode = mix(pal.col(2).mul(0.17), pal.col(1).mul(0.48), float(1).sub(depth)).add(vec3(1).mul(rim).mul(ribbon).mul(0.75))
    .add(pal.col(0).mul(response.light).mul(float(1).sub(depth)));
  body.positionNode = positionLocal.add(fieldNormal.mul(fieldHeight).mul(float(1).sub(depth)));
  body.opacityNode = float(0.73).add(rim.mul(0.2)); add(wall, body, 'water-visible-depth', 0.25);
  const curtain = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide });
  const p = uv(), moving = vec2(p.x, p.y.div(12).sub(u.t.mul(0.65))), streak = sheet ? texture(sheet, moving).r : pow(sin(p.x.mul(100).add(p.y.mul(0.3)).sub(u.t.mul(3))).mul(0.5).add(0.5), 2);
  const foamPhase = phase ? texture(phase, moving).r : float(0.5);
  const edge = smoothstep(0, 0.06, p.x).mul(float(1).sub(smoothstep(0.94, 1, p.x))), fade = float(1).sub(smoothstep(0.7, 1, attribute('aWaterEdge', 'float')));
  const cascadeResponse = reactiveWaterNodes(p, reactive);
  curtain.colorNode = pal.col(1).mul(0.18).add(vec3(0.45, 0.7, 0.85).mul(streak.mul(0.95).add(foamPhase.mul(0.35))))
    .add(pal.col(0).mul(cascadeResponse.light)).add(vec3(0.8, 0.93, 1).mul(cascadeResponse.crest));
  curtain.positionNode = positionLocal.add(vec3(sin(p.y.mul(0.8).sub(u.t.mul(2.1))).mul(0.18), 0, sin(p.y.mul(0.6).add(u.t.mul(1.7))).mul(0.18)).mul(attribute('aWaterEdge', 'float')).mul(u.motion))
    .add(fieldNormal.mul(fieldHeight).mul(float(1).sub(smoothstep(0,0.2,attribute('aWaterEdge','float')))));
  curtain.opacityNode = streak.mul(0.5).add(0.3).mul(edge).mul(fade); add(fall, curtain, 'water-spills-into-void', 0.75);
  froth.object.renderOrder = 0.85; object.add(froth.object);
  object.add(transitions.object);
  let uploadedVersion = field.stats.version;
  return { object, field, heightTexture, reflections, foam: froth, transitions, uniforms: u, runs, spills, stats: { runs: runs.length, spills: spills.length, transitions: transitions.ends.length, vertices: (top.p.length + wall.p.length + fall.p.length) / 3, surfaceVertices: top.p.length / 3 },
    async warmReflections(options) { const ready = await reflections.warm(options); u.reflectionReady.value = ready ? 1 : 0; return ready; },
    update(dt, { gentle = false, paused = false, scroll = gentle ? 0.5 : 1, playerPos = null, playerS = 0 } = {}) {
      if (paused) return;
      u.t.value += Math.max(0, dt) * scroll; u.motion.value = gentle ? 0.5 : 1;
      const changed = field.step(dt, { gentle, paused });
      if (changed || uploadedVersion !== field.stats.version) { heightTexture.needsUpdate = true; uploadedVersion = field.stats.version; }
      froth.update(dt, { gentle, paused, playerS });
      reflections.update(dt, { paused, playerPos, playerS, wet: runs.some(r => playerS >= r.fromS - 45 && playerS <= r.toS + 30) });
      u.reflectionReady.value = reflections.stats.ready ? 1 : 0;
    },
    reset() { field.reset(); heightTexture.needsUpdate = true; uploadedVersion = field.stats.version; froth.reset(); },
    dispose() { for (const child of object.children) { if (child === froth.object || child === transitions.object) continue; child.geometry.dispose(); child.material.dispose(); } transitions.dispose(); froth.dispose(); reflections.dispose(); heightTexture.dispose(); field.dispose?.(); object.clear(); },
  };
}
