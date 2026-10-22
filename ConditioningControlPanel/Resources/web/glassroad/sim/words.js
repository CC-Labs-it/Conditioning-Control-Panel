/* sim/words.js - the song's words in bubbles on the road (handoff-1022/MUSIC-ROAD-BRIEF.md section 4).
 *
 * Pure: no three.js, no DOM. EVERY SUNG WORD rides a bubble, the Racing Thoughts way (dtrh/race/wordBubbles.js,
 * the owner's picture: one word, one bubble, and the sentence is what a LINE of them spells down one lane).
 * scriptBubbles(song) builds the road's script once:
 *   - two words closer than mergeS share one bubble ("in a"); nothing is ever dropped;
 *   - a PHRASE is a run of speech with no gap over phraseGapS, cut on a clause's punctuation and at
 *     phraseMax bubbles; every bubble of one phrase sits in ONE lane, in a line;
 *   - the lane steps one lane, only between phrases and only through a silence of laneMoveS or more, so the
 *     player reads the line, drives the line, and steers in the gap where the voice stopped;
 *   - a trigger word (chart triggers kind word, "good girl") is a ROW of five across the whole road, one
 *     bubble wearing the whole phrase: you do not get to steer around the word she just said; a `gate: true`
 *     word is a row of three. Any bubble of a row pops the row.
 * createWordRun(song, track, { seed }) then reads, every frame, the song position tS (music.songS, what the
 * player hears) and the local player's { s, lat, speedMps }. Each bubble appears leadS before its word where
 * the player will be when it is sung (player.s + speed x time left), keeps re-aiming at that spot until
 * freezeS before, then holds still, so the word is in the bubble as its voice is heard. Only the local player
 * pops (nothing here is shared: the room carries the pop count, never positions). A pop event carries the
 * streak (bubbles popped in a row without a miss) and lineDone (a line's last bubble, every one of it taken).
 * Every time is the song clock: a frozen tS (pause) moves nothing; tS jumping back (the song loops) starts a
 * fresh pass.
 */
import { clamp, wrap } from '../core/math.js';

export const WORD_LOOK = Object.freeze({
  leadS: 2.6,          // a bubble shows this long before its word is sung
  freezeS: 0.8,        // ...and holds its spot from this long before
  aimTcS: 0.25,        // song-time smoothing of the re-aim, so speed wobble never jitters it
  mergeS: 0.12,        // two words closer than this share one bubble (a third starts the next one)
  phraseGapS: 0.3,     // silence longer than this ends a phrase
  phraseMax: 4,        // and so does a fifth bubble
  laneMoveS: 0.3,      // the silence a line needs before the next may sit in another lane
  lanes: [-0.62, -0.31, 0, 0.31, 0.62],   // lane shares of the usable half width
  gateRow: [-0.62, 0, 0.62],
  triggerRow: [-0.86, -0.43, 0, 0.43, 0.86],
  triggerMatchS: 0.2,  // a trigger's first word must start this close to the trigger
  minSpeedMps: 14,     // the aim never assumes a slower player (the countdown, a stall)
  popSM: 1.6, popLatM: 1.6,
  missBehindM: 3,      // passed by this much: missed
  missAfterS: 1.5,     // or this long after the word ended
  keepS: 1.2,          // popped and missed views stay this long for their OUT
  edgeMarginM: 2.4,    // lateral room kept clear of the rails
  heightM: 1.1,
  loopBackS: 1,        // tS this much below the last read is a loop, not jitter
});

const ROW_MAX = 5;
const CUT = /[,.;:!?…]["')\]]?$/;
/** What a bubble shows: the word with its clause punctuation and quotes taken off. */
export const faceOf = (w) => String(w || '').replace(/^["'(\[]+/, '').replace(/[,.;:!?…"')\]]+$/, '').trim();

/** Deterministic 0..1 from a seed and an index. */
export function hash01(seed, i) {
  let h = (Math.imul(seed | 0, 0x9e3779b1) ^ Math.imul((i | 0) + 1, 0x85ebca6b)) >>> 0;
  h = Math.imul(h ^ (h >>> 16), 0x7feb352d) >>> 0;
  h = Math.imul(h ^ (h >>> 15), 0x846ca68b) >>> 0;
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

/**
 * The script as bubbles, in sung order: [{ i, w, startS, endS, row (0 lane, 3 gate, 5 trigger), line, gapS, last }].
 * `line` is the phrase index (a row is a line of its own), `gapS` the silence before a line's first bubble,
 * `last` marks a line's final bubble.
 */
export function scriptBubbles(song, L = WORD_LOOK) {
  const words = ((song && song.words) || []).filter((w) => w && faceOf(w.w));
  const trig = ((song && song.triggers) || []).filter((t) => t.kind === 'word' && t.w);
  const out = [];
  let ti = 0;
  for (let k = 0; k < words.length; k++) {
    const w = words[k];
    // a trigger phrase: the words it names, from the one sung on the trigger, become one row
    while (ti < trig.length && trig[ti].startS < w.startS - L.triggerMatchS) ti++;
    const t = trig[ti];
    if (t && Math.abs(t.startS - w.startS) <= L.triggerMatchS) {
      const n = Math.max(1, Math.min(words.length - k, String(t.w).trim().split(/\s+/).length));
      const end = words[k + n - 1];
      out.push({ w: faceOf(t.w), startS: w.startS, endS: Math.max(end.endS, w.startS), row: 5, n: 1, cut: true });
      k += n - 1; ti++;
      continue;
    }
    const last = out[out.length - 1];
    if (last && !last.row && !w.gate && !last.cut && last.n < 2 && w.startS - last.startS < L.mergeS) {
      last.w += ' ' + faceOf(w.w); last.n = 2; last.endS = Math.max(last.endS, w.endS); last.cut = CUT.test(w.w);
      continue;
    }
    out.push({ w: faceOf(w.w), startS: w.startS, endS: Math.max(w.endS, w.startS), row: w.gate ? 3 : 0, n: 1, cut: CUT.test(w.w) || !!w.gate });
  }
  // phrases: a row is a line on its own; otherwise a gap, a cut or a full line starts the next
  let line = -1, count = 0, prevEnd = -Infinity, prevCut = true;
  for (const b of out) {
    const gap = b.startS - prevEnd;
    if (b.row || prevCut || count >= L.phraseMax || gap > L.phraseGapS) { line++; count = 0; b.gapS = gap; }
    else b.gapS = 0;
    b.line = line; count++;
    prevEnd = Math.max(b.endS, b.startS); prevCut = b.cut || b.row > 0;
  }
  out.forEach((b, i) => { b.i = i; b.last = i === out.length - 1 || out[i + 1].line !== b.line; delete b.cut; delete b.n; });
  return out;
}

/**
 * Lateral share per bubble: every bubble of one line in its line's lane; the lane walks one step per line
 * and only through a silence of laneMoveS or more (a seeded direction, turned back at the edges). Rows 0.
 */
export function laneShares(bubbles, seed, L = WORD_LOOK) {
  const xs = new Float32Array(bubbles.length), top = L.lanes.length - 1;
  let lane = top >> 1, line = -1;
  for (let i = 0; i < bubbles.length; i++) {
    const b = bubbles[i];
    if (b.line !== line) {
      line = b.line;
      if (!b.row && line > 0 && b.gapS >= L.laneMoveS) {   // the first line starts in the middle
        const dir = lane <= 0 ? 1 : lane >= top ? -1 : hash01(seed, line) < 0.5 ? -1 : 1;
        lane = clamp(lane + dir, 0, top);
      }
    }
    xs[i] = b.row ? 0 : L.lanes[lane];
  }
  return xs;
}

const signed = (d, lapM) => { const x = wrap(d, lapM); return x > lapM / 2 ? x - lapM : x; };

/**
 * @returns {{ update(tS: number, player: {s: number, lat: number, speedMps: number}): object[], live: object[],
 *   pops: number, shown: number, total: number, streak: number, best: number, lines: number,
 *   reset(seed?: number): void, onPop(fn: Function): () => void, words: object[] }}
 */
export function createWordRun(song, track, { seed = 1, look = WORD_LOOK } = {}) {
  const L = look, lapM = track.lengthM;
  const picked = scriptBubbles(song, L);
  const fr = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0], w: 14 };
  let shares = laneShares(picked, seed, L);
  const rowOf = (b) => (b.row === 5 ? L.triggerRow : b.row === 3 ? L.gateRow : null);
  // per bubble: its state; views are reused objects (no per-frame allocation)
  const B = picked.map((p) => ({ ...p, state: 'wait', s: 0, frozen: false, inS: 0, outS: 0, hit: -1,
    view: { id: p.i, w: p.w, gate: p.row > 0, row: p.row, line: p.line, state: 'wait', n: p.row || 1,
      xs: new Array(ROW_MAX).fill(0), p: Array.from({ length: ROW_MAX }, () => [0, 0, 0]),
      s: 0, inT: 0, outT: -1, remainS: 0, hit: -1 } }));
  const nLines = picked.length ? picked[picked.length - 1].line + 1 : 0;
  const lineSize = new Uint16Array(nLines), linePop = new Uint16Array(nLines);
  for (const p of picked) lineSize[p.line]++;
  const live = [], fns = [];
  let lastT = null, pops = 0, shown = 0, streak = 0, best = 0, lines = 0, first = 0;

  function pass() {
    for (const b of B) { b.state = 'wait'; b.frozen = false; b.hit = -1; }
    live.length = 0; linePop.fill(0); first = 0; streak = 0;
  }

  function aimAt(b, tS, player) {
    const v = Math.max(L.minSpeedMps, player.speedMps || 0);
    return player.s + v * Math.max(0, b.startS - tS);
  }

  function place(b) {
    const f = track.frameAt(wrap(b.s, lapM), fr);
    const usable = Math.max(0.5, f.w / 2 - L.edgeMarginM);
    const v = b.view, row = rowOf(b);
    v.s = wrap(b.s, lapM);
    for (let k = 0; k < v.n; k++) {
      const x = (row ? row[k] : shares[b.i]) * usable;
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
    // bubbles are in sung order: skip the ones long gone, stop at the first not yet due
    while (first < B.length && B[first].state === 'gone') first++;
    for (let i = first; i < B.length; i++) {
      const b = B[i];
      if (b.state === 'wait') {
        if (tS < b.startS - L.leadS) break;
        if (tS > b.startS) { b.state = 'gone'; streak = 0; continue; }   // a seek or a late start landed past it
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
          b.state = 'popped'; b.outS = tS; b.hit = hit; pops++; streak++; if (streak > best) best = streak;
          linePop[b.line]++;
          const lineDone = b.last && linePop[b.line] === lineSize[b.line];
          if (lineDone) lines++;
          const pp = b.view.p[hit];
          const e = { w: b.w, gate: b.row > 0, row: b.row, p: [pp[0], pp[1], pp[2]], tS, streak, lineDone, lineLen: lineSize[b.line], racer: 0 };
          for (const fn of fns) fn(e);
        } else if ((b.frozen && ds < -L.missBehindM) || tS > b.endS + L.missAfterS) { b.state = 'missed'; b.outS = tS; streak = 0; }
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
    get streak() { return streak; },
    get best() { return best; },
    get lines() { return lines; },
    words: picked,
    reset(s = seed) { seed = s; shares = laneShares(picked, seed, L); pass(); lastT = null; pops = 0; shown = 0; best = 0; lines = 0; },
    onPop(fn) { fns.push(fn); return () => { const i = fns.indexOf(fn); if (i >= 0) fns.splice(i, 1); }; },
  };
}
