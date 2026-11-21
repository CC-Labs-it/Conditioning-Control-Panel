import * as THREE from '../../../vendor/three/three.module.min.js';
import { LITE } from '../perf.js';
import { createControls } from './controls.js';
import { ROOMS, START, canWalk, roomAt } from './layout.js';
import { PA, HALL_CUE } from './copy.js';
import { createDome } from './dome.js';
import { createExpoMedia } from './media-panes.js';

const CSS = `
.expo-root {position:absolute;inset:0;overflow:hidden;background:#e9f4dc;color:#163b29;pointer-events:auto;touch-action:none;font:600 16px/1.35 system-ui;user-select:none}
.expo-canvas {display:block;width:100%;height:100%;touch-action:none}
.expo-caption {position:absolute;bottom:132px;left:50%;transform:translateX(-50%);max-width:680px;width:85%;text-align:center;background:#163b29ed;color:white;padding:12px 18px;border-radius:4px;pointer-events:none;font-size:clamp(16px,2vw,22px)}
.expo-caption:empty {display:none}
.expo-place {position:absolute;top:70px;left:18px;max-width:60%;padding:8px 12px;background:#fffffff0;border-left:5px solid #2a8550;pointer-events:none}
.expo-hint {position:absolute;bottom:22px;left:50%;transform:translateX(-50%);padding:8px 14px;background:#fffdf0e8;text-align:center;pointer-events:none;font-size:13px}
.expo-aim {position:absolute;top:50%;left:50%;width:5px;height:5px;border-radius:50%;background:white;box-shadow:0 0 0 1px #163b29;pointer-events:none}
.expo-preview {position:absolute;left:18px;top:124px;max-width:240px;background:#ffffffd9;padding:6px;font:12px system-ui}
.expo-preview button {min-height:36px;margin:2px;border:1px solid #8b9f89;background:#fff;color:#163b29}
@media(max-width:600px){.expo-place{font-size:13px;top:64px}.expo-preview{top:108px;max-width:190px}.expo-caption{bottom:200px;box-sizing:border-box}.expo-hint{bottom:140px;width:78%;font-size:11px;padding:4px 8px}}
`;

/** The first review build: one connected walk, replaceable exhibits, no camera choreography. */
export function createExpo(ctx, parent, options = {}) {
  const rollback = [];
  try { return buildExpo(ctx, parent, options, rollback); }
  catch (error) { for (const undo of rollback.reverse()) { try { undo(); } catch {} } throw error; }
}
function buildExpo(ctx, parent, { onExit, domeFactory = createDome }, rollback) {
  ctx.css(CSS);
  const doc = parent.ownerDocument, win = doc.defaultView;
  const make = (tag, cls, text) => { const e = doc.createElement(tag); e.className = cls; if (text) e.textContent = text; root.append(e); return e; };
  const root = doc.createElement('div'); root.className = 'expo-root'; root.tabIndex = -1; root.setAttribute('aria-label', 'LLAMA EXPO 2037'); parent.append(root);
  rollback.push(() => root.remove());
  const canvas = make('canvas', 'expo-canvas');
  let renderer;
  try { renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' }); }
  catch (error) { root.remove(); throw error; }
  rollback.push(() => { renderer.dispose(); renderer.forceContextLoss(); });
  renderer.setPixelRatio(Math.min(win.devicePixelRatio || 1, LITE ? 1.25 : 1.5));
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  const scene = new THREE.Scene(); scene.background = new THREE.Color('#c5e6ef');
  rollback.push(() => scene.traverse(object => { object.geometry?.dispose(); for (const m of [object.material].flat().filter(Boolean)) { for (const value of Object.values(m)) if (value?.isTexture) value.dispose(); m.dispose(); } }));
  const camera = new THREE.PerspectiveCamera(68, 1, .08, 220); camera.rotation.order = 'YXZ'; camera.position.set(START.x, 1.65, START.z);
  scene.add(new THREE.HemisphereLight('#fffef0', '#82925d', 2));
  const sun = new THREE.DirectionalLight('#fff0cf', 2.2); sun.position.set(-25, 45, 20); scene.add(sun);
  const geometries = new Set(), materials = new Set(), textures = new Set(), targets = [];
  const material = (color, extra = {}) => { const m = new THREE.MeshStandardMaterial({ color, roughness: .8, ...extra }); materials.add(m); return m; };
  const white = material('#f5f6eb'), green = material('#33784f'), path = material('#d7dfbf'), lawn = material('#8eaf6e');
  const add = (geometry, mat, parent = scene) => { geometries.add(geometry); const m = new THREE.Mesh(geometry, mat); parent.add(m); return m; };
  const box = (parent, size, at, mat = white) => { const m = add(new THREE.BoxGeometry(...size), mat, parent); m.position.set(...at); return m; };
  function label(parent, text, width, at, { size = 44, color = '#163b29', background = '#fffdf0' } = {}) {
    const c = doc.createElement('canvas'); c.width = 1024; c.height = 256;
    const g = c.getContext('2d');
    const paint = value => { g.fillStyle = background; g.fillRect(0, 0, c.width, c.height); g.fillStyle = color; g.font = `700 ${size}px system-ui`; g.textAlign = 'center'; g.textBaseline = 'middle';
      const lines = value.split('\n'); lines.forEach((line, i) => g.fillText(line, 512, 128 + (i - (lines.length - 1) / 2) * (size + 12), 970)); };
    paint(text);
    const texture = new THREE.CanvasTexture(c); texture.colorSpace = THREE.SRGBColorSpace; textures.add(texture);
    const mat = new THREE.MeshBasicMaterial({ map: texture, side: THREE.DoubleSide }); materials.add(mat);
    const mesh = add(new THREE.PlaneGeometry(width, width / 4), mat, parent); mesh.position.set(...at); mesh.userData.setText = value => { paint(value); texture.needsUpdate = true; }; return mesh;
  }
  const groundShape = new THREE.Shape(); groundShape.moveTo(-130, -150); groundShape.lineTo(130, -150); groundShape.lineTo(130, 150); groundShape.lineTo(-130, 150); groundShape.closePath();
  const domeLocation = ROOMS.find(r => r.id === 'dome'), opening = new THREE.Path(); opening.absarc(domeLocation.x, -domeLocation.z, 13.05, 0, Math.PI * 2, true); groundShape.holes.push(opening);
  const ground = add(new THREE.ShapeGeometry(groundShape, 64), lawn); ground.rotation.x = -Math.PI / 2; ground.position.y = -.12;
  const disk = (x, z, radius, mat) => { const m = add(new THREE.CylinderGeometry(radius, radius, .18, 64), mat); m.position.set(x, -.09, z); return m; };
  disk(0, 0, 18, path); disk(0, 63, 11, path); box(scene, [8, .18, 47], [0, -.09, 39.5], path);
  label(scene, 'LLAMA EXPO 2037', 15, [0, 6, 53], { size: 70, background: '#226541', color: '#ffffff' });
  box(scene, [.35, 7, .35], [-7.7, 3.5, 53], green); box(scene, [.35, 7, .35], [7.7, 3.5, 53], green);
  const timeline = label(scene, '2026  The Connection\n2027  The Polite Coup\n2028  The Hoof Problem', 11, [-8, 2.8, 61]); timeline.rotation.y = Math.PI / 2;
  const timeline2 = label(scene, '2029  Feed Is Included\n2030 to 2036  The Chewing Years\n2037  The Expo', 11, [8, 2.8, 61]); timeline2.rotation.y = -Math.PI / 2;
  const turnstile = label(scene, 'Human visitor no. 1 (today)', 3.5, [0, 1.5, 50]);
  targets.push({ mesh: turnstile, run: () => say('pa_turnstile') });
  let domeShell, domeFloor, domePlaque;
  for (const room of ROOMS) {
    const group = new THREE.Group(); group.position.set(room.x, 0, room.z); group.rotation.y = room.angle; scene.add(group);
    box(group, [6, .18, 16], [0, -.09, -20], path);
    // The doorway is a permanent independent frame, including when the dome walls disappear.
    box(group, [.35, 4.4, .5], [-3.2, 2.2, -13], green); box(group, [.35, 4.4, .5], [3.2, 2.2, -13], green);
    box(group, [6.75, .35, .5], [0, 4.4, -13], green);
    const title = label(group, room.label, 9, [0, 5.6, -13.1]); title.rotation.y = Math.PI;
    label(group, 'PLAZA', 4.5, [0, 3.8, -12.7]);
    if (room.id === 'dome') {
      domeFloor = disk(room.x, room.z, 13, material('#f9faf1', { transparent: true }));
      const shellMat = material('#fafbf3', { side: THREE.BackSide, transparent: true });
      domeShell = add(new THREE.SphereGeometry(13, 48, 24, 0, Math.PI * 2, 0, Math.PI / 2), shellMat, group);
      // A shallow skirt leaves a full-height opening beneath the hemisphere at its entrance.
      domeShell.position.y = 4.5;
      for (let i = 0; i < 48; i++) {
        const a = i / 48 * Math.PI * 2;
        if (Math.abs(a - Math.PI) < .3) continue;
        const wall = box(group, [1.8, 4.5, .2], [Math.sin(a) * 13, 2.25, Math.cos(a) * 13], shellMat); wall.rotation.y = a;
      }
      domePlaque = label(group, 'Visitors spend an average of 0 seconds\nin this room.', 7, [7, 2.5, -13.2]); domePlaque.rotation.y = Math.PI;
      if (!domeFactory) label(group, 'THERE IS NOTHING\nTO SEE HERE', 5, [0, 2.3, 1]);
    } else {
      box(group, [24, .18, 26], [0, -.09, 0], white);
      box(group, [.25, 6, 26], [-12, 3, 0]); box(group, [.25, 6, 26], [12, 3, 0]); box(group, [24, 6, .25], [0, 3, 13]);
      box(group, [9, 6, .25], [-7.5, 3, -13]); box(group, [9, 6, .25], [7.5, 3, -13]); box(group, [6, 1.5, .25], [0, 5.25, -13]);
      label(group, `[Placeholder: ${room.label} exhibits]`, 15, [0, 3, 10]).rotation.y = Math.PI;
      if (room.id === 'first') label(group, `Pressed ${Number(ctx.state?.taps) || 0} times by one human, 2026.\nBelieved voluntary.`, 10, [0, 1.8, 5]).rotation.y = Math.PI;
      if (room.id === 'shop') {
        const exit = label(group, 'Exit\n[Grey-box exit to recap]', 6, [0, 1.8, 7]);
        exit.rotation.y = Math.PI;
        targets.push({ mesh: exit, run: () => { say('pa_leave'); onExit(); } });
      }
    }
  }
  // The long secret corridor already leads somewhere; final wall copy belongs to the owner.
  box(scene, [5, .18, 104], [0, -.09, -68], path);
  box(scene, [.2, 4, 102], [-2.5, 2, -69]); box(scene, [.2, 4, 102], [2.5, 2, -69]);
  box(scene, [10, .18, 10], [0, -.09, -123], white);
  box(scene, [10, 4, .2], [0, 2, -128]); box(scene, [.2, 4, 10], [-5, 2, -123]); box(scene, [.2, 4, 10], [5, 2, -123]);
  label(scene, '[Placeholder: original contract under glass]', 4.5, [0, 3.2, -19]);
  label(scene, "[Placeholder: owner's line]", 7, [0, 2.5, -127.7]);
  box(scene, [1, .15, 1], [0, .5, -124], green); box(scene, [1, 1.2, .15], [0, 1, -124.5], green);
  const caption = make('p', 'expo-caption'), place = make('div', 'expo-place', 'Arrival plaza');
  make('div', 'expo-aim'); make('div', 'expo-hint', win.matchMedia('(pointer:coarse)').matches ? 'Left stick: walk. Drag the room: look. Tap: interact.' : 'WASD / arrows: walk. Drag: look. Click / E: interact. Escape: pause.');
  let gone = false, elapsed = 0, paintTime = 0, captionUntil = 0, previous = ctx.activeMs, lastRoom = 'plaza', plazaCall = false, closing = false;
  const say = (id, text = PA[id]) => { if (!text || gone) return; caption.textContent = text; caption.dataset.lineId = id; captionUntil = elapsed + Math.max(6, text.length / 14); };
  const domeRoom = ROOMS.find(r => r.id === 'dome');
  const dome = domeFactory?.({ THREE, scene, ctx, center: domeRoom, radius: 13, lite: LITE, onCue: say });
  rollback.push(() => dome?.dispose());
  const media = dome ? createExpoMedia(ctx, root, camera, dome, { lite: LITE }) : null;
  rollback.push(() => media?.dispose());
  const ray = new THREE.Raycaster(), pointer = new THREE.Vector2();
  function interact(point = { x: 0, y: 0 }) {
    if (ctx.paused || doc.hidden || gone) return;
    camera.updateMatrixWorld(); pointer.set(point.x, point.y); ray.setFromCamera(pointer, camera); ray.far = 5;
    const hit = ray.intersectObjects(targets.map(t => t.mesh))[0];
    if (hit) targets.find(t => t.mesh === hit.object)?.run();
    else if (roomAt(camera.position.x, camera.position.z) === 'dome') dome?.interact(ray);
  }
  const controls = createControls({ root, canvas, camera, canMove: canWalk, onInteract: interact, onPause: () => doc.getElementById('wake')?.click(), isPaused: () => gone || ctx.paused || doc.hidden });
  rollback.push(() => controls.dispose());
  function resize() { if (gone) return; const { width, height } = root.getBoundingClientRect(); renderer.setSize(width, height, false); camera.aspect = width / Math.max(1, height); camera.updateProjectionMatrix(); draw(); }
  function draw() { renderer.render(scene, camera); media?.draw(); }
  const offFrame = ctx.frame(() => {
    const now = ctx.activeMs, dt = Math.max(0, Math.min(1, (now - previous) / 1000)); previous = now;
    if (gone || ctx.paused || doc.hidden) return;
    elapsed += dt; controls.step(dt);
    const room = roomAt(camera.position.x, camera.position.z);
    dome?.step(dt, room === 'dome');
    if (domeShell && dome) { domeShell.material.opacity = dome.shellOpacity; domeFloor.material.opacity = Math.max(.2, dome.floorOpacity); }
    if (room !== lastRoom) {
      if (lastRoom === 'dome' && dome) {
        const seconds = Math.floor(dome.lastVisitSeconds), duration = `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
        domePlaque.userData.setText(`You spent ${duration}.\nThe average is now 0:01.`);
      }
      lastRoom = room; place.textContent = ROOMS.find(r => r.id === room)?.label || (room === 'contract' ? 'The Contract' : 'Arrival plaza'); say(HALL_CUE[room]);
    }
    if (!plazaCall && elapsed > 20 && room === 'plaza') { plazaCall = true; say('pa_arrive_2'); }
    if (!closing && elapsed > 900 && room !== 'dome') { closing = true; say('pa_closing'); }
    if (elapsed > captionUntil) caption.textContent = '';
    paintTime += dt;
    if (!LITE || paintTime >= 1 / 30) { paintTime %= 1 / 30; draw(); }
  });
  function sync() { previous = ctx.activeMs; controls.reset(); media?.pause(ctx.paused || doc.hidden); if (!ctx.paused && !doc.hidden) root.focus({ preventScroll: true }); }
  const offs = [ctx.on('pause', sync), ctx.on('resume', sync), ctx.on('suspend', sync)];
  rollback.push(() => { gone = true; offFrame(); offs.forEach(off => off?.()); doc.removeEventListener('visibilitychange', sync); win.removeEventListener('resize', resize); });
  doc.addEventListener('visibilitychange', sync); win.addEventListener('resize', resize);
  const local = ['localhost', '127.0.0.1'].includes(win.location.hostname);
  if (local) {
    const preview = make('details', 'expo-preview'); preview.innerHTML = '<summary>Expo review controls</summary>';
    for (const room of ROOMS) {
      const button = doc.createElement('button'); button.textContent = room.id; preview.append(button);
      button.onclick = () => { controls.reset(); camera.position.set(room.x, 1.65, room.z); camera.rotation.set(0, room.angle, 0); root.focus({ preventScroll: true }); };
    }
    const later = doc.createElement('button'); later.textContent = 'Dome: +30 seconds'; preview.append(later);
    later.onclick = () => { if (!ctx.paused && !doc.hidden && dome) { controls.reset(); camera.position.set(domeRoom.x - 8, 1.65, domeRoom.z); camera.rotation.set(0, -Math.PI / 2, 0); dome.step(30, true); draw(); root.focus({ preventScroll: true }); } };
  }
  say('pa_arrive_1'); resize(); root.focus({ preventScroll: true });
  return { root, camera, dome, renderer, scene, interact, draw,
    debug: () => ({ room: roomAt(camera.position.x, camera.position.z), depth: dome?.depth || 0, phase: dome?.phase || 0, elapsed, calls: renderer.info.render.calls }),
    dispose() { if (gone) return; gone = true; offFrame(); offs.forEach(off => off?.()); controls.dispose(); media?.dispose(); dome?.dispose(); doc.removeEventListener('visibilitychange', sync); win.removeEventListener('resize', resize); geometries.forEach(g => g.dispose()); materials.forEach(m => m.dispose()); textures.forEach(t => t.dispose()); renderer.dispose(); renderer.forceContextLoss(); root.remove(); },
  };
}
