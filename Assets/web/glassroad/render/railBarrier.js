import { Group, Mesh, BufferGeometry, Float32BufferAttribute, MeshBasicNodeMaterial,
  DoubleSide, Matrix4, Vector3, Color } from 'three/webgpu';
import { attribute } from 'three/tsl';

// Two prebuilt contact patches. The rest of the tall guard stays transparent.
export function createRailBarrier(bus, { colour = () => '#75ffe2' } = {}) {
  const object = new Group(); object.name = 'rail-impact-honeycomb';
  const material = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide });
  material.colorNode = attribute('barrierColour', 'vec3');
  material.opacityNode = attribute('barrierAlpha', 'float');
  const base = [], distances = [];
  const radius = .62, line = .025;
  for (let q = -2; q <= 2; q++) for (let r = -2; r <= 2; r++) {
    if (Math.abs(q + r) > 2) continue;
    const cx = radius * 1.5 * q, cy = radius * Math.sqrt(3) * (r + q / 2);
    for (let e = 0; e < 6; e++) {
      const a = e * Math.PI / 3, b = (e + 1) * Math.PI / 3;
      const points = [
        [cx + (radius - line) * Math.cos(a), cy + (radius - line) * Math.sin(a)],
        [cx + (radius + line) * Math.cos(a), cy + (radius + line) * Math.sin(a)],
        [cx + (radius + line) * Math.cos(b), cy + (radius + line) * Math.sin(b)],
        [cx + (radius - line) * Math.cos(b), cy + (radius - line) * Math.sin(b)],
      ];
      for (const i of [0, 1, 2, 0, 2, 3]) {
        base.push(...points[i], 0); distances.push(Math.hypot(cx, cy));
      }
    }
  }
  const slots = Array.from({ length: 2 }, () => {
    const geometry = new BufferGeometry();
    geometry.setAttribute('position', new Float32BufferAttribute(base, 3));
    geometry.setAttribute('barrierColour', new Float32BufferAttribute(new Float32Array(base.length), 3));
    geometry.setAttribute('barrierAlpha', new Float32BufferAttribute(new Float32Array(distances.length), 1));
    const mesh = new Mesh(geometry, material);
    mesh.frustumCulled = false; mesh.renderOrder = 3; object.add(mesh);
    return { mesh, t: 2, gain: 0, gentle: false };
  });
  const basis = new Matrix4(), x = new Vector3(), y = new Vector3(), z = new Vector3(), tint = new Color();
  let cursor = 0, paused = false, gentle = false, reducedMotion = false, disposed = false;
  function hit(e) {
    if (disposed || paused || !e?.p || !e.tangent || !e.up || !e.normal || (e.racer ?? 0) !== 0) return;
    if (![...e.p, ...e.tangent, ...e.up, ...e.normal].every(Number.isFinite)) return;
    const slot = slots[cursor++ % slots.length];
    slot.t = 0; slot.gain = Math.min(.8, .3 + Math.max(0, e.dv || 0) / 30);
    slot.gentle = gentle || reducedMotion;
    y.fromArray(e.up).normalize(); z.fromArray(e.normal).normalize();
    // Both sides need a right-handed basis before conversion to a quaternion.
    x.crossVectors(y, z).normalize(); y.crossVectors(z, x).normalize();
    basis.makeBasis(x, y, z); slot.mesh.quaternion.setFromRotationMatrix(basis);
    slot.mesh.position.fromArray(e.p).addScaledVector(z, .06);
    slot.mesh.visible = true;
    tint.set(colour());
    const colours = slot.mesh.geometry.attributes.barrierColour;
    for (let i = 0; i < colours.count; i++) colours.setXYZ(i, tint.r, tint.g, tint.b);
    colours.needsUpdate = true;
  }
  const offs = [bus?.on('wall', hit), bus?.on('pause', e => { paused = !!e?.on; })].filter(Boolean);
  function update(dt, opts = {}) {
    if (disposed) return;
    paused = !!opts.paused; gentle = !!opts.gentle; reducedMotion = !!opts.reducedMotion;
    if (paused) return;
    for (const slot of slots) {
      slot.t += Math.max(0, dt || 0);
      const calm = slot.gentle || gentle || reducedMotion, life = calm ? .9 : .65;
      if (slot.t >= life) { slot.mesh.visible = false; continue; }
      const position = slot.mesh.geometry.attributes.position, alpha = slot.mesh.geometry.attributes.barrierAlpha;
      const envelope = Math.min(1, slot.t / .065) * (1 - slot.t / life) ** 2;
      for (let i = 0; i < alpha.count; i++) {
        const d = distances[i], wave = calm ? 0 : Math.sin(slot.t * 22 - d * 2.4) * Math.exp(-slot.t * 6) * .24;
        position.array[i * 3 + 2] = wave;
        alpha.array[i] = envelope * slot.gain * (calm ? .3 : 1) * (1 - d / 4);
      }
      position.needsUpdate = true; alpha.needsUpdate = true;
    }
  }
  function reset() { for (const s of slots) { s.t = 2; s.mesh.visible = false; } paused = false; }
  return { object, hit, update, reset, dispose() {
    if (disposed) return; disposed = true;
    for (const off of offs) off();
    object.removeFromParent(); for (const s of slots) s.mesh.geometry.dispose(); material.dispose();
  } };
}
