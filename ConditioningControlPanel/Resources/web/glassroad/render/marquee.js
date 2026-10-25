import { Group, Mesh, InstancedMesh, Object3D, BoxGeometry, PlaneGeometry, TorusGeometry,
  CanvasTexture, MeshBasicNodeMaterial, Matrix4, Vector3, DoubleSide, SRGBColorSpace } from 'three/webgpu';
import { acquireLoomAtlas } from './loomAtlas.js';
import { wordTheme } from './wordTheme.js';

const CAPACITY = 6, LINKS = 18, CW = 512, CH = 288;
const unit = x => Math.max(0, Math.min(1, Number(x) || 0));
const ease = x => { const u = unit(x); return u * u * (3 - 2 * u); };

// Song-relative motion only. The camera and physical road never participate.
export function marqueePose(row, songS, gentle = false, side = 0) {
  const w = Math.max(12, Math.min(30, (row.frame?.w || 24) * .78)), h = w * .5625;
  const age = songS - row.startS, exit = Math.max(0, songS - row.endS);
  const fall = 32 * (1 - unit((age + 1.1) / 1.1) ** 2);
  const bounce = gentle || age < 0 || age >= .72 ? 0 : Math.abs(Math.sin(age / .36 * Math.PI)) * 2.2 * (1 - age / .72);
  let jolt = 0;
  if (!gentle) for (const at of row.joltTimes || []) {
    const d = songS - at;
    if (d >= 0 && d < .65) jolt += Math.sin(d * Math.PI * 4 / .65) * (1 - d / .65) * .035;
  }
  const swing = gentle || age < 0 ? 0 : Math.sin(age * 1.3 + side) * .045 + jolt;
  const snap = row.params?.exit === 'snap', away = ease(exit / (gentle ? 2.4 : 1.6));
  const direction = side || 1, roll = swing + (!gentle && snap ? direction * away * 1.1 : 0);
  const x = side * ((row.frame?.w || 24) / 2 + w / 2 + 1.5) + (snap ? direction * away * (w + 12) : 0);
  // Rotation cannot lower any corner into the seven-metre road clearance.
  const extent = Math.abs(Math.cos(roll)) * (h + .8) / 2 + Math.abs(Math.sin(roll)) * (w + .8) / 2;
  const y = Math.max(7.2 + extent, 8.4 + h / 2 + fall + bounce + away * 38);
  return { x, y, w, h, roll, yaw: gentle ? 0 : Math.sin(Math.max(0, age) * .7) * .055,
    whip: gentle ? 0 : bounce * .4 + Math.abs(jolt) * 14, away, bottom: y - extent };
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
  blank(); paint(null, 0);
  return { texture, paint, get privatePixels() { return privatePixels; },
    clear() { blank(); last = null; lastPaint = -Infinity; texture.needsUpdate = true; },
    dispose() { if (disposed) return; blank(); disposed = true; last = null; canvas.width = canvas.height = 1; texture.dispose(); } };
}

/** All screens, links, straps and canvas textures exist before the scene's covered warmup. */
export function createMarquees(scene, track, { feel, photosafe, gentle: safety, source = () => null,
  make = () => document.createElement('canvas'), acquireAtlas = acquireLoomAtlas, visibility = globalThis.document } = {}) {
  const object = new Group(); object.name = 'song-marquees';
  const theme = wordTheme(track), box = new BoxGeometry(1, 1, 1), plane = new PlaneGeometry(1, 1);
  const link = new TorusGeometry(1, .22, 5, 12), slots = [], materials = [];
  let atlas = null;
  try { atlas = acquireAtlas(); } catch { /* The shared Loom can be unavailable; use the procedural canvas. */ }
  const matrix = new Matrix4(), xAxis = new Vector3(), yAxis = new Vector3(), zAxis = new Vector3();
  const dummy = new Object3D(), from = new Vector3(), to = new Vector3(), delta = new Vector3(), up = new Vector3(0, 1, 0);
  let clock = 0, disposed = false, cursor = 0, copies = 0;
  const material = options => { const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, ...options }); materials.push(m); return m; };
  for (let i = 0; i < CAPACITY; i++) {
    const root = new Group(), hanger = new Group(), screen = new Group(); root.add(hanger, screen); object.add(root);
    root.name = 'marquee-slot-' + i; root.visible = false;
    const ink = material({ color: theme.ink }), rim = material({ color: theme.accent });
    const metal = material({ color: '#a7a4ad' }), cloth = material({ color: theme.accent });
    const surface = createMarqueeSurface(make(), atlas?.texture, i);
    const picture = material({ map: surface.texture, side: DoubleSide });
    const backing = new Mesh(box, ink), border = new Mesh(box, rim), face = new Mesh(plane, picture);
    screen.add(backing, border, face); border.position.z = .08; face.position.z = .54;
    const chain = new InstancedMesh(link, metal, LINKS * 2), straps = new InstancedMesh(box, cloth, LINKS * 2);
    chain.frustumCulled = straps.frustumCulled = false; hanger.add(chain, straps);
    const anchors = [-1, 1].map(() => { const m = new Mesh(box, ink); hanger.add(m); return m; });
    slots.push({ root, screen, backing, border, face, chain, straps, anchors, surface, mats: [ink, rim, metal, cloth, picture],
      id: null, row: null, side: 0, bornS: 0, soft: false, alpha: 0 });
  }
  scene.add(object);
  function clear() {
    for (const s of slots) { s.root.visible = false; s.id = null; s.row = null; s.surface.clear(); }
    clock = 0; cursor = 0;
  }
  function pose(s, row, time, gentle) {
    const p = marqueePose(row, time, gentle || s.soft, s.side), f = row.frame;
    s.root.position.fromArray(f.p);
    matrix.makeBasis(xAxis.fromArray(f.b), yAxis.fromArray(f.n), zAxis.fromArray(f.t).negate());
    s.root.quaternion.setFromRotationMatrix(matrix);
    s.screen.position.set(p.x, p.y, 0); s.screen.rotation.set(0, p.yaw, p.roll);
    s.backing.scale.set(p.w + .8, p.h + .8, .75);
    s.border.scale.set(p.w + .4, p.h + .4, .88); s.face.scale.set(p.w, p.h, 1);
    s.screen.updateMatrix();
    const chains = row.params?.variant === 'chains', cables = row.params?.variant === 'cables';
    s.chain.visible = chains; s.straps.visible = !chains;
    const rise = Math.max(gentle ? .65 : .3, safety?.rise?.(.3) || 0);
    // The transport owns OUT, including longer host-configured Gentle fades.
    s.alpha = unit(row.alpha) * ease((time - s.bornS) / rise) * (s.soft ? .55 : 1);
    for (const m of s.mats) m.opacity = s.alpha;
    for (let side = 0; side < 2; side++) {
      const sign = side ? 1 : -1;
      const anchorX = s.side ? p.x + sign * (p.w / 2 + 2) : sign * (f.w / 2 + 3);
      // In a side pair both anchors stay outside the road, including their mounts.
      const outsideX = s.side ? s.side * Math.max(f.w / 2 + 3, Math.abs(anchorX)) : anchorX;
      from.set(outsideX, 64, -4); to.set(sign * p.w * .43, p.h / 2 + .15, 0).applyMatrix4(s.screen.matrix);
      s.anchors[side].position.copy(from); s.anchors[side].scale.set(1.2, .8, 1.2);
      delta.subVectors(to, from); const length = delta.length(); delta.normalize();
      for (let j = 0; j < LINKS; j++) {
        const u = (j + .5) / LINKS, idx = side * LINKS + j;
        dummy.position.lerpVectors(from, to, u);
        dummy.position.z += Math.sin(u * Math.PI) * Math.sin(time * 10 - u * 6) * p.whip;
        dummy.quaternion.setFromUnitVectors(up, delta);
        if (chains) dummy.rotateY(j % 2 * Math.PI / 2);
        dummy.scale.set(chains ? .22 : cables ? .15 : row.params?.variant === 'satin' ? .65 : .4,
          length / LINKS * (chains ? .61 : 1.05), chains ? .22 : .1);
        if (row.params?.exit === 'snap' && p.away > 0) dummy.scale.multiplyScalar(1 - p.away);
        dummy.updateMatrix(); (chains ? s.chain : s.straps).setMatrixAt(idx, dummy.matrix);
      }
    }
    (chains ? s.chain : s.straps).instanceMatrix.needsUpdate = true;
  }
  const hide = () => { if (visibility?.hidden) clear(); };
  visibility?.addEventListener?.('visibilitychange', hide);
  function update(rows = [], dt = 0, { paused = false, gentle = safety?.on ?? !!feel?.get('gentle.on'), songS = clock } = {}) {
    if (disposed) return;
    if (visibility?.hidden) { clear(); return; }
    if (!paused && Number.isFinite(songS)) clock = songS;
    const wanted = new Map();
    for (const row of rows) if (row.kind === 'MARQUEE' && row.frame && row.alpha > .001) {
      const sides = row.params?.count === 2 ? [-1, 1] : [0];
      for (const side of sides) wanted.set(row.id + ':' + side, { row, side });
    }
    for (const s of slots) if (s.id && !wanted.has(s.id)) { s.root.visible = false; s.id = null; s.row = null; s.surface.clear(); }
    for (const [id, item] of wanted) {
      const s = slots.find(v => v.id === id) || slots.find(v => !v.id);
      if (!s) continue;
      if (!s.id) { s.id = id; s.bornS = Number.isFinite(item.row.bornS) ? item.row.bornS : clock; s.soft = photosafe?.request('marquee') === false; }
      s.row = item.row; s.side = item.side; s.root.visible = true;
      if (!paused) pose(s, item.row, clock, gentle);
    }
    // Round-robin uploads stay bounded. Null sources are processed even when the budget or clock stops.
    let budget = 2;
    for (let j = 0; j < slots.length; j++) {
      const i = (cursor + j) % slots.length, s = slots[i]; if (!s.id) continue;
      const picture = source(i);
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
      materials.forEach(m => m.dispose()); box.dispose(); plane.dispose(); link.dispose(); atlas?.dispose();
    } };
}
