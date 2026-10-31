import { Group, Mesh, BoxGeometry, BufferGeometry, CircleGeometry, TorusGeometry, Shape, Vector2, ExtrudeGeometry, Float32BufferAttribute, Color } from 'three/webgpu';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { placeOnFrame, fadeSet, ease, easeIn, unit } from './cueKit.js';

// STOMP: a giant stiletto pump that stomps down beside the road on its beat, holds, and lifts away.
// PROP sole: a glass bridge over the road with a giant stylised foot pressing on it; the toes curl on a jolt.
// Both sit on fixed road spots from the cue transport and keep clear of the road surface and the karts.
const HEELS = 4, FEET = 2;
// The pump is drawn as a side profile (u toward the toe, v up, shoe units before the 2.6 scale) and extruded across its width,
// so the camera, which sees it side-on, reads the pointed toe, the arch and the needle heel from any distance.
const V = (u, v) => new Vector2(u, v), pts = a => a.map(([u, v]) => V(u, v));
const SOLE = [[7.25, .22], [6.75, .04], [5.65, 0], [2.95, 0], [1.75, .32], [.55, 1.25], [-.65, 2.55], [-1.85, 3.7], [-2.75, 4.3], [-3.9, 4.45]];   // the sole underside, toe to heel seat
const SOLE_T = .28, TOP = [[2.05, 2.55], [.65, 2.9], [-.95, 3.85], [-2.35, 5.25], [-3.3, 6.5], [-3.8, 7.0]];   // sole thickness; the open topline from the throat to the heel top
const SHELL = '#e0115f', LINING = '#4a0820', DARK = '#1a0710', TIP = '#0b0305', TRIM = '#ff9cc4', GLINT = '#fff2f8';
/** Width taper (1 = full width): a pointed toe and a slimmer heel cup, so it reads as a pump from oblique angles too. */
const taper = u => u > 3 ? 1 - .66 * ((u - 3) / 4.6) ** 1.5 : u < -2 ? 1 - .22 * unit((-2 - u) / 2.1) : 1;
/** Extrude a profile across the width, centred on x, the profile's u running toward local -z (the toe). */
function slab(shape, depth, bevel, colour, { x = null, taperX = taper, curve = 6, lining = null } = {}) {
  const geo = new ExtrudeGeometry(shape, { depth, curveSegments: curve, steps: 1, bevelEnabled: bevel > 0, bevelThickness: bevel, bevelSize: bevel * .75, bevelSegments: 3 });
  geo.rotateY(Math.PI / 2).translate(x === null ? -depth / 2 : x, 0, 0);
  // Bake a lamp into vertex colours (the materials are unlit): sides flat, top bevels catch a patent glint, undersides go dim.
  const p = geo.attributes.position, n = geo.attributes.normal, base = new Color(colour), white = new Color('#ffffff'), inside = new Color(lining || colour), c = new Color(), out = [];
  for (let i = 0; i < p.count; i++) {
    const ny = n.getY(i), gloss = unit((ny - .35) / .4) * (1 - unit((ny - .92) / .08));
    // The flat wall along the open topline is the inside of the shoe: a dark lining between two glossy rims, so it reads open from above.
    if (lining && Math.abs(n.getX(i)) < .05 && ny > .2 && -p.getZ(i) < 3.3) { c.copy(inside); out.push(c.r, c.g, c.b); if (taperX) p.setX(i, p.getX(i) * taperX(-p.getZ(i))); continue; }
    c.copy(base).multiplyScalar(.8 + .2 * Math.max(0, ny) - .25 * Math.max(0, -ny)).lerp(white, gloss * (Math.abs(n.getX(i)) < .05 ? .25 : .55)); out.push(c.r, c.g, c.b);
    if (taperX) p.setX(i, p.getX(i) * taperX(-p.getZ(i)));
  }
  geo.setAttribute('color', new Float32BufferAttribute(out, 3)); geo.deleteAttribute('normal'); geo.deleteAttribute('uv');
  return geo;
}
/** The whole pump as ONE geometry (one draw per heel): patent shell, dark sole, needle heel with its tip, topline binding and toe glints. */
function stilettoGeometry() {
  const W = 3, BV = .3, face = W / 2 + BV + .01, parts = [];
  const shell = new Shape(); shell.moveTo(7.55, .52);
  shell.splineThru(pts([[7.15, 1.1], [6.15, 1.7], [4.55, 2.2], [3.15, 2.75]]));             // the vamp over the toe box to the throat
  shell.splineThru(pts(TOP));                                                               // the open topline dipping then rising to the heel cup
  shell.splineThru(pts([[-4.05, 6.4], [-4.07, 5.5], [-3.95, 4.73]]));                       // the heel counter, rounded at the back
  shell.splineThru(pts(SOLE.slice(0, -1).reverse().map(([u, v]) => [u, v + SOLE_T])));        // along the sole top back to the toe
  shell.closePath(); parts.push(slab(shell, W, BV, SHELL, { lining: LINING }));
  const sole = new Shape(); sole.moveTo(...SOLE[0]); sole.splineThru(pts(SOLE.slice(1)));
  sole.lineTo(-3.95, 4.45 + SOLE_T); sole.splineThru(pts(SOLE.slice(0, -1).reverse().map(([u, v]) => [u, v + SOLE_T]))); sole.closePath();
  parts.push(slab(sole, W - .2, .25, DARK));
  // The needle: a concave back edge sweeping from the heel cup to a small tip, a straighter breast in front; slimmer toward the floor.
  const heel = new Shape(); heel.moveTo(-3.95, 4.85);
  heel.splineThru(pts([[-3.8, 3.6], [-3.5, 2.2], [-3.25, .9], [-3.17, .3]])); heel.lineTo(-2.9, .3);
  heel.splineThru(pts([[-2.87, 1.0], [-2.8, 2.0], [-2.6, 3.2], [-2.25, 4.1], [-1.95, 4.45]])); heel.lineTo(-2.6, 4.9); heel.closePath();
  parts.push(slab(heel, .5, .07, DARK, { taperX: null, curve: 4 }));
  for (let i = parts.at(-1).attributes.position.count, p = parts.at(-1).attributes.position; i--;) p.setX(i, p.getX(i) * (.45 + .55 * unit(p.getY(i) / 4.6)));
  const lift = new BoxGeometry(.42, .3, .34).toNonIndexed().translate(0, .15, 3.04); parts.push(slab0(lift, TIP));
  // Topline binding and the glints sit just proud of both side faces (the shoe is seen from either side of the road).
  const bind = new Shape(), T = [[3.15, 2.75], ...TOP]; bind.moveTo(...T[0]); bind.splineThru(pts(T.slice(1)));
  bind.lineTo(T.at(-1)[0] - .05, T.at(-1)[1] - .3); bind.splineThru(pts(T.slice(0, -1).reverse().map(([u, v]) => [u, v - .26]))); bind.closePath();
  const glint = new Shape(); glint.moveTo(7.0, .95); glint.quadraticCurveTo(5.6, 1.78, 3.4, 2.18); glint.quadraticCurveTo(5.4, 1.42, 7.0, .95);
  const cup = new Shape(); cup.moveTo(-3.45, 6.35); cup.quadraticCurveTo(-3.85, 5.6, -3.7, 4.95); cup.quadraticCurveTo(-3.62, 5.6, -3.45, 6.35);
  for (const s of [-1, 1]) for (const [shape, col] of [[bind, TRIM], [glint, GLINT], [cup, GLINT]]) parts.push(slab(shape, .05, 0, col, { x: s > 0 ? face : -face - .05 }));
  const geo = mergeGeometries(parts).translate(0, 0, -.12); for (const g of parts) g.dispose();   // nudged toward the toe: the heel cup keeps the old road clearance
  return geo;
}
/** A plain part (no profile): same baked lamp, no taper. */
function slab0(geo, colour) {
  const n = geo.attributes.normal, base = new Color(colour), c = new Color(), out = [];
  for (let i = 0; i < n.count; i++) { c.copy(base).multiplyScalar(.8 + .2 * Math.max(0, n.getY(i))); out.push(c.r, c.g, c.b); }
  geo.setAttribute('color', new Float32BufferAttribute(out, 3)); geo.deleteAttribute('normal'); geo.deleteAttribute('uv');
  return geo;
}
/** Smooth cross sections join the heel, raised instep and ball without intersecting blobs. */
function footLoft(sections, rings = 48, sides = 32, archLift = .55) {
  const positions = [], indices = [], count = sections.length - 1;
  const sample = (t, k) => {
    const i = Math.min(count - 1, Math.floor(t)), u = t - i;
    const a = sections[Math.max(0, i - 1)][k], b = sections[i][k];
    const c = sections[i + 1][k], d = sections[Math.min(count, i + 2)][k];
    return .5 * (2 * b + (c - a) * u + (2 * a - 5 * b + 4 * c - d) * u * u + (3 * b - a - 3 * c + d) * u * u * u);
  };
  for (let r = 0; r <= rings; r++) {
    const t = r / rings * count, z = sample(t, 0), w = Math.max(.002, sample(t, 1));
    const low = Math.max(.04, sample(t, 2)), high = Math.max(low + .002, sample(t, 3));
    for (let j = 0; j <= sides; j++) {
      const a = j / sides * Math.PI * 2, x = Math.cos(a) * w;
      // The inner edge lifts into an arch. Heel and ball retain their broad contact patches.
      const arch = Math.exp(-(((z - .2) / 1.9) ** 2)) * unit(-x / 2) * unit(-Math.sin(a));
      positions.push(x, low + (high - low) * (Math.sin(a) + 1) / 2 + arch * archLift, z);
      if (r < rings && j < sides) {
        const n = r * (sides + 1) + j, next = n + sides + 1;
        indices.push(n, next, n + 1, next, next + 1, n + 1);
      }
    }
  }
  const geometry = new BufferGeometry(); geometry.setAttribute('position', new Float32BufferAttribute(positions, 3));
  geometry.setIndex(indices); geometry.computeVertexNormals();
  return footFinish(geometry, '#efbcae', archLift > 0);
}
/** Soft broad light and warm sole colour keep their form in the unlit cue pass. */
function footFinish(geometry, colour, soleDetail = false) {
  const p = geometry.attributes.position, n = geometry.attributes.normal, colours = [];
  const base = new Color(colour), warmth = new Color('#d68a87'), light = new Color('#fff0df'), c = new Color();
  for (let i = 0; i < p.count; i++) {
    const ny = n.getY(i), lamp = unit(ny * .72 - n.getX(i) * .32 + n.getZ(i) * .2);
    c.copy(base).lerp(warmth, unit(-ny) * .3).multiplyScalar(.72 + lamp * .27);
    if (soleDetail) {
      const x = p.getX(i), z = p.getZ(i), underside = unit(-ny * 1.8);
      const heel = Math.exp(-((x / 1.8) ** 2 + ((z - 3.5) / 1.5) ** 2));
      const ball = Math.exp(-((x / 2.7) ** 2 + ((z + 2.7) / 1.05) ** 2));
      const crease = Math.exp(-(((z + 3.45 - .1 * x - .055 * x * x) / .17) ** 2));
      c.lerp(warmth, underside * (.16 * heel + .2 * ball));
      c.multiplyScalar(1 - underside * crease * .16);
    }
    c.lerp(light, Math.pow(lamp, 8) * .12); colours.push(c.r, c.g, c.b);
  }
  geometry.setAttribute('color', new Float32BufferAttribute(colours, 3));
  return geometry;
}
function footGeometry() {
  const body = footLoft([[5.75, .002, 1.6, 1.61], [5.1, 1.55, .38, 3.1], [3.55, 2.2, .12, 4.15],
    [1.9, 2.05, .35, 3.95], [.2, 1.85, .82, 3.3], [-1.7, 2.65, .25, 2.7],
    [-3.1, 2.95, .12, 2.4], [-4.1, 2.5, .35, 1.8], [-4.65, .002, .9, .91]]);
  const toe = footLoft([[.6, .002, .55, .56], [.28, .66, .08, 1.14], [-.55, .68, .04, 1.16],
    [-1.25, .6, .1, 1.07], [-1.8, .3, .24, .83], [-1.94, .002, .52, .53]], 20, 20, 0);
  const outline = new Shape(); outline.moveTo(-.3, -.48); outline.lineTo(.3, -.48);
  outline.quadraticCurveTo(.42, -.48, .42, -.34); outline.lineTo(.42, .34);
  outline.quadraticCurveTo(.42, .48, .25, .48); outline.lineTo(-.25, .48);
  outline.quadraticCurveTo(-.42, .48, -.42, .34); outline.lineTo(-.42, -.34);
  outline.quadraticCurveTo(-.42, -.48, -.3, -.48); outline.closePath();
  const nail = new ExtrudeGeometry(outline, { depth: .035, steps: 1, bevelEnabled: true,
    bevelThickness: .04, bevelSize: .04, bevelSegments: 2, curveSegments: 5 });
  nail.rotateX(-Math.PI / 2); footFinish(nail, '#c51d54');
  return { body, toe, nail };
}
function foot(set, geometry) {
  const g = new Group(), material = set.make({ color: '#ffffff', vertexColors: true, depthWrite: true });
  g.name = 'cue-foot';
  const body = new Mesh(geometry.body, material); body.name = 'cue-foot-body'; g.add(body);
  const toes = [];
  const layout = [[-2.05, -3.8, 1.15, 1.05], [-.65, -4.1, .92, 1], [.53, -4.02, .8, .91],
    [1.56, -3.73, .7, .8], [2.34, -3.26, .59, .67]];
  for (const [x, z, width, length] of layout) {
    const t = new Group(); t.name = 'cue-foot-toe'; t.position.set(x, .43, z); t.scale.set(width, width, length); g.add(t);
    const tip = new Mesh(geometry.toe, material); t.add(tip);
    const nail = new Mesh(geometry.nail, material); nail.name = 'cue-foot-nail'; nail.position.set(0, 1.1, -.92);
    nail.rotation.x = -.1; t.add(nail); toes.push(t);
  }
  g.userData.toes = toes;
  return g;
}

export function createCueGiants(scene) {
  const object = new Group(); object.name = 'cue-giants'; scene.add(object);
  const heels = [], feet = [];
  // One pump geometry, one wave ring and one ground shadow are shared by the whole pool; each heel keeps its own fading materials.
  const heelGeo = stilettoGeometry(), waveGeo = new TorusGeometry(1, .18, 6, 40), shadowGeo = new CircleGeometry(1, 40);
  const footGeo = footGeometry(), shared = [heelGeo, waveGeo, shadowGeo, ...Object.values(footGeo)];
  for (let i = 0; i < HEELS; i++) {
    const set = fadeSet(); set.owned = [];
    const root = new Group(), shoe = new Mesh(heelGeo, set.make({ color: '#ffffff', vertexColors: true, depthWrite: true }));
    const wave = new Mesh(waveGeo, set.make({ color: '#ff9cc4' })), shadow = new Mesh(shadowGeo, set.make({ color: '#1a0710' }));
    wave.rotation.x = Math.PI / 2; shadow.rotation.x = -Math.PI / 2; root.add(shadow, shoe, wave); root.visible = false; object.add(root);
    heels.push({ root, shoe, wave, shadow, set, id: null });
  }
  for (let i = 0; i < FEET; i++) {
    const set = fadeSet(); set.owned = [];
    const root = new Group(), glass = new Mesh(new BoxGeometry(1, 1, 1), set.make({ color: '#ffc6e4' })), sole = foot(set, footGeo);
    set.owned.push(glass.geometry); root.add(glass, sole); root.visible = false; object.add(root);
    feet.push({ root, glass, sole, set, glassMat: set.mats[0], id: null });
  }
  function claim(pool, row) { return pool.find(s => s.id === row.id) || pool.find(s => !s.id); }
  function update(rows = [], { songS = NaN, paused = false, gentle = false } = {}) {
    if (paused) return;
    const stomps = rows.filter(r => r.kind === 'STOMP' && r.frame && r.locked);
    const soles = rows.filter(r => r.kind === 'PROP' && r.params?.variant === 'sole' && r.frame && r.locked);
    for (const pool of [heels, feet]) for (const s of pool) if (s.id && !rows.some(r => r.id === s.id && r.locked)) { s.id = null; s.root.visible = false; }
    for (const row of stomps) {
      const s = claim(heels, row); if (!s) continue; s.id = row.id; s.root.visible = true;
      placeOnFrame(s.root, row.frame);
      const side = row.params?.side || 1, age = songS - row.startS;
      // Side-on to the road so the camera reads the silhouette: toe out, the stiletto nearest the road.
      s.root.translateX(side * (row.frame.w / 2 + 10));
      // Down fast, landing on the beat; a short squash; hold; lift away.
      const drop = age < 0 ? 34 * (1 - easeIn(unit((age + .32) / .32))) : 0;
      const lift = age > .9 ? 40 * ease((age - .9) / 1.1) : 0;
      const squash = gentle || age < 0 || age > .18 ? 0 : Math.sin(age / .18 * Math.PI) * .12;
      s.shoe.position.y = drop + lift; s.shoe.scale.set(2.6 * (1 + squash), 2.6 * (1 - squash), 2.6);
      s.shoe.rotation.y = -side * Math.PI / 2;
      const ring = unit(age / .7);
      s.wave.visible = age >= 0 && age < .7 && !gentle; s.wave.scale.setScalar(2 + ring * 16); s.wave.position.y = .2;
      // Its shadow gathers under it as it falls (the warning), holds while it stands, and thins as it lifts.
      const near = age < 0 ? ease((age + .9) / .9) : age > .9 ? 1 - ease((age - .9) / 1.1) : 1, spread = .45 + .55 * near;
      s.shadow.visible = near > 0; s.shadow.position.set(side * 4.5, .12, 0); s.shadow.scale.set(12 * spread, 4 * spread, 1);
      s.set.opacity(unit(row.alpha)); s.wave.material.opacity = unit(row.alpha) * (1 - ring) * .8; s.shadow.material.opacity = unit(row.alpha) * near * .55;
    }
    for (const row of soles) {
      const s = claim(feet, row); if (!s) continue; s.id = row.id; s.root.visible = true;
      placeOnFrame(s.root, row.frame);
      const w = row.frame.w + 10, side = row.params?.side || 0, age = songS - row.startS;
      s.glass.scale.set(w, .5, 14); s.glass.position.set(0, 9, 0); s.glassMat.opacity = unit(row.alpha) * .42;
      const press = age < 0 ? 18 * (1 - easeIn(unit((age + .45) / .45))) : 0;
      s.sole.position.set(side * row.frame.w * .2, 9.3 + press, side * 1.5); s.sole.scale.setScalar(1.35);
      // Present the toes and inner arch to the approaching driver rather than the heel alone.
      s.sole.rotation.y = Math.PI + .28 + side * .12;
      // Curl on each jolt: toes fold down then relax.
      let curl = 0;
      for (const t of row.joltTimes || []) { const d = songS - t; if (d >= 0 && d < .9) curl = Math.max(curl, Math.sin(unit(d / .9) * Math.PI)); }
      for (const t of s.sole.userData.toes) t.rotation.x = (gentle ? .4 : 1) * curl * .9;
      for (const m of s.set.mats) if (m !== s.glassMat) m.opacity = unit(row.alpha);
    }
  }
  return { object, update,
    clear() { for (const s of [...heels, ...feet]) { s.id = null; s.root.visible = false; } },
    get stats() { return { heels: heels.filter(s => s.id).length, feet: feet.filter(s => s.id).length }; },
    dispose() { object.removeFromParent(); for (const g of shared) g.dispose(); for (const s of [...heels, ...feet]) { s.set.dispose(); for (const g of new Set(s.set.owned)) g.dispose(); } } };
}
