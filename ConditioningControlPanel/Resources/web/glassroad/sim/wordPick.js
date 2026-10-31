/* sim/wordPick.js - which sung words ride a bubble and which are painted on the road (pass 4, owner 2026-10-23).
 *
 * Owner: "most of the words written on the track, like road markings, so we see them as we pass by without the
 * need to get them all; we keep some words, the most impactful ones, in bubbles: way less bubbles to gather".
 * Pure: no three.js, no DOM. pickWords(song) returns one kind per chart word (song.words order):
 *   ROAD     painted on the road surface (render/roadWords.js), never collected
 *   BUBBLE   an impactful word in a bubble (sim/words.js), at most one per line and minGapS apart
 *   GATE     a chart `gate: true` word, always a bubble (a row of three)
 *   TRIGGER  a word of a chart trigger phrase (triggers kind 'word', "good girl"), always a bubble (a row of five)
 * Gate and trigger words are kept whatever the spacing. The rest of the bubbles come from the chart's
 * `bubble: true` words, best first (an IMPACT word, then a longer word), each one at least minGapS from every
 * bubble already taken and alone on its line (impactOnly: IMPACT words only). Deterministic: the same chart gives the same pick.
 */

export const ROAD = 0, BUBBLE = 1, GATE = 2, TRIGGER = 3;

export const PICK = Object.freeze({
  minGapS: 4.5,          // two bubble moments never closer than this (trigger and gate words excepted)
  triggerMatchS: 0.2,    // a trigger's first word starts this close to the trigger (same as sim/words.js)
  impactBonus: 10,       // an IMPACT word outranks any plain word
  impactOnly: true,      // only IMPACT words become plain bubbles (owner: 'the most impactful ones')
});

/** The words that carry the song (lowercase, no punctuation). Only chart `bubble: true` words are candidates. */
export const IMPACT = Object.freeze(new Set([
  'bambi', 'pink', 'good', 'girl', 'accept', 'allowed', 'softer', 'soft', 'breathe', 'drop', 'deeper', 'sleep',
  'breath', 'smile', 'warm', 'focus', 'obey', 'empty', 'brain', 'pretending', 'comfy', 'easy', 'pulse', 'brighter',
]));

const clean = (w) => String(w || '').toLowerCase().replace(/[^a-z']/g, '');

/** @returns {Uint8Array} one kind per song.words entry. */
export function pickWords(song, P = PICK) {
  const words = (song && song.words) || [];
  // RACE YOUR OWN SONG: a heard chart has no authored IMPACT list; its confident longer words (chart bubble marks)
  // are the bubbles. Decided here so road, air and bubble runs agree on one pick.
  if (song && song.userSong && P.impactOnly) P = { ...P, impactOnly: false };
  const kind = new Uint8Array(words.length);
  const taken = [];   // startS of every bubble moment
  // trigger phrases: the run of words starting on the trigger, as many as the phrase names
  for (const t of (song && song.triggers) || []) {
    if (t.kind !== 'word' || !t.w) continue;
    const parts = String(t.w).trim().split(/\s+/), n = parts.length, first = clean(parts[0]);
    // the trigger's own first word near its time (a word sung just before it, "you drop", is not the trigger)
    let i0 = -1;
    for (let i = 0; i < words.length; i++) {
      if (Math.abs(words[i].startS - t.startS) > P.triggerMatchS || clean(words[i].w) !== first) continue;
      if (i0 < 0 || Math.abs(words[i].startS - t.startS) < Math.abs(words[i0].startS - t.startS)) i0 = i;
    }
    if (i0 < 0) continue;
    for (let i = i0; i < Math.min(words.length, i0 + n); i++) kind[i] = TRIGGER;
    taken.push(words[i0].startS);
  }
  for (let i = 0; i < words.length; i++) if (!kind[i] && words[i].gate) { kind[i] = GATE; taken.push(words[i].startS); }
  const lineUsed = new Set();
  for (let i = 0; i < words.length; i++) if (kind[i]) lineUsed.add(words[i].line);
  const cand = [];
  for (let i = 0; i < words.length; i++) {
    const w = words[i];
    if (kind[i] || !w.bubble) continue;
    const c = clean(w.w);
    if (P.impactOnly && !IMPACT.has(c)) continue;
    cand.push({ i, score: (IMPACT.has(c) ? P.impactBonus : 0) + Math.min(c.length, 9) });
  }
  cand.sort((a, b) => b.score - a.score || words[a.i].startS - words[b.i].startS);
  for (const c of cand) {
    const w = words[c.i];
    if (lineUsed.has(w.line)) continue;
    if (taken.some((s) => Math.abs(s - w.startS) < P.minGapS)) continue;
    kind[c.i] = BUBBLE; taken.push(w.startS); lineUsed.add(w.line);
  }
  return kind;
}

/** Counts per kind, for tests and the dev beacon. */
export function pickStats(kind) {
  const n = [0, 0, 0, 0];
  for (const k of kind) n[k]++;
  return { road: n[ROAD], bubble: n[BUBBLE], gate: n[GATE], trigger: n[TRIGGER] };
}
