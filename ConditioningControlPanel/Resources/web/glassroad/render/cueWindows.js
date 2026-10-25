import { Group, Mesh, BoxGeometry, PlaneGeometry, MeshBasicNodeMaterial, DoubleSide } from 'three/webgpu';
import { createMarqueeSurface } from './marquee.js';
import { acquireLoomAtlas } from './loomAtlas.js';
import { wordTheme } from './wordTheme.js';
import { placeOnFrame, pop, ease, unit, rng, hashId, beatPhase } from './cueKit.js';
import { S } from '../sim/state.js';

// Browser-style windows in the world, beside the road. SWARM opens them on the beat (the rate doubles on each
// of its jolts) and closes them in a ripple at its end. PROP screens-turn stands a row of side screens that all
// spin round to show their backs on the cue. PROP flee-close is one window with a big close button that slides
// away down the road as you approach. Every window is fixed in the world except the fleeing one; none collide.
const CAPACITY = 28, SURFACES = 6;
export function createCueWindows(scene, track, { source = () => null, make = () => document.createElement('canvas'),
  acquireAtlas = acquireLoomAtlas } = {}) {
  const object = new Group(); object.name = 'cue-windows'; scene.add(object);
  const theme = wordTheme(track), box = new BoxGeometry(1, 1, 1), plane = new PlaneGeometry(1, 1);
  const mats = [], mat = o => { const m = new MeshBasicNodeMaterial({ transparent: true, depthWrite: false, fog: false, ...o }); mats.push(m); return m; };
  const chrome = mat({ color: '#f4f0f6' }), bar = mat({ color: theme.accent }), close = mat({ color: '#ff3b5c' });
  const cross = mat({ color: '#ffffff' }), back = mat({ color: '#1b1420' }), dot = mat({ color: '#ffffff' });
  let atlas = null; try { atlas = acquireAtlas(); } catch { /* procedural surface */ }
  const surfaces = Array.from({ length: SURFACES }, (_, i) => createMarqueeSurface(make(), atlas?.texture, i + 2));
  const faces = surfaces.map(s => mat({ map: s.texture, side: DoubleSide }));
  const slots = [];
  for (let i = 0; i < CAPACITY; i++) {
    const root = new Group(), body = new Group(); root.add(body); root.visible = false; object.add(root);
    const frame = new Mesh(box, chrome), title = new Mesh(box, bar), face = new Mesh(plane, faces[i % SURFACES]);
    const xBtn = new Mesh(box, close), x1 = new Mesh(box, cross), x2 = new Mesh(box, cross), rear = new Mesh(plane, back);
    const dots = [0, 1, 2].map(() => new Mesh(box, dot));
    body.add(frame, title, face, xBtn, x1, x2, rear, ...dots);
    slots.push({ root, body, frame, title, face, xBtn, x1, x2, rear, dots, id: null, born: 0, closeAt: Infinity, turnAt: Infinity,
      s: 0, x: 0, y: 0, w: 6, chrome: true, flee: false });
  }
  const f = { p: [0, 0, 0], t: [0, 0, 1], n: [0, 1, 0], b: [1, 0, 0], w: 0 };
  const along = (s, from) => { const L = track.lengthM, d = ((s - from) % L + L) % L; return d > L / 2 ? d - L : d; };
  function layout(slot) {
    const w = slot.w, h = w * .62, t = slot.chrome ? h * .14 : 0;
    slot.frame.scale.set(w + .3, h + t + .3, .25); slot.frame.position.set(0, t / 2, -.05);
    slot.title.visible = slot.xBtn.visible = slot.x1.visible = slot.x2.visible = slot.chrome;
    for (const d of slot.dots) d.visible = slot.chrome;
    slot.face.scale.set(w, h, 1); slot.face.position.set(0, 0, .12);
    slot.rear.scale.set(w, h + t, 1); slot.rear.position.set(0, t / 2, -.2); slot.rear.rotation.y = Math.PI;
    if (!slot.chrome) return;
    slot.title.scale.set(w + .3, t, .3); slot.title.position.set(0, h / 2 + t / 2, .02);
    const b = slot.flee ? t * 2.4 : t * .8;
    slot.xBtn.scale.set(b, b, .4); slot.xBtn.position.set(slot.flee ? 0 : w / 2 - b * .7, slot.flee ? -h * .05 : h / 2 + t / 2, slot.flee ? .4 : .2);
    for (const [k, xs] of [slot.x1, slot.x2].entries()) {
      xs.scale.set(b * .75, b * .14, .2); xs.position.copy(slot.xBtn.position); xs.position.z += .25; xs.rotation.z = k ? -Math.PI / 4 : Math.PI / 4;
    }
    slot.dots.forEach((d, k) => { d.scale.set(t * .35, t * .35, .2); d.position.set(-w / 2 + t * (.5 + k * .55), h / 2 + t / 2, .2); });
    if (slot.flee) { slot.face.scale.set(w, h * .8, 1); slot.face.position.y = h * .1; }
  }
  function seat(slot) {
    track.frameAt(slot.s, f); placeOnFrame(slot.root, f);
    slot.root.translateX(slot.x); slot.root.translateY(slot.y);
  }
  // A fresh slot always starts upright and facing the road.
  function spawn(id, s, x, y, w, now, { chrome = true, flee = false, yaw = 0 } = {}) {
    const slot = slots.find(v => !v.id) || slots.reduce((a, b) => (a.born < b.born ? a : b));
    Object.assign(slot, { id, s, x, y, w, born: now, closeAt: Infinity, turnAt: Infinity, chrome, flee });
    layout(slot); seat(slot); slot.yaw = yaw; slot.body.rotation.set(0, yaw, 0); slot.root.visible = true;
    return slot;
  }
  const seen = new Map();   // per row: last spawned beat and turned flags
  function update(rows = [], { songS = NaN, song = null, kart = null, speed = 0, dt = 0, paused = false, gentle = false, alpha = 1 } = {}) {
    if (paused || !kart) return;
    const at = kart[S.s], plan = Math.max(14, Math.min(60, speed || 0)), step = Math.min(.1, Math.max(0, dt || 0));
    track.frameAt(at, f);
    const swarms = rows.filter(r => r.kind === 'SWARM'), turns = rows.filter(r => r.kind === 'PROP' && r.params?.variant === 'screens-turn');
    const flees = rows.filter(r => r.kind === 'PROP' && r.params?.variant === 'flee-close');
    const ph = beatPhase(song, songS);
    for (const row of swarms) {
      const st = seen.get(row.id) || { beat: null }; seen.set(row.id, st);
      if (songS < row.startS || ph === null) continue;
      const beat = Math.floor(ph);
      if (songS < row.endS && st.beat !== beat) {
        st.beat = beat;
        const doubled = (row.joltTimes || []).filter(t => songS >= t).length;
        const n = Math.min(8, (row.params?.perBeat ?? 1) * (row.params?.multiply ?? 2) ** doubled) * (gentle ? .5 : 1);
        const r = rng(hashId(row.id + beat));
        for (let i = 0; i < Math.max(1, Math.round(n)); i++) {
          const side = r() < .5 ? -1 : 1, w = 5 + r() * 4;
          // Hung over the road's edges, clear of every kart: some courses are tubes with no roadside to stand on.
          const sp = spawn(row.id, at + plan * (1.2 + r() * 1.8), side * ((f.w || 20) * (.15 + r() * .3)), 8 + w * .31 + r() * 7, w, songS,
            { yaw: -side * (.25 + r() * .35) });
          sp.closeAt = row.endS + i * .03 + r() * .25;
        }
      }
    }
    for (const row of turns) {
      if (seen.has(row.id)) continue;
      seen.set(row.id, { beat: null });
      // A row of side screens already standing ahead; on the cue they spin round in a ripple.
      for (let i = 0; i < 10; i++) {
        const side = i % 2 ? 1 : -1, d = 40 + Math.floor(i / 2) * 26 + plan * Math.max(0, row.startS - songS);
        const sp = spawn(row.id, at + d, side * Math.max(4, (f.w || 20) / 2 - 4), 10.8, 10, songS, { chrome: false, yaw: -side * .35 });
        sp.turnAt = row.startS + i * .07; sp.closeAt = row.endS;
      }
    }
    for (const row of flees) {
      let sp = slots.find(v => v.id === row.id);
      if (!sp && !seen.has(row.id)) { seen.set(row.id, { beat: null }); sp = spawn(row.id, at + plan * 2.2, 0, 8.4, 9, songS, { flee: true }); sp.closeAt = row.endS; }
      if (sp && songS >= row.startS) {
        // It keeps out of reach: never closer than 28 m, sliding toward the edge, a little faster than you.
        const gap = along(sp.s, at), min = 28 + 4 * ease((songS - row.startS) / 2);
        if (gap < min) sp.s = at + min;
        sp.x += (hashId(row.id) % 2 ? 1 : -1) * 2.6 * step;
      }
    }
    for (const slot of slots) {
      if (!slot.id) continue;
      const gone = along(slot.s, at) < -25 || songS > slot.closeAt + .4 || songS < slot.born - .5;
      if (gone) { slot.id = null; slot.root.visible = false; continue; }
      if (slot.flee) seat(slot);
      const open = gentle ? ease((songS - slot.born) / .4) : pop((songS - slot.born) / .28);
      const shut = 1 - ease((songS - slot.closeAt) / .3);
      slot.root.scale.set(open * (gentle ? 1 : .9 + .1 * shut), open * shut, 1);
      if (Number.isFinite(slot.turnAt)) slot.body.rotation.y = slot.yaw + Math.PI * ease((songS - slot.turnAt) / (gentle ? .9 : .45));
    }
    for (const m of mats) m.opacity = unit(alpha);
    // Bounded uploads: one surface per frame. Revoked or expired media clears regardless.
    const i = Math.floor((songS || 0) * 15) % SURFACES;
    surfaces[i].paint(source(100 + i), songS || 0, { allowed: true });
  }
  return { object, update,
    clear() { for (const s of slots) { s.id = null; s.root.visible = false; } seen.clear(); for (const s of surfaces) s.clear(); },
    get stats() { return { active: slots.filter(s => s.id).length, capacity: CAPACITY }; },
    dispose() { object.removeFromParent(); for (const s of surfaces) s.dispose(); mats.forEach(m => m.dispose()); box.dispose(); plane.dispose(); atlas?.dispose(); } };
}
