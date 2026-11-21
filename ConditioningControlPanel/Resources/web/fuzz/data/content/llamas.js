/* data/content/llamas.js - the named herd (owner 2026-11-21, handoff-1121/llamas/BRIEF.md). Twelve llamas with names,
 * one-liners and a portrait each (assets/llamas/<id>.webp, 384 square, generated and approved). Herd order: the three
 * Section 12 shelters are the panel (the chair, then the two others), then the rest join in this order as the herd grows
 * to twelve. `prop` is the one tiny thing the pen's drawn llama wears (ui/llama-pen.js), read at icon size. */

export const LLAMAS = Object.freeze([
  { id: 'reginald', name: 'Sir Reginald the Third of Hoofelton', role: 'chair', prop: 'monocle', line: 'Presides. Chews. Presides again.' },
  { id: 'margaret', name: 'Dame Margaret Fleecington', role: 'panel', prop: 'pearls', line: 'Has read your file. Twice. Disapproves.' },
  { id: 'henry', name: 'Henry the Sex Machine', role: 'panel', prop: 'chain', line: 'Sits on the panel for reasons nobody will put in writing.' },
  { id: 'greg', name: 'Greg', role: 'herd', prop: 'lanyard', line: 'Just Greg. Has a desk somewhere.' },
  { id: 'bob', name: 'Bob', role: 'herd', prop: 'hardhat', line: 'Health and safety. Mostly the second one.' },
  { id: 'lashes', name: 'Lady Lashes', role: 'herd', prop: 'tiara', line: 'Blinks slowly at you. You blink back.' },
  { id: 'kevin', name: 'Kevin', role: 'herd', prop: 'headset', line: 'Muted on every call. Unmuted on none.' },
  { id: 'spitz', name: 'DJ Spitz', role: 'herd', prop: 'cap', line: 'Drops beats. Drops spit. Same timing.' },
  { id: 'gary', name: 'Gary from Accounts', role: 'herd', prop: 'visor', line: 'Your feed is not included. Gary checked.' },
  { id: 'doctor', name: 'Dr. Alpaca (not an alpaca)', role: 'herd', prop: 'stethoscope', line: 'Not an alpaca. Please stop asking.' },
  { id: 'bartholomew', name: 'Bartholomew Fluffington-Smythe', role: 'herd', prop: 'sweater', line: 'Summers somewhere. Winters somewhere else.' },
  { id: 'nana', name: 'Nana Spitwell', role: 'herd', prop: 'scarf', line: 'Knits. Spits. Remembers everything.' },
].map((l) => Object.freeze(l)));

export const LLAMA_BY_ID = Object.freeze(Object.fromEntries(LLAMAS.map((l) => [l.id, l])));

/** The panel, in seat order on the bench as the picture shows it: left, chair, right. */
export const PANEL_SEATS = Object.freeze(['margaret', 'reginald', 'henry']);

/** The portrait of one llama, relative to the game root. */
export const llamaPortrait = (id) => `assets/llamas/${id}.webp`;

/** The first n of the herd (clamped to 0..12): who is in the pen with n llamas. */
export function herdRoster(n) {
  const k = Math.max(0, Math.min(LLAMAS.length, Math.floor(Number(n)) || 0));
  return LLAMAS.slice(0, k);
}
