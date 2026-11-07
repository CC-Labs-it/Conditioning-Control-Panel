/* fx/index.js - the Prize Counter juice (lane B3-W3), installed on a scene from scene/index.js:
 *
 *   const fx = installFx(scene, init);   // init { motion, gentle, reducedMotion } from the host
 *   fx.duck(true);                       // the counter opened inside a game
 *   fx.set({ motion: 'less' });
 *
 * It only uses the scene's hooks (onShelvesSwap, onFrame), parts and stamp(): shelves drop and lift, dust at the
 * three contact points, a tiny landing shake, the OWNED stamp slam, EMI's idle and cheer, and the sfx bus. */
import * as T from 'three';
import { fxLevel } from './spring.js';
import { createShelfFx } from './shelves.js';
import { createEmi, createSlam } from './emi.js';
import { createSfx } from './sfx.js';
import { createDust } from './dust.js';

export const SHAKE = { add: 0.45, metres: 0.035, decay: 7 };

export function installFx(scene, init = {}) {
  const opts = { motion: init.motion || 'full', gentle: !!init.gentle, reducedMotion: !!init.reducedMotion };
  let L = fxLevel(opts);
  const level = () => L;
  const { parts, hooks } = scene;
  const sfx = createSfx(), dust = createDust(T, parts.scene), emi = createEmi(parts.counter, level);
  const slams = [];
  let trauma = 0;
  const kick = (k) => { if (L.shake) trauma = Math.min(1, trauma + SHAKE.add * L.amp * k); };

  // Gentle fades: the shelf's materials are shared with the cache, so a fading shelf borrows clones and hands them back.
  const borrowed = new Map();
  function fade(node, a, end = a >= 1) {
    let list = borrowed.get(node);
    if (!list && a < 1) {
      list = [];
      node.traverse((o) => {
        if (!o.isMesh) return;
        const orig = o.material, clone = [].concat(orig).map((m) => Object.assign(m.clone(), { transparent: true }));
        list.push({ o, orig, clone, base: clone.map((m) => m.opacity) });
        o.material = Array.isArray(orig) ? clone : clone[0];
      });
      borrowed.set(node, list);
    }
    if (!list) return;
    if (end) {
      list.forEach((b) => { b.o.material = b.orig; b.clone.forEach((m) => m.dispose()); });
      borrowed.delete(node);
      return;
    }
    list.forEach((b) => b.clone.forEach((m, i) => { m.opacity = b.base[i] * a; }));
  }

  const shelves = createShelfFx({
    level,
    on(type, d) {
      if (type === 'land') {
        sfx.thud(d.impact ? 1 : 0.7);
        kick(1);
        const bay = d.node.userData.bay, pts = parts.dust(bay);
        if (L.dust && pts) dust.puff(pts.map((n, i) => n && { at: n.getWorldPosition(new T.Vector3()), side: i - 1 }).filter(Boolean), L.amp);
      } else if (type === 'rattle') sfx.rattle();
      else if (type === 'lift') sfx.whoosh();
      else if (type === 'fade') fade(d.node, d.a, d.end);
    },
  });

  const prevSwap = hooks.onShelvesSwap, prevFrame = hooks.onFrame;
  hooks.onShelvesSwap = (swap) => { prevSwap?.(swap); return shelves.swap(swap); };
  hooks.onFrame = (dt, t) => {
    prevFrame?.(dt, t);
    shelves.step(dt);
    if (emi) emi.step(dt);
    dust.step(dt);
    for (let i = slams.length - 1; i >= 0; i--) if (slams[i].step(dt)) slams.splice(i, 1);
    if (trauma > 0.002) {
      const m = SHAKE.metres * trauma * trauma, cam = parts.camera;
      cam.position.x += (Math.random() - 0.5) * 2 * m; cam.position.y += (Math.random() - 0.5) * 2 * m;
      trauma *= Math.exp(-SHAKE.decay * dt);
    } else trauma = 0;
  };

  // A receipt landed: the scene swaps the tag for the stamp, then the stamp slams and EMI cheers.
  const stamp = scene.stamp;
  scene.stamp = async (itemId) => {
    await stamp(itemId);
    for (const s of parts.shelves().values()) for (const e of s.slots.values()) {
      const obj = e.id === itemId && e.tagNode && e.tagNode.children[e.tagNode.children.length - 1];
      if (obj) slams.push(createSlam(obj, level, { hit: () => { sfx.slam(); kick(0.6); if (emi) emi.cheer(); }, fade: (a) => fade(obj, a) }));
    }
  };

  const setMotion = scene.setMotion, dispose = scene.dispose;
  setMotion(L.motion);
  scene.dispose = () => { sfx.dispose(); dispose(); };
  return {
    sfx, duck: (on) => sfx.duck(on), cheer: () => emi && emi.cheer(),
    set(o) { Object.assign(opts, o); L = fxLevel(opts); setMotion(L.motion); },
    get level() { return L; },
  };
}
