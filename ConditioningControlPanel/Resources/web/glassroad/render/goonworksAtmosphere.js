/* A full-sky silk spiral and a small constellation of each course's keepsakes. */
import { Group, Mesh, InstancedMesh, SphereGeometry, TorusGeometry, BoxGeometry,
  BufferGeometry, Float32BufferAttribute, MeshBasicNodeMaterial, BackSide, Object3D, Color } from 'three/webgpu';
import { uniform, vec3, color, positionGeometry, normalize, atan, acos, log, sin, mix,
  smoothstep, clamp, normalWorld, normalView, positionView, dot, materialColor, fwidth, float } from 'three/tsl';

const TAU = Math.PI * 2;
export const GOON_SKY = Object.freeze({ radius: 2000, arms: 4, turnRate: .22, morphRate: .13 });

/** Background keepsakes sit above every driving deck, including their full size and bob. */
export function goonworksAtmosphereLayout(track, { phone = false } = {}) {
  const rows = [];
  if (track.id !== 'goonworks') return { rows, roof: 0 };
  let roof = -Infinity;
  for (let s = 0; s < track.lengthM; s += 8) {
    const f = track.frameAt(s);
    roof = Math.max(roof, f.p[1] + Math.abs(f.b[1]) * f.w / 2);
  }
  const count = phone ? 35 : 75;
  for (let i = 0; i < count; i++) {
    const s = (i + .4) / count * track.lengthM, f = track.frameAt(s), side = i % 2 ? 1 : -1;
    const off = side * (f.w / 2 + 42 + i % 7 * 9), scale = 5 + i % 4;
    rows.push({ s, kind: ['bubble', 'cloud', 'hex', 'lock', 'bow'][i % 5],
      p: [f.p[0] + f.b[0] * off, roof + 45 + i % 5 * 13, f.p[2] + f.b[2] * off],
      scale, phase: i * 2.399, yaw: Math.atan2(-f.t[0], -f.t[2]) });
  }
  return { rows, roof };
}

/** Merge colored parts once. Each complete keepsake remains a single instanced draw. */
function charm(parts) {
  const positions = [], normals = [], colours = [], tint = new Color();
  for (const [geometry, hex, p = [0, 0, 0], scale = [1, 1, 1], rz = 0] of parts) {
    geometry.scale(...scale); geometry.rotateZ(rz); geometry.translate(...p);
    const flat = geometry.toNonIndexed(), a = flat.attributes.position, n = flat.attributes.normal;
    tint.set(hex);
    for (let i = 0; i < a.count; i++) {
      positions.push(a.getX(i), a.getY(i), a.getZ(i));
      normals.push(n.getX(i), n.getY(i), n.getZ(i)); colours.push(tint.r, tint.g, tint.b);
    }
    flat.dispose(); geometry.dispose();
  }
  const out = new BufferGeometry(); out.setAttribute('position', new Float32BufferAttribute(positions, 3));
  out.setAttribute('normal', new Float32BufferAttribute(normals, 3)); out.setAttribute('color', new Float32BufferAttribute(colours, 3));
  out.computeBoundingSphere(); return out;
}

function keepsakeGeometry(kind) {
  const sphere = () => new SphereGeometry(1, 12, 8), box = () => new BoxGeometry(1, 1, 1);
  if (kind === 'cloud') return charm([
    [sphere(), '#ffb4de', [-.65, 0, 0], [.8, .42, .5]],
    [sphere(), '#fff0fc', [0, .18, 0], [.72, .55, .55]],
    [sphere(), '#ef8acb', [.65, -.05, 0], [.72, .38, .5]],
  ]);
  if (kind === 'hex') {
    const pieces = [[new TorusGeometry(.84, .055, 5, 6), '#75ffd1']];
    for (let i = 0; i < 12; i++) pieces.push([box(), i % 3 ? '#55d691' : '#e4fff1',
      [-.42 + i % 4 * .26, -.34 + Math.floor(i / 4) * .3, .02], [.13, .045 + i % 2 * .07, .045]]);
    return charm(pieces);
  }
  if (kind === 'lock') return charm([
    [box(), '#ffd578', [0, -.12, 0], [.88, .68, .28]],
    [new TorusGeometry(.3, .065, 6, 16, Math.PI), '#fff0bc', [0, .22, 0]],
    [sphere(), '#5e305e', [0, -.06, .151], [.055, .055, .018]],
    [box(), '#5e305e', [0, -.13, .151], [.045, .1, .02]],
  ]);
  const pieces = [
    [sphere(), '#ff96d2', [-.45, .05, 0], [.58, .3, .18], -.25],
    [sphere(), '#f9b8e6', [.45, .05, 0], [.58, .3, .18], .25],
    [box(), '#f385c9', [-.22, -.47, 0], [.28, .65, .1], -.28],
    [box(), '#ffd3ed', [.22, -.47, 0], [.28, .65, .1], .28],
    [sphere(), '#fff0e4', [0, .03, .1], [.17, .2, .18]],
  ];
  for (const side of [-1, 1]) for (let i = 0; i < 7; i++) {
    const a = i / 7 * TAU;
    pieces.push([new TorusGeometry(.075, .016, 4, 10), '#fff5f2',
      [side * .44 + Math.cos(a) * .48, .05 + Math.sin(a) * .25, .135], [1, 1, 1]]);
  }
  return charm(pieces);
}

export function createGoonworksAtmosphere(track, options = {}) {
  const layout = goonworksAtmosphereLayout(track, options), object = new Group(); object.name = 'goonworks-sky-keepsakes';
  const clock = uniform(0), contrast = uniform(1), d = normalize(positionGeometry);
  const radial = acos(clamp(d.y, -.9999, .9999)).add(.12), angle = atan(d.z, d.x);
  const morph = sin(clock.mul(GOON_SKY.morphRate));
  const warp = sin(angle.mul(3).add(clock.mul(.08))).mul(.2).mul(morph);
  const sweep = angle.mul(GOON_SKY.arms).add(log(radial).mul(morph.mul(1.4).add(6.5)))
    .add(warp).sub(clock.mul(GOON_SKY.turnRate));
  const arm = smoothstep(-.32, .32, sin(sweep));
  const hue = sin(angle.add(radial.mul(.6)).add(clock.mul(.045))).mul(.5).add(.5);
  const ink = mix(color('#783c96'), color('#d648a8'), hue);
  const silk = mix(color('#f8b0dc'), color('#91e7e5'), sin(clock.mul(.037).add(radial)).mul(.5).add(.5));
  const band = mix(ink, silk, arm.mul(.62).add(.12));
  const horizon = smoothstep(-.18, .4, d.y).mul(.28).add(.72);
  const skyMat = new MeshBasicNodeMaterial({ side: BackSide, depthWrite: false, depthTest: false, fog: false, toneMapped: false });
  skyMat.colorNode = mix(color('#c4abc8'), band.mul(horizon), contrast);
  const skyGeo = new SphereGeometry(GOON_SKY.radius, 32, 20), sky = new Mesh(skyGeo, skyMat);
  sky.name = 'goonworks-expanding-whole-sky-spiral'; sky.renderOrder = -1; sky.frustumCulled = false;
  sky.userData.screenPlacementIgnore = true; object.add(sky);

  const enamel = new MeshBasicNodeMaterial({ vertexColors: true });
  const light = clamp(dot(normalize(normalWorld), normalize(vec3(-.4, .85, .3))).mul(.23).add(.77), .5, 1);
  const p = positionGeometry, grainPhase = p.x.mul(41).add(p.y.mul(29));
  const grain = sin(grainPhase).mul(float(1).sub(smoothstep(.5, 1.5, fwidth(grainPhase)))).mul(.012).add(.988);
  enamel.colorNode = materialColor.rgb.mul(light).mul(grain);
  const soap = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, fog: false });
  const rim = dot(normalize(normalView), normalize(positionView.negate())).abs().oneMinus();
  soap.colorNode = mix(color('#ffa5e4'), color('#b7fff1'), rim);
  soap.opacityNode = rim.pow(2).mul(.48).add(.035);
  const batches = [], geometries = [skyGeo], pose = new Object3D();
  for (const kind of ['bubble', 'cloud', 'hex', 'lock', 'bow']) {
    const rows = layout.rows.filter(row => row.kind === kind), geometry = kind === 'bubble'
      ? new SphereGeometry(1, 16, 10) : keepsakeGeometry(kind);
    geometries.push(geometry);
    const mesh = new InstancedMesh(geometry, kind === 'bubble' ? soap : enamel, rows.length);
    mesh.name = 'goonworks-keepsake-' + kind; object.add(mesh); batches.push({ mesh, rows });
  }
  let time = 0, disposed = false;
  function place(quiet = false) {
    for (const { mesh, rows } of batches) {
      for (let i = 0; i < rows.length; i++) {
        const row = rows[i]; pose.position.fromArray(row.p);
        pose.rotation.set(0, row.yaw + (quiet ? 0 : Math.sin(time * .11 + row.phase) * .16), 0);
        if (!quiet) pose.position.y += Math.sin(time * .18 + row.phase) * 1.2;
        pose.scale.setScalar(row.scale); pose.updateMatrix(); mesh.setMatrixAt(i, pose.matrix);
      }
      mesh.instanceMatrix.needsUpdate = true;
    }
  }
  place();
  for (const { mesh } of batches) { mesh.computeBoundingSphere(); mesh.boundingSphere.radius += 3; }
  return { object, layout, uniforms: { clock, contrast }, stats: { draws: 6, keepsakes: layout.rows.length },
    update(dt, view = {}) {
      if (disposed || view.paused) return;
      const quiet = !!(view.gentle || view.reducedMotion);
      if (!quiet) time += Number.isFinite(dt) ? Math.max(0, Math.min(.1, dt)) : 0;
      clock.value = quiet ? 0 : time; contrast.value = quiet ? .38 : 1;
      if (view.playerPos) sky.position.copy(view.playerPos);
      place(quiet);
    },
    reset() { if (!disposed) { time = 0; clock.value = 0; contrast.value = 1; sky.position.set(0, 0, 0); place(); } },
    dispose() {
      if (disposed) return; disposed = true; object.removeFromParent();
      for (const { mesh } of batches) mesh.dispose();
      for (const geometry of geometries) geometry.dispose();
      skyMat.dispose(); enamel.dispose(); soap.dispose();
    },
  };
}
