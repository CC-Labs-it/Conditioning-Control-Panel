import { Group, Mesh, BoxGeometry, TorusGeometry, MeshBasicNodeMaterial, Matrix4, Vector3 } from 'three/webgpu';
import { phraseGateLayout } from '../track/phraseGates.js';
import { createPhraseGateContact } from '../sim/phraseGates.js';

const ease = x => { x = Math.max(0, Math.min(1, x)); return x * x * (3 - 2 * x); };
export function phraseEnvelope(age, gentle = false) {
  const rise = gentle ? .5 : .3, fade = gentle ? .9 : .6;
  return age < rise ? ease(age / rise) : age < rise + 1 ? 1 : 1 - ease((age - rise - 1) / fade);
}

// Open luminous brackets and a three-ring badge distinguish these from item boxes and laser walls.
// Everything exists before the loading warmup. There is no collision geometry or shader added on contact.
export function createPhraseGates(scene, track, { feel, photosafe, doc = globalThis.document } = {}) {
  const gates = phraseGateLayout(track), contact = createPhraseGateContact(gates), object = new Group();
  object.name = 'phrase-gates';
  const box = new BoxGeometry(1, 1, 1), ring = new TorusGeometry(1, .07, 6, 40);
  const color = gates[0]?.color || '#ffffff';
  const ink = new MeshBasicNodeMaterial({ color: '#192332' });
  const light = new MeshBasicNodeMaterial({ color });
  const materialList = [ink, light];
  for (const gate of gates) {
    const root = new Group(), f = gate.frame, width = f.w / 2 + .7;
    root.name = gate.id;
    root.position.fromArray(f.p);
    root.quaternion.setFromRotationMatrix(new Matrix4().makeBasis(new Vector3(...f.b), new Vector3(...f.n), new Vector3(...f.t).negate()));
    function beam(x, y, w, h, d, mat) {
      const m = new Mesh(box, mat); m.position.set(x, y, 0); m.scale.set(w, h, d); root.add(m);
    }
    for (const side of [-1, 1]) {
      beam(side * width, 3.6, .7, 7.2, .65, ink);
      beam(side * width, 3.6, .25, 6.8, .72, light);
      beam(side * (width - 1.1), 7.1, 2.7, .24, .7, light);
    }
    beam(0, 7.4, f.w + 2, .45, .45, ink);
    beam(0, 7.4, f.w + 2, .12, .52, light);
    // A low threshold ribbon connects the posts without resembling a solid barrier.
    beam(0, .1, f.w, .035, .35, light);
    for (const radius of [.55, .85, 1.15]) {
      const m = new Mesh(ring, light); m.position.y = 9; m.scale.setScalar(radius); root.add(m);
    }
    object.add(root);
  }
  scene.add(object);
  const label = doc?.createElement('div');
  if (label) {
    label.className = 'gr-phrase-gate'; label.setAttribute('aria-hidden', 'true');
    Object.assign(label.style, { position: 'fixed', left: '50%', top: '29%', zIndex: '8', pointerEvents: 'none',
      display: 'none', opacity: '0', transform: 'translate(-50%,-50%)', width: '82vw', textAlign: 'center',
      color, font: '900 clamp(26px, 5.4vw, 76px) system-ui, sans-serif', lineHeight: '1.1', letterSpacing: '.055em',
      textShadow: '0 3px 0 #14202b, 2px 0 0 #14202b, -2px 0 0 #14202b, 0 0 22px #24384b',
      willChange: 'opacity,transform' });
    doc.body.appendChild(label);
  }
  let age = -1, gentle = false, last = null;
  function clear() { age = -1; last = null; contact.reset(); if (label) label.style.display = 'none'; }
  function update(st, dt, { paused = false, mode = 'race' } = {}) {
    if (mode !== 'race') { clear(); return; }
    if (paused) return;
    const gate = contact.update(st, dt);
    if (gate) {
      gentle = !!feel?.get('gentle.on') || photosafe?.request('phrase') === false;
      age = 0; last = gate;
      if (label) { label.textContent = gate.text; label.style.display = 'block'; }
    } else if (age >= 0) age += Math.max(0, dt);
    if (age >= 0) {
      const alpha = phraseEnvelope(age, gentle);
      if (label) {
        label.style.opacity = String(alpha);
        label.style.transform = `translate(-50%,-50%) scale(${gentle ? 1 : .97 + .03 * ease(age / .3)})`;
      }
      if (alpha === 0 && age > .5) { age = -1; if (label) label.style.display = 'none'; }
    }
  }
  return { gates, update, clear,
    get stats() { return { gates: gates.length, hits: contact.count, active: age >= 0, age, text: last?.text || '' }; },
    dispose() { clear(); object.removeFromParent(); box.dispose(); ring.dispose(); materialList.forEach(m => m.dispose()); label?.remove(); } };
}
