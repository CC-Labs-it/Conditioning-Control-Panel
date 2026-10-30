import { Group, Mesh, BoxGeometry, PlaneGeometry, CylinderGeometry, SphereGeometry, TorusGeometry, CircleGeometry,
  Shape, ShapeGeometry, Vector3, Quaternion, Matrix4, Color, DoubleSide } from 'three/webgpu';
import { acquireLoomAtlas } from './loomAtlas.js';
import { wordTheme } from './wordTheme.js';
import { placeOnFrame, fadeSet, labelTexture, ease, easeOut, unit, pop, beatKick } from './cueKit.js';
import { S } from '../sim/state.js';
import { mirrorOpening } from './cameraViews.js';

// Course one-offs for the song cues (PROP). Spot props sit on the fixed road spot the transport gives them;
// zone props stream fixed world dressing around the player for their span (carousel poles, spotlights) or
// hang in the sky ahead like the sky does (star, spiral bloom). Nothing here collides or moves the camera.
const POLES = 12, POLE_GAP = 32, SPOTS = 12, SPOT_GAP = 26;
const frame = () => ({ p: [0, 0, 0], t: [0, 0, 1], n: [0, 1, 0], b: [1, 0, 0], w: 0 });
function starGeometry(points = 5, outer = 1, inner = .45) {
  const s = new Shape();
  for (let i = 0; i <= points * 2; i++) {
    const r = i % 2 ? inner : outer, a = Math.PI / 2 + i * Math.PI / points;
    i ? s.lineTo(Math.cos(a) * r, Math.sin(a) * r) : s.moveTo(Math.cos(a) * r, Math.sin(a) * r);
  }
  return new ShapeGeometry(s);
}

export function createCueProps(scene, track, { make = () => document.createElement('canvas'), acquireAtlas = acquireLoomAtlas,
  particles = null } = {}) {
  const object = new Group(); object.name = 'cue-props'; scene.add(object);
  const theme = wordTheme(track), set = fadeSet(), geos = [];
  const geo = g => (geos.push(g), g);
  const box = geo(new BoxGeometry(1, 1, 1)), plane = geo(new PlaneGeometry(1, 1)), sph = geo(new SphereGeometry(1, 16, 12));
  const cyl = geo(new CylinderGeometry(1, 1, 1, 16)), cone = geo(new CylinderGeometry(.6, 6, 1, 24, 1, true));
  let atlas = null; try { atlas = acquireAtlas(); } catch { /* the bloom falls back to the star colour */ }
  const f = frame(), v = new Vector3(), q = new Quaternion(), m4 = new Matrix4(), bx = new Vector3(), ny = new Vector3(), tz = new Vector3();
  const along = (s, from) => { const L = track.lengthM, d = ((s - from) % L + L) % L; return d > L / 2 ? d - L : d; };

  // --- mirror: a tall framed mirror rises from the road and parts down the middle as you reach it
  const mirror = new Group(); mirror.visible = false; object.add(mirror);
  const gold = set.make({ color: '#e8c36a' }), silverTex = labelTexture(make, 64, 256);
  silverTex.paint('silver', (c, w, h) => { const g = c.createLinearGradient(0, 0, w, h); g.addColorStop(0, '#fbe9f4'); g.addColorStop(.4, '#b9a6c4'); g.addColorStop(.52, '#ffffff'); g.addColorStop(.62, '#d9c4dc'); g.addColorStop(1, '#8a7896'); c.fillStyle = g; c.fillRect(0, 0, w, h);
    c.fillStyle = 'rgba(255,255,255,.55)'; for (let i = 0; i < 40; i++) c.fillRect((i * 37) % w, (i * 71) % h, 2, 2); });
  const glassMat = set.make({ map: silverTex.texture, side: DoubleSide }), glint = set.make({ color: '#ffffff' });
  const halves = [-1, 1].map(side => {
    const g = new Group(), pane = new Mesh(plane, glassMat), rim = new Mesh(box, gold), shine = new Mesh(plane, glint);
    g.add(rim, pane, shine); g.userData = { side, pane, rim, shine }; mirror.add(g); return g;
  });

  // --- carousel: sparse striped posts and a turning canopy, with a clear driving corridor
  const stripeTex = labelTexture(make, 64, 256);
  stripeTex.paint('stripes', (c, w, h) => { for (let y = 0; y < h; y += 32) { c.fillStyle = (y / 32) % 2 ? '#ffffff' : theme.accent; c.fillRect(0, y, w, 32); } });
  stripeTex.texture.wrapS = stripeTex.texture.wrapT = 1000;   // RepeatWrapping
  const poleMat = set.make({ map: stripeTex.texture });
  const poles = Array.from({ length: POLES }, () => {
    const g = new Group(), pole = new Mesh(cyl, poleMat), cap = new Mesh(sph, gold);
    g.name = 'carousel-post';
    pole.scale.set(.25, 9, .25); pole.position.y = 4.5; cap.scale.setScalar(.55); cap.position.y = 9.2;
    g.add(pole, cap); g.visible = false; object.add(g);
    return { g };
  });
  const canopyTex = labelTexture(make, 512, 64);
  canopyTex.paint('canopy', (c, w, h) => { for (let x = 0; x < w; x += 32) { c.fillStyle = (x / 32) % 2 ? '#fff3ad' : theme.accent; c.fillRect(x, 0, 32, h); } });
  const canopy = new Mesh(geo(new CylinderGeometry(0, 520, 150, 32, 1, true)), set.make({ map: canopyTex.texture, side: DoubleSide }));
  canopy.visible = false; object.add(canopy);

  // --- spotlights: pink cones from overhead onto the road, swaying on the beat
  const spotMat = set.make({ color: '#ff4fb0', side: DoubleSide }), poolMat = set.make({ color: '#ff8fcf' });
  const disc = geo(new CircleGeometry(1, 32));
  const spots = Array.from({ length: SPOTS }, () => {
    const g = new Group(), beam = new Mesh(cone, spotMat), floor = new Mesh(disc, poolMat);
    beam.scale.set(1, 22, 1); beam.position.y = 11; floor.rotation.x = -Math.PI / 2; floor.position.y = .15; floor.scale.setScalar(6);
    g.add(beam, floor); g.visible = false; object.add(g); return { g, beam, floor, key: null };
  });

  // --- name to the bin: a giant bin by the road takes your name tag, then deletes it letter by letter
  const bin = new Group(); bin.visible = false; object.add(bin);
  const binMat = set.make({ color: '#d7dde6' }), binDark = set.make({ color: '#5d6675' });
  const can = new Mesh(geo(new CylinderGeometry(4.2, 3.4, 9, 24)), binMat); can.position.y = 4.5;
  const ribs = [1.5, 4.5, 7.5].map(y => { const r = new Mesh(geo(new TorusGeometry(3.9 - y * .05, .18, 6, 32)), binDark); r.rotation.x = Math.PI / 2; r.position.y = y; return r; });
  const lid = new Group(), lidTop = new Mesh(geo(new CylinderGeometry(4.5, 4.5, .6, 24)), binDark);
  lidTop.position.set(0, 0, 4.5); lid.add(lidTop); lid.position.set(0, 9.2, -4.5);
  bin.add(can, ...ribs, lid);
  const nameTex = labelTexture(make, 1024, 192), tag = new Mesh(plane, set.make({ map: nameTex.texture, side: DoubleSide }));
  tag.visible = false; object.add(tag);
  let playerName = 'UNIT';

  // --- star at the vanishing point, its flare and the ring of light it throws back over you
  const star = new Group(); star.visible = false; object.add(star);
  const starMat = set.make({ color: '#ff5fbf', side: DoubleSide }), haloTex = labelTexture(make, 256, 256);
  haloTex.paint('halo', (c, w, h) => { const g = c.createRadialGradient(w / 2, h / 2, 0, w / 2, h / 2, w / 2); g.addColorStop(0, 'rgba(255,190,230,1)'); g.addColorStop(.35, 'rgba(255,95,191,.55)'); g.addColorStop(1, 'rgba(255,95,191,0)'); c.fillStyle = g; c.fillRect(0, 0, w, h); });
  const halo = new Mesh(plane, set.make({ map: haloTex.texture })), starMesh = new Mesh(geo(starGeometry()), starMat);
  star.add(halo, starMesh);
  const ring = new Mesh(geo(new TorusGeometry(1, .04, 6, 64)), set.make({ color: '#ffd1ec' })); ring.visible = false; object.add(ring);

  // --- the line across the road
  const line = new Group(); line.visible = false; object.add(line);
  const lineMat = set.make({ color: '#ff4fb0' }), lineGlow = set.make({ color: '#ffd1ec', side: DoubleSide });
  const bar = new Mesh(box, lineMat), sheet = new Mesh(plane, lineGlow); line.add(bar, sheet);

  // --- spiral bloom in the sky
  const bloomTex = atlas?.texture ? atlas.texture.clone() : null;
  if (bloomTex) { bloomTex.repeat.set(1 / 3, 1 / 2); bloomTex.offset.set(1 / 3, .5); bloomTex.needsUpdate = true; }
  const bloom = new Mesh(geo(new CircleGeometry(1, 48)), set.make(bloomTex ? { map: bloomTex, side: DoubleSide } : { color: '#ff5fbf', side: DoubleSide }));
  bloom.visible = false; object.add(bloom);

  const fired = new Set();
  /** Seat a sky object ahead of the player, smoothly following the road's flattened heading. */
  function sky(obj, kart, ahead, up, k) {
    track.frameAt(kart[S.s], f);
    tz.fromArray(f.t); tz.y = 0; tz.normalize(); ny.set(0, 1, 0); bx.crossVectors(ny, tz).negate();
    v.set(kart[S.posX], kart[S.posY], kart[S.posZ]).addScaledVector(tz, ahead).addScaledVector(ny, up);
    m4.makeBasis(bx, ny, tz.clone().negate()); q.setFromRotationMatrix(m4);
    if (!obj.userData.placed) { obj.position.copy(v); obj.quaternion.copy(q); obj.userData.placed = true; }
    else { obj.position.lerp(v, k); obj.quaternion.slerp(q, k); }
  }

  function update(rows = [], { songS = NaN, song = null, kart = null, camera = null, dt = 0, paused = false, gentle = false } = {}) {
    if (paused || !kart) return;
    const step = Math.min(.1, Math.max(0, dt || 0)), at = kart[S.s], k = Math.min(1, step / .8);
    const props = rows.filter(r => r.kind === 'PROP' || r.kind === 'BURST');
    const of = variant => props.filter(r => r.params?.variant === variant);
    const kick = gentle ? 0 : beatKick(song, songS, 5);

    // mirror
    const mr = of('mirror').find(r => r.locked && r.frame);
    mirror.visible = !!mr;
    if (mr) {
      placeOnFrame(mirror, mr.frame);
      const w = Math.min(18, mr.frame.w * .8), h = 14;
      const reveal = mr.params.cameraCloseUp === true ? mr.leadS : .2;
      const rise = ease((songS - mr.startS + reveal) / .9);
      const near = mirrorOpening(mr, at, track.lengthM, songS), split = gentle ? near : easeOut(near);
      for (const g of halves) {
        const { side, pane, rim, shine } = g.userData;
        g.position.set(side * (w / 4 + split * (w / 2 + 4)), -h + rise * (h + .4), 0);
        pane.scale.set(w / 2, h, 1); pane.position.set(0, h / 2, 0);
        rim.scale.set(w / 2 + .8, h + .8, .5); rim.position.set(0, h / 2, -.3);
        const sweep = ((songS * .35) % 1) * 2 - .5;
        shine.scale.set(1.2, h * 1.2, 1); shine.position.set((sweep - .25) * w, h / 2, .05); shine.rotation.z = .5;
        shine.visible = !gentle && Math.abs((sweep - .25) * w) < w / 4;
      }
    }

    // Keep the song's canopy cue without figurines crowding the tube.
    const intro = of('carousel-intro')[0], ride = of('carousel')[0];
    const live = ride || intro;
    canopy.visible = !!live;
    const standS = (intro || ride)?.startS ?? 0, stand = live ? ease((songS - standS) / .8) : 0;
    if (live) {
      track.frameAt(at, f);
      const first = Math.floor((at - 30) / POLE_GAP);
      for (let i = 0; i < POLES; i++) {
        const idx = first + Math.floor(i / 2), side = i % 2 ? 1 : -1, s = idx * POLE_GAP + (side > 0 ? POLE_GAP / 2 : 0), p = poles[i];
        track.frameAt(s, f); placeOnFrame(p.g, f); p.g.translateX(side * (f.w / 2 - .6));
        p.g.visible = true; p.g.scale.set(1, Math.max(.01, stand), 1);
      }
      sky(canopy, kart, 0, 260, 1);
      canopy.rotation.y += step * (gentle ? .05 : .16) * (ride && songS > ride.endS ? 0 : 1);
    } else for (const p of poles) p.g.visible = false;

    // spotlights
    const dip = of('road-dip')[0];
    if (dip) {
      const first = Math.floor((at - 20) / SPOT_GAP), on = ease((songS - dip.startS) / .5) * unit(dip.alpha);
      spots.forEach((sp, i) => {
        const s = (first + i) * SPOT_GAP, side = (first + i) % 2 ? 1 : -1;
        track.frameAt(s, f); placeOnFrame(sp.g, f);
        sp.g.translateX(side * f.w * .2); sp.g.visible = on > .01;
        sp.beam.rotation.z = side * (.18 + (gentle ? 0 : Math.sin(songS * 1.6 + i) * .08));
        sp.floor.scale.setScalar(5 + kick * 1.2);
      });
      spotMat.opacity = .22 * on; poolMat.opacity = .5 * on;
    } else for (const sp of spots) sp.g.visible = false;

    // name to the bin
    const nb = of('name-bin').find(r => r.locked && r.frame);
    bin.visible = tag.visible = !!nb;
    if (nb) {
      // A giant desktop bin floating over the road edge: tube courses have no roadside to stand it on.
      placeOnFrame(bin, nb.frame); bin.translateX(Math.max(3, nb.frame.w / 2 - 3) * (nb.params?.side || 1)); bin.translateY(8.5); bin.rotateY(-(nb.params?.side || 1) * .5); bin.scale.setScalar(.01 + .99 * pop((songS - nb.startS + .6) / .5));
      const del = (nb.joltTimes || [])[0] ?? nb.endS, letters = playerName.length;
      const gone = Math.floor(unit((songS - del) / .8) * (letters + 1));
      const shown = playerName.slice(0, Math.max(0, letters - gone));
      nameTex.paint(shown, (c, w, h) => {
        c.font = '900 120px system-ui, sans-serif'; c.textAlign = 'center'; c.textBaseline = 'middle';
        c.lineWidth = 14; c.strokeStyle = theme.ink; c.strokeText(shown, w / 2, h / 2); c.fillStyle = '#ffffff'; c.fillText(shown, w / 2, h / 2);
      });
      // The tag rises off the kart and arcs into the bin's mouth, then hangs there while it is deleted.
      const fly = ease((songS - nb.startS) / 1.2), mouth = v.set(0, 12, 0); bin.localToWorld(mouth);
      const from = [kart[S.posX], kart[S.posY] + 3, kart[S.posZ]];
      tag.position.set(from[0] + (mouth.x - from[0]) * fly, from[1] + (mouth.y - from[1]) * fly + Math.sin(fly * Math.PI) * 8, from[2] + (mouth.z - from[2]) * fly);
      if (camera) tag.quaternion.copy(camera.quaternion);
      tag.scale.set(10, 10 * 192 / 1024, 1).multiplyScalar(.6 + .4 * (1 - fly * .3));
      tag.visible = !!shown && songS >= nb.startS;
      lid.rotation.x = -1.3 * (1 - ease((songS - del - .9) / .4));
    }

    // star, flare and ring
    const sr = of('star')[0];
    star.visible = !!sr;
    if (sr) {
      sky(star, kart, 380, 24, k);
      const grow = ease((songS - sr.startS) / 1.5), flareAt = (sr.joltTimes || [])[0];
      const flare = flareAt == null ? 0 : Math.max(0, 1 - Math.abs(songS - flareAt - .25) / .6);
      const size = 18 * grow * (1 + (gentle ? .5 : 1.6) * flare) * (1 + kick * .06);
      starMesh.scale.setScalar(size); halo.scale.setScalar(size * 4.2); starMesh.rotation.z += step * (gentle ? .05 : .3);
      starMesh.position.z = .5;
      const throwT = flareAt == null ? -1 : (songS - flareAt) / 1.1;
      ring.visible = !gentle && throwT >= 0 && throwT < 1;
      if (ring.visible) {
        ring.position.copy(star.position).lerp(v.set(kart[S.posX], kart[S.posY] + 4, kart[S.posZ]), easeOut(throwT));
        ring.quaternion.copy(star.quaternion); ring.scale.setScalar(20 + throwT * 40);
        ring.material.opacity = 1 - throwT;
      }
    } else ring.visible = false;

    // the line
    const lr = of('line').find(r => r.frame);
    line.visible = !!lr;
    if (lr) {
      placeOnFrame(line, lr.frame);
      bar.scale.set(lr.frame.w + 6, .25, 1.2); bar.position.y = .15;
      sheet.scale.set(lr.frame.w + 6, 3 + kick * .8, 1); sheet.position.y = 1.6; lineGlow.opacity = .35;
      if (lr.hitS != null && !fired.has(lr.id) && particles) {
        fired.add(lr.id);
        // The road past the line crumbles away: glowing tiles falling from the width of the road ahead.
        for (let i = 0; i < 6; i++) {
          v.fromArray(lr.frame.p).addScaledVector(bx.fromArray(lr.frame.b), (i / 5 - .5) * lr.frame.w).addScaledVector(tz.fromArray(lr.frame.t), 8 + i * 3);
          particles.burst('tiles', [v.x, v.y, v.z], { count: 14, dir: lr.frame.t, speed: 6, spread: .9, up: 2, gravity: 18, size: 1.8, life: 2.4, gentle });
        }
      }
    }

    // spiral bloom
    const br = of('spiral-bloom')[0];
    bloom.visible = !!br;
    if (br) {
      sky(bloom, kart, 360, 70, k);
      const open = ease((songS - br.startS) / 1.6), close = 1 - ease((songS - br.endS + .6) / .6);
      bloom.scale.setScalar(150 * open * close + .01); bloom.rotateZ(step * (gentle ? .1 : .5));
    }

    // world bursts on their beat
    for (const row of props) if (row.kind === 'BURST' && particles && !fired.has(row.id) && songS >= row.startS) {
      fired.add(row.id);
      const variant = row.params?.variant;
      if (variant === 'hearts' && row.frame) {
        // Heart cannons on both road edges fire up and across; three volleys on the beat.
        for (const side of [-1, 1]) for (let i = 0; i < 3; i++) {
          v.fromArray(row.frame.p).addScaledVector(bx.fromArray(row.frame.b), side * (row.frame.w / 2 + 3)).addScaledVector(tz.fromArray(row.frame.t), i * -12);
          const d = [-side * row.frame.b[0] + row.frame.n[0] * 1.6, -side * row.frame.b[1] + row.frame.n[1] * 1.6, -side * row.frame.b[2] + row.frame.n[2] * 1.6];
          particles.burst('hearts', [v.x, v.y + 1, v.z], { count: 22, dir: d, speed: 16, spread: .35, up: 4, size: 1.6, life: 3, gentle, seed: i * 2 + side });
        }
      }
    }
  }
  return { object, update,
    setName(name) { playerName = String(name || '').trim().slice(0, 18).toUpperCase() || 'UNIT'; },
    clear() { fired.clear(); for (const o of [mirror, canopy, bin, tag, star, ring, line, bloom]) { o.visible = false; o.userData.placed = false; }
      for (const p of poles) p.g.visible = false; for (const sp of spots) sp.g.visible = false; },
    get stats() { return { mirror: mirror.visible, carousel: canopy.visible, bin: bin.visible, star: star.visible, line: line.visible, bloom: bloom.visible }; },
    dispose() { object.removeFromParent(); set.dispose(); for (const g of geos) g.dispose(); for (const t of [silverTex, stripeTex, canopyTex, nameTex, haloTex]) t.dispose(); bloomTex?.dispose(); atlas?.dispose(); } };
}
