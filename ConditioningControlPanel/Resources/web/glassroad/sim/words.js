/* sim/words.js - the song's words in bubbles on the road (handoff-1022/MUSIC-ROAD-BRIEF.md section 4).
 *
 * Pure: no three.js, no DOM. createWordRun(song, track, { seed }) reads a normalised chart (audio/chart.js)
 * and, every frame, the song position tS (music.songS, what the player hears) and the local player's
 * { s, lat, speedMps }. Each kept `bubble: true` word appears leadS before its startS where the player
 * will be when it is sung (player.s + speed x time left), keeps re-aiming at that spot until freezeS
 * before, then holds still, so the word is in the bubble as its voice is heard. `gate: true` words are a
 * row of three across the road; any one pops the word. Only the local player pops (nothing here is
 * shared: the room carries the pop count, never positions). Every time is the song clock: a frozen tS
 * (pause) moves nothing; tS jumping back (the song loops) starts a fresh pass.
 */
import { clamp, wrap } from '../core/math.js';

export const WORD_LOOK = Object.freeze({
  leadS: 2.5,          // a bubble shows this long before its word is sung
  freezeS: 0.8,        // ...and holds its spot from this long before
  aimTcS: 0.25,        // song-time smoothing of the re-aim, so speed wobble never jitters it
  minGapS: 0.9,        // at most one bubble per this much song time
  gateGapS: 0.5,       // a gate word may come this close to the last bubble
  minSpeedMps: 14,     // the aim never assumes a slower player (the countdown, a stall)
  popSM: 1.6, popLatM: 1.6,
  missBehindM: 3,      // passed by this much: missed
  missAfterS: 1.5,     // or this long after the word ended
  keepS: 1.2,          // popped and missed views stay this long for their OUT
  edgeMarginM: 2.4,    // lateral room kept clear of the rails
  laneShiftPerS: 0.35, // how far (share of the usable half width) one bubble may sit from the last, per second between them
  laneMax: 0.85,
  gateRow: [-0.62, 0, 0.62],
  heightM: 1.1,
  loopBackS: 1,        // tS this much below the last read is a loop, not jitter
});

/** Deterministic 0..1 from a seed and an index. */
export function hash01(seed, i) {
  let h = (Math.imul(seed | 0, 0x9e3779b1) ^ Math.imul((i | 0) + 1, 0x85ebca6b)) >>> 0;
  h = Math.imul(h ^ (h >>> 16), 0x7feb352d) >>> 0;
  h = Math.imul(h ^ (h >>> 15), 0x846ca68b) >>> 0;
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

/** The kept bubble words, in sung order: thinned to minGapS, gate words allowed down to gateGapS. */
export function pickWords(song, L = WORD_LOOK) {
  const out = [];
  let last = -Infinity;
  for (const w of (song && song.words) || []) {
    if (!w.bubble || !w.w) continue;
    const gap = w.startS - last;
    if (gap < (w.gate ? L.gateGapS : L.minGapS)) continue;
    out.push({ i: out.length, w: w.w, startS: w.startS, endS: Math.max(w.endS, w.startS), gate: !!w.gate });
    last = w.startS;
  }
  return out;
}

/** Lateral share (-laneMax..laneMax) per kept word: a seeded walk, each step bounded by the time between. */
export function laneShares(picked, seed, L = WORD_LOOK) {
  const xs = new Float32Array(picked.length);
  let prev = 0, prevT = null;
  for (let i = 0; i < picked.length; i++) {
    const p = picked[i];
    if (p.gate) { xs[i] = 0; prev = 0; prevT = p.startS; continue; }
    const gap = prevT === null ? 2 : p.startS - prevT;
    const step = Math.min(1, L.laneShiftPerS * gap) * (hash01(seed, i) * 2 - 1);
    xs[i] = prev = clamp(prev + step, -L.laneMax, L.laneMax);
    prevT = p.startS;
  }
  return xs;
}

const signed = (d, lapM) => { const x = wrap(d, lapM); return x > lapM / 2 ? x - lapM : x; };

/**
 * @returns {{ update(tS: number, player: {s: number, lat: number, speedMps: number}): object[], live: object[],
 *   pops: number, shown: number, total: number, reset(seed?: number): void, onPop(fn: Function): () => void }}
 */
export function createWordRun(song, track, { seed = 1, look = WORD_LOOK } = {}) {
  const L = look, lapM = track.lengthM;
  const picked = pickWords(song, L);
  const fr = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0], w: 14 };
  let shares = laneShares(picked, seed, L);
  // per kept word: its bubble state; views are reused objects (no per-frame allocation)
  const B = picked.map((p) => ({ ...p, state: 'wait', s: 0, frozen: false, inS: 0, outS: 0, hit: -1,
    view: { id: p.i, w: p.w, gate: p.gate, state: 'wait', n: p.gate ? L.gateRow.length : 1, xs: [0, 0, 0], p: [[0, 0, 0], [0, 0, 0], [0, 0, 0]],
      s: 0, inT: 0, outT: -1, remainS: 0, hit: -1 } }));
  const live = [], fns = [];
  let lastT = null, pops = 0, shown = 0;

  function pass() { for (const b of B) { b.state = 'wait'; b.frozen = false; b.hit = -1; } live.length = 0; }

  function aimAt(b, tS, player) {
    const v = Math.max(L.minSpeedMps, player.speedMps || 0);
    return player.s + v * Math.max(0, b.startS - tS);
  }

  function place(b) {
    const f = track.frameAt(wrap(b.s, lapM), fr);
    const usable = Math.max(0.5, f.w / 2 - L.edgeMarginM);
    const v = b.view;
    v.s = wrap(b.s, lapM);
    for (let k = 0; k < v.n; k++) {
      const x = (b.gate ? L.gateRow[k] : shares[b.i]) * usable;
      v.xs[k] = x;
      const p = v.p[k];
      for (let j = 0; j < 3; j++) p[j] = f.p[j] + f.b[j] * x + f.n[j] * L.heightM;
    }
  }

  function update(tS, player) {
    if (!Number.isFinite(tS) || !player) return live;
    if (lastT !== null && tS < lastT - L.loopBackS) pass();
    const dtS = lastT === null ? 0 : Math.max(0, tS - lastT);
    lastT = tS;
    const k = dtS > 0 ? 1 - Math.exp(-dtS / L.aimTcS) : 0;
    live.length = 0;
    for (const b of B) {
      if (b.state === 'wait') {
        if (tS < b.startS - L.leadS || tS > b.startS) continue;
        b.state = 'live'; b.inS = tS; b.frozen = false; b.s = aimAt(b, tS, player); shown++;
      }
      if (b.state === 'live') {
        if (!b.frozen) {
          if (b.startS - tS <= L.freezeS) b.frozen = true;
          else b.s += (aimAt(b, tS, player) - b.s) * k;
        }
        place(b);
        const ds = signed(b.s - player.s, lapM);
        let hit = -1;
        if (Math.abs(ds) <= L.popSM) for (let j = 0; j < b.view.n; j++) if (Math.abs(b.view.xs[j] - player.lat) <= L.popLatM) { hit = j; break; }
        if (hit >= 0) {
          b.state = 'popped'; b.outS = tS; b.hit = hit; pops++;
          const pp = b.view.p[hit];
          const e = { w: b.w, gate: b.gate, p: [pp[0], pp[1], pp[2]], tS };
          for (const fn of fns) fn(e);
        } else if ((b.frozen && ds < -L.missBehindM) || tS > b.endS + L.missAfterS) { b.state = 'missed'; b.outS = tS; }
      } else if (b.state === 'popped' || b.state === 'missed') {
        if (tS - b.outS > L.keepS) { b.state = 'gone'; continue; }
      } else continue;
      const v = b.view;
      v.state = b.state; v.inT = tS - b.inS; v.outT = b.state === 'live' ? -1 : tS - b.outS; v.remainS = b.startS - tS; v.hit = b.hit;
      live.push(v);
    }
    return live;
  }

  return {
    update, live,
    get pops() { return pops; },
    get shown() { return shown; },
    get total() { return picked.length; },
    words: picked,
    reset(s = seed) { seed = s; shares = laneShares(picked, seed, L); pass(); lastT = null; pops = 0; shown = 0; },
    onPop(fn) { fns.push(fn); return () => { const i = fns.indexOf(fn); if (i >= 0) fns.splice(i, 1); }; },
  };
}
