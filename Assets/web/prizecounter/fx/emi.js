/* fx/emi.js - EMI behind the counter (lane B3-W3), and the OWNED stamp slam. Pure: plain transforms, no three.
 * EMI breathes and twitches an antenna now and then; a buy makes her cheer (hops, shoulders up, antennas wiggle) and
 * every part eases back to its rest pose. amp 0 (motion off, Gentle) holds her at rest. */
import { springStep, between } from './spring.js';

export const EMI = { breathe: 3.6, breatheY: 0.006, twitchEvery: [3, 7], twitchV: 1.4, ant: [14, 0.12],
  cheer: 0.9, hops: 3, hopH: 0.07, shoulderY: 0.03, shoulderZ: 0.35, cheerV: 5 };

export function createEmi(root, level, rng = Math.random) {
  const find = (n) => (root && root.getObjectByName ? root.getObjectByName(n) : null);
  const body = find('emi_attendant');
  if (!body) return null;
  const parts = ['shoulderL', 'shoulderR', 'ant0', 'ant1', 'ant2', 'ballpiv'].map(find);
  const rest = [body, ...parts].map((o) => o && { o, y: o.position.y, z: o.rotation.z });
  const [, sl, sr, a0, a1, a2, ball] = rest;
  const ant = { x: 0, v: 0 };
  let t = 0, cheerT = Infinity, nextTwitch = between(rng, EMI.twitchEvery);

  function step(dt) {
    t += dt;
    const A = level().amp;
    if (A === 0) { rest.forEach((r) => { if (r) { r.o.position.y = r.y; r.o.rotation.z = r.z; } }); ant.x = ant.v = 0; cheerT = Infinity; return; }
    if (t >= nextTwitch) { ant.v += (rng() < 0.5 ? -1 : 1) * EMI.twitchV; nextTwitch = t + between(rng, EMI.twitchEvery); }
    springStep(ant, ...EMI.ant, dt);
    const c = t - cheerT, on = c >= 0 && c < EMI.cheer;
    if (c >= EMI.cheer) cheerT = Infinity;
    const env = on ? Math.sin((Math.PI * c) / EMI.cheer) : 0;                   // in and out of the cheer pose
    const hop = on ? Math.abs(Math.sin((Math.PI * EMI.hops * c) / EMI.cheer)) * EMI.hopH * (1 - c / EMI.cheer) : 0;
    const breath = Math.sin((t * 2 * Math.PI) / EMI.breathe) * EMI.breatheY;
    rest[0].o.position.y = rest[0].y + (breath + hop) * A;
    [[sl, -1], [sr, 1]].forEach(([r, side]) => {
      if (!r) return;
      r.o.position.y = r.y + (breath * 0.6 + env * EMI.shoulderY) * A;
      r.o.rotation.z = r.z + side * env * EMI.shoulderZ * A;
    });
    [[a0, 0.4], [a1, 0.7], [a2, 1], [ball, 1.3]].forEach(([r, k]) => { if (r) r.o.rotation.z = r.z + ant.x * k * A; });
  }
  return { step, cheer() { cheerT = t; ant.v += EMI.cheerV * (rng() < 0.5 ? -1 : 1); }, get cheering() { return cheerT !== Infinity; } };
}

/* The OWNED stamp: full = drops in big, hits (sound, cheer), overshoots under size and settles; less = half of that;
 * Gentle = a fade; off = a cut. hit() runs once at the contact, fade(a) only under Gentle. step(dt) -> true when done. */
export const SLAM = { from: 2, fall: 0.09, spring: [26, 0.3], kickV: -6 };

export function createSlam(obj, level, { hit = () => {}, fade = () => {} } = {}) {
  const L = level(), s0 = obj.scale.x || 1, set = (k) => obj.scale.set(s0 * k, s0 * k, s0 * k);
  let t = 0, struck = false;
  const sp = { x: 0, v: SLAM.kickV * L.amp };
  const strike = () => { if (!struck) { struck = true; hit(); } };
  if (L.amp === 0 && !L.fade) { set(1); strike(); return { step: () => true }; }
  if (L.fade) { fade(0); strike(); }
  else set(1 + (SLAM.from - 1) * L.amp);
  return {
    step(dt) {
      t += dt;
      if (L.fade) { const a = Math.min(1, t / 0.35); fade(a); return a >= 1; }
      if (t < SLAM.fall) { const k = t / SLAM.fall; set(1 + (SLAM.from - 1) * L.amp * (1 - k * k)); return false; }
      strike();
      springStep(sp, ...SLAM.spring, dt);
      set(1 + sp.x);
      if (Math.abs(sp.x) < 1e-3 && Math.abs(sp.v) < 1e-2 && t > SLAM.fall + 0.1) { set(1); return true; }
      return false;
    },
  };
}
