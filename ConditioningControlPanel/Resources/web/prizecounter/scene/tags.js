/* tags.js - prices on Astra's blank price tags (lane B3-W4). Each selling row's tag gets a thin plane on its card face
 * with the price and a small Sparkles mark drawn into a CanvasTexture. Owned rows keep the OWNED stamp prop, so they
 * never get a plane. One material per look (price + ink), shared by every tag that shows it.
 *
 *   const tags = createTags(T); tags.attach(tagProp, row); tags.setWallet(sp, tier); tags.drop(root); tags.dispose(); */

const INK = '#23202b', RED = '#a3262f', GOLD = '#c99a2e', PAPER = '#ece6d8', PINK = '#e0457b';

/** What a row's tag shows, or null (owned or not on sale: no price plane). Pure. */
export function tagFace(row, sp = Infinity, tier = 0) {
  if (!row || row.owned || row.sale !== 'on') return null;
  const prime = row.needs === 'prime' && tier < 2;
  return { text: String(row.priceSp), ink: prime ? 'prime' : row.priceSp > sp ? 'short' : 'ink' };
}

function star(g, cx, cy, r, fill) {
  g.beginPath();
  for (let i = 0; i < 8; i++) {
    const a = (i / 8) * Math.PI * 2 - Math.PI / 2, k = i % 2 ? 0.34 : 1;
    g.lineTo(cx + Math.cos(a) * r * k, cy + Math.sin(a) * r * k);
  }
  g.closePath(); g.fillStyle = fill; g.fill();
}

function paint(canvas, face) {
  const g = canvas.getContext('2d'), w = canvas.width, h = canvas.height;
  g.clearRect(0, 0, w, h);
  if (face.ink === 'prime') { g.fillStyle = INK; g.beginPath(); g.roundRect(6, 10, w - 12, h - 20, 18); g.fill(); }
  const color = face.ink === 'prime' ? PAPER : face.ink === 'short' ? RED : INK;
  g.font = '700 84px "Special Elite", "Courier New", monospace';
  g.textBaseline = 'middle';
  const tw = g.measureText(face.text).width, x = (w - (tw + 62)) / 2;
  star(g, x + 24, h / 2, 26, face.ink === 'prime' ? PINK : GOLD);
  g.fillStyle = color;
  g.fillText(face.text, x + 62, h / 2 + 4);
}

export function createTags(T) {
  const looks = new Map(), live = new Set();
  let sp = Infinity, tier = 0;
  const geo = new T.PlaneGeometry(0.27, 0.084);
  function material(face) {
    const key = `${face.text}|${face.ink}`;
    if (!looks.has(key)) {
      const canvas = document.createElement('canvas');
      canvas.width = 384; canvas.height = 120;
      paint(canvas, face);
      const map = new T.CanvasTexture(canvas);
      map.colorSpace = T.SRGBColorSpace;
      map.anisotropy = 4;
      looks.set(key, new T.MeshBasicMaterial({ map, transparent: true, toneMapped: false, depthWrite: false }));
    }
    return looks.get(key);
  }
  const apply = (tag) => { const f = tagFace(tag.row, sp, tier); tag.mesh.visible = !!f; if (f) tag.mesh.material = material(f); };
  return {
    attach(prop, row) {
      if (!tagFace(row)) return;
      const card = prop.getObjectByName('tag_card');
      const mesh = new T.Mesh(geo);
      mesh.position.set(0, card ? card.position.y : 0.048, 0.0085);
      mesh.renderOrder = 1;
      prop.add(mesh);
      const tag = { mesh, row };
      live.add(tag); apply(tag);
    },
    setWallet(nextSp, nextTier) { sp = Number.isFinite(nextSp) ? nextSp : Infinity; tier = nextTier | 0; live.forEach(apply); },
    /** Forget every tag under root (a shelf leaving, a tag swapped for the stamp). */
    drop(root) { for (const tag of live) { let o = tag.mesh; while (o && o !== root) o = o.parent; if (o) live.delete(tag); } },
    dispose() { looks.forEach((m) => { m.map.dispose(); m.dispose(); }); looks.clear(); live.clear(); geo.dispose(); },
  };
}
