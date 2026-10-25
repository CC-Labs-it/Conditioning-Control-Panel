/* Cotton-candy banks and soap bubbles stay fixed in the world, with close parallax.
 * Four shared instanced draws need no extra texture or decoder. */
import { Group, InstancedMesh, SphereGeometry, MeshBasicNodeMaterial, Object3D, Color } from 'three/webgpu';
import { vec3, normalize, normalWorld, normalView, positionView, dot, clamp, mix } from 'three/tsl';

export const THRESHOLD_PINKS = Object.freeze(['#ff91cc', '#fff5fc', '#e60087']);
export function thresholdStoneColour(track, row) {
  const lanes = track.fields?.find(f => f.id === row.field)?.lanes || [0];
  return THRESHOLD_PINKS[Math.max(0, lanes.indexOf(row.x)) % 3];
}

/** Seeded scenery with every puff and its full motion clear of all road decks. */
export function thresholdAtmosphereLayout(track, { phone = false } = {}) {
  if (track.id !== 'threshold') return { clouds: [], bubbles: [] };
  let seed = 28419;
  const rnd = () => ((seed = seed * 16807 % 2147483647) - 1) / 2147483646;
  const samples = [];
  for (let s = 0; s < track.lengthM; s += 10) samples.push(track.frameAt(s));
  const clouds = [], bubbles = [];
  const clear = (p, radius) => samples.every(f => {
    const d = p.map((v, i) => v - f.p[i]);
    if (Math.abs(d.reduce((n, v, i) => n + v * f.t[i], 0)) > radius + 12) return true;
    const up = d.reduce((n, v, i) => n + v * f.n[i], 0);
    return up < -radius - 5 || up > radius + 12 ||
      Math.abs(d.reduce((n, v, i) => n + v * f.b[i], 0)) > f.w / 2 + radius + 3;
  });
  const point = (f, side, up) => f.p.map((v, i) => v + f.b[i] * side + f.n[i] * up);
  const count = phone ? 70 : 150;
  for (let i = 0; i < count; i++) {
    const f = track.frameAt((i + .25) / count * track.lengthM), side = i % 2 ? -1 : 1;
    const close = i % 3 === 0, r = close ? 1.2 + rnd() * 2.3 : 2.5 + rnd() * 5;
    const p = point(f, side * (f.w / 2 + r + (close ? 8 : 18 + rnd() * 70)), close ? 3 + rnd() * 6 : 10 + rnd() * 36);
    if (clear(p, r + 1.5)) bubbles.push({ p, r, phase: rnd() * Math.PI * 2, close });
  }
  const banks = phone ? 30 : 64;
  for (let i = 0; i < banks; i++) {
    const f = track.frameAt((i + .5) / banks * track.lengthM), side = i % 2 ? 1 : -1;
    const r = 5 + rnd() * 5, close = i % 4 === 0;
    const centre = point(f, side * (f.w / 2 + (close ? 22 : 45 + rnd() * 70)), close ? -9 : 18 + rnd() * 38);
    for (let j = 0; j < 5; j++) {
      const size = r * (.65 + rnd() * .5);
      const p = centre.map((v, k) => v + (k === 1 ? (j === 2 ? r * .4 : 0) : (rnd() - .5) * r * 2.3));
      if (clear(p, size * 1.5 + 1.5)) clouds.push({ p, r: size, phase: i * .7, colour: i % 3 });
    }
  }
  return { clouds, bubbles };
}

export function createThresholdAtmosphere(track, options = {}) {
  const layout = thresholdAtmosphereLayout(track, options), object = new Group(); object.name = 'threshold-pink-atmosphere';
  const geometry = new SphereGeometry(1, 16, 10), pose = new Object3D(), colour = new Color(), white = new Color('#fff5fc');
  const clouds = new MeshBasicNodeMaterial({ color: '#ffffff' });
  clouds.colorNode = vec3(1).mul(clamp(dot(normalize(normalWorld), normalize(vec3(-.4, .85, .35))).mul(.2).add(.8), .55, 1));
  const soap = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false });
  const rim = dot(normalize(normalView), normalize(positionView.negate())).abs().oneMinus();
  const pink = new Color('#ffb2dd'), pearl = new Color('#fff7fe');
  soap.colorNode = mix(vec3(pink.r, pink.g, pink.b), vec3(pearl.r, pearl.g, pearl.b), rim.pow(3));
  soap.opacityNode = rim.pow(2).mul(.45).add(.035);
  const glint = new MeshBasicNodeMaterial({ color: '#fff7fe', transparent: true, opacity: .72, depthWrite: false });
  const mistPaint = new MeshBasicNodeMaterial({ color: '#f586c9', transparent: true, opacity: .13, depthWrite: false });
  const cloudMesh = new InstancedMesh(geometry, clouds, layout.clouds.length);
  const bubbleMesh = new InstancedMesh(geometry, soap, layout.bubbles.length);
  const glints = new InstancedMesh(geometry, glint, layout.bubbles.length);
  const mistRows = layout.clouds.filter((_, i) => i % 6 === 0), mist = new InstancedMesh(geometry, mistPaint, mistRows.length);
  cloudMesh.name = 'pink-cloud-banks'; bubbleMesh.name = 'floating-soap-bubbles'; glints.name = 'bubble-glints';
  mist.name = 'pink-mist-banks'; object.add(cloudMesh, bubbleMesh, glints, mist);
  layout.clouds.forEach((row, i) => cloudMesh.setColorAt(i, colour.set(THRESHOLD_PINKS[row.colour]).lerp(white, .48)));
  let time = 0, disposed = false;
  function place(quiet = false) {
    layout.clouds.forEach((row, i) => {
      const drift = quiet ? 0 : Math.sin(time * .16 + row.phase) * 1.2;
      pose.position.fromArray(row.p); pose.position.y += drift; pose.scale.set(row.r * 1.4, row.r * .7, row.r);
      pose.updateMatrix(); cloudMesh.setMatrixAt(i, pose.matrix);
    });
    layout.bubbles.forEach((row, i) => {
      const phase = time * .38 + row.phase;
      pose.position.fromArray(row.p);
      if (!quiet) { pose.position.y += Math.sin(phase) * 1.1; pose.position.x += Math.cos(phase * .7) * .45; }
      pose.scale.setScalar(row.r); pose.updateMatrix(); bubbleMesh.setMatrixAt(i, pose.matrix);
      pose.position.x -= row.r * .3; pose.position.y += row.r * .48; pose.position.z -= row.r * .58;
      pose.scale.set(row.r * .14, row.r * .24, row.r * .085); pose.updateMatrix(); glints.setMatrixAt(i, pose.matrix);
    });
    mistRows.forEach((row, i) => {
      pose.position.fromArray(row.p); pose.position.y -= row.r * .35;
      if (!quiet) pose.position.y += Math.sin(time * .16 + row.phase) * 1.2;
      pose.scale.set(row.r * 1.4, row.r * .25, row.r); pose.updateMatrix(); mist.setMatrixAt(i, pose.matrix);
    });
    for (const mesh of [cloudMesh, bubbleMesh, glints, mist]) mesh.instanceMatrix.needsUpdate = true;
  }
  place();
  for (const mesh of [cloudMesh, bubbleMesh, glints, mist]) { mesh.computeBoundingSphere(); mesh.boundingSphere.radius += 4; }
  return { object, layout,
    update(dt, view = {}) {
      if (disposed || view.paused) return;
      const quiet = !!(view.gentle || view.reducedMotion);
      if (!quiet) time += Math.max(0, Math.min(.1, dt));
      place(quiet);
    },
    reset() { time = 0; place(); },
    dispose() {
      if (disposed) return; disposed = true; object.removeFromParent();
      for (const mesh of [cloudMesh, bubbleMesh, glints, mist]) mesh.dispose();
      geometry.dispose(); clouds.dispose(); soap.dispose(); glint.dispose(); mistPaint.dispose();
    },
  };
}
