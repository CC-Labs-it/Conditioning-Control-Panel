import { Group, Mesh, PlaneGeometry, MeshBasicNodeMaterial, DoubleSide, Color, Matrix4, Vector3 } from 'three/webgpu';
import { uv, sin, cos, mix, smoothstep, float, vec3, positionGeometry } from 'three/tsl';
import { objectUniform, objectState } from './objectUniform.js';

const sat = x => Math.max(0, Math.min(1, Number.isFinite(x) ? x : 0));
const smooth = x => { const n = sat(x); return n * n * (3 - 2 * n); };
export const VEIL_CAPACITY = 8;
export function veilSize(frame, params = {}) {
  // A curtain you drive through: road width plus margin, two to three kart heights tall.
  return { width: Math.max(4, frame?.w || 4) + 8, height: Math.max(5, Math.min(9, (params.height || 4) * 1.7)) * (params.scale || 1) };
}

// One shared tessellation and ONE prebuilt material for every row (a material per row built 16 identical
// shader graphs at load): each row's values sit on its mesh (objectState) and are written like uniforms.
export function createVeils(scene, track, { feel, photosafe, gentle } = {}) {
  const object = new Group(); object.name = 'song-veils'; scene.add(object);
  const geometry = new PlaneGeometry(1, 1, 48, 12), rows = [];
  const matrix = new Matrix4(), bx = new Vector3(), ny = new Vector3(), tz = new Vector3();
  const material = (() => {
    const u = { time: objectUniform('time', 0), bass: objectUniform('bass', 0), alpha: objectUniform('alpha', 0), motion: objectUniform('motion', 1),
      primary: objectUniform('primary', new Color('#fa76bd')), accent: objectUniform('accent', new Color('#873e9a')) };
    const at = uv(), wave = sin(at.x.mul(10).add(u.time)).mul(.12).add(sin(at.y.mul(7).sub(u.time.mul(.7))).mul(.06));
    const stripe = sin(at.x.mul(19).add(at.y.mul(11)).add(wave.mul(5)).sub(u.time)).mul(.5).add(.5);
    const feather = smoothstep(0, .1, at.y).mul(float(1).sub(smoothstep(.86, 1, at.y)));
    const material = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, side: DoubleSide, fog: false });
    const crest = smoothstep(.82, 1, stripe), rim = smoothstep(.0, .06, at.y).mul(float(1).sub(smoothstep(.06, .14, at.y)))
      .add(smoothstep(.86, .93, at.y).mul(float(1).sub(smoothstep(.93, 1, at.y))));
    // Liquid rainbow bands that flow across the curtain, tinted toward the course palette so it belongs there.
    const hue = stripe.mul(.9).add(at.x.mul(.6)).add(u.time.mul(.08));
    const rainbow = vec3(cos(hue.mul(6.2832)), cos(hue.mul(6.2832).sub(2.0944)), cos(hue.mul(6.2832).sub(4.1888))).mul(.5).add(.5);
    material.colorNode = mix(mix(rainbow, mix(u.primary, u.accent, stripe), .3), vec3(1, 1, 1), crest.mul(.5).add(rim.mul(.75)));
    material.opacityNode = feather.mul(stripe.mul(.35).add(.65)).add(rim.mul(.3)).mul(u.alpha);
    material.positionNode = positionGeometry.add(vec3(0, wave.mul(.15).mul(u.motion),
      wave.mul(u.bass.mul(1.6).add(.6)).mul(u.motion)));
    return material;
  })();
  for (let i = 0; i < VEIL_CAPACITY; i++) {
    const mesh = new Mesh(geometry, material); mesh.visible = false; object.add(mesh);
    const u = objectState(mesh, { time: 0, bass: 0, alpha: 0, motion: 1, primary: new Color('#fa76bd'), accent: new Color('#873e9a') });
    rows.push({ mesh, u, id: null, born: 0, soft: false });
  }
  let clock = 0, bassLevel = 0, visible = 0, disposed = false;
  function clear() {
    for (const row of rows) { row.mesh.visible = false; row.id = null; row.u.alpha.value = 0; }
    clock = bassLevel = visible = 0;
  }
  function update(cues = [], dt, view = {}) {
    if (disposed || view.paused) return;
    const step = Math.max(0, Math.min(.1, dt || 0)); clock += step;
    const calm = typeof view.gentle === 'boolean' ? view.gentle : !!(gentle?.on || feel?.get('gentle.on'));
    bassLevel += (sat(view.bass) - bassLevel) * (1 - Math.exp(-step / (calm ? .9 : .25)));
    const active = cues.filter(c => c.kind === 'VEIL' && c.alpha > .001 && c.frame);
    for (const row of rows) if (!active.some(c => c.id === row.id)) { row.id = null; row.mesh.visible = false; }
    visible = 0;
    for (const cue of active) {
      let row = rows.find(r => r.id === cue.id);
      if (!row) {
        row = rows.find(r => r.id === null); if (!row) continue;
        row.id = cue.id; row.born = clock; row.soft = photosafe?.request('song-veil') === false;
      }
      const soft = calm || row.soft, f = cue.frame, size = veilSize(f, cue.params);
      const rise = Math.max(.3, gentle?.rise?.(soft ? .6 : .3) || (soft ? .6 : .3));
      row.mesh.visible = true; row.mesh.position.fromArray(f.p);
      matrix.makeBasis(bx.fromArray(f.b), ny.fromArray(f.n), tz.fromArray(f.t).negate());
      row.mesh.quaternion.setFromRotationMatrix(matrix);
      row.mesh.position.addScaledVector(ny, size.height / 2 + .12);
      row.mesh.scale.set(size.width, size.height, 1);
      row.u.alpha.value = sat(cue.alpha) * smooth((clock - row.born) / rise) * (soft ? .4 : .92);
      row.u.time.value += step * (soft ? .12 : .55 + bassLevel * .3);
      row.u.bass.value = bassLevel; row.u.motion.value = soft ? .2 : 1;
      const palette = view.palette || [];
      if (palette[0]) row.u.primary.value.set(palette[0]);
      if (palette[1]) row.u.accent.value.set(palette[1]);
      visible++;
    }
  }
  return { object, update, clear, get stats() { return { visible, clock, capacity: rows.length }; },
    dispose() { if (disposed) return; clear(); disposed = true; object.removeFromParent(); geometry.dispose(); material.dispose(); } };
}
