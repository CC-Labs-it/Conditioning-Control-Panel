/* render/items/statusMath.js - what a hit does to a kart, drawn so EVERY racer sees it (music road pass 2).
 *
 * Pure (no three.js; render/items/status.js draws it). A kart under an effect wears one readable look per
 * kind, sized to read at 40 m: Flashes deals playing cards round it, Pink sits it in a pink cloud, Blackout in
 * a dark one, Freeze in a frost shell with crystals, Spiral a turning spiral over its head, Brain Drain a
 * green funnel rising off it, Melt pink drips, Shatter prism shards spinning round it. statusStep gives every
 * look an IN (a pop with a little overshoot, Gentle: never quicker than its rise floor) and an OUT (a shrink
 * and fade after the effect ends). The burst pool is the shared particle store for hit bursts, throw sparks
 * and the trail sheds: fixed arrays, no allocation per frame.
 */

// sprite shapes the status material draws (render/items/status.js)
export const SHAPE = Object.freeze({ puff: 0, card: 1, spiral: 2, star: 3, crystal: 4, shard: 5, drop: 6, ring: 7, heart: 8 });

const rgb = (r, g, b) => [r / 255, g / 255, b / 255];

/**
 * Per kind: col the main colour, alt the second, and parts, each a group of sprites in kart space
 * (x right, y up, z back): { shape, n, r (orbit radius m), hz (turns per second), up (m), size (m),
 * bob (m), rise (m per loop for drips / funnels, negative falls), loopS, alpha, spin (sprite turns per s) }.
 */
export const STATUS_LOOK = Object.freeze({
  inS: 0.28, outS: 0.45, overshoot: 0.22, gentleHz: 0.5, selfScale: 0.6, selfAlpha: 0,   // selfAlpha 0: the player sees its own hit on screen, so its kart wears no look (it would sit in the lens)
  burstN: 26, burstSpeed: 6.5, burstLifeS: 0.75, burstSize: 0.42, ringSize: 4.2, ringLifeS: 0.5,
  kinds: {
    flash: { col: rgb(255, 90, 180), alt: rgb(255, 236, 246), parts: [
      { shape: SHAPE.card, n: 7, r: 1.55, hz: 0.55, up: 1.25, size: 0.78, bob: 0.25, spin: 0.4, alpha: 1 },
      { shape: SHAPE.star, n: 6, r: 1.05, hz: -0.9, up: 1.9, size: 0.38, bob: 0.15, alpha: 0.9 },
      { shape: SHAPE.puff, n: 1, r: 0, hz: 0, up: 1.1, size: 3.2, alpha: 0.35 } ] },
    spiral: { col: rgb(120, 236, 224), alt: rgb(255, 140, 220), parts: [
      { shape: SHAPE.spiral, n: 1, r: 0, hz: 0, up: 2.55, size: 1.9, spin: -0.8, alpha: 1 },
      { shape: SHAPE.star, n: 6, r: 1.35, hz: 0.8, up: 1.35, size: 0.36, bob: 0.3, alpha: 0.9 } ] },
    brainDrain: { col: rgb(150, 255, 170), alt: rgb(90, 200, 255), parts: [
      { shape: SHAPE.puff, n: 10, r: 0.55, hz: 0.7, up: 1.2, size: 0.55, rise: 2.4, loopS: 1.3, alpha: 0.9 },
      { shape: SHAPE.ring, n: 1, r: 0, hz: 0, up: 3.2, size: 1.5, spin: 0.5, alpha: 0.7 } ] },
    freeze: { col: rgb(190, 236, 255), alt: rgb(255, 255, 255), parts: [
      { shape: SHAPE.crystal, n: 7, r: 1.4, hz: 0.18, up: 1.1, size: 0.62, bob: 0.12, spin: 0.15, alpha: 1 },
      { shape: SHAPE.puff, n: 1, r: 0, hz: 0, up: 0.9, size: 3.0, alpha: 0.4 } ] },
    blackout: { col: rgb(40, 22, 70), alt: rgb(170, 140, 255), parts: [
      { shape: SHAPE.puff, n: 8, r: 0.95, hz: 0.12, up: 1.55, size: 1.5, bob: 0.2, alpha: 0.85 },
      { shape: SHAPE.star, n: 4, r: 1.2, hz: -0.4, up: 2.1, size: 0.3, bob: 0.2, alpha: 0.8 } ] },
    pink: { col: rgb(255, 96, 190), alt: rgb(255, 190, 230), parts: [
      { shape: SHAPE.puff, n: 9, r: 1.1, hz: 0.1, up: 0.9, size: 1.7, bob: 0.18, alpha: 0.75 },
      { shape: SHAPE.heart, n: 5, r: 1.3, hz: 0.35, up: 2.0, size: 0.42, bob: 0.25, alpha: 0.95 } ] },
    melt: { col: rgb(255, 120, 200), alt: rgb(255, 200, 235), parts: [
      { shape: SHAPE.drop, n: 8, r: 0.8, hz: 0.05, up: 2.1, size: 0.42, rise: -2.0, loopS: 0.9, alpha: 1 },
      { shape: SHAPE.puff, n: 1, r: 0, hz: 0, up: 0.15, size: 2.6, alpha: 0.45 } ] },
    shatter: { col: rgb(255, 210, 140), alt: rgb(150, 220, 255), parts: [
      { shape: SHAPE.shard, n: 9, r: 1.5, hz: 1.4, up: 1.0, size: 0.5, bob: 0.35, spin: 2.2, alpha: 1 } ] },
    // toys kit placeholders (Astra's props replace them)
    pendulum: { col: rgb(255, 200, 120), alt: rgb(255, 240, 200), parts: [
      { shape: SHAPE.star, n: 5, r: 1.2, hz: 1.1, up: 1.9, size: 0.38, bob: 0.4, alpha: 0.95 } ] },
    disc: { col: rgb(120, 220, 255), alt: rgb(255, 255, 255), parts: [
      { shape: SHAPE.shard, n: 8, r: 1.4, hz: 1.6, up: 1.0, size: 0.45, bob: 0.3, spin: 2.4, alpha: 1 } ] },
    tether: { col: rgb(255, 110, 170), alt: rgb(255, 220, 235), parts: [
      { shape: SHAPE.ring, n: 1, r: 0, hz: 0, up: 1.6, size: 1.6, spin: 1.2, alpha: 0.8 },
      { shape: SHAPE.star, n: 6, r: 1.3, hz: 1.4, up: 1.8, size: 0.34, bob: 0.3, alpha: 0.9 } ] },
    snap: { col: rgb(240, 240, 255), alt: rgb(255, 200, 235), parts: [
      { shape: SHAPE.star, n: 4, r: 1.0, hz: 0.6, up: 1.4, size: 0.32, bob: 0.2, alpha: 0.85 } ] },
    bigSpiral: { col: rgb(190, 120, 255), alt: rgb(255, 150, 220), parts: [
      { shape: SHAPE.spiral, n: 1, r: 0, hz: 0, up: 2.8, size: 2.4, spin: -1.0, alpha: 1 } ] },
  },
});

/** Every kind with a status look. */
export const STATUS_IDS = Object.freeze(Object.keys(STATUS_LOOK.kinds));

/**
 * One racer's status clock. st = { kind, age, outAge } (outAge < 0 while the effect runs). kind = the
 * racer's live effect (fxv.effect) or null. Returns k, the look's scale 0..1+ (IN overshoots a little).
 */
export function statusStep(st, kind, dt, { gentle = false, riseS = 0.3 } = {}, L = STATUS_LOOK) {
  const live = kind && L.kinds[kind] ? kind : null;
  if (live && live !== st.kind) { st.kind = live; st.age = 0; st.outAge = -1; st.fresh = true; }
  else st.fresh = false;
  if (!st.kind) return 0;
  st.age += dt;
  if (!live && st.outAge < 0) st.outAge = dt;
  else if (!live) st.outAge += dt;
  else if (live === st.kind) st.outAge = -1;
  const inS = gentle ? Math.max(L.inS, riseS) : L.inS;
  const a = Math.min(1, st.age / inS);
  // IN: ease out with a small overshoot (none under Gentle), OUT: shrink and fade
  const over = gentle ? 0 : L.overshoot * Math.sin(Math.PI * a) * (1 - a * 0.4);
  let k = (1 - (1 - a) * (1 - a)) + over;
  if (st.outAge >= 0) {
    const o = Math.min(1, st.outAge / L.outS);
    k *= 1 - o * o;
    if (o >= 1) { st.kind = null; return 0; }
  }
  return k;
}

/** A fresh status clock. */
export const statusState = () => ({ kind: null, age: 0, outAge: -1, fresh: false });

/**
 * The sprites of one look at time t (seconds since it began) and scale k, in kart space. Calls
 * push(x, y, z, size, shape, alpha, spinRad, useAlt) per sprite. seed spreads racers apart.
 */
export function statusSprites(kind, t, k, seed, push, { gentle = false } = {}, L = STATUS_LOOK) {
  const spec = L.kinds[kind];
  if (!spec || k <= 0) return 0;
  const rate = gentle ? L.gentleHz : 1;
  let n = 0;
  for (const p of spec.parts) {
    for (let i = 0; i < p.n; i++) {
      const ph = i / p.n, ang = (ph + t * p.hz * rate) * Math.PI * 2 + seed;
      let y = p.up + (p.bob ? Math.sin(t * 1.7 * rate + i * 1.9 + seed) * p.bob : 0);
      let r = p.r * (0.55 + 0.45 * Math.min(1, k));
      let a = p.alpha;
      if (p.rise) {
        const f = ((t * rate / p.loopS) + ph) % 1;     // each sprite loops from the start height
        y = p.up + p.rise * f;
        a *= Math.sin(Math.PI * f);
        if (p.rise > 0) r *= 1 - f * 0.6;               // a funnel narrows as it climbs
      }
      const spin = (p.spin || 0) * t * rate * Math.PI * 2 + i;
      push(Math.cos(ang) * r, y, Math.sin(ang) * r, p.size * Math.max(0, k), p.shape, a * Math.min(1, k), spin, i % 2 === 1);
      n++;
    }
  }
  return n;
}

/**
 * The shared particle store: hit bursts, launch sparks and trail sheds. Fixed arrays; spawn overwrites the
 * oldest when full. rand() 0..1.
 */
export function createBurstPool(cap = 600) {
  const P = new Float32Array(cap * 3), Vv = new Float32Array(cap * 3), life = new Float32Array(cap), max = new Float32Array(cap);
  const size = new Float32Array(cap), shape = new Uint8Array(cap), col = new Float32Array(cap * 3), drag = new Float32Array(cap), grav = new Float32Array(cap);
  let head = 0;
  return {
    cap,
    /** One particle at p moving v (m/s), life s, size m, colour c [r,g,b]. */
    spawn(px, py, pz, vx, vy, vz, lifeS, sizeM, shp, c, g = -4, dr = 1.6) {
      const i = head; head = (head + 1) % cap;
      P[i * 3] = px; P[i * 3 + 1] = py; P[i * 3 + 2] = pz; Vv[i * 3] = vx; Vv[i * 3 + 1] = vy; Vv[i * 3 + 2] = vz;
      life[i] = lifeS; max[i] = lifeS; size[i] = sizeM; shape[i] = shp; col[i * 3] = c[0]; col[i * 3 + 1] = c[1]; col[i * 3 + 2] = c[2];
      grav[i] = g; drag[i] = dr;
      return i;
    },
    /** A sphere of n particles out of p at speed (m/s), half in col and half in alt. */
    burst(p, n, speed, lifeS, sizeM, shp, colA, colB, rand, up = 1.5) {
      for (let j = 0; j < n; j++) {
        const u = rand() * 2 - 1, th = rand() * Math.PI * 2, s = Math.sqrt(1 - u * u), sp = speed * (0.45 + rand() * 0.75);
        this.spawn(p[0], p[1], p[2], Math.cos(th) * s * sp, u * sp * 0.6 + up, Math.sin(th) * s * sp,
          lifeS * (0.7 + rand() * 0.5), sizeM * (0.7 + rand() * 0.6), shp, j % 2 ? colB : colA);
      }
    },
    step(dt) {
      if (!(dt > 0)) return;
      for (let i = 0; i < cap; i++) {
        if (life[i] <= 0) continue;
        life[i] -= dt;
        const k = Math.exp(-drag[i] * dt);
        Vv[i * 3] *= k; Vv[i * 3 + 1] = Vv[i * 3 + 1] * k + grav[i] * dt; Vv[i * 3 + 2] *= k;
        P[i * 3] += Vv[i * 3] * dt; P[i * 3 + 1] += Vv[i * 3 + 1] * dt; P[i * 3 + 2] += Vv[i * 3 + 2] * dt;
      }
    },
    /** fn(x, y, z, size, shape, alpha, r, g, b) for each live particle; alpha fades over the last half of life. */
    each(fn) {
      let n = 0;
      for (let i = 0; i < cap; i++) {
        if (life[i] <= 0) continue;
        const f = life[i] / max[i], a = Math.min(1, f * 2);
        fn(P[i * 3], P[i * 3 + 1], P[i * 3 + 2], size[i] * (0.6 + 0.4 * f), shape[i], a, col[i * 3], col[i * 3 + 1], col[i * 3 + 2]);
        n++;
      }
      return n;
    },
    live() { let n = 0; for (let i = 0; i < cap; i++) if (life[i] > 0) n++; return n; },
    clear() { life.fill(0); head = 0; },
  };
}

/**
 * A projectile's trail: a ring of the last `cap` points, a new one every gapM of travel. push returns
 * true when it stored a point. points(fn) walks newest to oldest with age share 0 (head) .. 1 (tail).
 */
export function createTrail(cap = 28, gapM = 0.45) {
  const pts = new Float32Array(cap * 3);
  let n = 0, head = 0, lx = 0, ly = 0, lz = 0;
  return {
    reset() { n = 0; head = 0; },
    push(x, y, z) {
      if (n && Math.hypot(x - lx, y - ly, z - lz) < gapM) return false;
      pts[head * 3] = x; pts[head * 3 + 1] = y; pts[head * 3 + 2] = z;
      head = (head + 1) % cap; n = Math.min(cap, n + 1); lx = x; ly = y; lz = z;
      return true;
    },
    get n() { return n; },
    points(fn) {
      for (let j = 0; j < n; j++) {
        const i = (head - 1 - j + cap) % cap;
        fn(pts[i * 3], pts[i * 3 + 1], pts[i * 3 + 2], n > 1 ? j / (n - 1) : 0);
      }
    },
    last(out) { const i = (head - 1 + cap) % cap; out[0] = pts[i * 3]; out[1] = pts[i * 3 + 1]; out[2] = pts[i * 3 + 2]; return out; },
  };
}
