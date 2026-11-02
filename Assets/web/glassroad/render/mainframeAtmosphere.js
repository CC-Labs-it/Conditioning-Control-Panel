/* Fixed world-space code constellations. Four shared draws, one tiny owned glyph atlas. */
import { Group, InstancedMesh, TorusGeometry, CircleGeometry, PlaneGeometry, SphereGeometry,
  MeshBasicNodeMaterial, DataTexture, RGBAFormat, NearestFilter, LinearMipmapLinearFilter, DoubleSide, Object3D } from 'three/webgpu';
import { uniform, vec2, vec3, uv, texture, fract, sin, mix, smoothstep, normalWorld, normalize, dot, clamp } from 'three/tsl';

export function mainframeAtmosphereLayout(track, { phone = false } = {}) {
  const hexes = [], cascades = [];
  if (track.id !== 'mainframe') return { hexes, cascades };
  const samples = [];
  for (let s = 0; s < track.lengthM; s += 10) samples.push(track.frameAt(s));
  const clear = (p, radius) => samples.every(f => {
    const dx = p[0] - f.p[0], dy = p[1] - f.p[1], dz = p[2] - f.p[2];
    const project = a => dx * a[0] + dy * a[1] + dz * a[2];
    return Math.abs(project(f.t)) > radius + 12 || project(f.n) < -radius - 6 ||
      project(f.n) > radius + 14 || Math.abs(project(f.b)) > f.w / 2 + radius + 8;
  });
  const count = phone ? 30 : 64;
  for (let i = 0; i < count; i++) {
    const s = (i + .3) / count * track.lengthM, f = track.frameAt(s), side = i % 2 ? 1 : -1;
    const radius = 4 + (i * 7 % 13) * .25, width = 7 + i % 4, height = 24 + i % 5 * 3;
    const yaw = Math.atan2(-f.t[0], -f.t[2]), phase = i * 2.399;
    for (let attempt = 0; attempt < 8; attempt++) {
      const off = f.w / 2 + 24 + i % 3 * 9 + attempt * 18;
      const p = f.p.map((v, k) => v + f.b[k] * side * off + (k === 1 ? 20 + i % 4 * 9 : 0));
      if (!clear(p, radius + 2)) continue;
      hexes.push({ s, p, radius, yaw, phase });
      break;
    }
    for (let attempt = 0; attempt < 8; attempt++) {
      const off = f.w / 2 + 32 + i % 4 * 8 + attempt * 18;
      const p = f.p.map((v, k) => v - f.b[k] * side * off + (k === 1 ? 30 + i % 3 * 11 : 0));
      if (!clear(p, Math.hypot(width, height) / 2 + 2)) continue;
      cascades.push({ s, p, width, height, yaw, phase });
      break;
    }
  }
  return { hexes, cascades };
}

// Five by seven source glyphs keep the code legible without fonts, canvas or downloaded art.
const GLYPHS = [
  ['01110','10001','10011','10101','11001','10001','01110'],
  ['00100','01100','00100','00100','00100','00100','01110'],
  ['01110','10001','00001','00010','00100','01000','11111'],
  ['11110','00001','00001','01110','00001','00001','11110'],
  ['10001','10001','10001','11111','00001','00001','00001'],
  ['11111','10000','10000','11110','00001','00001','11110'],
  ['01110','10000','10000','11110','10001','10001','01110'],
  ['11111','00001','00010','00100','01000','01000','01000'],
  ['01110','10001','10001','01110','10001','10001','01110'],
  ['01110','10001','10001','01111','00001','00001','01110'],
  ['01110','10001','10001','11111','10001','10001','10001'],
  ['11110','10001','10001','11110','10001','10001','11110'],
  ['01111','10000','10000','10000','10000','10000','01111'],
  ['11110','10001','10001','10001','10001','10001','11110'],
  ['11111','10000','10000','11110','10000','10000','11111'],
  ['11111','10000','10000','11110','10000','10000','10000'],
];

export function mainframeCodeAtlas() {
  const width = 128, height = 256, data = new Uint8Array(width * height * 4);
  let seed = 15317;
  for (let row = 0; row < 25; row++) for (let column = 0; column < 16; column++) {
    seed = seed * 16807 % 2147483647;
    if (seed % 7 === 0) continue;
    const glyph = GLYPHS[seed % GLYPHS.length], alpha = 130 + seed % 126;
    for (let y = 0; y < 7; y++) for (let x = 0; x < 5; x++) {
      if (glyph[y][x] !== '1') continue;
      const at = ((row * 10 + y + 3) * width + column * 8 + x + 1) * 4;
      data[at] = 66; data[at + 1] = 255; data[at + 2] = 139; data[at + 3] = alpha;
    }
  }
  return { data, width, height };
}

export function createMainframeAtmosphere(track, options = {}) {
  const layout = mainframeAtmosphereLayout(track, options), object = new Group();
  object.name = 'mainframe-code-atmosphere';
  const atlas = mainframeCodeAtlas(), map = new DataTexture(atlas.data, atlas.width, atlas.height, RGBAFormat);
  map.name = 'mainframe-original-hex-code'; map.magFilter = NearestFilter; map.minFilter = LinearMipmapLinearFilter;
  map.generateMipmaps = true; map.needsUpdate = true;
  const clock = uniform(0), calm = uniform(0), at = uv();
  const green = vec3(.08, 1, .38), light = vec3(.7, 1, .82);
  const frame = new MeshBasicNodeMaterial({ fog: false });
  const sheen = clamp(dot(normalize(normalWorld), normalize(vec3(-.4, .8, .6))).mul(.25).add(.7), .4, 1);
  frame.colorNode = green.mul(sheen);
  const codeMaterial = (rain) => {
    const material = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide, fog: false });
    const y = rain ? fract(at.y.add(clock.mul(.065))) : at.y;
    const glyph = texture(map, vec2(at.x, y));
    const head = smoothstep(.8, 1, fract(at.y.add(clock.mul(.11)).add(sin(at.x.mul(31)).mul(.3))));
    material.colorNode = mix(green, light, head.mul(calm.oneMinus()));
    const edge = smoothstep(0, .06, at.x).mul(smoothstep(0, .06, at.x.oneMinus()))
      .mul(smoothstep(0, .08, at.y)).mul(smoothstep(0, .08, at.y.oneMinus()));
    material.opacityNode = glyph.a.mul(edge).mul(rain ? .72 : .62);
    return material;
  };
  const faceMaterial = codeMaterial(false), rainMaterial = codeMaterial(true);
  const metal = new MeshBasicNodeMaterial({ color: '#a9ffe0', fog: false });
  const geometries = [new TorusGeometry(1, .045, 5, 6), new CircleGeometry(.85, 6),
    new PlaneGeometry(1, 1), new SphereGeometry(.075, 6, 4)];
  const meshes = [new InstancedMesh(geometries[0], frame, layout.hexes.length),
    new InstancedMesh(geometries[1], faceMaterial, layout.hexes.length),
    new InstancedMesh(geometries[2], rainMaterial, layout.cascades.length),
    new InstancedMesh(geometries[3], metal, layout.hexes.length * 6)];
  ['green-code-hex-bevels', 'hex-code-insets', 'matrix-code-cascades', 'hex-contact-nodes'].forEach((name, i) => {
    meshes[i].name = name; object.add(meshes[i]);
  });
  const pose = new Object3D(), local = new Object3D(); let time = 0, disposed = false;
  function place(quiet = false) {
    for (let i = 0; i < layout.hexes.length; i++) {
      const row = layout.hexes[i];
      pose.position.fromArray(row.p); pose.position.y += quiet ? 0 : Math.sin(time * .23 + row.phase) * .7;
      pose.rotation.set(.09, row.yaw + (quiet ? 0 : Math.sin(time * .13 + row.phase) * .13), .12);
      pose.scale.setScalar(row.radius); pose.updateMatrix();
      meshes[0].setMatrixAt(i, pose.matrix); meshes[1].setMatrixAt(i, pose.matrix);
      for (let j = 0; j < 6; j++) {
        const angle = j * Math.PI / 3;
        local.position.set(Math.cos(angle), Math.sin(angle), 0).applyMatrix4(pose.matrix);
        local.scale.setScalar(row.radius); local.updateMatrix(); meshes[3].setMatrixAt(i * 6 + j, local.matrix);
      }
    }
    meshes[0].instanceMatrix.needsUpdate = true;
    meshes[1].instanceMatrix.needsUpdate = true;
    meshes[3].instanceMatrix.needsUpdate = true;
  }
  for (let i = 0; i < layout.cascades.length; i++) {
    const row = layout.cascades[i];
    pose.position.fromArray(row.p); pose.rotation.set(0, row.yaw, 0);
    pose.scale.set(row.width, row.height, 1); pose.updateMatrix(); meshes[2].setMatrixAt(i, pose.matrix);
  }
  meshes[2].instanceMatrix.needsUpdate = true;
  place();
  for (const mesh of meshes) { mesh.computeBoundingSphere(); if (mesh.boundingSphere) mesh.boundingSphere.radius += 2; }
  return { object, layout,
    update(dt, view = {}) {
      if (disposed || view.paused) return;
      const quiet = !!(view.gentle || view.reducedMotion);
      if (!quiet) time += Number.isFinite(dt) ? Math.max(0, Math.min(.1, dt)) : 0;
      clock.value = quiet ? 0 : time; calm.value = quiet ? 1 : 0; place(quiet);
    },
    reset() { if (disposed) return; time = 0; clock.value = 0; calm.value = 0; place(); },
    dispose() {
      if (disposed) return; disposed = true; object.removeFromParent();
      for (const mesh of meshes) mesh.dispose();
      for (const geometry of geometries) geometry.dispose();
      for (const material of [frame, faceMaterial, rainMaterial, metal]) material.dispose();
      map.dispose();
    },
  };
}
