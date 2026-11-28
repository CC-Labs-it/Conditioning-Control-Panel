/* ui/door-card.js - the small edition's door card (lane D, owner 2026-11-24, DOORS-BRIEF.md): every in-run door to the
 * full game (the shop's locked tile, a locked ending seed, the menu's door) opens THIS card first; no link ever opens
 * from the door's own press. The card is in the game's legal window look (ui/legal.js, like the picture card) and says
 * what the full game is. Two buttons: the link (the door's own nudge, sent from that real press) and Back. Once the run
 * has had its one link (ui/run-links.js, shared with the ad cards) the link button is gone and a line says why; Back
 * stays. Escape, Back and the X close it. Nothing here reads the edition: callers mount doors in small only.
 *
 * COPY: title = LOCKED_TILE_COPY.label and headline = SMALL_WALL_COPY.offer (approved, frozen, passed in by the
 * caller); the rest is DOOR_CARD_COPY, drafts (data/content/small-funnel.js). */

import { createLegal, esc } from './legal.js';
import { DOOR_CARD_COPY as C, SMALL_WALL_COPY } from '../data/content/small-funnel.js';

let current = null;

/** Opens the card. go(ev) sends the door's nudge from the link button's press and answers whether it went; links is
 *  { left(), spend() } (ui/run-links.js runLinks). Answers { close, open }. One card at a time: a second door press
 *  while one is up answers the open card. */
export function openDoorCard({ doc = document, title, head = SMALL_WALL_COPY.offer, go = () => false, links = { left: () => true, spend() {} },
  gentle = () => false, onClose = () => {}, makeLegal = createLegal } = {}) {
  if (current?.open) return current;
  const legal = makeLegal({ doc, gentle, above: !!doc.getElementById?.('pause') && !doc.getElementById('pause').hidden });
  const live = links.left();
  const win = legal.show({
    title,
    head,
    body: `<p class="intro">${esc(C.sub)}</p>`
      + '<div class="foot door-foot">'
      + (live ? `<div class="btns"><button class="plain" type="button" data-door="go">${esc(C.go)}</button></div>`
        : `<p class="status" data-door="spent">${esc(C.spent)}</p>`)
      + `<button class="link" type="button" data-door="back">${esc(C.back)}</button>`
      + '</div>',
  });
  win.classList.add('door-card');
  let open = true;
  const close = () => {
    if (!open) return;
    open = false;
    doc.removeEventListener('keydown', onKey, true);
    legal.close();
    if (current === card) current = null;
    onClose();
  };
  /* The legal window's X is dead on purpose (the contract's joke); this card's X closes. A clone drops its listener. */
  const deadX = win.querySelector('.fakex');
  if (deadX) {
    const x = deadX.cloneNode(true);
    x.setAttribute('aria-label', C.xLabel);
    x.style.cursor = 'pointer';
    x.style.color = '#fff';
    deadX.replaceWith(x);
    x.addEventListener('click', close);
  }
  /* Escape closes the card and nothing else: taken before the page's own Escape (pause) sees it. */
  function onKey(e) {
    if (e.key !== 'Escape' || !open) return;
    e.preventDefault();
    e.stopImmediatePropagation();
    close();
  }
  doc.addEventListener('keydown', onKey, true);
  win.querySelector('[data-door=back]').addEventListener('click', close);
  win.querySelector('[data-door=go]')?.addEventListener('click', (ev) => {
    if (!open || !links.left()) return;
    if (!go(ev)) return;   // not a real press, or nowhere to go: nothing spent, the card stays
    links.spend();
    close();
  });
  (win.querySelector('[data-door=back]'))?.focus?.({ preventScroll: true });
  const card = { close, get open() { return open; } };
  current = card;
  return card;
}

/** Test seam: forget a card left open by a test's fake page. */
export const _resetDoorCard = () => { current = null; };
