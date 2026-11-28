/* ui/llama-pen.js - the llama pen (owner 2026-11-21, llama round change 1). A small fenced pen in the top left corner
 * from the first second of a run: the herd the contract sheltered (life.llamas, 3 to 12) and its number in contract
 * words ("five (5)"). A press shows the latest Section 12 line (UPDATE_LINES, no new jokes). Llamas that joined since the
 * pen last showed walk in with small hops; at twelve the fence shows a gate. Gentle visuals and reduced motion: no walk,
 * no hop, the herd is just there. Below the dock rail width (phones, narrow windows) the pen is a chip with the number.
 * Nothing here writes the herd: the intro's opt-out does (ui/intro.js).
 * Named llamas (owner 2026-11-21, handoff-1121/llamas/BRIEF.md): each llama is a code-drawn icon with one tiny prop
 * from its outfit (data/content/llamas.js), every 7 to 13 s one of them spits (never in Gentle or reduced motion, never
 * while the game is paused or suspended), and hovering one (a tap on touch) shows its portrait, name and one-liner.
 * Pure rules first (tests need no page), then the mount. */

import { LLAMA_WORDS, TERMS, UPDATE_LINES } from '../data/content/terms.js';
import { herdRoster } from '../data/content/llamas.js';

export const PEN_MIN = 3, PEN_MAX = 12;
export const PEN_SAY_MS = 7000;
/** How long one newcomer takes to walk in, and the gap before the next one sets off. */
export const WALK_MS = 1400, WALK_GAP_MS = 700;
/** A spit every 7 to 13 s (random), lasting about a second. */
export const SPIT_MIN_MS = 7000, SPIT_MAX_MS = 13000, SPIT_MS = 1000;

const PORTRAITS = new URL('../assets/llamas/', import.meta.url).href;
const int = (v, d) => (Number.isFinite(Number(v)) ? Math.floor(Number(v)) : d);
const clamp = (n) => Math.min(PEN_MAX, Math.max(PEN_MIN, n));
const esc = (s) => String(s).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

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
  return { n, words: herdWords(n), gate: n >= PEN_MAX, newcomers: newcomers(seen, n), line: penLine(life), herd: herdRoster(n) };
}

/** Crowding: each llama gets a fixed step, tighter past six, so twelve still fit the corner (under 200 px). */
export const penStep = (n) => (n > 6 ? 12 : 18);

/** The wait before the next spit, from one random number in [0, 1). */
export const spitDelay = (r) => SPIT_MIN_MS + Math.min(0.999, Math.max(0, Number(r) || 0)) * (SPIT_MAX_MS - SPIT_MIN_MS);

/** Whether a spit may play now: never in Gentle or reduced motion, never paused, suspended or hidden. */
export const spitAllowed = ({ gentle = false, frozen = false, hidden = false } = {}) => !gentle && !frozen && !hidden;

/* ---- the drawn llama: side view facing right, cream fleece, brown ink, 26 x 28 ---- */
const INK = '#6b4a2b', FLEECE = '#f7f1e3';
const BODY = '<ellipse cx="10" cy="18.5" rx="8" ry="4.6"/><circle cx="2.6" cy="16.6" r="1.6"/>'
  + '<rect x="3.6" y="20" width="2" height="7" rx=".8"/><rect x="6.8" y="20.5" width="2" height="6.5" rx=".8"/>'
  + '<rect x="12.6" y="20.5" width="2" height="6.5" rx=".8"/><rect x="15.6" y="20" width="2" height="7" rx=".8"/>';
const HEAD = '<rect x="14.4" y="7.5" width="4.2" height="12" rx="2"/><ellipse cx="18.6" cy="7.6" rx="3.4" ry="2.7"/>'
  + '<ellipse cx="21.6" cy="8.6" rx="2.4" ry="1.8"/><path d="M16.2 6.2 L16.4 1.8 L18 5.4 Z"/><path d="M18.2 5.6 L19 1.4 L20.2 5.4 Z"/>';
/** Each herd member's one tiny prop, drawn over the head group (so it tilts with the spit). */
export const PROPS = Object.freeze({
  monocle: '<circle cx="19.2" cy="7.2" r="1.35" fill="none" stroke="#d4a017" stroke-width=".55"/><path d="M18.4 8.3 Q17.6 10.8 16.4 12.6" fill="none" stroke="#d4a017" stroke-width=".3"/>',
  lanyard: '<path d="M14.8 10.8 L16.6 14.9 L18.4 10.8" fill="none" stroke="#2f6fd0" stroke-width=".6"/><rect x="15.7" y="14.7" width="1.9" height="1.5" fill="#fff" stroke="#6b6b6b" stroke-width=".3"/>',
  pearls: '<g fill="#fff" stroke="#b3a7a0" stroke-width=".35"><circle cx="14.9" cy="12.6" r=".75"/><circle cx="16.3" cy="13.2" r=".75"/><circle cx="17.7" cy="13" r=".75"/><circle cx="18.7" cy="12.2" r=".7"/></g>',
  chain: '<path d="M14.6 11.6 Q16.6 15 18.6 11.6" fill="none" stroke="#d4a017" stroke-width="1.1"/><circle cx="16.6" cy="13.9" r=".8" fill="#d4a017"/>',
  hardhat: '<path d="M15.6 6 A3.2 2.8 0 0 1 22 6 Z" fill="#f5c400" stroke="#8a6d00" stroke-width=".5"/><path d="M14.8 6.1 H22.8" stroke="#8a6d00" stroke-width=".8"/>',
  tiara: '<path d="M16.6 5.3 L17.2 3.6 L18 4.8 L18.8 3 L19.6 4.8 L20.4 3.6 L21 5.3 Z" fill="#f6e6ff" stroke="#c0569a" stroke-width=".45"/>',
  headset: '<path d="M16.2 7.6 Q18.6 1.6 21 6.6" fill="none" stroke="#222" stroke-width=".9"/><circle cx="16.3" cy="8.1" r="1.1" fill="#222"/><path d="M16.6 9 Q18 11.2 20.6 10.6" fill="none" stroke="#222" stroke-width=".5"/>',
  cap: '<path d="M15.8 5.9 A3 2.4 0 0 1 21.6 5.9 Z" fill="#b3243a" stroke="#6b1020" stroke-width=".4"/><path d="M15.9 5.8 L13.4 6.4" stroke="#6b1020" stroke-width="1"/>',
  visor: '<path d="M16.2 5.4 L23.8 6 L23.4 7.2 L16.6 6.6 Z" fill="#3cb371" fill-opacity=".8" stroke="#1f6b40" stroke-width=".4"/>',
  stethoscope: '<path d="M14.8 11.2 Q15.4 15.6 17.2 13.4 Q18.6 11.6 18.4 10.6" fill="none" stroke="#555" stroke-width=".55"/><circle cx="16.2" cy="15.2" r=".75" fill="#9a9a9a" stroke="#555" stroke-width=".3"/>',
  sweater: '<path d="M13.4 13.4 Q16.4 16.9 19.4 13.4" fill="none" stroke="#8c7650" stroke-width="2.2" stroke-linecap="round"/><path d="M13.4 13.4 Q16.4 16.9 19.4 13.4" fill="none" stroke="#d9c79c" stroke-width="1.4" stroke-linecap="round"/><path d="M16.4 15.8 L15.6 18.2 M16.6 15.8 L17.6 18" stroke="#8c7650" stroke-width="1.1" stroke-linecap="round"/>',
  scarf: '<g fill="#c8507a" stroke="#7d2a4a" stroke-width=".35"><rect x="14.1" y="10.6" width="4.8" height="1.9" rx=".5"/><rect x="15" y="12.3" width="1.5" height="4" rx=".4"/></g><path d="M15.3 13.6 H16.2 M15.3 14.9 H16.2" stroke="#7d2a4a" stroke-width=".3"/>',
});

/** One llama icon as inline SVG. Outline first (ink, fat stroke), fleece on top, so only the outer edge shows. */
export function llamaSvg(prop = null) {
  const two = (shapes) => `<g fill="${INK}" stroke="${INK}" stroke-width="1.5" stroke-linejoin="round">${shapes}</g><g fill="${FLEECE}">${shapes}</g>`;
  return '<svg class="lp-ic" viewBox="0 0 26 28" width="20" height="22" aria-hidden="true">'
    + `<g class="lp-body">${two(BODY)}</g>`
    + `<g class="lp-head">${two(HEAD)}<circle cx="19.2" cy="7.2" r=".7" fill="#2a2130"/>`
    + `<path d="M22.6 9.4 Q23.3 9.9 23.8 9.5" fill="none" stroke="${INK}" stroke-width=".5"/>${PROPS[prop] || ''}</g>`
    + '<circle class="lp-drop" cx="24" cy="9" r="1.4" fill="#eaf6ff" stroke="#6f97b8" stroke-width=".5"/></svg>';
}

const GATE_SVG = '<svg viewBox="0 0 16 18" width="16" height="18" aria-hidden="true"><g fill="none" stroke="#6b4a2b" stroke-width="2" stroke-linecap="round"><path d="M2 2v15"/><path d="M2 5l12 -3M2 12l12 -3M14 2v8M2 12l12 -10"/></g></svg>';

let spitTimer = 0;

/** Mounts the pen. `extras.penHerd` remembers the herd it last showed; `save` writes it; `frozen` says the game is paused
 *  or suspended. Returns { el, say, hide, model, card, spit }. */
export function mountLlamaPen({ doc = document, life, extras = {}, gentle = () => false, frozen = () => false, save = () => {}, random = Math.random }) {
  clearTimeout(spitTimer);
  for (const id of ['llama-pen', 'llama-say', 'llama-card']) doc.getElementById(id)?.remove();
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
  const herd = m.herd.map((l, i) => {
    const k = i - (m.n - m.newcomers);   // newcomers are the last ones in the row, nearest the gate side
    const style = walk && k >= 0 ? ` style="animation-delay:${k * WALK_GAP_MS}ms"` : '';
    return `<span class="lp-l${walk && k >= 0 ? ' lp-walk' : ''}" data-llama="${l.id}"${style}>${llamaSvg(l.prop)}</span>`;
  }).join('');
  pen.innerHTML = `<span class="lp-fence" style="--lp-step:${penStep(m.n)}px"><span class="lp-herd">${herd}</span>`
    + `${m.gate ? `<span class="lp-gate">${GATE_SVG}</span>` : ''}</span>`
    + `<span class="lp-n" aria-hidden="true">${m.words} llamas</span>`
    + `<span class="lp-chip" aria-hidden="true"><i>${llamaSvg()}</i><b>${m.n}</b></span>`;
  const bubble = doc.createElement('div');
  bubble.id = 'llama-say';
  bubble.className = 'lp-say';
  bubble.setAttribute('role', 'status');
  bubble.hidden = true;
  const card = doc.createElement('div');
  card.id = 'llama-card';
  card.className = 'lp-say lp-card';
  card.setAttribute('role', 'tooltip');
  card.hidden = true;
  host.append(pen, bubble, card);

  let timer = 0;
  const hide = () => {
    clearTimeout(timer);
    bubble.hidden = true;
    pen.setAttribute('aria-expanded', 'false');
  };
  const say = () => {
    clearTimeout(timer);
    hideCard();
    bubble.textContent = penLine(life);
    bubble.hidden = false;
    pen.setAttribute('aria-expanded', 'true');
    timer = setTimeout(hide, PEN_SAY_MS);
  };

  /* The llama card: round portrait, name, one-liner, under the llama it belongs to. */
  let cardFor = null, cardTimer = 0;
  function hideCard() { clearTimeout(cardTimer); card.hidden = true; cardFor = null; }
  function showCard(slot) {
    const l = m.herd.find((x) => x.id === slot?.dataset.llama);
    if (!l) return;
    clearTimeout(cardTimer);
    if (cardFor !== l.id) {
      card.innerHTML = `<img src="${PORTRAITS}${l.id}.webp" alt="" width="44" height="44" decoding="async"><span><b>${esc(l.name)}</b><span>${esc(l.line)}</span></span>`;
      cardFor = l.id;
    }
    hide();
    card.hidden = false;
    const r = slot.getBoundingClientRect(), vw = doc.documentElement.clientWidth || 9999;
    card.style.left = `${Math.max(8, Math.min(r.left - 10, vw - card.offsetWidth - 8))}px`;
    cardTimer = setTimeout(hideCard, PEN_SAY_MS);
  }
  const slotOf = (e) => e.target?.closest?.('.lp-l');
  let touch = false;
  pen.addEventListener('pointerdown', (e) => { touch = e.pointerType !== 'mouse'; });
  pen.addEventListener('pointerover', (e) => { if (e.pointerType === 'mouse' && slotOf(e)) showCard(slotOf(e)); });
  pen.addEventListener('pointerleave', (e) => { if (e.pointerType === 'mouse') hideCard(); });
  pen.addEventListener('click', (e) => {
    const slot = touch ? slotOf(e) : null;
    touch = false;
    if (slot) { if (cardFor === slot.dataset.llama && !card.hidden) hideCard(); else showCard(slot); return; }
    if (bubble.hidden) say(); else hide();
  });
  bubble.addEventListener('click', hide);
  card.addEventListener('click', hideCard);

  /* The spit: one random llama, every 7 to 13 s, only while the game runs and motion is allowed. */
  const slots = () => [...pen.querySelectorAll('.lp-l')];
  const spit = (slot = slots()[Math.floor(random() * slots().length)]) => {
    if (!slot) return null;
    slot.classList.remove('lp-walk', 'lp-spit');
    void slot.offsetWidth;   // restart the keyframes
    slot.classList.add('lp-spit');
    setTimeout(() => slot.classList.remove('lp-spit'), SPIT_MS + 50);
    return slot;
  };
  const tick = () => {
    if (!pen.isConnected) return;
    const win = doc.defaultView;
    const reduced = !!win?.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
    if (spitAllowed({ gentle: gentle() || reduced, frozen: frozen(), hidden: !!doc.hidden })) spit();
    spitTimer = setTimeout(tick, spitDelay(random()));
  };
  spitTimer = setTimeout(tick, spitDelay(random()));

  if (extras.penHerd !== m.n) { extras.penHerd = m.n; save(); }
  return { el: pen, say, hide, model: m, card: { show: showCard, hide: hideCard, el: card }, spit };
}
