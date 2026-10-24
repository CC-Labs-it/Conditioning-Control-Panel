// Shared shallow water bodies and gravity-fed spill curtains. All shaders exist before warm-up.
import { Group, Mesh, BufferGeometry, Float32BufferAttribute, MeshBasicNodeMaterial, DoubleSide } from 'three/webgpu';
import { uniform, uv, vec2, vec3, float, sin, mix, smoothstep, texture, normalWorld, positionWorld, cameraPosition, normalize, pow, attribute, positionLocal } from 'three/tsl';
import { WATER_VOLUME as L, waterRuns, waterHalfWidth, waterSpills } from './waterMath.js';
import { createWaterSurface } from './materials/water.js';
import { Q } from './quality.js';

const data = () => ({ p: [], uv: [], edge: [], flow: [], surface: [], index: [] });
function vertex(g, p, u, v, edge = 0, flow = [0, 0], surface = 3) {
  const i = g.p.length / 3; g.p.push(...p); g.uv.push(u, v); g.edge.push(edge); g.flow.push(...flow); g.surface.push(surface); return i;
}
function quad(g, a, b, c, d) { g.index.push(a, b, c, b, d, c); }
function geometry(g) {
  const out = new BufferGeometry();
  for (const [name, values, size] of [['position', g.p, 3], ['uv', g.uv, 2], ['aWaterEdge', g.edge, 1], ['flow', g.flow, 2], ['surface', g.surface, 1]]) out.setAttribute(name, new Float32BufferAttribute(values, size));
  out.setIndex(g.index); out.computeVertexNormals(); out.computeBoundingSphere(); return out;
}
function edgePoint(track, s, side, lift = L.liftM) {
  const f = track.frameAt(s), x = waterHalfWidth(track, s, f) * side;
  return { f, x, p: f.p.map((v, k) => v + f.b[k] * x + f.n[k] * lift) };
}

export function buildWaterVolume(track, pal, art = null) {
  const object = new Group(); object.name = 'water-volume';
  const runs = waterRuns(track), spills = waterSpills(track, runs), top = data(), wall = data(), fall = data();
  const u = { t: uniform(0), motion: uniform(1) };
  const tex = id => art?.detail?.(id) || art?.tex?.(id + (Q.name === 'phone' ? '-phone' : '')) || null;
  const normal = tex('water-normal'), foam = tex('water-foam-mask'), sheet = tex('waterfall-sheet'), phase = tex('waterfall-phase-mask');
  for (const run of runs) {
    const rows = Math.max(1, Math.ceil((run.toS - run.fromS) / L.stepM)), columns = 16;
    let previous = null;
    for (let row = 0; row <= rows; row++) {
      const s = run.fromS + (run.toS - run.fromS) * row / rows, f = track.frameAt(s), half = waterHalfWidth(track, s, f), ids = [];
      const flow = f.flow || (track.flowAt ? track.flowAt(f.seg, f.segT || 0, [0, 0]) : [0, 0]);
      for (let column = 0; column <= columns; column++) {
        const x = -half + 2 * half * column / columns, p = f.p.map((v, k) => v + f.b[k] * x + f.n[k] * L.liftM);
        ids.push(vertex(top, p, x, s, half - Math.abs(x), flow, f.surface === 'pool' ? 4 : 3));
        if (previous && column) quad(top, previous[column - 1], previous[column], ids[column - 1], ids[column]);
      }
      previous = ids;
      if (!row) continue;
      const lastS = s - (run.toS - run.fromS) / rows;
      for (const side of [-1, 1]) {
        const a = edgePoint(track, lastS, side), b = edgePoint(track, s, side), base = wall.p.length / 3;
        for (const e of [a, b]) { vertex(wall, e.p, e.x, e === a ? lastS : s, 0); vertex(wall, [e.p[0], e.p[1] - L.depthM, e.p[2]], e.x, e === a ? lastS : s, 1); }
        quad(wall, base, base + 1, base + 2, base + 3);
      }
    }
    // Close the two ends so the visible depth reads as a body rather than a pair of curtains.
    for (const s of [run.fromS, run.toS]) {
      const a = edgePoint(track, s, -1), b = edgePoint(track, s, 1), base = wall.p.length / 3;
      for (const e of [a, b]) { vertex(wall, e.p, e.x, s, 0); vertex(wall, [e.p[0], e.p[1] - L.depthM, e.p[2]], e.x, s, 1); }
      quad(wall, base, base + 1, base + 2, base + 3);
    }
  }
  for (const spill of spills) {
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
            e.p[2] + e.f.b[2] * outward], t, depth * spill.dropM, depth));
          if (previous && d) quad(fall, previous[d - 1], previous[d], ids[d - 1], ids[d]);
        }
        previous = ids;
      }
    }
  }
  function add(g, mat, name, order) { if (!g.index.length) { mat.dispose(); return; } const mesh = new Mesh(geometry(g), mat); mesh.name = name; mesh.renderOrder = order; object.add(mesh); }
  const skin = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide });
  const fields = { ...(normal ? { slope: q => texture(normal, q.mul(0.25)).rg.mul(2).sub(1) } : {}), ...(foam ? { foam: q => texture(foam, q.mul(0.25)).r } : {}) };
  const surface = createWaterSurface({ surface: attribute('surface', 'float'), flow: attribute('flow', 'vec2'), p: uv(), trueEdge: attribute('aWaterEdge', 'float'), V: normalize(cameraPosition.sub(positionWorld)), N0: normalWorld, pal, u, fields });
  // Broad moving crests cross the whole current; the existing foam mask breaks their edges.
  const travel = uv().y.sub(u.t.mul(attribute('flow', 'vec2').x.abs().add(1.4)));
  const foamSample = foam ? texture(foam, vec2(uv().x.mul(0.24), travel.mul(0.18))).r : float(0.65);
  const crestPhase = travel.mul(0.65).add(sin(uv().x.mul(0.43)).mul(1.8));
  const crest = smoothstep(0.65, 0.96, sin(crestPhase)).mul(foamSample.mul(0.75).add(0.25));
  const bank = float(1).sub(smoothstep(0.3, 4.5, attribute('aWaterEdge', 'float')));
  skin.colorNode = surface.colour.mul(0.42).add(vec3(0.025, 0.055, 0.09)).add(vec3(0.85, 0.96, 1).mul(crest).mul(bank.mul(0.5).add(0.12)).mul(u.motion));
  skin.opacityNode = float(0.97);
  const wave = sin(uv().y.mul(0.9).sub(u.t.mul(1.3))).mul(sin(uv().x.mul(1.1).add(u.t.mul(0.5)))).mul(L.waveM).mul(u.motion);
  const roller = sin(crestPhase).mul(0.5).add(0.5).pow(3).mul(bank).mul(0.85).mul(u.motion);
  skin.positionNode = positionLocal.add(vec3(0, wave.add(roller), 0));
  // Transparent water follows the road (0), then wakes (1) and spray (2) composite above it.
  add(top, skin, 'water-surface-raised', 0.5);
  const body = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide });
  const depth = attribute('aWaterEdge', 'float'), rim = float(1).sub(smoothstep(0, 0.2, depth));
  const ribbon = sin(uv().y.mul(1.3).sub(u.t.mul(2))).mul(0.18).add(0.82);
  body.colorNode = mix(pal.col(2).mul(0.17), pal.col(1).mul(0.48), float(1).sub(depth)).add(vec3(1).mul(rim).mul(ribbon).mul(0.75));
  body.opacityNode = float(0.73).add(rim.mul(0.2)); add(wall, body, 'water-visible-depth', 0.25);
  const curtain = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide });
  const p = uv(), moving = vec2(p.x, p.y.div(12).sub(u.t.mul(0.65))), streak = sheet ? texture(sheet, moving).r : pow(sin(p.x.mul(100).add(p.y.mul(0.3)).sub(u.t.mul(3))).mul(0.5).add(0.5), 2);
  const foamPhase = phase ? texture(phase, moving).r : float(0.5);
  const edge = smoothstep(0, 0.06, p.x).mul(float(1).sub(smoothstep(0.94, 1, p.x))), fade = float(1).sub(smoothstep(0.7, 1, attribute('aWaterEdge', 'float')));
  curtain.colorNode = pal.col(1).mul(0.18).add(vec3(0.45, 0.7, 0.85).mul(streak.mul(0.95).add(foamPhase.mul(0.35))));
  curtain.positionNode = positionLocal.add(vec3(sin(p.y.mul(0.8).sub(u.t.mul(2.1))).mul(0.18), 0, sin(p.y.mul(0.6).add(u.t.mul(1.7))).mul(0.18)).mul(attribute('aWaterEdge', 'float')).mul(u.motion));
  curtain.opacityNode = streak.mul(0.5).add(0.3).mul(edge).mul(fade); add(fall, curtain, 'water-spills-into-void', 0.75);
  return { object, uniforms: u, runs, spills, stats: { runs: runs.length, spills: spills.length, vertices: (top.p.length + wall.p.length + fall.p.length) / 3 },
    update(dt, { gentle = false, paused = false, scroll = gentle ? 0.5 : 1 } = {}) { if (paused) return; u.t.value += Math.max(0, dt) * scroll; u.motion.value = gentle ? 0.5 : 1; },
    dispose() { for (const child of object.children) { child.geometry.dispose(); child.material.dispose(); } object.clear(); },
  };
}
