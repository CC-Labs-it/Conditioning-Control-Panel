/* render/hitCamMath.js - the HIT CAM inset's pure law (three-free, DOM-free; tests/hitcam.test.mjs).
 *
 * When the player's thrown item lands on a rival, a small picture-in-picture "hit cam" in the top left follows
 * the victim LIVE (the sim keeps no history, so it is a follow-cam, not a rewind) while it spins out.
 * Here: the timeline (slide in with an easeOutBack swing, hold, slide out with a small pull back), the contact
 * beats after the hit (crash zoom, decaying shake, exposure flash, pink glow, the comic impact word), the
 * replay lens curve, whether a hit is worth an inset at all, which victim of a multi-hit tick to show, the
 * word per item kind, the inset rect per viewport and the low 3/4 orbit rig around the victim.
 * Look and easing are ported from the chess capture replay (piecebypiece/board/replay-plan.js: easeOutBack /
 * easeInBack, lensZoom, the decay-pattern punch and shake, the impact word pop).
 * K is the `hitCam` feel group as a plain object (hitCamNumbers(feel) reads it); Gentle drops flash and
 * shake, halves the zoom and keeps only fades and a soft glow.
 */

export const clamp01 = (x) => (x < 0 ? 0 : x > 1 ? 1 : x);
const smooth = (x) => { const t = clamp01(x); return t * t * (3 - 2 * t); };
/** Overshoots past 1 then settles (c1 = how far). */
export const easeOutBack = (t, c1 = 1.70158) => { const c3 = c1 + 1, u = t - 1; return 1 + c3 * u * u * u + c1 * u * u; };
/** Pulls back a little before leaving. */
export const easeInBack = (t, c1 = 0.8) => (c1 + 1) * t * t * t - c1 * t * t;
const easeIn = (t) => t * t;

/** Defaults for every `hitCam` feel key (feel.json is the source; these keep the module usable without it). */
export const HIT_CAM_DEFAULTS = Object.freeze({
  on: true, holdS: 2.2, leadTimeoutS: 2.5, minDistM: 14, enterS: 0.34, exitS: 0.3, overshoot: 1.35,
  fovDeg: 42, backM: 3.1, upM: 1.05, lookUpM: 0.35, aheadM: 0.3, sideDeg: 32, orbitDegps: 18, orbitMaxDeg: 70, followTcS: 0.1,
  creep: 0.1, creepS: 0.9, punch: 0.2, punchS: 0.45, shakeM: 0.11, shakeS: 0.42, shakeHz: 19,
  flash: 0.55, flashS: 0.22, glowS: 0.7, wordS: 0.75, contactDelayS: 0.12,
});
const KEYS = Object.keys(HIT_CAM_DEFAULTS);

/** The `hitCam` group as a plain object; a missing key keeps its default. */
export function hitCamNumbers(feel) {
  const out = {};
  for (const k of KEYS) {
    let v;
    try { v = feel ? feel.get('hitCam.' + k) : undefined; } catch (e) { v = undefined; }
    out[k] = v === undefined ? HIT_CAM_DEFAULTS[k] : v;
  }
  return out;
}

/**
 * The replay lens (chess replay-plan.js lensZoom, re-tuned): creeps in while the shot is incoming, crashes in at
 * contact and eases back out after. x = seconds relative to contact (negative before); returns a field of view
 * multiplier on tan(fov/2) (< 1 = closer).
 */
export function lensZoom(x, K = HIT_CAM_DEFAULTS, gentle = false) {
  const g = gentle ? 0.5 : 1;
  const run = x < 0 ? clamp01(1 + x / K.creepS) : 1;
  const creep = K.creep * smooth(run);
  const crash = x >= 0 ? K.punch * Math.max(0, 1 - x / K.punchS) ** 2 : 0;
  const out = x > 0 ? smooth(x / 0.9) : 0;
  return 1 - g * (creep * (1 - 0.6 * out) + crash);
}

/** The fov actually used for a base vertical fov and a multiplier on tan(fov/2). */
export const fovFor = (fovDeg, mul) => 360 / Math.PI * Math.atan(Math.tan(fovDeg * Math.PI / 360) * mul);

/**
 * One frame of the inset's look. s = { openT, contactT (null while incoming), closeT, cutT }, t = now (s).
 * @returns {{ visible: boolean, done: boolean, slide: number, scale: number, zoom: number, flash: number,
 *   shake: number, glow: number, word: { alpha: number, scale: number }, chip: number, incoming: boolean }}
 *   slide: 0 seated, 1 fully off to the left (overshoot goes negative); zoom: fov multiplier.
 */
export function hitCamFrame(s, t, K = HIT_CAM_DEFAULTS, gentle = false) {
  const word = { alpha: 0, scale: 0 };
  if (!s) return { visible: false, done: true, slide: 1, scale: 1, zoom: 1, flash: 0, shake: 0, glow: 0, word, chip: 0, incoming: false };
  // ENTER: in from the left edge with a swing past the seat (Gentle: no swing)
  const e = clamp01((t - s.openT) / Math.max(1e-3, K.enterS));
  let slide = 1 - (gentle ? smooth(e) : easeOutBack(e, K.overshoot));
  let scale = 0.92 + 0.08 * smooth(e);
  // EXIT: a small pull back, then out the way it came
  const x = t >= s.closeT ? clamp01((t - s.closeT) / Math.max(1e-3, K.exitS)) : 0;
  if (x > 0) { slide += gentle ? smooth(x) : easeInBack(x); scale *= 1 - 0.1 * easeIn(x); }
  const done = t >= s.closeT && x >= 1;
  // CONTACT beats, decaying from the contact moment (a cut re-punches: contactT moves)
  const hasHit = s.contactT !== null && s.contactT !== undefined;
  const h = hasHit ? t - s.contactT : -1;
  const hit = hasHit && h >= 0;
  const decay = (sec, pow = 2) => (hit && h < sec ? (1 - h / sec) ** pow : 0);
  const zoom = hasHit ? lensZoom(h, K, gentle) : lensZoom(-K.creepS * (1 - clamp01((t - s.openT) / K.creepS)), K, gentle);
  const flash = gentle ? 0 : K.flash * decay(K.flashS, 1);
  const shake = gentle ? 0 : K.shakeM * decay(K.shakeS);
  const glow = decay(K.glowS, 1.5) * (gentle ? 0.5 : 1);
  if (hit && h < K.wordS) {
    word.alpha = 1 - clamp01((h - K.wordS * 0.6) / (K.wordS * 0.4));
    word.scale = (gentle ? 1 : easeOutBack(clamp01(h / 0.14), 2.6)) * (1 + 0.06 * (h / K.wordS));
  }
  // the REPLAY chip stamps in a beat after the panel seats
  const chip = clamp01((t - s.openT - K.enterS * 0.5) / 0.3);
  return { visible: !done, done, slide, scale, zoom, flash, shake, glow, word, chip, incoming: !hasHit };
}

/** The shake offset at time h after contact: amp along the camera right and up (metres, at the look point). */
export function shakeAt(h, amp, hz = HIT_CAM_DEFAULTS.shakeHz, seed = 0) {
  if (!(amp > 0)) return [0, 0];
  const w = h * Math.PI * 2 * hz;
  return [amp * Math.sin(w + seed * 1.7), amp * Math.cos(w * 1.31 + seed * 2.3)];
}

/**
 * Worth an inset? Only when the hit is not plainly on screen nearby: the victim is further than minDistM, or
 * behind the player, or outside the main camera's view.
 */
export function shouldShow({ distM = 0, behind = false, onScreen = true } = {}, K = HIT_CAM_DEFAULTS) {
  return distM > K.minDistM || behind || !onScreen;
}

/**
 * May the hit cam open at all? The local player's own landed hit on someone else, during racing, not paused,
 * no card up, the player not finished, the feel switch and the quality tier on.
 */
export function hitCamAllowed({ on = true, tierScale = 1, racing = false, paused = false, card = false, finished = false } = {}) {
  return !!on && tierScale > 0 && racing && !paused && !card && !finished;
}

/** A hit event the player made that the hit cam may show (item-hit payload, me = the local racer index). */
export function isPlayerHit(e, me = 0) {
  return !!e && e.by === me && e.victim !== me && e.victim >= 0 && e.verdict === 'hit';
}

/**
 * Several victims in one tick (Snap, Fog's wide wave): show the farthest of those worth showing (the one the
 * player is least likely to have seen); ties keep the first. cands: [{ victim, distM, behind, onScreen }].
 * @returns the chosen candidate or null
 */
export function pickVictim(cands, K = HIT_CAM_DEFAULTS) {
  let best = null;
  for (const c of cands || []) if (c && shouldShow(c, K) && (!best || c.distM > best.distM)) best = c;
  return best;
}

/** Comic impact words per item kind (render/items look: Pendulum swings, Disc spins, Fog splats ...). */
export const HIT_WORDS = Object.freeze({
  pendulum: ['BONK', 'DONG', 'WHAM'], disc: ['ZING', 'WHACK', 'SPUN'], tether: ['YANK', 'GOTCHA'], snap: ['SNAP', 'CRACK'],
  bigSpiral: ['WHOOSH', 'SPUN'], spiral: ['SWIRL', 'SPUN'], flash: ['ZAP', 'FLASH'], brainDrain: ['ZAP', 'BZZT'],
  freeze: ['FROZE', 'CRACK'], blackout: ['BLINK', 'DARK'], pink: ['SPLAT', 'FOGGED'], melt: ['SPLAT', 'MELT'],
  shatter: ['SMASH', 'CRASH'], other: ['POW', 'WHAM', 'BONK'],
});
/** The word for a kind; seed picks among its list (deterministic). */
export function hitWord(kind, seed = 0) {
  const list = HIT_WORDS[kind] || HIT_WORDS.other;
  return list[((Math.floor(seed) % list.length) + list.length) % list.length];
}

/**
 * The inset rect in CSS px for a viewport (16:9 picture). Desktop: under the lap / time block, about a fifth of
 * the width. Narrow portrait (<= 640 px, the HUD moves the minimap top left under the time): smaller, under the
 * minimap. Short landscape (phone on its side): sized from the height, under the time block.
 * safe = { top, left } safe-area insets.
 * @returns {{ x: number, y: number, w: number, h: number, phone: boolean }}
 */
export function hitCamRect(vw, vh, safe = {}) {
  const st = Math.max(0, safe.top || 0), sl = Math.max(0, safe.left || 0);
  const left = Math.max(16, sl);
  let w, top, phone = false;
  if (vw <= 640) { phone = true; w = Math.min(176, Math.max(140, Math.round(vw * 0.4))); top = Math.max(14, st) + 52 + 85 + 10; }
  else if (vh <= 480) { phone = true; w = Math.min(220, Math.max(150, Math.round(vh * 0.44))); top = Math.max(14, st) + 76; }
  else { w = Math.min(320, Math.max(220, Math.round(vw * 0.19))); top = Math.max(14, st) + 76; }
  const h = Math.round(w * 9 / 16);
  // never past the bottom third (the place numeral lives bottom left)
  top = Math.min(top, Math.max(0, Math.round(vh * 0.62) - h));
  return { x: left, y: top, w, h, phone };
}

// ---- the rig ------------------------------------------------------------------------------

const cross = (o, a, b) => { const x = a[1] * b[2] - a[2] * b[1], y = a[2] * b[0] - a[0] * b[2], z = a[0] * b[1] - a[1] * b[0]; o[0] = x; o[1] = y; o[2] = z; return o; };
const norm = (o) => { const l = Math.hypot(o[0], o[1], o[2]) || 1; o[0] /= l; o[1] /= l; o[2] /= l; return o; };

/** The azimuth (radians off straight behind) at time since contact: the low 3/4 seat, orbiting while stunned. */
export function orbitAz(sinceContactS, side = 1, K = HIT_CAM_DEFAULTS) {
  const orbit = Math.min(K.orbitMaxDeg, K.orbitDegps * Math.max(0, sinceContactS || 0));
  return side * (K.sideDeg + orbit) * Math.PI / 180;
}

/**
 * The rig's eye and look OFFSETS from the victim (so a 30 m/s kart never drags the damping): eye low behind and
 * to one side at azimuth az, look just above and a little ahead of the kart. fwd and up are unit vectors.
 * @returns {{ eye: number[], look: number[] }} offsets from the kart position
 */
export function rigOffsets(fwd, up, az, K = HIT_CAM_DEFAULTS, out = { eye: [0, 0, 0], look: [0, 0, 0] }) {
  // flatten forward into the kart's own plane so a pitched kart keeps the seat
  const d = fwd[0] * up[0] + fwd[1] * up[1] + fwd[2] * up[2];
  const f = norm([fwd[0] - up[0] * d, fwd[1] - up[1] * d, fwd[2] - up[2] * d]);
  const r = norm(cross([0, 0, 0], f, up));
  const c = Math.cos(az), s = Math.sin(az);
  for (let i = 0; i < 3; i++) {
    const dir = -f[i] * c + r[i] * s;
    out.eye[i] = dir * K.backM + up[i] * K.upM;
    out.look[i] = f[i] * K.aheadM + up[i] * K.lookUpM;
  }
  return out;
}
