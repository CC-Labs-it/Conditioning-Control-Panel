// dev/copy-marks.mjs - the shop copy keywords (owner 2026-11-08: "bold, in the theme colour"; diegetic colours in their colour).
// dev/gen-items.mjs lays these over copy.json and rows.json, so the design source and its public guide stay plain.
// Per row: what / line -> list of words. 'word' = a keyword (**word**); 'pink:word' = a diegetic colour ([[pink:word]]).
// The first whole-word match is marked; a word that is not in the copy fails the build. Rules: handoff-1108/tc-copy-keywords.md.
// Rare on purpose: one mark a line, two at most, only the hypnotic word or the key noun that carries the line. Names stay plain.

export const COPY_MARKS = Object.freeze({
  softw: { what: ['moan'] },
  ticker: { what: ['thoughts'] },
  tunnel: { what: ['falling'], line: ['Deeper'] },
  drop: { what: ['pink:pink'] },
  colour: { what: ['pink:pink'], line: ['pink:pink'] },
  cursor: { what: ['pink:pink'] },
  paint: { what: ['pink:pink', 'black:black'] },
  e_spiral: { what: ['spiral'] },
  e_sublim: { what: ['blink'] },
  e_stare: { what: ['stare'], line: ['look'] },
  e_blink: { what: ['still'] },
  a_hum: { what: ['hum'] },
  a_phones: { what: ['headphones'] },
  a_whisp: { what: ['whispered'] },
  a_voice: { what: ['one word'] },
  a_bin: { what: ['inside your head'] },
  a_chant: { line: ['calm'] },
  a_close: { what: ['your ear'] },
  s_good: { what: ['praise'], line: ['so well'] },
  s_rest: { line: ['Stop'] },
  s_glow: { what: ['do nothing'] },
  t_orders: { what: ['obeying'], line: ['Now'] },
  t_denied: { what: ['refused'] },
  v_gif: { line: ['loops'] },
  d_scroll: { line: ['One more'] },
  b_radio: { what: ['voice'] },
  p_badge: { what: ['gold:gold'], line: ['gold:gold'] },
  p_gold: { what: ['gold:gold'] },
  p_line: { line: ['blankness'] },
  tr_head: { what: ['hypnosis'] },
  tr_still: { line: ['Nothing'] },
  tr_feed: { what: ['trance'] },
  tr_skin: { line: ['Follow it'] },
  tr_pod: { line: ['listening'] },
  tr_count: { line: ['Good'] },
  tr_deep: { line: ['Deeper'] },
  bi_iq: { what: ['IQ'] },
  bi_lips: { line: ['Mwah'] },
  bi_words: { what: ['easier words'] },
  bi_gloss: { line: ['Shiny'] },
  bi_giggle: { what: ['giggle'] },
  bi_end: { what: ['pink:bubble'] },
  dr_head: { line: ['Unit'] },
  dr_serial: { what: ['unit number'] },
  dr_term: { what: ['green:green', 'black:black'] },
  dr_hex: { line: ['accepted'] },
  dr_task: { line: ['Comply'] },
  dr_auto: { what: ['taps for you'] },
  si_head: { line: ['Shoulders back'] },
  si_curtsy: { line: ['Lower'] },
  si_posture: { what: ['sit up straight'], line: ['Stay'] },
  si_notes: { what: ['so good'] },
  si_end: { line: ['Good'] },
  go_hold: { what: ['hold'], line: ['let go'] },
  go_edge: { line: ['Almost'] },
  go_cave: { what: ['glowing screen'] },
  lo_head: { line: ['Click'] },
  lo_beg: { what: ['Beg'] },
  lo_key: { what: ['keyholder'] },
  lo_feed: { line: ['do not touch'] },
  lo_refuse: { line: ['Not yet'] },
  lo_tease: { line: ['So close'] },
  lo_end: { what: ['key'] },
  ce_head: { what: ['bars'] },
  ce_wall: { line: ['denied'] },
  ce_nums: { what: ['black:black bar'] },
  ce_all: { what: ['black:black bars'] },
  ce_end: { what: ['black:black bar'] },
  in_head: { what: ['pink:pink'] },
  in_spot: { what: ['pink:pink'] },
  in_spread: { what: ['pink:Pink'] },
  in_share: { what: ['pink:pink'] },
  in_end: { what: ['pink:pink', 'spiral'] },
  onlyyes: { what: ['yes'] },
  gold: { what: ['gold:gold button'] },
  flash: { line: ['Blink'] },
  drain: { what: ['blurs'] },
  triggers: { line: ['Catch it'] },
  listen: { line: ['Pay attention'] },
  x_dream: { what: ['soft and slow'] },
  x_story: { what: ['sleep story'], line: ['you stopped'] },
  x_drill: { line: ['Again'] },
  x_manners: { what: ['gold:gold paper'] },
  x_induct: { what: ['hypnosis induction'], line: ['eyes close'] },
  z_save: { line: ['blankness'] },
  dualwield: { line: ['Zero thoughts'] },
  word_comet: { what: ['gold:gold'] },
  sp_shapes: { line: ['Follow it'] },
  mini_deep: { line: ['Further in'] },
});

const COLOUR = /^(pink|green|blue|purple|red|gold|black|white|silver):(.+)$/;
const escRe = (s) => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

/** One field with its marks laid on. Throws when a listed word is missing, so stale marks never ship silently. */
export function markCopy(text, words, where = '') {
  let out = String(text ?? '');
  for (const w of words || []) {
    const m = COLOUR.exec(w), word = m ? m[2] : w;
    /* Whole words only, and never inside a mark laid a moment ago. */
    const re = new RegExp(`(?<![\\w*:])${escRe(word)}(?![\\w*\\]])`);
    if (!re.test(out)) throw new Error(`copy-marks: "${word}" not in ${where}: ${out}`);
    out = out.replace(re, m ? `[[${m[1]}:${word}]]` : `**${word}**`);
  }
  return out;
}
