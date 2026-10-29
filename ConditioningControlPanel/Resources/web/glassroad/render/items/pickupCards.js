// Collectible cards: shared artwork, four borrowed moving-picture faces and no media ownership.
// Playtest 2026-10-28: each card rides inside a chartreuse glass bubble with a "?" stamp on both faces, so an
// item box reads as an item box at 40 m and never as a scenery screen; a picture is only a small inset.
import { Group, Mesh, Shape, ShapeGeometry, ExtrudeGeometry, CanvasTexture, SRGBColorSpace, MeshBasicNodeMaterial } from 'three/webgpu';
import { boxBubbleCloud } from './materials.js';
import { PICKUP_CARD, PICKUP_BOX, pickupFaceDue, pickupBoxPulse } from './pickupCardMath.js';
export { PICKUP_CARD, PICKUP_BOX, pickupBoxPose } from './pickupCardMath.js';

const W = 256, H = 360;

function roundedPath() {
  const x = -PICKUP_CARD.width / 2, y = 0, w = PICKUP_CARD.width, h = PICKUP_CARD.height, r = .13;
  const s = new Shape();
  s.moveTo(x + r, y); s.lineTo(x + w - r, y); s.quadraticCurveTo(x + w, y, x + w, y + r);
  s.lineTo(x + w, y + h - r); s.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
  s.lineTo(x + r, y + h); s.quadraticCurveTo(x, y + h, x, y + h - r);
  s.lineTo(x, y + r); s.quadraticCurveTo(x, y, x + r, y); return s;
}
export function pickupCardEdgeGeometry() {
  const geometry = new ExtrudeGeometry(roundedPath(), { depth: .065, bevelEnabled: true, bevelSize: .012, bevelThickness: .012, bevelSegments: 1, steps: 1, curveSegments: 5 });
  geometry.translate(0, 0, -.0325);
  // Artwork owns each face. Hidden extrusion caps competed with it at far-camera depth precision.
  const walls = geometry.groups.find(group => group.materialIndex === 1);
  geometry.setDrawRange(walls.start, walls.count); geometry.clearGroups();
  return geometry;
}
function cover(g, picture, x, y, w, h) {
  const iw = picture.videoWidth || picture.naturalWidth || picture.width, ih = picture.videoHeight || picture.naturalHeight || picture.height;
  if (!(iw > 0 && ih > 0)) return false;
  const k = Math.max(w / iw, h / ih);
  g.drawImage(picture, (iw - w / k) / 2, (ih - h / k) / 2, w / k, h / k, x, y, w, h); return true;
}
function rounded(g, x, y, w, h, r) {
  g.beginPath(); g.moveTo(x + r, y); g.lineTo(x + w - r, y); g.quadraticCurveTo(x + w, y, x + w, y + r);
  g.lineTo(x + w, y + h - r); g.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
  g.lineTo(x + r, y + h); g.quadraticCurveTo(x, y + h, x, y + h - r);
  g.lineTo(x, y + r); g.quadraticCurveTo(x, y, x + r, y); g.closePath();
}
/** The item mark: a fat "?" with a thick ink outline, readable from far down the road. */
function stamp(g, cx, cy, size, fill) {
  g.save(); g.font = `900 ${size}px system-ui, "Segoe UI", sans-serif`; g.textAlign = 'center'; g.textBaseline = 'middle';
  g.lineJoin = 'round'; g.lineWidth = size * .14; g.strokeStyle = PICKUP_BOX.ink; g.strokeText('?', cx, cy);
  g.fillStyle = fill; g.fillText('?', cx, cy); g.restore();
}
function frame(g) {
  g.strokeStyle = PICKUP_BOX.ink; g.lineWidth = 12; rounded(g, 6, 6, W - 12, H - 12, 18); g.stroke();
  g.strokeStyle = '#ffffff'; g.lineWidth = 3; rounded(g, 17, 17, W - 34, H - 34, 11); g.stroke();
}
function sheen(g, t) {
  // A slow travelling sheen, never a flashing light.
  const x = -W + (t * .09 % 1) * W * 3, s = g.createLinearGradient(x, 0, x + 70, H);
  s.addColorStop(0, '#ffffff00'); s.addColorStop(.5, '#ffffff30'); s.addColorStop(1, '#ffffff00');
  g.fillStyle = s; g.fillRect(0, 0, W, H);
}

export function createPickupCards({ source = () => null, doc = document, bubbleCap = 64 } = {}) {
  const make = () => Object.assign(doc.createElement('canvas'), { width: W, height: H });
  const backCanvas = make(), bg = backCanvas.getContext('2d'), backTex = new CanvasTexture(backCanvas);
  backTex.colorSpace = SRGBColorSpace;
  const back = new Image(); back.src = new URL('../../art/ui/pickup-card-back.svg', import.meta.url).href;
  let disposed = false, backDirty = true, clock = 0, nextSource = source;
  back.onload = () => { if (!disposed) backDirty = true; };
  const fronts = Array.from({ length: PICKUP_CARD.faces }, () => {
    const canvas = make(), ctx = canvas.getContext('2d'), tex = new CanvasTexture(canvas);
    tex.colorSpace = SRGBColorSpace;
    return { canvas, ctx, tex, material: new MeshBasicNodeMaterial({ map: tex, toneMapped: false }), previous: undefined, elapsed: 0, active: false };
  });
  const faceGeo = new ShapeGeometry(roundedPath(), 5), pos = faceGeo.attributes.position, uv = faceGeo.attributes.uv;
  for (let i = 0; i < pos.count; i++) uv.setXY(i, pos.getX(i) / PICKUP_CARD.width + .5, pos.getY(i) / PICKUP_CARD.height);
  const edgeGeo = pickupCardEdgeGeometry();
  const edgeMat = new MeshBasicNodeMaterial({ color: PICKUP_BOX.colour, toneMapped: false });
  const backMat = new MeshBasicNodeMaterial({ map: backTex, toneMapped: false });
  const bubbles = boxBubbleCloud(bubbleCap);

  function paint(entry, picture, index, gentle) {
    const g = entry.ctx;
    const field = g.createRadialGradient(W / 2, H * .4, 10, W / 2, H * .45, H * .62);
    field.addColorStop(0, PICKUP_BOX.light); field.addColorStop(1, PICKUP_BOX.colour);
    g.fillStyle = field; g.fillRect(0, 0, W, H);
    let credited = false;
    if (picture) {
      // a small inset only: the "?" owns the card
      const x = W / 2 - 62, y = 246, w = 124, h = 68;
      g.save(); rounded(g, x, y, w, h, 8); g.clip();
      try { credited = cover(g, picture, x, y, w, h); } catch { /* provider retired */ }
      g.restore();
      if (credited) {
        g.strokeStyle = PICKUP_BOX.ink; g.lineWidth = 4; rounded(g, x, y, w, h, 8); g.stroke();
        g.fillStyle = PICKUP_BOX.ink; g.textAlign = 'center'; g.font = 'bold 14px system-ui';
        g.fillText('via Scrolller', W / 2, 334);
      }
    }
    stamp(g, W / 2, credited ? 134 : H / 2 + 6, credited ? 196 : 250, '#ffffff');
    frame(g); sheen(g, gentle ? 0 : clock + index * 2); entry.tex.needsUpdate = true;
  }
  function update(dt = 0, gentle = false, still = gentle) {
    clock += Math.max(0, dt) * (gentle ? .5 : 1);
    bubbles.uniforms.pulse.value = pickupBoxPulse(clock, still);
    if (backDirty) {
      bg.fillStyle = '#271a30'; bg.fillRect(0, 0, W, H);
      if (back.complete && back.naturalWidth) bg.drawImage(back, 0, 0, W, H);
      // the owner's Drop Duel back keeps its quilt and frame; the item mark sits over the medallion
      stamp(bg, W / 2, H / 2 + 6, 230, PICKUP_BOX.colour);
      bg.strokeStyle = PICKUP_BOX.colour; bg.lineWidth = 8; rounded(bg, 4, 4, W - 8, H - 8, 18); bg.stroke();
      backTex.needsUpdate = true; backDirty = false;
    }
    for (const [i, entry] of fronts.entries()) {
      const picture = nextSource(i + Math.floor(clock / 8) * PICKUP_CARD.faces) || null;
      entry.elapsed += Math.max(0, dt);
      // Even an offscreen face drops its old pixels on replacement/expiry, including on pause.
      const changed = entry.previous !== picture;
      if ((entry.active || changed) && pickupFaceDue(entry.previous, picture, dt, entry.elapsed, entry.previous === undefined)) {
        paint(entry, picture, i, gentle); entry.previous = picture; entry.elapsed = 0;
      }
      entry.active = false;
    }
  }
  update();
  return {
    bubbles,
    build(index) {
      const root = new Group(), front = new Mesh(faceGeo, fronts[index % fronts.length].material), rear = new Mesh(faceGeo, backMat);
      front.position.z = .05; rear.rotation.y = Math.PI; rear.position.z = -.05;
      root.add(new Mesh(edgeGeo, edgeMat), front, rear); root.userData.pickupFace = index % fronts.length;
      // perf: the cards keep frustum culling (36 cards x 3 meshes were drawn every frame wherever they stood);
      // the warm still links them, it turns culling off for its own frames (render/renderer.js)
      return root;
    },
    use(index) { fronts[index % fronts.length].active = true; },
    update,
    setMediaSource(fn) { nextSource = typeof fn === 'function' ? fn : () => null; },
    dispose() {
      disposed = true; nextSource = () => null; back.onload = null; back.src = '';
      for (const e of fronts) { e.ctx.clearRect(0, 0, W, H); e.previous = null; e.tex.dispose(); e.material.dispose(); }
      bg.clearRect(0, 0, W, H); backTex.dispose(); backMat.dispose(); edgeMat.dispose(); faceGeo.dispose(); edgeGeo.dispose();
      bubbles.dispose();
    },
  };
}
