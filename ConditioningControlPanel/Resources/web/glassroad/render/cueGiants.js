import { Group, Mesh, BoxGeometry, SphereGeometry, CylinderGeometry, TorusGeometry, Color } from 'three/webgpu';
import { placeOnFrame, fadeSet, ease, easeIn, unit } from './cueKit.js';

// STOMP: a giant stiletto that stomps down beside the road on its beat, holds, and lifts away.
// PROP sole: a glass bridge over the road with a giant stylised foot pressing on it; the toes curl on a jolt.
// Both sit on fixed road spots from the cue transport and keep clear of the road surface and the karts.
const HEELS = 4, FEET = 2;
function stiletto(set) {
  const g = new Group(), gloss = set.make({ color: '#e0115f' }), sole = set.make({ color: '#1a0710' }), shine = set.make({ color: '#ff9cc4' });
  const add = (geo, m, p, s, r = [0, 0, 0]) => { const x = new Mesh(geo, m); x.position.set(...p); x.scale.set(...s); x.rotation.set(...r); g.add(x); return x; };
  const sph = new SphereGeometry(1, 20, 14), box = new BoxGeometry(1, 1, 1), cyl = new CylinderGeometry(1, 1, 1, 14), ring = new TorusGeometry(1, .12, 8, 24);
  add(box, sole, [0, .25, -1], [3.2, .5, 8.6], [-.22, 0, 0]);            // the sole, sloping from heel to toe
  add(sph, gloss, [0, 1.3, -4.4], [1.75, 1.35, 2.6]);                   // toe box, low at the front
  add(box, gloss, [0, 2.6, -1.4], [3.1, 2.4, 4.2], [-.4, 0, 0]);         // the vamp rising to the instep
  add(sph, gloss, [0, 4.4, 2.6], [1.7, 2.2, 1.6]);                       // heel counter at the back
  add(ring, shine, [0, 5.9, 1.6], [1.6, 2.1, 1], [Math.PI / 2 - .25, 0, 0]);   // the ankle opening
  add(cyl, sole, [0, 1.6, 3.4], [.32, 4.2, .32]);                         // the stiletto, thin and tall
  add(box, sole, [0, -.3, 3.4], [.6, .5, .6]);                            // its tip
  for (const x of [sph, box, cyl, ring]) set.owned.push(x);
  return g;
}
function foot(set) {
  const g = new Group(), skin = set.make({ color: '#f6b9c8' }), sole = set.make({ color: '#e7899f' }), nail = set.make({ color: '#e0115f' });
  const sph = new SphereGeometry(1, 20, 14); set.owned.push(sph);
  const add = (m, p, s) => { const x = new Mesh(sph, m); x.position.set(...p); x.scale.set(...s); g.add(x); return x; };
  add(skin, [0, 2.2, 3.2], [2.4, 2.1, 2.6]);      // heel
  add(skin, [0, 1.8, -.6], [3.0, 1.6, 4.0]);      // arch to ball
  add(sole, [0, .35, 2.8], [2.2, .45, 2.3]);      // the pressed heel pad
  add(sole, [0, .35, -2.2], [2.9, .45, 2.0]);     // the pressed ball
  const toes = [];
  for (let i = 0; i < 5; i++) {
    const t = new Group(), r = 1.15 - i * .14; t.position.set(-2.2 + i * 1.1, 1.0, -4.6 + Math.abs(i - 1) * .35); g.add(t);
    const tip = new Mesh(sph, skin); tip.scale.set(r * .55, r * .5, r * .7); tip.position.set(0, 0, -r * .5); t.add(tip);
    const n = new Mesh(sph, nail); n.scale.set(r * .35, r * .12, r * .35); n.position.set(0, r * .45, -r * .7); t.add(n);
    toes.push(t);
  }
  g.userData.toes = toes;
  return g;
}

export function createCueGiants(scene) {
  const object = new Group(); object.name = 'cue-giants'; scene.add(object);
  const heels = [], feet = [];
  for (let i = 0; i < HEELS; i++) {
    const set = fadeSet(); set.owned = [];
    const root = new Group(), shoe = stiletto(set), wave = new Mesh(new TorusGeometry(1, .18, 6, 40), set.make({ color: '#ff9cc4' }));
    wave.rotation.x = Math.PI / 2; root.add(shoe, wave); root.visible = false; object.add(root); set.owned.push(wave.geometry);
    heels.push({ root, shoe, wave, set, id: null });
  }
  for (let i = 0; i < FEET; i++) {
    const set = fadeSet(); set.owned = [];
    const root = new Group(), glass = new Mesh(new BoxGeometry(1, 1, 1), set.make({ color: '#ffc6e4' })), sole = foot(set);
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
      s.set.opacity(unit(row.alpha)); s.wave.material.opacity = unit(row.alpha) * (1 - ring) * .8;
    }
    for (const row of soles) {
      const s = claim(feet, row); if (!s) continue; s.id = row.id; s.root.visible = true;
      placeOnFrame(s.root, row.frame);
      const w = row.frame.w + 10, side = row.params?.side || 0, age = songS - row.startS;
      s.glass.scale.set(w, .5, 14); s.glass.position.set(0, 9, 0); s.glassMat.opacity = unit(row.alpha) * .42;
      const press = age < 0 ? 18 * (1 - easeIn(unit((age + .45) / .45))) : 0;
      s.sole.position.set(side * row.frame.w * .2, 9.3 + press, side * 1.5); s.sole.scale.setScalar(1.35);
      s.sole.rotation.y = side * .12;
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
    dispose() { object.removeFromParent(); for (const s of [...heels, ...feet]) { s.set.dispose(); for (const g of new Set(s.set.owned)) g.dispose(); } } };
}
