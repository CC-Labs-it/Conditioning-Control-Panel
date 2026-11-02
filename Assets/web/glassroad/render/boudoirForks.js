/* Real fork decks share the road shader and clocks. Only signs and construction are owned here. */
import { Group, Mesh, BufferGeometry, BufferAttribute, InstancedMesh, BoxGeometry, MeshBasicNodeMaterial,
  CanvasTexture, SRGBColorSpace, LinearFilter, LinearMipmapLinearFilter, DoubleSide, Matrix4, Quaternion, Vector3 } from 'three/webgpu';
import { surfaceId } from '../track/schema.js';
import { FACES } from './fonts.js';
import { Q } from './quality.js';

export const FORK_RENDER = Object.freeze({ thicknessM: .45, railHeightM: 1.2, railThicknessM: .09,
  portalDepthM: .025, portalFadeM: 24, sampleM: 3, phoneSampleM: 5 });
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const subtract = (a, b) => a.map((v, k) => v - b[k]);
const neg = a => a.map(v => -v);
const point = (f, x = 0, y = 0, z = 0) => f.p.map((v, k) => v + f.b[k] * x + f.n[k] * y + f.t[k] * z);
const smooth = (a, b, v) => { const t = Math.max(0, Math.min(1, (v - a) / (b - a))); return t * t * (3 - 2 * t); };
export function forkPortalDepth(localS, lengthM) {
  return -FORK_RENDER.portalDepthM * (1 - smooth(0, Math.min(FORK_RENDER.portalFadeM, lengthM / 3), Math.min(localS, lengthM - localS)));
}
function dataBuilder(water = false) {
  const positions = [], normals = [], uvs = [], indices = [], surface = [], flow = [], forkWidth = [], roadEdge = [];
  return {
    vertex(p, n, u, v, frame = null, edge = 1) {
      positions.push(...p); normals.push(...n); uvs.push(u, v);
      if (water) { surface.push(surfaceId(frame.surface || 'glass')); flow.push(...(frame.flow || [0, 0])); forkWidth.push(frame.w / 2); roadEdge.push(edge); }
      return positions.length / 3 - 1;
    },
    quad(a, b, c, d) {
      const p = i => positions.slice(i * 3, i * 3 + 3), A = p(a), e = subtract(p(b), A), f = subtract(p(d), A);
      const cross = [e[1] * f[2] - e[2] * f[1], e[2] * f[0] - e[0] * f[2], e[0] * f[1] - e[1] * f[0]];
      if (dot(cross, normals.slice(a * 3, a * 3 + 3)) >= 0) indices.push(a, b, d, a, d, c);
      else indices.push(a, d, b, a, c, d);
    },
    triangle(a, b, c) {
      const p = i => positions.slice(i * 3, i * 3 + 3), A = p(a), e = subtract(p(b), A), f = subtract(p(c), A);
      const cross = [e[1] * f[2] - e[2] * f[1], e[2] * f[0] - e[0] * f[2], e[0] * f[1] - e[1] * f[0]];
      if (dot(cross, normals.slice(a * 3, a * 3 + 3)) >= 0) indices.push(a, b, c);
      else indices.push(a, c, b);
    },
    done() { return { positions: new Float32Array(positions), normals: new Float32Array(normals), uvs: new Float32Array(uvs), indices: new Uint32Array(indices),
      ...(water ? { surface: new Float32Array(surface), flow: new Float32Array(flow), forkWidth: new Float32Array(forkWidth), roadEdge: new Float32Array(roadEdge) } : {}) }; },
  };
}
const hasRail = (f, side) => (f.flags || []).includes('rail') || (f.flags || []).includes(side < 0 ? 'railL' : 'railR');
// Authored rail loss only means an internal apron edge when the original road was fully railed.
const internalEdge = (fork, f, side) => !hasRail(f, side) && (fork.points?.[Math.max(0, f.seg - fork.baseSeg)]?.flags || []).includes('rail');

/** MeshData mirrors the physical engine samples. UV.y is main-lap progress, not branch distance. */
export function buildForkMeshData(forks, { stepM = FORK_RENDER.sampleM } = {}) {
  const road = dataBuilder(true), rails = dataBuilder(), summaries = [], minimap = [];
  for (const fork of forks || []) {
    const samples = fork.samples(stepM); if (samples.length < 2) continue;
    if (fork.movingBridge) {
      for (const s of [fork.movingBridge.fromM, fork.movingBridge.toM])
        if (!samples.some(f => Math.abs(f.localS - s) < .001)) samples.push(fork.frameAt(s));
      samples.sort((a, b) => a.localS - b.localS);
    }
    const top = [], bottom = [], sides = [[], []];
    for (const frame of samples) {
      const h = frame.w / 2, y = forkPortalDepth(frame.localS, fork.lengthM), topRow = [], bottomRow = [];
      for (let col = 0; col <= 4; col++) {
        const x = -h + frame.w * col / 4;
        const edge = col === 2 ? 1 : internalEdge(fork, frame, col < 2 ? -1 : 1) ? 0 : 1;
        topRow.push(road.vertex(point(frame, x, y), frame.n, x, frame.s, frame, edge));
        bottomRow.push(road.vertex(point(frame, x, y - FORK_RENDER.thicknessM), neg(frame.n), x, frame.s, frame, edge));
      }
      top.push(topRow); bottom.push(bottomRow);
      for (const [j, side] of [-1, 1].entries()) sides[j].push([
        road.vertex(point(frame, side * h, y), frame.b.map(v => v * side), 0, frame.s, frame),
        road.vertex(point(frame, side * h, y - FORK_RENDER.thicknessM), frame.b.map(v => v * side), .45, frame.s, frame),
      ]);
    }
    let railSpans = 0, buriedSkirts = 0;
    for (let i = 0; i < samples.length - 1; i++) {
      const bridge = fork.movingBridge;
      if (bridge && samples[i].localS < bridge.toM - .01 && samples[i + 1].localS > bridge.fromM + .01) continue;
      for (let col = 0; col < 4; col++) {
        road.quad(top[i][col], top[i][col + 1], top[i + 1][col], top[i + 1][col + 1]);
        road.quad(bottom[i][col], bottom[i][col + 1], bottom[i + 1][col], bottom[i + 1][col + 1]);
      }
      const mid = fork.frameAt((samples[i].localS + samples[i + 1].localS) / 2);
      for (const [j, side] of [-1, 1].entries()) {
        if (internalEdge(fork, mid, side)) { buriedSkirts++; continue; }
        const rows = sides[j]; road.quad(rows[i][0], rows[i][1], rows[i + 1][0], rows[i + 1][1]);
      }
      for (const side of [-1, 1]) {
        if (!hasRail(mid, side)) continue;
        railSpans++;
        for (const outer of [false, true]) {
          const ids = [];
          for (const f of [samples[i], samples[i + 1]]) {
            const x = side * (f.w / 2 + (outer ? FORK_RENDER.railThicknessM : 0));
            const n = f.b.map(v => v * side * (outer ? 1 : -1));
            ids.push([rails.vertex(point(f, x), n, 0, f.s), rails.vertex(point(f, x, FORK_RENDER.railHeightM), n, FORK_RENDER.railHeightM, f.s)]);
          }
          rails.quad(ids[0][0], ids[0][1], ids[1][0], ids[1][1]);
        }
      }
    }
    for (const [index, sign] of [[0, -1], [samples.length - 1, 1]]) {
      const f = samples[index], y = forkPortalDepth(f.localS, fork.lengthM), n = f.t.map(v => v * sign), h = f.w / 2;
      const a = road.vertex(point(f, -h, y), n, -h, f.s, f), b = road.vertex(point(f, h, y), n, h, f.s, f);
      const c = road.vertex(point(f, -h, y - FORK_RENDER.thicknessM), n, -h, f.s, f), d = road.vertex(point(f, h, y - FORK_RENDER.thicknessM), n, h, f.s, f);
      road.quad(a, b, c, d);
    }
    summaries.push({ id: fork.id, samples: samples.length, railSpans, buriedSkirts, fromS: fork.fromS, toS: fork.toS, lengthM: fork.lengthM });
    minimap.push({ id: fork.id, label: fork.label, fromS: fork.fromS, toS: fork.toS, points: samples.map(f => [f.p[0], f.p[2]]) });
  }
  return { road: road.done(), rails: rails.done(), summaries, minimap };
}

/** Choice arrows follow the branch's actual departure, including left and right variants. */
export function forkSignPlan(track) {
  return (track.forks || []).map(fork => {
    const portal = (track.forkPortals || []).filter(p => p.id === fork.id && Math.abs(p.s0 - fork.fromS) < fork.lengthM / 2).sort((a, b) => Math.abs(a.s0 - fork.fromS) - Math.abs(b.s0 - fork.fromS))[0];
    let direction = 1;
    for (const localS of [Math.min(35, fork.lengthM * .2), Math.min(70, fork.lengthM * .4)]) {
      const f = fork.frameAt(localS), main = track.frameAt(fork.mainS(localS));
      const offset = dot(subtract(f.p, main.p), main.b);
      if (Math.abs(offset) > .5) { direction = Math.sign(offset); break; }
    }
    if (portal) direction = portal.side;
    const mainLabel = fork.id.includes('gallery') ? 'Gallery weave' : fork.id.includes('bath') ? 'Water chute' : fork.id.includes('wardrobe') ? 'Wardrobe spiral' : 'Main road';
    return { id: fork.id, s: (portal?.s0 ?? fork.fromS) - 28, direction,
      choices: [{ label: mainLabel, arrow: 'straight', side: -direction }, { label: fork.label || 'Alternate road', arrow: direction < 0 ? 'left' : 'right', side: direction }] };
  });
}

function makeGeometry(data) {
  const g = new BufferGeometry();
  for (const [name, size, array] of [['position', 3, data.positions], ['normal', 3, data.normals], ['uv', 2, data.uvs],
    ['surface', 1, data.surface], ['flow', 2, data.flow], ['forkWidth', 1, data.forkWidth], ['roadEdge', 1, data.roadEdge]]) if (array) g.setAttribute(name, new BufferAttribute(array, size));
  g.setIndex(new BufferAttribute(data.indices, 1)); g.computeBoundingSphere(); return g;
}
function signAtlas(plan, phone) {
  const scale = phone ? .5 : 1, width = 1024 * scale, rowH = 128 * scale;
  const height = 2 ** Math.ceil(Math.log2(Math.max(128 * scale, plan.length * rowH)));
  const canvas = globalThis.document?.createElement('canvas') || (typeof OffscreenCanvas !== 'undefined' ? new OffscreenCanvas(width, height) : null);
  if (!canvas) return { texture: null, width, height, rowH };
  canvas.width = width; canvas.height = height;
  const ctx = canvas.getContext('2d'); ctx.fillStyle = '#241827'; ctx.fillRect(0, 0, width, height);
  for (const [row, fork] of plan.entries()) for (const [col, choice] of fork.choices.entries()) {
    const x = col * width / 2, y = row * rowH, w = width / 2;
    ctx.fillStyle = col ? '#194e51' : '#402d44'; ctx.fillRect(x + 3 * scale, y + 3 * scale, w - 6 * scale, rowH - 6 * scale);
    ctx.strokeStyle = '#edd39b'; ctx.lineWidth = 4 * scale; ctx.strokeRect(x + 5 * scale, y + 5 * scale, w - 10 * scale, rowH - 10 * scale);
    ctx.fillStyle = '#fff4d7'; ctx.font = FACES.display.css(43 * scale); ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.fillText(choice.label, x + w * .55, y + rowH * .5, w * .76);
    const cx = x + 42 * scale, cy = y + rowH * .5, sign = choice.arrow === 'left' ? -1 : 1;
    ctx.strokeStyle = '#fff4d7'; ctx.lineWidth = 7 * scale; ctx.lineCap = 'round'; ctx.lineJoin = 'round'; ctx.beginPath();
    if (choice.arrow === 'straight') { ctx.moveTo(cx, cy + 24 * scale); ctx.lineTo(cx, cy - 22 * scale); ctx.moveTo(cx - 13 * scale, cy - 8 * scale); ctx.lineTo(cx, cy - 22 * scale); ctx.lineTo(cx + 13 * scale, cy - 8 * scale); }
    else { ctx.moveTo(cx - sign * 22 * scale, cy + 19 * scale); ctx.lineTo(cx - sign * 22 * scale, cy); ctx.lineTo(cx + sign * 20 * scale, cy); ctx.moveTo(cx + sign * 5 * scale, cy - 13 * scale); ctx.lineTo(cx + sign * 20 * scale, cy); ctx.lineTo(cx + sign * 5 * scale, cy + 13 * scale); }
    ctx.stroke();
  }
  const texture = new CanvasTexture(canvas); texture.colorSpace = SRGBColorSpace;
  texture.minFilter = LinearMipmapLinearFilter; texture.magFilter = LinearFilter;
  return { texture, width, height, rowH };
}

export function buildBoudoirForks(track, { roadMaterial, railMaterial, phone = Q.name === 'phone' } = {}) {
  const group = new Group(); group.name = 'boudoir-forks';
  const data = buildForkMeshData(track.forks, { stepM: phone ? FORK_RENDER.phoneSampleM : FORK_RENDER.sampleM });
  const geometries = [], materials = [], meshes = [], instances = [], plan = forkSignPlan(track);
  const diagnostics = { forks: data.summaries, signs: plan, drawCalls: 0, vertices: 0, triangles: 0, textureBytes: 0, portalDepthM: FORK_RENDER.portalDepthM };
  if (data.road.indices.length && (!roadMaterial || !railMaterial)) throw Error('Fork decks require the shared road and rail materials');
  function add(data, material, name) {
    if (!data.indices.length) return;
    const geometry = makeGeometry(data), mesh = new Mesh(geometry, material); mesh.name = name;
    geometries.push(geometry); meshes.push(mesh); group.add(mesh);
    diagnostics.vertices += data.positions.length / 3; diagnostics.triangles += data.indices.length / 3;
    return mesh;
  }
  add(data.road, roadMaterial, 'fork-road'); add(data.rails, railMaterial, 'fork-rails');
  const atlas = signAtlas(plan, phone), face = new MeshBasicNodeMaterial({ color: '#ffffff', map: atlas.texture, side: DoubleSide });
  const frameMaterial = new MeshBasicNodeMaterial({ color: '#3c2939' }), arrowMaterial = new MeshBasicNodeMaterial({ color: '#e6c58a', side: DoubleSide });
  materials.push(face, frameMaterial, arrowMaterial);
  if (atlas.texture) diagnostics.textureBytes = atlas.width * atlas.height * 4 * 4 / 3;
  const faces = dataBuilder(), arrows = dataBuilder(), boxes = [], box = new BoxGeometry(1, 1, 1); geometries.push(box);
  for (const [row, sign] of plan.entries()) {
    const f = track.frameAt(sign.s);
    for (const [col, choice] of sign.choices.entries()) {
      const x = choice.side * 5.1, y = 7.5, w = 9.8, h = 2.5, n = neg(f.t), z = -.07;
      const u0 = col / 2, u1 = (col + 1) / 2, v0 = 1 - (row + 1) * atlas.rowH / atlas.height, v1 = 1 - row * atlas.rowH / atlas.height;
      const a = faces.vertex(point(f, x - w / 2, y - h / 2, z), n, u0, v0), b = faces.vertex(point(f, x + w / 2, y - h / 2, z), n, u1, v0);
      const c = faces.vertex(point(f, x - w / 2, y + h / 2, z), n, u0, v1), d = faces.vertex(point(f, x + w / 2, y + h / 2, z), n, u1, v1);
      faces.quad(a, b, c, d); boxes.push({ f, x, y, z: .13, scale: [w + .25, h + .25, .35] });
    }
    for (const side of [-1, 1]) boxes.push({ f, x: side * (f.w / 2 + 1.4), y: 4.1, z: .2, scale: [.4, 8.2, .4] });
    boxes.push({ f, x: 0, y: 8.55, z: .2, scale: [f.w + 3.3, .28, .35] });
    for (const choice of sign.choices) {
      const arrowFrame = track.frameAt(sign.s + 12), cx = choice.side * Math.min(4, arrowFrame.w / 5), turn = choice.arrow === 'straight' ? 0 : choice.side * .45;
      const shape = [[-.45, -2.7], [.45, -2.7], [.45, .6], [1.6, .6], [0, 2.8], [-1.6, .6], [-.45, .6]];
      const ids = shape.map(([x, z]) => arrows.vertex(point(arrowFrame, cx + x * Math.cos(turn) + z * Math.sin(turn), .035, z * Math.cos(turn) - x * Math.sin(turn)), arrowFrame.n, 0, 0));
      // Separate stem and head keep all triangles nondegenerate.
      arrows.quad(ids[0], ids[1], ids[6], ids[2]);
      arrows.triangle(ids[5], ids[3], ids[4]);
    }
  }
  add(faces.done(), face, 'fork-choice-signs'); add(arrows.done(), arrowMaterial, 'fork-choice-arrows');
  if (boxes.length) {
    const batch = new InstancedMesh(box, frameMaterial, boxes.length), matrix = new Matrix4(), basis = new Matrix4(), rotation = new Quaternion(), scale = new Vector3();
    for (const [i, row] of boxes.entries()) {
      basis.makeBasis(new Vector3(...row.f.b), new Vector3(...row.f.n), new Vector3(...row.f.t).negate()); rotation.setFromRotationMatrix(basis);
      batch.setMatrixAt(i, matrix.compose(new Vector3(...point(row.f, row.x, row.y, row.z)), rotation, scale.fromArray(row.scale)));
    }
    batch.name = 'fork-sign-frames'; batch.computeBoundingSphere(); group.add(batch); instances.push(batch);
  }
  diagnostics.drawCalls = meshes.length + instances.length;
  let disposed = false;
  return { group, minimap: data.minimap, diagnostics, update() {},
    dispose() { if (disposed) return; disposed = true; group.removeFromParent();
      instances.forEach(mesh => mesh.dispose()); geometries.forEach(geometry => geometry.dispose()); materials.forEach(material => material.dispose()); atlas.texture?.dispose(); } };
}
