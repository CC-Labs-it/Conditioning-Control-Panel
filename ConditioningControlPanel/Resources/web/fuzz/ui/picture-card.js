/* ui/picture-card.js - Addendum A: where the pictures, gifs and clips come from (owner: Scrolller pictures need a sign-in, otherwise the
 * player loads their own files). Own files are object URLs for this visit only: nothing uploaded, nothing stored. A reload
 * offers the picker again and the frames stay empty meanwhile. Sign-in is honest about not existing yet: no fake sign-in,
 * no fetch. Comes up on the Slideshow and Swipe Feed routes, and from the pause card. */

import { brandText } from '../data/brand.js';
import { PICTURE_CARD as C, X_LINES_PICTURES } from '../data/content/terms.js';
import { createLegal, esc } from './legal.js';

export const PICTURE_ROUTES = Object.freeze(['slides', 'scroll']);

/** The pause card's one line about the source. */
export function pictureRow(pictures) {
  if (pictures.source === 'online') return C.rowOnline;
  if (pictures.source === 'own') return pictures.ownCount ? C.rowOwn.replace('{n}', pictures.ownCount) : C.rowOwnEmpty;
  return C.rowNone;
}

/** Should the fork open the card: never chosen before, or the files chosen before are gone. */
export function pictureCardDue(pictures, asked) {
  return !asked || (pictures.source === 'own' && pictures.ownCount === 0);
}

export function openPictureCard({ doc = document, pictures, lane = null, lost = false, above = false, gentle = () => false, onChoice = () => {}, onClose = () => {} }) {
  const legal = createLegal({ doc, gentle, above, xLines: X_LINES_PICTURES });
  const line = lost ? C.lost.replace('{lane}', lane || '') : lane ? C.picked.replace('{lane}', lane) : C.change;
  const choice = (kind, title, sub) => `<button type="button" data-pick="${kind}"><b>${esc(title)}</b><small>${esc(sub)}</small></button>`;
  const win = legal.show({
    title: C.title,
    head: C.head,
    body: `<p class="intro">${esc(brandText(line))}</p>`
      + `<div class="src">${choice('signin', C.signin, C.signinSub)}${choice('files', C.files, C.filesSub)}${choice('none', C.none, C.noneSub)}</div>`
      + `<input class="tc-file" id="pic-files" type="file" multiple accept="image/*,video/*,.gif" tabindex="-1" aria-label="${esc(C.filesLabel)}">`
      + `<p class="status" id="pic-status" role="status"></p>`
      + (above ? `<div class="foot"><button class="link" id="pic-back" type="button">${esc(C.back)}</button></div>` : ''),
  });
  const status = win.querySelector('#pic-status');
  const input = win.querySelector('#pic-files');
  let open = true;
  const close = () => {
    if (!open) return;
    open = false;
    legal.close();
    onClose();
  };
  const pick = (kind) => win.querySelector(`[data-pick=${kind}]`);

  pick('signin').addEventListener('click', () => { status.textContent = C.signinStub; });
  /* The picker opens inside the press itself; a browser refuses it from anywhere later. */
  pick('files').addEventListener('click', () => { status.textContent = ''; input.click(); });
  input.addEventListener('change', () => {
    const kept = pictures.replaceOwn(input.files);
    input.value = '';
    if (!kept) { status.textContent = C.notPictures; return; }
    pictures.setSource('own');
    onChoice('own');
    close();
  });
  pick('none').addEventListener('click', () => {
    pictures.setSource('none');
    pictures.clearOwn();
    onChoice('none');
    close();
  });
  win.querySelector('#pic-back')?.addEventListener('click', close);
  pick('files').focus({ preventScroll: true });

  return {
    close,
    get open() { return open; },
    get above() { return above; },
  };
}
