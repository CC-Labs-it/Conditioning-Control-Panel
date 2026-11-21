import { drawSpiral, normalizeParams } from '../../../arcademy/engine/loom/loomSpiral.js';
import { STRAY_WORDS, THEME_STRAY_WORDS } from '../../data/content/side.js';
import { herdRoster, llamaPortrait } from '../../data/content/llamas.js';
import { createDepth, DOME_CUES } from './depth.js';

const clamp = n => Math.max(0, Math.min(1, n));
const rise = (depth, start, end) => {
  const x = clamp((depth - start) / (end - start));
  return x * x * (3 - 2 * x);
};
const TAU = Math.PI * 2;

/** A bounded effect laboratory. The parent keeps collision, the floor and the bright exit door. */
export function createDome({ THREE, scene, ctx, center, radius = 13, lite = false, onCue = () => {} }) {
  const group = new THREE.Group(); group.position.set(center.x, 0, center.z); group.rotation.y = center.angle || 0; scene.add(group);
  const state = createDepth(), resources = new Set(), announced = new Set(), soundAbort = new AbortController();
  const bubbles = [], fog = [], words = [], panes = [], rings = [], mediaOccluders = [];
  let clock = 0, motion = 0, disposed = false, signWord = 'THERE IS\nNOTHING TO\nSEE HERE', lastPop = -10;
  const keep = item => { resources.add(item); return item; };
  const material = props => keep(new THREE.MeshBasicMaterial({ transparent: true, depthWrite: false, ...props }));
  function mesh(geometry, mat, parent = group) {
    const result = new THREE.Mesh(keep(geometry), mat); parent.add(result); return result;
  }
  function canvasTexture(width, height, paint) {
    const canvas = document.createElement('canvas'); canvas.width = width; canvas.height = height;
    paint(canvas.getContext('2d'), width, height);
    const texture = keep(new THREE.CanvasTexture(canvas)); texture.colorSpace = THREE.SRGBColorSpace;
    return texture;
  }
  function label(text, { background = null, ink = '#effff0', width = 768, height = 256 } = {}) {
    return canvasTexture(width, height, (g, w, h) => {
      if (background) { g.fillStyle = background; g.fillRect(0, 0, w, h); }
      const rows = text.split('\n');
      g.textAlign = 'center'; g.textBaseline = 'middle'; g.fillStyle = ink;
      g.font = `800 ${Math.min(74, h / (rows.length + 0.7))}px sans-serif`;
      rows.forEach((row, i) => g.fillText(row, w / 2, h * (i + 0.5) / rows.length, w - 60));
    });
  }
  const signTexture = label('THERE IS\nNOTHING TO\nSEE HERE', { background: '#234b39', width: 768, height: 420 });
  const signMat = material({ map: signTexture, opacity: 1, side: THREE.DoubleSide });
  const sign = mesh(new THREE.PlaneGeometry(3.7, 2.02), signMat); sign.position.set(0, 2.15, 0); sign.rotation.y = Math.PI;
  const post = mesh(new THREE.CylinderGeometry(0.055, 0.07, 1.25, 10), material({ color: '#668775', opacity: 1 })); post.position.y = 0.62;
  const foot = mesh(new THREE.CylinderGeometry(0.6, 0.65, 0.09, 24), material({ color: '#668775', opacity: 1 })); foot.position.y = 0.045;
  mediaOccluders.push(...[sign, post, foot].map(mesh => ({ mesh })));

  // One Loom image, rotated in space. No per-frame texture redraw or replacement spiral.
  const loomTexture = canvasTexture(768, 768, (g, size) => drawSpiral(g, size, normalizeParams({
    arms: 4, turns: 2, style: 'log', duty: 0.48, colors: ['#b7e8c5', '#f0d8b6'], bg: '#fcfff3',
  }), 0));
  const floorMat = material({ map: loomTexture, opacity: 0, side: THREE.DoubleSide });
  const watermark = mesh(new THREE.CircleGeometry(radius - 0.12, 64), floorMat);
  watermark.rotation.x = -Math.PI / 2; watermark.position.y = 0.035;
  const skyMat = material({ map: loomTexture, opacity: 0, side: THREE.BackSide });
  const sky = mesh(new THREE.SphereGeometry(radius - 0.1, 48, 24, 0, TAU, 0, Math.PI / 2), skyMat);
  sky.position.y = 4.5;
  const tunnelTexture = keep(loomTexture.clone()); tunnelTexture.wrapS = THREE.RepeatWrapping;
  const tunnelMat = material({ map: tunnelTexture, opacity: 0, side: THREE.BackSide });
  // The same open doorway remains outside the spiral surface at every depth.
  const tunnel = mesh(new THREE.CylinderGeometry(radius - 0.14, radius - 0.14, 24, 48, 1, true, Math.PI + 0.27, TAU - 0.54), tunnelMat);
  tunnel.position.y = -1;

  const loader = new THREE.TextureLoader();
  const bubbleTexture = keep(loader.load(new URL('../../assets/img/bubble.png', import.meta.url).href));
  bubbleTexture.colorSpace = THREE.SRGBColorSpace;
  const bubbleGeometry = keep(new THREE.SphereGeometry(1, 20, 14));
  for (let i = 0; i < (lite ? 10 : 24); i++) {
    const bodyMat = keep(new THREE.MeshPhongMaterial({ color: '#d4fff0', transparent: true,
      opacity: 0, shininess: 110, specular: '#ffffff', depthWrite: false }));
    const body = new THREE.Mesh(bubbleGeometry, bodyMat); group.add(body);
    const rimMat = keep(new THREE.SpriteMaterial({ map: bubbleTexture, transparent: true, opacity: 0, depthWrite: false }));
    const rim = new THREE.Sprite(rimMat); body.add(rim); rim.scale.setScalar(2.05);
    bubbles.push({ body, bodyMat, rimMat, size: 0.28 + (i % 4) * 0.14, seed: i * 2.39996, poppedAt: -100 });
  }

  // Layered translucent volumes stand in for the later Steam volume shader.
  const puffGeometry = keep(new THREE.SphereGeometry(1, 16, 12));
  for (let i = 0; i < (lite ? 5 : 12); i++) {
    const mat = material({ color: i % 2 ? '#d0b9e0' : '#b8e6d9', opacity: 0, side: THREE.DoubleSide });
    const puff = new THREE.Mesh(puffGeometry, mat); group.add(puff);
    fog.push({ mesh: puff, mat, seed: i * 2.39996 });
  }
  // These layered word planes preserve real parallax until the text mesh art pass.
  ['THERE', 'IS', 'NOTHING', 'TO', 'SEE', 'HERE'].forEach((word, i) => {
    const object = new THREE.Group(); group.add(object);
    const tex = label(word), front = material({ map: tex, opacity: 0, side: THREE.DoubleSide });
    const back = material({ map: tex, color: '#245a48', opacity: 0, side: THREE.DoubleSide });
    const geometry = keep(new THREE.PlaneGeometry(Math.max(1.4, word.length * 0.48), 0.9));
    const shadow = new THREE.Mesh(geometry, back); shadow.position.set(0.065, -0.06, -0.065); object.add(shadow);
    const face = new THREE.Mesh(geometry, front); object.add(face); mediaOccluders.push({ mesh: face });
    words.push({ object, front, back, seed: i * 1.0472 });
  });

  const roster = herdRoster(ctx.life?.llamas || 0);
  for (let i = 0; i < (lite ? 3 : 6); i++) {
    const object = new THREE.Group(); group.add(object);
    const frameMat = material({ color: '#d5bd80', opacity: 0 });
    const frame = mesh(new THREE.BoxGeometry(3.2, 2.4, 0.12), frameMat, object);
    mediaOccluders.push({ mesh: frame, owner: object });
    const llama = roster[i % Math.max(1, roster.length)];
    const texture = llama && i % 2 === 0
      ? keep(loader.load(new URL(`../../${llamaPortrait(llama.id)}`, import.meta.url).href))
      : label('Picture pane\nplaceholder', { background: '#e1ece3', ink: '#31523e' });
    texture.colorSpace = THREE.SRGBColorSpace;
    const faceMat = material({ map: texture, opacity: 0, side: THREE.DoubleSide });
    const face = mesh(new THREE.PlaneGeometry(2.95, 2.16), faceMat, object); face.position.z = 0.075;
    panes.push({ object, frameMat, faceMat, width: 2.95, height: 2.16, offset: 0.076,
      placeholder: !(llama && i % 2 === 0), seed: i * TAU / (lite ? 3 : 6),
      get opacity() { return faceMat.opacity; } });
  }
  bubbles.forEach((bubble, i) => {
    if (i % 3) return;
    const mat = keep(new THREE.SpriteMaterial({ map: panes[i % panes.length].faceMat.map,
      transparent: true, opacity: 0, depthWrite: false }));
    const picture = new THREE.Sprite(mat); picture.scale.set(1.25, 0.95, 1); bubble.body.add(picture);
    bubble.pictureMat = mat;
  });
  // Light traces make the shared breathing clock visible without flashes.
  for (let i = 0; i < (lite ? 2 : 4); i++) {
    const mat = material({ color: i % 2 ? '#f3daaf' : '#96d9ca', opacity: 0 });
    const ring = mesh(new THREE.TorusGeometry(radius * 0.78, 0.018, 6, 96), mat);
    ring.rotation.x = Math.PI / 2; ring.position.y = 0.1 + i * 1.1;
    rings.push({ mesh: ring, mat });
  }
  const themes = [ctx.pick?.('f5'), ctx.has?.('taste2') ? ctx.state?.store?.taste2?.theme : null];
  const triggerWords = themes.flatMap(theme => THEME_STRAY_WORDS[theme] || []);
  if (!triggerWords.length) triggerWords.push(...STRAY_WORDS);
  function cue(id) { onCue(id, DOME_CUES[id]); }
  function setSign(text) {
    if (signWord === text) return;
    signWord = text;
    const old = signMat.map;
    signMat.map = label(text, { background: '#234b39', width: 768, height: 420 });
    old.dispose(); resources.delete(old);
  }

  function step(dt, inside) {
    if (disposed || ctx.paused) return;
    const elapsed = Number.isFinite(dt) ? Math.max(0, dt) : 0;
    clock += elapsed;
    const transition = state.step(elapsed, inside), d = state.depth;
    if (transition.entered && !announced.has(0)) { announced.add(0); cue('dome_0'); }
    if (transition.left && state.lastVisitSeconds >= 60) cue('dome_leave');
    if (inside) for (let phase = 1; phase <= state.phase; phase++) {
      if (!announced.has(phase)) { announced.add(phase); cue(`dome_${phase}`); }
    }
    const still = !!ctx.reduced, gentle = !!ctx.gentle;
    // Preference changes slow or hold the current pose instead of snapping to another phase.
    motion += elapsed * (still ? 0 : gentle ? 0.18 : 1);
    const breath = (Math.sin(motion * TAU / 12) + 1) / 2;
    const floor = rise(d, 0.1, 0.15), skyOn = rise(d, 0.25, 0.3), mist = rise(d, 0.5, 0.56);
    const pictures = rise(d, 0.75, 0.8), dream = rise(d, 0.95, 1);
    floorMat.opacity = floor * (0.09 + skyOn * 0.15 + pictures * 0.22);
    watermark.rotation.z = motion * 0.025;
    skyMat.opacity = skyOn * 0.88; sky.rotation.y = motion * 0.018;
    tunnelMat.opacity = pictures * 0.82; tunnelTexture.offset.x = motion * 0.002;
    sign.position.x = Math.sin(motion * 0.3) * 0.008 * floor;
    if (state.phase === 5) {
      const turn = clock % 8, fade = Math.min(1, turn / 0.8, (8 - turn) / 0.8);
      setSign(triggerWords[Math.floor(clock / 8) % triggerWords.length]);
      signMat.opacity = fade;
    } else { setSign('THERE IS\nNOTHING TO\nSEE HERE'); signMat.opacity = 1; }
    bubbles.forEach((bubble, i) => {
      const angle = bubble.seed + motion * 0.035, orbit = 2 + (i % 5) * 1.7;
      const limit = gentle || still ? (lite ? 4 : 7) : bubbles.length;
      const life = clamp((clock - bubble.poppedAt - 4) / (gentle ? 1 : 0.6));
      const alpha = i < limit ? skyOn * life : 0;
      bubble.body.position.set(Math.cos(angle) * orbit, 0.4 + ((motion * 0.25 + i * 1.1) % 7), Math.sin(angle) * orbit);
      bubble.body.scale.setScalar(bubble.size * (1 + dream * breath * 0.07));
      bubble.bodyMat.opacity = alpha * 0.08; bubble.rimMat.opacity = alpha * 0.82;
      if (bubble.pictureMat) bubble.pictureMat.opacity = alpha * pictures * 0.65;
      bubble.body.visible = alpha > 0.001;
    });
    fog.forEach((puff, i) => {
      const angle = puff.seed + motion * 0.016, size = 3.8 + i % 3;
      puff.mesh.position.set(Math.cos(angle) * 7, 0.12 + mist * (0.6 + (i % 3) * 0.6), Math.sin(angle) * 7);
      puff.mesh.scale.set(size, 0.5 + mist * (0.7 + dream), size);
      puff.mat.opacity = (gentle && i > 3 ? 0 : mist) * (0.022 + breath * 0.008);
      puff.mesh.visible = puff.mat.opacity > 0.001;
    });
    words.forEach((word, i) => {
      const cycle = (motion * 0.045 + i * 0.31) % 4;
      const travel = 1 - Math.abs(cycle - 2);
      const cross = Math.sin(motion * 0.068 + word.seed * 2);
      word.object.position.set(Math.cos(word.seed) * travel * 9.5, 2.8 + (1 + cross) * 1.6,
        Math.sin(word.seed) * travel * 9.5);
      word.object.rotation.y = word.seed + motion * 0.024;
      const alpha = (gentle && i > 2 ? 0 : mist) * 0.8;
      word.front.opacity = alpha; word.back.opacity = alpha;
      word.object.visible = alpha > 0.001;
    });
    panes.forEach((pane, i) => {
      const angle = pane.seed + motion * 0.026;
      pane.object.position.set(Math.sin(angle) * 9.2, 2.8 + (i % 2) * 1.6 + dream * breath * 0.2, Math.cos(angle) * 9.2);
      pane.object.rotation.y = angle + Math.PI;
      pane.frameMat.opacity = pictures; pane.faceMat.opacity = pictures;
      pane.object.visible = pictures > 0.001;
    });
    rings.forEach((ring, i) => {
      ring.mat.opacity = dream * (0.16 + breath * 0.16);
      ring.mesh.scale.setScalar(1 + dream * breath * (0.015 + i * 0.006));
      ring.mesh.visible = ring.mat.opacity > 0.001;
    });
    group.visible = true;
  }
  step(0, false);
  return {
    step,
    interact(raycaster) {
      if (disposed || ctx.paused || !state.inside || state.phase < 2) return false;
      group.updateMatrixWorld(true);
      const hit = raycaster.intersectObjects(bubbles.filter(b => b.body.visible).map(b => b.body), false)[0];
      if (!hit || hit.distance > 5) return false;
      const bubble = bubbles.find(b => b.body === hit.object); bubble.poppedAt = clock;
      bubble.body.visible = false;
      if (clock - lastPop > 0.16) {
        lastPop = clock;
        void ctx.audio?.play?.('assets/audio/sfx/chip_pop.mp3', { bus: 'sfx', gain: 0.22, signal: soundAbort.signal });
      }
      return true;
    },
    get depth() { return state.depth; }, get phase() { return state.phase; },
    get inside() { return state.inside; },
    get picturePanes() { return panes.filter(pane => pane.placeholder); },
    get mediaOccluders() { return mediaOccluders; },
    get seconds() { return state.seconds; }, get visitSeconds() { return state.visitSeconds; },
    get lastVisitSeconds() { return state.lastVisitSeconds; },
    get shellOpacity() { return 1 - rise(state.depth, 0.75, 0.81); },
    get floorOpacity() { return 1 - rise(state.depth, 0.95, 1) * 0.65; },
    // Future recorded stems read these same gains; this prototype has no room recording.
    get sound() { return { note: rise(state.depth, 0.1, 0.15), sky: rise(state.depth, 0.25, 0.3),
      fog: rise(state.depth, 0.5, 0.56), pictures: rise(state.depth, 0.75, 0.8), dream: rise(state.depth, 0.95, 1) }; },
    dispose() {
      if (disposed) return; disposed = true; soundAbort.abort();
      scene.remove(group); for (const resource of resources) resource.dispose(); resources.clear();
    },
  };
}
