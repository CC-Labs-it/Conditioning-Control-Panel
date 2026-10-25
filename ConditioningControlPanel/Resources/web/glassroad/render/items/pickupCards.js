// Collectible cards: shared artwork, four borrowed moving-picture faces and no media ownership.
import { Group, Mesh, Shape, ShapeGeometry, ExtrudeGeometry, CanvasTexture, SRGBColorSpace, MeshBasicNodeMaterial } from 'three/webgpu';
import { createPlaceholder } from '../mediaPlaceholders.js';
import { PICKUP_CARD, pickupFaceDue } from './pickupCardMath.js';
export { PICKUP_CARD } from './pickupCardMath.js';

const W = 256, H = 360;
const COLOURS = [[1, .25, .65], [.35, .9, 1], [1, .8, .25], [.65, .4, 1], [1, 1, 1], [.2, .12, .3]];

function roundedPath() {
  const x = -PICKUP_CARD.width / 2, y = 0, w = PICKUP_CARD.width, h = PICKUP_CARD.height, r = .13;
  const s = new Shape();
  s.moveTo(x + r, y); s.lineTo(x + w - r, y); s.quadraticCurveTo(x + w, y, x + w, y + r);
  s.lineTo(x + w, y + h - r); s.quadraticCurveTo(x + w, y + h, x + w - r, y + h);
  s.lineTo(x + r, y + h); s.quadraticCurveTo(x, y + h, x, y + h - r);
  s.lineTo(x, y + r); s.quadraticCurveTo(x, y, x + r, y); return s;
}
function cover(g, picture, x, y, w, h) {
  const iw = picture.videoWidth || picture.naturalWidth || picture.width, ih = picture.videoHeight || picture.naturalHeight || picture.height;
  if (!(iw > 0 && ih > 0)) return false;
  const k = Math.max(w / iw, h / ih);
  g.drawImage(picture, (iw - w / k) / 2, (ih - h / k) / 2, w / k, h / k, x, y, w, h); return true;
}
function foil(g, t) {
  const gradient = g.createLinearGradient(0, 0, W, H);
  ['#fff1b2', '#ed68b2', '#87e8ef', '#fff5d0', '#ac88e5', '#fff1b2'].forEach((c, i) => gradient.addColorStop(i / 5, c));
  g.strokeStyle = gradient; g.lineWidth = 9; g.strokeRect(5, 5, W - 10, H - 10);
  g.strokeStyle = '#fff3d9'; g.lineWidth = 1.5; g.strokeRect(15, 15, W - 30, H - 30);
  for (const [x, y] of [[24, 24], [W - 24, 24], [24, H - 24], [W - 24, H - 24]]) {
    g.save(); g.translate(x, y); g.rotate(Math.PI / 4); g.fillStyle = '#fff0c7'; g.fillRect(-5, -5, 10, 10); g.restore();
  }
  // A slow travelling foil sheen, never a flashing light.
  const x = -W + (t * .09 % 1) * W * 3, sheen = g.createLinearGradient(x, 0, x + 70, H);
  sheen.addColorStop(0, '#ffffff00'); sheen.addColorStop(.5, '#ffffff28'); sheen.addColorStop(1, '#ffffff00');
  g.fillStyle = sheen; g.fillRect(0, 0, W, H);
}

export function createPickupCards({ source = () => null, doc = document } = {}) {
  const make = () => Object.assign(doc.createElement('canvas'), { width: W, height: H });
  const backCanvas = make(), bg = backCanvas.getContext('2d'), backTex = new CanvasTexture(backCanvas);
  backTex.colorSpace = SRGBColorSpace;
  const back = new Image(); back.src = new URL('../../art/ui/pickup-card-back.svg', import.meta.url).href;
  let disposed = false, backDirty = true, clock = 0, nextSource = source;
  back.onload = () => { if (!disposed) backDirty = true; };
  const placeholder = createPlaceholder(0, 256, COLOURS); placeholder.draw(0);
  const fronts = Array.from({ length: PICKUP_CARD.faces }, () => {
    const canvas = make(), ctx = canvas.getContext('2d'), tex = new CanvasTexture(canvas);
    tex.colorSpace = SRGBColorSpace;
    return { canvas, ctx, tex, material: new MeshBasicNodeMaterial({ map: tex, toneMapped: false }), previous: undefined, elapsed: 0, active: false };
  });
  const faceGeo = new ShapeGeometry(roundedPath(), 5), pos = faceGeo.attributes.position, uv = faceGeo.attributes.uv;
  for (let i = 0; i < pos.count; i++) uv.setXY(i, pos.getX(i) / PICKUP_CARD.width + .5, pos.getY(i) / PICKUP_CARD.height);
  const edgeGeo = new ExtrudeGeometry(roundedPath(), { depth: .065, bevelEnabled: true, bevelSize: .012, bevelThickness: .012, bevelSegments: 1, steps: 1, curveSegments: 5 });
  edgeGeo.translate(0, 0, -.0325);
  const edgeMat = new MeshBasicNodeMaterial({ color: '#f7d7a1' });
  const backMat = new MeshBasicNodeMaterial({ map: backTex, toneMapped: false });

  function paint(entry, picture, index, gentle) {
    const g = entry.ctx;
    g.fillStyle = '#201225'; g.fillRect(0, 0, W, H);
    g.save(); g.beginPath(); g.rect(19, 19, W - 38, H - 67); g.clip();
    let credited = false;
    if (picture) { try { credited = cover(g, picture, 19, 19, W - 38, H - 67); } catch { /* provider retired */ } }
    if (!credited) {
      g.translate(W / 2, (H - 29) / 2); g.rotate(gentle ? 0 : clock * .18 + index);
      g.drawImage(placeholder.canvas, -230, -230, 460, 460);
    }
    g.restore();
    g.fillStyle = '#211424'; g.fillRect(19, H - 48, W - 38, 29);
    g.fillStyle = '#fff3db'; g.textAlign = 'center'; g.font = 'bold 17px system-ui';
    g.fillText(credited ? 'via Scrolller' : 'MYSTERY', W / 2, H - 27);
    foil(g, gentle ? 0 : clock + index * 2); entry.tex.needsUpdate = true;
  }
  function update(dt = 0, gentle = false) {
    clock += Math.max(0, dt) * (gentle ? .5 : 1);
    if (backDirty) {
      bg.fillStyle = '#271a30'; bg.fillRect(0, 0, W, H);
      if (back.complete && back.naturalWidth) bg.drawImage(back, 0, 0, W, H);
      foil(bg, 0); backTex.needsUpdate = true; backDirty = false;
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
    build(index) {
      const root = new Group(), front = new Mesh(faceGeo, fronts[index % fronts.length].material), rear = new Mesh(faceGeo, backMat);
      front.position.z = .05; rear.rotation.y = Math.PI; rear.position.z = -.05;
      root.add(new Mesh(edgeGeo, edgeMat), front, rear); root.userData.pickupFace = index % fronts.length;
      root.traverse(o => { if (o.isMesh) o.frustumCulled = false; }); return root;
    },
    use(index) { fronts[index % fronts.length].active = true; },
    update,
    setMediaSource(fn) { nextSource = typeof fn === 'function' ? fn : () => null; },
    dispose() {
      disposed = true; nextSource = () => null; back.onload = null; back.src = '';
      for (const e of fronts) { e.ctx.clearRect(0, 0, W, H); e.previous = null; e.tex.dispose(); e.material.dispose(); }
      bg.clearRect(0, 0, W, H); backTex.dispose(); backMat.dispose(); edgeMat.dispose(); faceGeo.dispose(); edgeGeo.dispose();
    },
  };
}
