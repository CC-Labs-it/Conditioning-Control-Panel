/* sim/roadWords.js - the song's ROAD words painted on the track like road markings (pass 4, owner 2026-10-23).
 *
 * Owner: "most of the words are written on the track, like road markings, so we see them as we pass by without
 * the need to get them all". Pure: no three.js, no DOM. sim/wordPick.js names the ROAD words (every sung word
 * that is not a bubble, a gate or a trigger); render/roadWords.js paints what this file places.
 * scriptRoad(song) builds the painted script once:
 *   - two road words sung closer than mergeS share one marking ("TO THE"), up to mergeMaxChars letters;
 *   - a PHRASE is a run of speech on one chart line with no gap over phraseGapS; one phrase keeps one lateral
 *     spot and one look (render/roadWordsMath.js picks the face per phrase);
 *   - a road word sung within clearS of a bubble moment is left unpainted: the bubble owns that stretch.
 * createRoadRun(song, track) then reads, every frame, the song position tS (music.songS, what the player hears)
 * and the local player's { s, lat, speedMps }. Each marking is painted leadS before its word at the spot the
 * kart will be when it is SUNG (the sim/words.js aim: the kart's real progress rate over song time, never a
 * drift's sideways speed), aheadM past the kart so the word lights just in front of the nose. While it is far
 * (beyond readM, and more than freezeS before its word) it may still ease toward the aim, too far off to read;
 * from then on it STAYS where it was painted. Markings run down the road in sung order, never on top of each
 * other (a marking starts gapM past the one before), and a bubble's spot keeps clearM of road on each side
 * clear. Behind the kart a marking fades out (render) and is dropped after outS. Every time is the song clock:
 * a frozen tS (pause) moves nothing; tS jumping back (the song loops) starts a fresh pass.
 */
import { clamp, wrap } from '../core/math.js';
import { pickWords, ROAD } from './wordPick.js';

export const ROAD_LOOK = Object.freeze({
  leadS: 2.9,          // a marking is painted this long before its word is sung
  freezeS: 1.5,        // ...and stays put from this long before it (or once within readM)
  readM: 55,           // closer than this the paint is readable: it never moves again
  aimTcS: 0.18,        // song-time smoothing of the far re-aim
  rateTcS: 0.2,        // smoothing of the kart's progress rate (metres of s per song second)
  minSpeedMps: 14,     // the aim never assumes a slower player (the countdown, a stall)
  aheadM: 3.2,         // the marking's centre sits this far past the predicted kart on its word
  bubbleAheadM: 1.4,   // where sim/words.js puts a bubble past the kart (the clear stretch centres on it)
  mergeS: 0.13,        // two road words closer than this share one marking
  mergeMaxChars: 9,    // ...when the two together stay this short
  phraseGapS: 0.45,    // silence longer than this ends a phrase (so does a new chart line)
  lenM: 10.2,          // taller faces remain readable from the low chase camera
  gapM: 1.6,           // road between two markings
  charM: 1.08,         // lateral metres per letter (an estimate for the edge clamp; render measures the real one)
  maxWidthM: 11,       // renderer also fits every strip row between the actual rails
  clearS: 0.22,        // a road word this close to a bubble moment is left unpainted
  clearM: 4.5,         // a bubble's predicted spot keeps this much road clear before and after
  followLat: 0.6,      // a phrase sits at this share of the player's lateral (toward the centre)...
  laneShare: 0.34,     // ...at most this share of the usable half width from the centre
  edgeMarginM: 1.4,    // lateral room kept clear of the rails
  behindM: 6,          // passed by this much (marking centre behind the kart): its OUT starts
  missAfterS: 3,       // or this long after its word ended
  outS: 1.1,           // the OUT lasts this long, then the marking is gone
  loopBackS: 1,        // tS this much below the last read is a loop, not jitter
});

const MARK = /[^A-Za-z0-9' -]/g;
/** What a marking shows: ASCII only, curly quotes straightened, clause punctuation and wrapping quotes off. */
export function markText(w) {
  return String(w || '').replace(/[‘’ʼ]/g, "'").replace(/[“”]/g, '"')
    .replace(MARK, '').replace(/^[' -]+|[' -]+$/g, '').trim();
}

// per song: the script (normalised charts are reused across races, so this is built once)
const SCRIPTS = new WeakMap();

/**
 * The painted script in sung order: [{ i, text, startS, endS, phrase, first, last, chars }].
 * `phrase` is the phrase index, `first` / `last` mark a phrase's ends, `chars` the letters (width estimate).
 * Also returns bubble moment times in `script.bubbles` (Float64Array) for the clear stretches.
 */
export function scriptRoad(song, L = ROAD_LOOK, kinds = null) {
  if (L === ROAD_LOOK && !kinds && song && SCRIPTS.has(song)) return SCRIPTS.get(song);
  const words = (song && song.words) || [];
  const kind = kinds || pickWords(song);
  const bub = [];
  for (let i = 0; i < words.length; i++) if (kind[i] !== ROAD) {
    const t = words[i].startS;
    if (!bub.length || t - bub[bub.length - 1] > 0.3) bub.push(t);   // a row's words are one moment
  }
  const bubbles = Float64Array.from(bub);
  const nearBubble = (t) => { for (const b of bubbles) if (Math.abs(b - t) < L.clearS) return true; return false; };
  const out = [];
  let prev = null, phrase = -1, prevEnd = -Infinity, prevLine = null;
  for (let i = 0; i < words.length; i++) {
    if (kind[i] !== ROAD) { prevEnd = Math.max(prevEnd, words[i].endS); prev = null; prevLine = null; continue; }
    const w = words[i], text = markText(w.w);
    if (!text || nearBubble(w.startS)) continue;
    const gap = w.startS - prevEnd;
    const newPhrase = phrase < 0 || prevLine === null || w.line !== prevLine || gap > L.phraseGapS;
    if (!newPhrase && prev && w.startS - prev.startS < L.mergeS && prev.n < 2 && prev.text.length + 1 + text.length <= L.mergeMaxChars) {
      prev.text += ' ' + text; prev.n = 2; prev.endS = Math.max(prev.endS, w.endS); prevEnd = Math.max(prevEnd, w.endS);
      continue;
    }
    if (newPhrase) phrase++;
    prev = { text, startS: w.startS, endS: Math.max(w.endS, w.startS), phrase, n: 1 };
    out.push(prev);
    prevEnd = Math.max(prevEnd, w.endS); prevLine = w.line;
  }
  out.forEach((e, i) => {
    e.i = i; e.first = i === 0 || out[i - 1].phrase !== e.phrase; e.last = i === out.length - 1 || out[i + 1].phrase !== e.phrase;
    e.chars = e.text.length; delete e.n;
  });
  out.bubbles = bubbles;
  out.phrases = out.length ? out[out.length - 1].phrase + 1 : 0;
  if (L === ROAD_LOOK && !kinds && song) SCRIPTS.set(song, out);
  return out;
}

const signed = (d, lapM) => { const x = wrap(d, lapM); return x > lapM / 2 ? x - lapM : x; };

/**
 * @returns {{ update(tS: number, player: {s: number, lat: number, speedMps: number}): object[], live: object[],
 *   reset(): void, script: object[], shown: number }}
 * A view: { id, text, phrase, state 'live' | 'out', s (road metres), x (lateral metres), lenM, inT (since painted),
 *   outT (-1 live), remainS (to its word), lenS (how long it is sung), dS (signed s from the kart), frozen }.
 */
export function createRoadRun(song, track, { look = ROAD_LOOK, script = null } = {}) {
  const L = look, lapM = track.lengthM;
  const S = script || scriptRoad(song, L);
  const fr = { p: [0, 0, 0], t: [0, 0, 0], n: [0, 0, 0], b: [0, 0, 0], w: 14 };
  const E = S.map((e) => ({ e, state: 'wait', s: 0, x: 0, frozen: false, inS: 0, outS: 0,
    view: { id: e.i, text: e.text, phrase: e.phrase, state: 'wait', s: 0, x: 0, lenM: L.lenM, inT: 0, outT: -1,
      remainS: 0, lenS: Math.max(0.08, e.endS - e.startS), dS: 0, frozen: false } }));
  const live = [];
  let lastT = null, lastS = null, rate = -1, first = 0, shown = 0, phraseX = 0, phraseOf = -1, prevPlaced = null;

  function pass() {
    for (const m of E) { m.state = 'wait'; m.frozen = false; }
    live.length = 0; first = 0; phraseOf = -1; prevPlaced = null;
  }

  function progressRate(player, dtS) {
    if (lastS !== null && dtS > 0) {
      const ds = signed(player.s - lastS, lapM), r = ds / dtS;
      if (Math.abs(ds) < 40 && r > -5) {
        const k = 1 - Math.exp(-dtS / L.rateTcS);
        rate = rate < 0 ? Math.max(0, r) : rate + (Math.max(0, r) - rate) * k;
      }
    }
    lastS = player.s;
  }

  const speedOf = (player) => Math.max(L.minSpeedMps, rate >= 0 ? rate : player.speedMps || 0);
  /**
   * Where the marking wants to be, in metres ahead of the kart: the kart on its word, pushed past the marking
   * before it and off any bubble spot. Markings keep a wrapped s that never changes once frozen; every compare
   * goes through signed(), so a lap wrap never moves one.
   */
  function aimAt(m, tS, player, prev) {
    const v = speedOf(player);
    let d = v * Math.max(0, m.e.startS - tS) + L.aheadM;
    if (prev) d = Math.max(d, signed(prev.s - player.s, lapM) + L.lenM + L.gapM);   // never on top of the one before
    // a bubble moment's spot: the marking's whole length stays clearM off it (pushed past it when it overlaps)
    for (const b of S.bubbles) {
      if (b < tS - 0.5 || b > m.e.startS + 1.5) continue;
      const bd = v * Math.max(0, b - tS) + L.bubbleAheadM;
      if (d + L.lenM / 2 > bd - L.clearM && d - L.lenM / 2 < bd + L.clearM) d = bd + L.clearM + L.lenM / 2;
    }
    return d;
  }

  function lateral(m, player) {
    const f = track.frameAt(wrap(m.s, lapM), fr);
    const usable = Math.max(0.5, f.w / 2 - L.edgeMarginM);
    const half = Math.min(L.maxWidthM, m.e.chars * L.charM) / 2;
    if (m.e.phrase !== phraseOf) {   // a phrase takes its spot once, from where the player drives now
      phraseOf = m.e.phrase;
      const lim = L.laneShare * usable;
      phraseX = clamp((player.lat || 0) * L.followLat, -lim, lim);
    }
    const room = Math.max(0, usable - half);
    return clamp(phraseX, -room, room);
  }

  function update(tS, player) {
    if (!Number.isFinite(tS) || !player) return live;
    if (lastT !== null && tS < lastT - L.loopBackS) { pass(); lastS = null; }
    const dtS = lastT === null ? 0 : Math.max(0, tS - lastT);
    lastT = tS;
    progressRate(player, dtS);
    const k = dtS > 0 ? 1 - Math.exp(-dtS / L.aimTcS) : 0;
    live.length = 0;
    while (first < E.length && E[first].state === 'gone') first++;
    let prev = null;
    for (let i = first; i < E.length; i++) {
      const m = E[i];
      if (m.state === 'gone') continue;
      if (m.state === 'wait') {
        if (tS < m.e.startS - L.leadS) break;
        if (tS > m.e.startS) { m.state = 'gone'; continue; }   // a seek or a late start landed past it
        prev = prevPlaced && prevPlaced.state !== 'gone' ? prevPlaced : null;
        m.state = 'live'; m.inS = tS; m.frozen = false;
        m.s = wrap(player.s + aimAt(m, tS, player, prev), lapM);
        m.x = lateral(m, player);
        m.prev = prev; prevPlaced = m; shown++;
      }
      const dS = signed(m.s - player.s, lapM);
      if (m.state === 'live') {
        if (!m.frozen) {
          if (m.e.startS - tS <= L.freezeS || dS <= L.readM) m.frozen = true;
          else {
            const pv = m.prev && m.prev.state !== 'gone' ? m.prev : null;
            m.s = wrap(player.s + dS + (aimAt(m, tS, player, pv) - dS) * k, lapM);
          }
        }
        if (dS < -L.behindM || tS > m.e.endS + L.missAfterS) { m.state = 'out'; m.outS = tS; }
      } else if (m.state === 'out' && tS - m.outS > L.outS) { m.state = 'gone'; continue; }
      const v = m.view;
      v.state = m.state; v.s = m.s; v.x = m.x; v.frozen = m.frozen;
      v.inT = tS - m.inS; v.outT = m.state === 'out' ? tS - m.outS : -1; v.remainS = m.e.startS - tS;
      v.dS = signed(m.s - player.s, lapM);
      live.push(v);
    }
    return live;
  }

  return {
    update, live, script: S,
    get shown() { return shown; },
    get total() { return S.length; },
    reset() { pass(); lastT = null; lastS = null; rate = -1; shown = 0; },
  };
}
