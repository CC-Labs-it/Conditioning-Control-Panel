/* ui/clause.js - the terms card every unlock asks you to accept (owner 2026-11-07, the game's core joke). Built on the legal
 * window (ui/legal.js): same veil, title bar, dead X and toast; Wake Up stays outside it and is never covered.
 * The card shortens over a run: purchases 1-8 tick every clause, 9-25 accept them all at once, 26+ show clause 1 and one
 * all-caps button. A later run (it has a mood) starts at the middle stage. A bundle shows clause 1 of the lead and of each
 * rider, at most three. Escape is never used here: it belongs to pause and panic. Copy: data/content/clauses.js. */

import { brandText } from '../data/brand.js';
import { richText, setRich } from './rich-text.js';
import { CLAUSES, CLAUSE_EXTRAS } from '../data/content/clauses.js';
import { createLegal, esc } from './legal.js';
import { BY_ID } from '../data/items.js';

export const STAGE_ONE_MAX = 8;
export const STAGE_TWO_MAX = 25;
export const BUNDLE_CAP = 3;
export const HEADS = Object.freeze({ 1: 'Please kindly accept the following terms to unlock:', 2: 'Accept the terms to unlock', 3: '' });

/** Which card: 1 ticks, 2 one button, 3 one clause and an all-caps button. `purchase` counts from 1. */
export function clauseStage(purchase, laterRun = false) {
  const s = purchase <= STAGE_ONE_MAX ? 1 : purchase <= STAGE_TWO_MAX ? 2 : 3;
  return laterRun ? Math.max(2, s) : s;
}

/** The clauses a card shows. A bundle: clause 1 of the lead and each rider, at most BUNDLE_CAP. */
export function clauseLines(id, riders = [], stage = 1, map = CLAUSES) {
  /* A question stage (dev/gen-items.mjs INTAKE_STAGES) is one moment: its own single clause, not one per row it brings. */
  if (riders.length && BY_ID[id]?.qstage && map[id]?.length) return map[id].slice(0, 1);
  if (riders.length) {
    const firsts = [id, ...riders].map((r) => map[r]?.[0]).filter(Boolean).slice(0, BUNDLE_CAP);
    return stage === 3 ? firsts.slice(0, 1) : firsts;
  }
  const own = map[id] || [];
  return stage === 3 ? own.slice(0, 1) : own.slice(0, 3);
}

/** Rows that never get a card: forks, endings, Journey ranks and specialties (they have their own flows). */
export const skipsCard = (item) => !item || item.kind === 'fork' || item.kind === 'end' || !!item.options || String(item.id).startsWith('rank_');

const pickOf = (list, rng) => list[Math.floor(rng() * list.length)] ?? list[0];

const CSS = `
.tc.clause .win { animation: tc-clause-in .16s ease-out 1; }
@keyframes tc-clause-in { from { opacity: 0; transform: translateY(6px); } to { opacity: 1; transform: none; } }
.tc.clause .paper { background: var(--tc-paper); border: 1px solid var(--tc-edge); padding: 10px 12px; font: 15px/1.32 var(--tc-legal); display: grid; gap: 8px; max-height: 46vh; overflow-y: auto; overscroll-behavior: contain; }
.tc.clause .paper p { margin: 0; }
.tc.clause .paper .checks label { font: 15px/1.32 var(--tc-legal); }
.tc.clause .late { font: 700 13px/1.2 var(--tc-ui); color: var(--tc-mute); text-transform: none; }
.tc.clause .plain.caps { font: 700 18px/1.1 var(--tc-ui); letter-spacing: .08em; padding: 10px 30px; }
.tc.clause .plain:not(:disabled) { border-color: #fff var(--tc-edge) var(--tc-edge) #fff; }
.tc.clause .accept-ready { box-shadow: 0 0 0 2px var(--tc-pink); }
.tc.clause .win.has-icon { position: relative; }
.tc.clause .win.has-icon .foot { padding-inline: 64px; }
.tc.clause .ticon { position: absolute; left: 10px; bottom: 10px; width: 52px; height: 52px; display: grid; place-items: center; background: var(--tc-paper); border: 1px solid var(--tc-edge); pointer-events: none; }
.tc.clause .ticon > * { width: 44px; height: 44px; }
@media (prefers-reduced-motion: reduce) { .tc.clause .win { animation: none; } }
body.gentle .tc.clause .win { animation-duration: .3s; }
`;

/**
 * Opens one card. Answers { close } and calls exactly one of onAccept() or onDecline(line), unless it is closed from outside
 * (pause, panic, an ending), which buys nothing.
 */
export function openClause({ doc, item, riders = [], purchase = 1, laterRun = false, gentle = () => false, rng = Math.random, onAccept, onDecline, icon = null }) {
  const stage = clauseStage(purchase, laterRun);
  const lines = clauseLines(item.id, riders, stage);
  if (!doc.getElementById('tc-clause-css')) {
    const st = doc.createElement('style');
    st.id = 'tc-clause-css';
    st.textContent = CSS;
    doc.head.appendChild(st);
  }
  const legal = createLegal({ doc, gentle, xLines: CLAUSE_EXTRAS.x });
  legal.veil.classList.add('clause');
  const name = brandText(item.name);
  const text = (t) => richText(t);
  const clauseHtml = stage === 1
    ? `<div class="checks">${lines.map((t, i) => `<label><input type="checkbox" data-clause="${i}"><span>${text(t)}</span></label>`).join('')}</div>`
    : lines.map((t) => `<p>${text(t)}</p>`).join('');
  const button = stage === 3 ? pickOf(CLAUSE_EXTRAS.accept, rng) : 'I accept';
  const body = `${stage === 3 ? `<p class="late">${esc(name)}</p>` : ''}<div class="paper">${clauseHtml}</div>`
    + `<div class="foot"><p class="status" aria-live="polite"></p><div class="btns">`
    + `<button type="button" class="plain${stage === 3 ? ' caps' : ''}" data-accept>${esc(button)}</button>`
    + `<button type="button" class="link" data-decline>Decline</button></div></div>`;
  const win = legal.show({ title: `Terms: ${name}`, head: HEADS[stage] || name, body, small: true });
  if (stage === 3) win.querySelector('#tc-head')?.remove();
  /* The unlock's own icon sits in the bottom left corner of the window (owner 2026-11-07). */
  if (icon) {
    const slot = doc.createElement('div');
    slot.className = 'ticon';
    slot.setAttribute('aria-hidden', 'true');
    slot.appendChild(icon);
    win.appendChild(slot);
    win.classList.add('has-icon');
  }
  const accept = win.querySelector('[data-accept]'), decline = win.querySelector('[data-decline]'), status = win.querySelector('.status');
  const boxes = [...win.querySelectorAll('input[data-clause]')];
  let done = false;
  const ready = () => boxes.every((b) => b.checked);
  const paint = () => {
    accept.disabled = !ready();
    accept.classList.toggle('accept-ready', boxes.length > 0 && ready());
    if (boxes.length) setRich(status, ready() ? pickOf(CLAUSE_EXTRAS.tick, rng) : '');
  };
  for (const b of boxes) b.addEventListener('change', paint);
  paint();

  function close() {
    if (done) return;
    done = true;
    legal.close();
  }
  accept.addEventListener('click', () => {
    if (done || !ready()) { if (!ready()) legal.nudge(); return; }
    close();
    onAccept?.();
  });
  decline.addEventListener('click', () => {
    if (done) return;
    const line = brandText(pickOf(CLAUSE_EXTRAS.decline, rng));
    close();
    onDecline?.(line);
  });
  legal.veil.addEventListener('keydown', (e) => {
    if (e.key !== 'Enter' || done || e.target === decline) return;
    if (e.target?.matches?.('input[type=checkbox]') && !ready()) return;
    e.preventDefault();
    accept.click();
  });
  (boxes[0] || accept).focus({ preventScroll: true });
  return { close, stage, lines, get closed() { return done; } };
}
