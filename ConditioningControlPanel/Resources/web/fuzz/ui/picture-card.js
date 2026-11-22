/* Pick a media source at startup or while playing. The host handles account consent.
 * Local files stay in the browser for this visit. */

import { brandText } from '../data/brand.js';
import { richText } from './rich-text.js';
import { PICTURE_CARD as C, X_LINES_PICTURES } from '../data/content/terms.js';
import { createLegal, esc } from './legal.js';
import { filesFromDrop, folderPickerWorks } from './folder-files.js';

/** The pause card's one line about the source. */
export function pictureRow(pictures) {
  if (pictures.source === 'scrolller') {
    const meter = pictures.usedPercent;
    return Number.isInteger(meter) ? `Pictures via Scrolller · ${meter}% of today's allowance used` : 'Pictures via Scrolller';
  }
  if (pictures.source === 'online') return C.rowOnline;
  if (pictures.source === 'own') return pictures.ownCount ? C.rowOwn.replace('{n}', pictures.ownCount) : C.rowOwnEmpty;
  return C.rowNone;
}

export function openPictureCard({ doc = document, pictures, above = false, gentle = () => false, onChoice = () => {}, onClose = () => {},
  navigate = (url) => doc.defaultView?.location.assign(url) }) {
  const legal = createLegal({ doc, gentle, above, xLines: X_LINES_PICTURES });
  /* Folders (owner 2026-11-08): desktop browsers only; a phone has no folder picker and no drop. */
  const folders = folderPickerWorks(doc);
  const choice = (kind, title, sub) => `<button type="button" data-pick="${kind}"><b>${esc(title)}</b><small>${esc(sub)}</small></button>`;
  const hosted = pictures.hasScrolllerHost;
  const first = hosted ? choice('signin', C.scrolller, C.signinSub) : choice('signin', C.signin, C.signinSiteSub);
  const perks = hosted ? '' : `<div class="perks">${C.perks.map(line => `<p>${esc(line)}</p>`).join('')}</div>`;
  const win = legal.show({
    title: C.title,
    head: C.head,
    body: `<p class="intro">${richText(C.change)}</p>`
      + `<div class="src">${first}${choice('files', C.files, C.filesSub)}${(folders ? `<p class="folder-row"><button type="button" id="pic-folder">${esc(C.folder)}</button> ${esc(C.folderDrop)}</p>` : '')}${choice('none', C.none, C.noneSub)}</div>`
      + perks
      + `<input class="tc-file" id="pic-files" type="file" multiple accept="image/*,video/*,.gif" tabindex="-1" aria-label="${esc(C.filesLabel)}">`
      + (folders ? `<input class="tc-file" id="pic-folder-input" type="file" webkitdirectory multiple tabindex="-1" aria-label="${esc(C.filesLabel)}">` : '')
      + `<p class="status" id="pic-status" role="status"></p>`
      + (above ? `<div class="foot"><button class="link" id="pic-back" type="button">${esc(C.back)}</button></div>` : ''),
  });
  const status = win.querySelector('#pic-status');
  const input = win.querySelector('#pic-files');
  let open = true, cancelRequest = null;
  const close = () => {
    if (!open) return;
    open = false;
    cancelRequest?.();
    legal.close();
    onClose();
  };
  const pick = (kind) => win.querySelector(`[data-pick=${kind}]`);

  const remote = pick('signin');
  remote.addEventListener('click', (ev) => {
    /* Standalone: the same game inside the website, in this tab. Only from the player's own press. */
    if (!hosted) { if (ev.isTrusted) navigate(C.signinUrl); return; }
    remote.disabled = true;
    status.textContent = C.checking;
    cancelRequest = pictures.chooseScrolller((accepted, reason) => {
      if (!open) return;
      if (accepted) { onChoice('scrolller'); close(); }
      else {
        remote.disabled = false;
        status.textContent = C.refused[reason] || C.refusedOther;
      }
    });
  });
  /* The picker opens inside the press itself; a browser refuses it from anywhere later. */
  pick('files').addEventListener('click', () => {
    cancelRequest?.();
    cancelRequest = null;
    remote.disabled = false;
    status.textContent = '';
    input.click();
  });
  /** One pick of own files (a list, a folder or a drop) replaces the last. */
  const useFiles = (files) => {
    if (!open) return;
    const kept = pictures.replaceOwn(files);
    if (!kept) { status.textContent = C.notPictures; return; }
    pictures.setSource('own');
    onChoice('own');
    close();
  };
  input.addEventListener('change', () => { const files = [...input.files]; input.value = ''; useFiles(files); });
  if (folders) {
    const dir = win.querySelector('#pic-folder-input');
    win.querySelector('#pic-folder').addEventListener('click', () => { cancelRequest?.(); cancelRequest = null; remote.disabled = false; status.textContent = ''; dir.click(); });
    dir.addEventListener('change', () => { const files = [...dir.files]; dir.value = ''; useFiles(files); });
    /* A drop anywhere on the card: files and folders, any mix, read to the bottom. */
    const veil = win.closest('.tc') || win;
    let depth = 0;
    const hasFiles = (ev) => [...(ev.dataTransfer?.types || [])].includes('Files');
    veil.addEventListener('dragenter', (ev) => { if (!hasFiles(ev)) return; ev.preventDefault(); depth++; win.classList.add('dropping'); });
    veil.addEventListener('dragover', (ev) => { if (!hasFiles(ev)) return; ev.preventDefault(); ev.dataTransfer.dropEffect = 'copy'; });
    veil.addEventListener('dragleave', () => { depth = Math.max(0, depth - 1); if (!depth) win.classList.remove('dropping'); });
    veil.addEventListener('drop', (ev) => {
      if (!hasFiles(ev)) return;
      ev.preventDefault();
      depth = 0; win.classList.remove('dropping');
      status.textContent = C.reading;
      filesFromDrop(ev.dataTransfer).then((files) => { if (open) { status.textContent = ''; useFiles(files); } });
    });
  }
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
    wake: () => legal.wakeNote('Choose a picture source to begin.'),
    get open() { return open; },
    get above() { return above; },
  };
}
