/* ui/llama-pen.js - the llama pen (owner 2026-11-21, llama round change 1). A small fenced pen in the top left corner
 * from the first second of a run: the herd the contract sheltered (life.llamas, 3 to 12, the same glyph as the Living Ad
 * card) and its number in contract words ("five (5)"). A press shows the latest Section 12 line (UPDATE_LINES, no new
 * jokes). Llamas that joined since the pen last showed walk in with small hops; at twelve the fence shows a gate. Gentle
 * visuals and reduced motion: no walk, no hop, the herd is just there. Below the dock rail width (phones, narrow windows)
 * the pen is a chip with the number. Nothing here writes the herd: the intro's opt-out does (ui/intro.js).
 * Pure rules first (tests need no page), then the mount. */

import { LLAMA_WORDS, TERMS, UPDATE_LINES } from '../data/content/terms.js';

export const PEN_MIN = 3, PEN_MAX = 12;
export const PEN_GLYPH = '\u{1F999}';
export const PEN_SAY_MS = 7000;
/** How long one newcomer takes to walk in, and the gap before the next one sets off. */
export const WALK_MS = 1400, WALK_GAP_MS = 700;

const int = (v, d) => (Number.isFinite(Number(v)) ? Math.floor(Number(v)) : d);
const clamp = (n) => Math.min(PEN_MAX, Math.max(PEN_MIN, n));

/** The herd on the save, held to what the game can shelter. */
export const herdOf = (life) => clamp(int(life?.llamas, PEN_MIN));

/** Contract words: "five (5)". */
export const herdWords = (n) => `${LLAMA_WORDS[clamp(int(n, PEN_MIN))]} (${clamp(int(n, PEN_MIN))})`;

/** How many llamas walk in now: the herd past what the pen showed last (a save from before the pen counts as three). */
export function newcomers(seen, n) {
  const was = clamp(int(seen, PEN_MIN));
  return Math.max(0, clamp(int(n, PEN_MIN)) - was);
}

/** Section 12 as the contract prints it, tags stripped, with the herd's own number. */
export function section12(n) {
  const src = TERMS.find((t) => t.startsWith('<b>12.')) || '';
  return src.replace(/<[^>]+>/g, '').replace('three (3)', herdWords(n));
}

/** The llama counts a line names ("five llamas", "One llama", "3 llamas"), as numbers. */
export function namedCounts(line) {
  const out = [];
  for (const m of String(line).matchAll(/\b([a-z]+|\d+) llamas?\b/gi)) {
    const w = m[1].toLowerCase();
    const n = /^\d+$/.test(w) ? Number(w) : LLAMA_WORDS.indexOf(w);
    if (n >= 0) out.push(n);
  }
  return out;
}

/** The pen's line: the update the player last accepted on a later visit, or Section 12 itself before any update. An
 *  update that names a llama count other than the herd in the pen falls back to Section 12 with the real number. */
export function penLine(life) {
  const u = int(life?.updates, 0), n = herdOf(life);
  const line = u > 0 ? UPDATE_LINES[(u - 1) % UPDATE_LINES.length] : '';
  return line && namedCounts(line).every((c) => c === n) ? line : section12(n);
}

/** The whole picture of the pen for one run. */
export function penModel(life, seen) {
  const n = herdOf(life);
  return { n, words: herdWords(n), gate: n >= PEN_MAX, newcomers: newcomers(seen, n), line: penLine(life) };
}

/** Crowding: each llama gets a fixed step, tighter past six, so twelve still fit the corner (under 200 px) whatever the emoji font. */
export const penStep = (n) => (n > 6 ? 12 : 18);

const GATE_SVG = '<svg viewBox="0 0 16 18" width="16" height="18" aria-hidden="true"><g fill="none" stroke="#6b4a2b" stroke-width="2" stroke-linecap="round"><path d="M2 2v15"/><path d="M2 5l12 -3M2 12l12 -3M14 2v8M2 12l12 -10"/></g></svg>';

/** Mounts the pen. `extras.penHerd` remembers the herd it last showed; `save` writes it. Returns { el, say, hide, model }. */
export function mountLlamaPen({ doc = document, life, extras = {}, gentle = () => false, save = () => {} }) {
  doc.getElementById('llama-pen')?.remove();
  doc.getElementById('llama-say')?.remove();
  const m = penModel(life, extras.penHerd);
  const host = doc.getElementById('world') || doc.body;
  const pen = doc.createElement('button');
  pen.id = 'llama-pen';
  pen.type = 'button';
  pen.setAttribute('aria-label', `Llama pen: ${m.words} llamas`);
  pen.setAttribute('aria-expanded', 'false');
  pen.setAttribute('aria-controls', 'llama-say');
  if (m.gate) pen.dataset.gate = '';
  const walk = !gentle() && m.newcomers > 0;
  const herd = Array.from({ length: m.n }, (_, i) => {
    const k = i - (m.n - m.newcomers);   // newcomers are the last ones in the row, nearest the gate side
    const style = walk && k >= 0 ? ` style="animation-delay:${k * WALK_GAP_MS}ms"` : '';
    return `<span class="lp-l${walk && k >= 0 ? ' lp-walk' : ''}"${style}>${PEN_GLYPH}</span>`;
  }).join('');
  pen.innerHTML = `<span class="lp-fence" style="--lp-step:${penStep(m.n)}px"><span class="lp-herd">${herd}</span>`
    + `${m.gate ? `<span class="lp-gate">${GATE_SVG}</span>` : ''}</span>`
    + `<span class="lp-n" aria-hidden="true">${m.words} llamas</span>`
    + `<span class="lp-chip" aria-hidden="true"><i>${PEN_GLYPH}</i><b>${m.n}</b></span>`;
  const bubble = doc.createElement('div');
  bubble.id = 'llama-say';
  bubble.className = 'lp-say';
  bubble.setAttribute('role', 'status');
  bubble.hidden = true;
  host.append(pen, bubble);

  let timer = 0;
  const hide = () => {
    clearTimeout(timer);
    bubble.hidden = true;
    pen.setAttribute('aria-expanded', 'false');
  };
  const say = () => {
    clearTimeout(timer);
    bubble.textContent = penLine(life);
    bubble.hidden = false;
    pen.setAttribute('aria-expanded', 'true');
    timer = setTimeout(hide, PEN_SAY_MS);
  };
  pen.addEventListener('click', () => { if (bubble.hidden) say(); else hide(); });
  bubble.addEventListener('click', hide);

  if (extras.penHerd !== m.n) { extras.penHerd = m.n; save(); }
  return { el: pen, say, hide, model: m };
}
