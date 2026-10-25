import { Group, Mesh, InstancedMesh, Object3D, OctahedronGeometry, BoxGeometry, TorusGeometry, PlaneGeometry, CanvasTexture, DoubleSide, MeshBasicNodeMaterial, Matrix4, Vector3 } from 'three/webgpu';
import { phraseGateLayout } from '../track/phraseGates.js';
import { createPhraseGateContact } from '../sim/phraseGates.js';
import { S } from '../sim/state.js';
import { phraseLook, gatePulse, gateMote, GATE_CAPACITY } from './phraseGateMotion.js';
import { createSongGateRun } from '../sim/songGates.js';
import { wordTheme, wordFace } from './wordTheme.js';
import { FACES } from './fonts.js';

export function phraseEnvelope(age, gentle = false) {
  return phraseLook(age, gentle).alpha;
}

// Open luminous brackets and a three-ring badge distinguish these from item boxes and laser walls.
// Everything exists before the loading warmup. There is no collision geometry or shader added on contact.
export function createPhraseGates(scene, track, { feel, photosafe, bus = null, doc = globalThis.document } = {}) {
  const gates = phraseGateLayout(track), contact = createPhraseGateContact(gates), object = new Group();
  object.name = 'phrase-gates';
  const box = new BoxGeometry(1, 1, 1), ring = new TorusGeometry(1, .07, 6, 40);
  const color = gates[0]?.color || '#ffffff';
  const ink = new MeshBasicNodeMaterial({ color: '#192332', transparent: true, depthWrite: false });
  const light = new MeshBasicNodeMaterial({ color, transparent: true, depthWrite: false });
  const sparkle = new MeshBasicNodeMaterial({ color, transparent: true, opacity: .72, depthWrite: false });
  const moteGeometry = new OctahedronGeometry(1), dummy = new Object3D(), pose = {};
  const materialList = [ink, light, sparkle], animated = [];
  const theme = wordTheme(track), face = wordFace(theme, 5), textures = [], signGeometry = new PlaneGeometry(1, 1);
  for (const gate of gates) {
    const root = new Group(), f = gate.frame, width = f.w / 2 + .7;
    const gateInk = ink.clone(), gateLight = light.clone(); materialList.push(gateInk, gateLight);
    root.name = gate.id;
    root.position.fromArray(f.p);
    root.quaternion.setFromRotationMatrix(new Matrix4().makeBasis(new Vector3(...f.b), new Vector3(...f.n), new Vector3(...f.t).negate()));
    function beam(x, y, w, h, d, mat) {
      const m = new Mesh(box, mat); m.position.set(x, y, 0); m.scale.set(w, h, d); root.add(m);
    }
    for (const side of [-1, 1]) {
      beam(side * width, 3.6, .7, 7.2, .65, gateInk);
      beam(side * width, 3.6, .25, 6.8, .72, gateLight);
      beam(side * (width - 1.1), 7.1, 2.7, .24, .7, gateLight);
    }
    beam(0, 7.4, f.w + 2, .45, .45, gateInk);
    beam(0, 7.4, f.w + 2, .12, .52, gateLight);
    // A low threshold ribbon connects the posts without resembling a solid barrier.
    beam(0, .1, f.w, .035, .35, gateLight);
    for (const radius of [.55, .85, 1.15]) {
      const m = new Mesh(ring, gateLight); m.position.y = 9; m.scale.setScalar(radius); root.add(m);
    }
    const motes = new InstancedMesh(moteGeometry, sparkle, GATE_CAPACITY);
    motes.frustumCulled = false;
    for (let i = 0; i < GATE_CAPACITY; i++) { dummy.scale.setScalar(0); dummy.updateMatrix(); motes.setMatrixAt(i, dummy.matrix); }
    root.add(motes);
    const pulseMaterial = new MeshBasicNodeMaterial({ color, transparent: true, opacity: 0, depthWrite: false });
    materialList.push(pulseMaterial);
    const pulse = new Mesh(ring, pulseMaterial); pulse.position.y = 3.6; root.add(pulse);
    let sign = null, canvas = null, context = null;
    if (doc?.createElement) {
      canvas = doc.createElement('canvas'); canvas.width = 1024; canvas.height = 160; context = canvas.getContext?.('2d');
      if (context) {
        const texture = new CanvasTexture(canvas); textures.push(texture);
        const material = new MeshBasicNodeMaterial({ map: texture, transparent: true, depthWrite: false, side: DoubleSide }); materialList.push(material);
        sign = new Mesh(signGeometry, material); sign.position.set(0, 6, -.08); sign.scale.set(f.w * .85, 2.3, 1); sign.visible = false; root.add(sign);
      }
    }
    animated.push({ gate, base: gate, root, gateInk, gateLight, sign, canvas, context, caption: '', motes, pulse, hitAge: -1, soft: false });
    object.add(root);
  }
  scene.add(object);
  const label = doc?.createElement('div'), echoes = [], stars = [];
  let word = null;
  if (label) {
    label.className = 'gr-phrase-gate'; label.setAttribute('aria-hidden', 'true');
    Object.assign(label.style, { position: 'fixed', left: '50%', top: '29%', zIndex: '8', pointerEvents: 'none',
      display: 'none', opacity: '0', transform: 'translate(-50%,-50%)', width: '82vw', textAlign: 'center',
      color, font: FACES[face.face].css(76, face.weight), fontSize: 'clamp(26px, 5.4vw, 76px)', lineHeight: '1.1', letterSpacing: theme.tracking + 'em',
      textShadow: '0 4px 0 #14202b, 2px 0 0 #14202b, -2px 0 0 #14202b, 0 0 22px #24384b',
      willChange: 'opacity,transform' });
    for (const tint of [color, '#fff2c9']) {
      const echo = doc.createElement('span');
      Object.assign(echo.style, { position: 'absolute', inset: '0', color: tint, opacity: '0' });
      label.appendChild(echo); echoes.push(echo);
    }
    word = doc.createElement('span');
    Object.assign(word.style, { position: 'relative', display: 'inline-block', color,
      WebkitTextStroke: '1px #16302b', textShadow: '0 4px 0 #14202b, 0 0 18px ' + color });
    label.appendChild(word);
    for (let i = 0; i < 10; i++) {
      const star = doc.createElement('span'); star.textContent = i % 3 ? '✦' : '✧';
      Object.assign(star.style, { position: 'absolute', left: i % 2 ? '84%' : '16%', top: '50%',
        color: i % 3 ? color : '#fff2c9', fontSize: i % 3 ? '.2em' : '.3em', opacity: '0' });
      label.appendChild(star); stars.push(star);
    }
    doc.body.appendChild(label);
  }
  let age = -1, gentle = false, last = null, clock = 0, songRun = null, activeSong = null, songTime = null;
  const matrix = new Matrix4(), basis = [new Vector3(), new Vector3(), new Vector3()];
  function position(row, gate, alpha = 1) {
    const f = gate.frame;
    row.gate = gate; row.root.position.fromArray(f.p);
    matrix.makeBasis(basis[0].fromArray(f.b), basis[1].fromArray(f.n), basis[2].fromArray(f.t).negate());
    row.root.quaternion.setFromRotationMatrix(matrix); row.root.scale.x = f.w / row.base.frame.w;
    row.gateInk.opacity = row.gateLight.opacity = alpha;
    if (row.sign) {
      row.sign.visible = !!gate.kind; row.sign.material.opacity = alpha;
      if (row.caption !== gate.text) {
        row.caption = gate.text; const ctx = row.context;
        ctx.clearRect(0, 0, 1024, 160); ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
        let px = 100; ctx.font = FACES[face.face].css(px, face.weight);
        px *= Math.min(1, 950 / Math.max(1, ctx.measureText(gate.text).width)); ctx.font = FACES[face.face].css(px, face.weight);
        ctx.lineWidth = 10; ctx.strokeStyle = theme.ink; ctx.strokeText(gate.text, 512, 80);
        ctx.fillStyle = color; ctx.fillText(gate.text, 512, 80); row.sign.material.map.needsUpdate = true;
      }
    }
  }
  function clear() {
    age = -1; last = null; clock = 0; contact.reset();
    songRun?.reset(); songTime = null;
    for (const row of animated) { row.hitAge = -1; row.pulse.material.opacity = 0; row.motes.count = 0; position(row, row.base); row.root.visible = !activeSong; }
    if (label) label.style.display = 'none';
  }
  function update(st, dt, { paused = false, mode = 'race', song = null, songS = NaN } = {}) {
    if (mode !== 'race') { clear(); return; }
    if (paused) return;
    const softNow = !!feel?.get('gentle.on');
    if (song !== activeSong) { activeSong = song; songRun = song ? createSongGateRun(song, track) : null; clear(); }
    if (songRun) {
      if (songTime !== null && Number.isFinite(songS) && Math.abs(songS - songTime) > .5) {
        age = -1; last = null; if (label) label.style.display = 'none';
        for (const row of animated) { row.hitAge = -1; row.pulse.material.opacity = 0; }
      }
      dt = songTime === null || !Number.isFinite(songS) ? 0 : Math.max(0, Math.min(.1, songS - songTime));
      songTime = Number.isFinite(songS) ? songS : null;
    }
    clock += Math.max(0, dt) * (softNow ? .3 : 1);
    const synced = songRun?.update(songS, st, { gentle: softNow });
    if (synced) {
      const free = animated.filter(row => !synced.live.includes(row.gate));
      for (const row of animated) row.root.visible = false;
      for (const cue of synced.live) {
        const row = animated.find(row => row.gate === cue) || free.shift();
        if (!row) continue;
        if (row.gate !== cue) { row.hitAge = -1; row.soft = false; }
        position(row, cue, cue.alpha); row.root.visible = true;
      }
    }
    const gate = synced ? synced.hit : contact.update(st, dt);
    if (gate) {
      gentle = softNow || photosafe?.request('phrase') === false;
      age = 0; last = gate;
      const row = animated.find(v => v.gate === gate); row.hitAge = 0; row.soft = gentle;
      if (label) { word.textContent = gate.text; echoes.forEach(e => e.textContent = gate.text); label.style.display = 'block'; }
      bus?.emit('phrase-gate', { id: gate.id, course: track.id, index: synced ? songRun.cues.findIndex(c => c.id === gate.id) : gates.indexOf(gate), gentle,
        ...(synced ? { song: true, text: gate.text, kind: gate.kind, songS } : {}) });
    } else if (age >= 0) age += Math.max(0, dt);
    for (const row of animated) {
      if (row.hitAge >= 0 && row.gate !== gate) row.hitAge += Math.max(0, dt);
      const soft = softNow || row.soft, p = gatePulse(row.hitAge, soft);
      row.pulse.material.opacity = p.alpha;
      row.pulse.scale.set(row.base.frame.w * .5 * p.spread, 3.6 * p.spread, 1);
      row.pulse.position.z = -p.travel;
      if (row.hitAge > 2) row.hitAge = -1;
      const near = row.root.visible && Math.hypot(...row.gate.frame.p.map((v, i) => v - st[S.posX + i])) < 220;
      row.motes.visible = near;
      if (!near) continue;
      row.motes.count = softNow ? 32 : GATE_CAPACITY;
      for (let i = 0; i < row.motes.count; i++) {
        gateMote(i, clock, row.base.frame.w, row.hitAge, soft, pose);
        dummy.position.set(pose.x, pose.y, pose.z); dummy.rotation.set(0, clock * .3 + i, i * .7);
        const size = pose.size * (songRun ? row.gate.alpha || 0 : 1);
        dummy.scale.set(size, size * 1.6, size); dummy.updateMatrix(); row.motes.setMatrixAt(i, dummy.matrix);
      }
      row.motes.instanceMatrix.needsUpdate = true;
    }
    if (age >= 0) {
      const look = phraseLook(age, gentle || softNow), alpha = look.alpha;
      if (label) {
        label.style.opacity = String(alpha);
        label.style.transform = `translate(-50%,calc(-50% + ${look.y}px)) rotate(${look.tilt}deg) scale(${look.scale},${look.scale * look.stretch})`;
        echoes.forEach((e, i) => { e.style.opacity = String(look.echo); e.style.transform = `translate(${(i ? 1 : -1) * look.split}px,${look.split * .2}px)`; });
        stars.forEach((s, i) => {
          const side = i % 2 ? 1 : -1, spread = 18 + look.sparkTravel * 35;
          s.style.opacity = String(look.sparks);
          s.style.transform = `translate(${side * spread}px,${((i >> 1) - 2) * (10 + look.sparkTravel * 10)}px) rotate(${side * look.sparkTravel * 55}deg) scale(${.6 + look.sparkTravel * .4})`;
        });
      }
      if (alpha === 0 && age > .5) { age = -1; if (label) label.style.display = 'none'; }
    }
  }
  return { gates, update, clear,
    get stats() { return { song: !!songRun, gates: songRun ? songRun.cues.length : gates.length, hits: songRun ? songRun.hits : contact.count, active: age >= 0, age, text: last?.text || '', clock,
      particles: animated.reduce((sum, row) => sum + (row.motes.visible ? row.motes.count : 0), 0), pulses: animated.filter(row => row.pulse.material.opacity > .001).length }; },
    dispose() { clear(); object.removeFromParent(); animated.forEach(row => row.motes.dispose()); textures.forEach(t => t.dispose()); signGeometry.dispose(); moteGeometry.dispose(); box.dispose(); ring.dispose(); materialList.forEach(m => m.dispose()); label?.remove(); } };
}
