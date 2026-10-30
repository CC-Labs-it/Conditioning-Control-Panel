import { Group, Mesh, InstancedMesh, Object3D, BoxGeometry, PlaneGeometry, TorusGeometry,
  CanvasTexture, MeshBasicNodeMaterial, Matrix4, Vector3, DoubleSide, SRGBColorSpace } from 'three/webgpu';
import { acquireLoomAtlas } from './loomAtlas.js';
import { wordTheme } from './wordTheme.js';
import { screenBodyGeometry, screenBodyMaterial } from './screenBody.js';
import { objectState } from './objectUniform.js';
import { screenBodySize } from './screenBodyMath.js';
import { marqueeFace, faceX, CLEAR_M, HANG_M, DROP_S } from '../sim/marqueeView.js';

const CAPACITY = 12, LINKS = 30, CW = 512, CH = 288;
const unit = x => Math.max(0, Math.min(1, Number(x) || 0));
const ease = x => { const u = unit(x); return u * u * (3 - 2 * u); };

// Song-relative motion only, around a fixed road spot. The camera and physical road never participate.
// The drop lands exactly on startS (gravity: slow at the top, fastest at impact), bounces twice, then sways
// one swing per two beats and dips on every beat. An exit at endS snaps one chain (swing off the road) or winches up.
// The transport (sim/marqueeView.js) sets where it hangs for the chase view: shiftM along the road's side axis,
// fallM so the drop starts just above the top edge, liftM over a raised canal floor, faceScale on climbs and
// tight turns, and replacedS when a newer drop retires it (it leaves as the next screen falls).
const FALL_S = DROP_S, FALL_M = 46;
export function marqueePose(row, songS, gentle = false, side = 0, beat = null) {
  const fw = row.frame?.w || 24, { w, h } = marqueeFace(fw, side, row.faceScale);
  const age = songS - row.startS, exit = Math.max(0, songS - Math.min(row.endS, row.replacedS ?? Infinity));
  const u = unit((age + FALL_S) / FALL_S), fall = (row.fallM ?? FALL_M) * (1 - u * u);
  const bounce = gentle || age < 0 || age >= .9 ? 0 : Math.abs(Math.sin(age / .45 * Math.PI)) * 2.6 * (1 - age / .9) ** 1.5;
  let sway = 0, dip = 0;
  if (!gentle && age > 0 && beat?.bpm > 0) {
    const ph = (songS - (beat.downbeatS || 0)) * beat.bpm / 60, frac = ph - Math.floor(ph);
    const settle = unit(age / 1.2);
    sway = Math.sin(ph * Math.PI) * .07 * settle;
    dip = Math.exp(-frac * 7) * .7 * settle;
  } else if (!gentle && age > 0) sway = Math.sin(age * 1.3 + side) * .05;
  let jolt = 0;
  if (!gentle) for (const at of row.joltTimes || []) {
    const d = songS - at;
    if (d >= 0 && d < .65) jolt += Math.sin(d * Math.PI * 4 / .65) * (1 - d / .65) * .09;
  }
  const snap = row.params?.exit === 'snap', away = ease(exit / (gentle ? 2.4 : 1.3));
  const direction = side || (row.params?.snapSide ?? 1);
  const roll = sway + jolt + (!gentle && snap ? direction * away * 1.25 : 0);
  const x = faceX(fw, side, row.shiftM, row.faceScale) + (snap ? direction * away * (w + 18) : 0);
  // Rotation cannot lower any corner into the seven-metre road clearance (over a raised canal floor too).
  const extent = Math.abs(Math.cos(roll)) * h * .54 + Math.abs(Math.sin(roll)) * w * .54;
  const y = (row.liftM || 0) + Math.max(CLEAR_M + extent, HANG_M + h / 2 + fall + bounce - dip + away * (snap ? 14 : 44));
  return { x, y, w, h, roll, yaw: gentle ? 0 : Math.sin(Math.max(0, age) * .7) * .05,
    whip: gentle ? 0 : bounce * .5 + Math.abs(jolt) * 10 + dip * .6 + (age > -FALL_S && age < 0 ? .8 : 0), away, bottom: y - extent,
    snapped: snap && exit > 0 ? (direction > 0 ? 0 : 1) : -1 };
}

/** A bounded copy of the existing consent-valid decoder, cleared even on a paused source-off. */
export function createMarqueeSurface(canvas, atlas, style = 0) {
  canvas.width = CW; canvas.height = CH;
  const ctx = canvas.getContext('2d'), texture = new CanvasTexture(canvas);
  texture.colorSpace = SRGBColorSpace;
  let last = null, lastPaint = -Infinity, disposed = false, privatePixels = false;
  function blank() { ctx.fillStyle = '#171320'; ctx.fillRect(0, 0, CW, CH); privatePixels = false; }
  function placeholder() {
    ctx.fillStyle = '#43334e';
    for (let y = 8; y < CH - 22; y += 22) ctx.fillRect((y * 3 + style * 29) % 96, y, CW - 96, 9);
  }
  function paint(picture, time, { paused = false, allowed = true } = {}) {
    if (disposed) return false;
    const changed = picture !== last;
    if (!changed && (paused || !allowed || (time >= lastPaint && time - lastPaint < 1 / 15))) return false;
    // A newly permitted decoder can wait for its copy budget; revocation never waits.
    if (picture && !allowed) {
      if (changed) { blank(); last = null; texture.needsUpdate = true; }
      return false;
    }
    blank(); last = picture; lastPaint = time;
    if (picture) {
      const w = picture.videoWidth || picture.naturalWidth || picture.width;
      const h = picture.videoHeight || picture.naturalHeight || picture.height;
      if (w > 0 && h > 0) {
        const scale = Math.max(CW / w, (CH - 22) / h);
        try { ctx.drawImage(picture, (CW - w * scale) / 2, (CH - 22 - h * scale) / 2, w * scale, h * scale); privatePixels = true; }
        catch { blank(); }
      }
    }
    if (!privatePixels) {
      if (atlas?.image) {
        const image = atlas.image, px = image.width / 3;
        try { ctx.drawImage(image, style % 3 * px, Math.floor(style / 3) * px, px, px, 0, 0, CW, CH); }
        catch { blank(); placeholder(); }
      } else placeholder();
    }
    ctx.fillStyle = '#171320'; ctx.fillRect(0, CH - 22, CW, 22);
    if (privatePixels) { ctx.fillStyle = '#ffffff'; ctx.font = '15px system-ui'; ctx.fillText('via Scrolller', 12, CH - 6); }
    texture.needsUpdate = true;
    return true;
  }
  let termKey = null;
  /** A terminal face: green monospace text on black with a block cursor. Never touches media. */
  function terminal(text, cursor) {
    const key = text + (cursor ? '|' : '');
    if (disposed || key === termKey) return false;
    termKey = key; last = null; blank();
    ctx.fillStyle = '#061309'; ctx.fillRect(0, 0, CW, CH);
    ctx.fillStyle = '#52e256'; ctx.font = '700 64px ui-monospace, Consolas, monospace'; ctx.textBaseline = 'middle';
    const line = '> ' + text;
    ctx.fillText(line, 28, CH / 2);
    if (cursor) { const w = ctx.measureText?.(line)?.width ?? line.length * 38; ctx.fillRect(34 + w, CH / 2 - 30, 34, 60); }
    texture.needsUpdate = true;
    return true;
  }
  blank(); paint(null, 0);
  return { texture, paint, terminal, get privatePixels() { return privatePixels; },
    clear() { blank(); last = null; termKey = null; lastPaint = -Infinity; texture.needsUpdate = true; },
    dispose() { if (disposed) return; blank(); disposed = true; last = null; canvas.width = canvas.height = 1; texture.dispose(); } };
}

/** All screens, links, straps and canvas textures exist before the scene's covered warmup. */
export function createMarquees(scene, track, { feel, photosafe, gentle: safety, source = () => null,
  make = () => document.createElement('canvas'), acquireAtlas = acquireLoomAtlas, visibility = globalThis.document } = {}) {
  const object = new Group(); object.name = 'song-marquees';
  const theme = wordTheme(track), box = new BoxGeometry(1, 1, 1), plane = new PlaneGeometry(1, 1);
  const cabinetGeometry = screenBodyGeometry(), bezelGeometry = screenBodyGeometry(true);
  const link = new TorusGeometry(1, .22, 5, 12), slots = [], materials = [];
  let atlas = null;
  try { atlas = acquireAtlas(); } catch { /* The shared Loom can be unavailable; use the procedural canvas. */ }
  const matrix = new Matrix4(), xAxis = new Vector3(), yAxis = new Vector3(), zAxis = new Vector3();
  const dummy = new Object3D(), from = new Vector3(), to = new Vector3(), delta = new Vector3(), up = new Vector3(0, 1, 0);
  let clock = 0, disposed = false, cursor = 0, copies = 0, drops = 0;
  const material = options => { const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, fog: false, ...options }); materials.push(m); return m; };
  // One ink and one rim for every slot (a pair per slot built 50 identical shader graphs at load); the fade
  // is read per drawn object from mesh.userData.fx.alpha. Metal, cloth and picture stay per slot.
  const ink = screenBodyMaterial(false, { color: theme.ink, transparent: true, depthWrite: false, objectAlpha: true });
  const rim = screenBodyMaterial(true, { color: theme.accent, transparent: true, depthWrite: false, objectAlpha: true });
  materials.push(ink, rim);
  for (let i = 0; i < CAPACITY; i++) {
    const root = new Group(), hanger = new Group(), screen = new Group(); root.add(hanger, screen); object.add(root);
    root.name = 'marquee-slot-' + i; root.visible = false;
    const metal = material({ color: '#2a2230' }), cloth = material({ color: theme.accent });
    const surface = createMarqueeSurface(make(), atlas?.texture, i);
    const picture = material({ map: surface.texture, side: DoubleSide });
    const backing = new Mesh(cabinetGeometry, ink), border = new Mesh(bezelGeometry, rim), face = new Mesh(plane, picture);
    screen.add(backing, border, face); backing.position.z = border.position.z = .525; face.position.z = .54;
    const chain = new InstancedMesh(link, metal, LINKS * 2), straps = new InstancedMesh(box, cloth, LINKS * 2);
    chain.frustumCulled = straps.frustumCulled = false; hanger.add(chain, straps);
    const anchors = [-1, 1].map(() => { const m = new Mesh(box, ink); hanger.add(m); return m; });
    const fade = objectState([backing, border, ...anchors], { alpha: 0 });
    slots.push({ root, screen, backing, border, face, chain, straps, anchors, surface, fade, mats: [metal, cloth, picture],
      id: null, pick: 0, row: null, side: 0, bornS: 0, soft: false, alpha: 0 });
  }
  scene.add(object);
  function clear() {
    for (const s of slots) { s.root.visible = false; s.id = null; s.row = null; s.surface.clear(); }
    clock = 0; cursor = 0;
  }
  let beat = null;
  function pose(s, row, time, gentle) {
    const p = marqueePose(row, time, gentle || s.soft, s.side, beat), f = row.frame;
    s.root.position.fromArray(f.p);
    matrix.makeBasis(xAxis.fromArray(f.b), yAxis.fromArray(f.n), zAxis.fromArray(f.t).negate());
    s.root.quaternion.setFromRotationMatrix(matrix);
    s.screen.position.set(p.x, p.y, 0); s.screen.rotation.set(0, p.yaw, p.roll);
    const depth = screenBodySize(p).depth;
    s.backing.scale.set(p.w, p.h, depth);
    s.border.scale.set(p.w, p.h, depth); s.face.scale.set(p.w, p.h, 1);
    s.screen.updateMatrix();
    const chains = row.params?.variant === 'chains', cables = row.params?.variant === 'cables';
    s.chain.visible = chains; s.straps.visible = !chains;
    const rise = Math.max(gentle ? .65 : .3, safety?.rise?.(.3) || 0);
    // The transport owns OUT, including longer host-configured Gentle fades.
    s.alpha = unit(row.alpha) * ease((time - s.bornS) / rise) * (s.soft ? .55 : 1);
    for (const m of s.mats) m.opacity = s.alpha;
    s.fade.alpha.value = s.alpha;
    for (let side = 0; side < 2; side++) {
      const sign = side ? 1 : -1;
      const anchorX = s.side ? p.x + sign * (p.w / 2 + 2) : sign * (f.w / 2 + 3);
      // In a side pair both anchors stay outside the road, including their mounts.
      const outsideX = s.side ? s.side * Math.max(f.w / 2 + 3, Math.abs(anchorX)) : anchorX;
      to.set(sign * p.w * .43, p.h / 2 + .15, 0).applyMatrix4(s.screen.matrix);
      // Chains rise out of sight to anchors beyond the road edge, so nothing ever stands on the road.
      from.set(outsideX, p.y + 70, -2);
      s.anchors[side].position.copy(from); s.anchors[side].scale.set(1.2, .8, 1.2);
      delta.subVectors(to, from); const length = delta.length(); delta.normalize();
      for (let j = 0; j < LINKS; j++) {
        const u = (j + .5) / LINKS, idx = side * LINKS + j;
        dummy.position.lerpVectors(from, to, u);
        dummy.position.z += Math.sin(u * Math.PI) * Math.sin(time * 10 - u * 6) * p.whip;
        dummy.quaternion.setFromUnitVectors(up, delta);
        if (chains) dummy.rotateY(j % 2 * Math.PI / 2);
        dummy.scale.set(chains ? .5 : cables ? .22 : row.params?.variant === 'satin' ? .65 : .4,
          length / LINKS * (chains ? .62 : 1.05), chains ? .5 : .14);
        if (p.snapped === side) dummy.scale.multiplyScalar(Math.max(0, 1 - p.away * 3));
        dummy.updateMatrix(); (chains ? s.chain : s.straps).setMatrixAt(idx, dummy.matrix);
      }
    }
    (chains ? s.chain : s.straps).instanceMatrix.needsUpdate = true;
  }
  const hide = () => { if (visibility?.hidden) clear(); };
  visibility?.addEventListener?.('visibilitychange', hide);
  function update(rows = [], dt = 0, { paused = false, gentle = safety?.on ?? !!feel?.get('gentle.on'), songS = clock, song = null } = {}) {
    beat = song?.bpm > 0 ? { bpm: song.bpm, downbeatS: song.downbeatS || 0 } : null;
    if (disposed) return;
    if (visibility?.hidden) { clear(); return; }
    if (!paused && Number.isFinite(songS)) clock = songS;
    const wanted = new Map();
    // A spot is only shown once it has settled on the road, so it never slides with the kart.
    for (const row of rows) if (row.kind === 'MARQUEE' && row.frame && row.locked && row.alpha > .001) {
      const sides = row.params?.count === 3 ? [-1, 0, 1] : row.params?.count === 2 ? [-1, 1] : [0];
      for (const side of sides) wanted.set(row.id + ':' + side, { row, side });
    }
    for (const s of slots) if (s.id && !wanted.has(s.id)) { s.root.visible = false; s.id = null; s.row = null; s.surface.clear(); }
    for (const [id, item] of wanted) {
      const s = slots.find(v => v.id === id) || slots.find(v => !v.id);
      if (!s) continue;
      // A screen shows as its drop begins (or from a seek's rebuild): it never waits visibly above the frame.
      if (!s.id) { s.id = id; s.pick = drops++; s.bornS = Math.max(Number.isFinite(item.row.bornS) ? item.row.bornS : clock, item.row.startS - FALL_S); s.soft = photosafe?.request('marquee') === false; }
      s.row = item.row; s.side = item.side; s.root.visible = true;
      if (!paused) pose(s, item.row, clock, gentle);
    }
    // Round-robin uploads stay bounded. Null sources are processed even when the budget or clock stops.
    let budget = 2;
    for (let j = 0; j < slots.length; j++) {
      const i = (cursor + j) % slots.length, s = slots[i]; if (!s.id) continue;
      const term = s.row?.params?.terminal;
      if (term) {
        // Typed one letter per 0.11 s from the cue; the cursor blinks on the beat.
        const typed = String(s.row.params.text || '').slice(0, Math.max(0, Math.floor((clock - s.row.startS) / .11)));
        s.surface.terminal(typed, Math.floor(clock * 2.2) % 2 === 0); continue;
      }
      // Seeded per drop, not per slot: a redrop reuses the freed slot 0 and would repeat the same picture.
      const picture = source(s.pick);
      if (s.surface.paint(picture, clock, { paused, allowed: budget > 0 })) { copies++; if (picture) budget--; }
    }
    if (!paused) cursor = (cursor + 1) % slots.length;
  }
  return { object, update, clear,
    get stats() { return { capacity: CAPACITY, active: slots.filter(s => s.root.visible).length, copies, clock,
      privateSurfaces: slots.filter(s => s.surface.privatePixels).length }; },
    dispose() {
      if (disposed) return; clear(); disposed = true; object.removeFromParent();
      visibility?.removeEventListener?.('visibilitychange', hide);
      for (const s of slots) { s.chain.dispose(); s.straps.dispose(); s.surface.dispose(); }
      materials.forEach(m => m.dispose()); box.dispose(); plane.dispose(); cabinetGeometry.dispose(); bezelGeometry.dispose(); link.dispose(); atlas?.dispose();
    } };
}
