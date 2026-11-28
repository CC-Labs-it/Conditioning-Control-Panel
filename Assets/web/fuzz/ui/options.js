/* ui/options.js - the pause card's Options view (owner 2026-11-23: "an option voice in the pause menu - camera,
 * fullscreen, media type, a selector for the media folder"). The card's markup is in index.html (#options); this file
 * shows and hides it and works the rows that need a host or the browser:
 *   Full screen     a host that sends caps.window.fullscreen (CONTRACT.md 3.3) gets `set-setting window.fullscreen`;
 *                   the unframed page uses the browser's own full screen; any other host shows no row.
 *   The folder row  only for a host that keeps the player's folder (caps.ownMedia.folder, CONTRACT.md 3.3): the host
 *                   opens its own folder window, remembers the pick and serves the files; nothing is uploaded.
 *   The library    no row: a host with caps.ownMedia.library (the CCP desktop app) hands "Use local files" the pictures
 *                   the app itself uses (useLibrary below, read by ui/picture-card.js).
 *   Camera          the Expo's own look options (ui/expo/camera-options.js), offered wherever the Expo can be walked.
 *                   During a visit the Expo keeps its own copy on the pause card, so this one stands down.
 *   Account         a host with an account page of its own (caps.account, CONTRACT.md 3.3): the row names who is signed
 *                   in and the button asks the host for that page (`account`), where signing out lives.
 * Mute, Gentle visuals and the pictures row keep their ids and their wiring in ui/app.js. */
import { PICTURE_CARD as C } from '../data/content/terms.js';
import { createCameraPreferences, mountCameraOptions } from './expo/door.js';

export const FULLSCREEN_KEY = 'window.fullscreen';

/** How the Full screen row works here: 'host', 'page', or '' for no row. */
export function fullscreenRoute({ hosted = false, caps = null, doc = null } = {}) {
  if (hosted) return typeof caps?.window?.fullscreen === 'boolean' ? 'host' : '';
  return doc?.fullscreenEnabled === true && typeof doc.documentElement?.requestFullscreen === 'function' ? 'page' : '';
}

/** True for a host that keeps and serves the player's own folder. */
export const folderHost = (caps, hosted) => hosted === true && caps?.ownMedia?.folder === true;

/** True for a host that answers "Use local files" with the pictures the app itself uses (caps.ownMedia.library,
 * CONTRACT.md 3.3: the CCP desktop app's ticked library). */
export const libraryHost = (caps, hosted) => hosted === true && caps?.ownMedia?.library === true;

/** The one https origin a library host serves its rows from, or ''. */
export function libraryOrigin(caps) {
  try { const u = new URL(String(caps?.ownMedia?.mediaOrigin || '')); return u.protocol === 'https:' ? u.origin : ''; } catch { return ''; }
}

/** True for a host that has an account page of its own to show. */
export const accountHost = (caps, hosted) => hosted === true && caps?.account === true;

const NONE = Object.freeze({ open() {}, close() { return false; }, refresh() {}, pickFolder() { return false; }, canPickFolder: false,
  useLibrary() { return false; }, canUseLibrary: false, get isOpen() { return false; } });

export function mountOptions({ doc, win = doc?.defaultView, caps = {}, hosted = false, send = () => {}, on = () => () => {},
  pictures = null, expo = false, identity = null, onChange = () => {} } = {}) {
  const $ = (id) => doc.getElementById(id);
  const card = $('pause')?.querySelector('[role=dialog]'), view = $('options');
  if (!card || !view) return NONE;
  const back = $('options-back');
  back.textContent = C.back;

  /* ---- full screen ---- */
  const route = fullscreenRoute({ hosted, caps, doc });
  const fullRow = $('fullscreen-row'), full = $('fullscreen');
  fullRow.hidden = !route;
  if (route === 'host') {
    full.checked = caps.window.fullscreen;
    full.addEventListener('change', () => send('set-setting', { key: FULLSCREEN_KEY, value: full.checked }));
    on('setting', (f) => { if (f?.key === FULLSCREEN_KEY) full.checked = !!f.value; });
  } else if (route === 'page') {
    const paint = () => { full.checked = !!doc.fullscreenElement; };
    full.addEventListener('change', () => {
      const going = full.checked ? doc.documentElement.requestFullscreen() : doc.exitFullscreen?.();
      Promise.resolve(going).catch(() => {}).then(paint);
    });
    doc.addEventListener('fullscreenchange', paint);
    paint();
  }

  /* ---- the player's folder, kept by the host ---- */
  const folders = folderHost(caps, hosted) && !!pictures?.replaceOwnHosted;
  const folderRow = $('folder-row'), folderNow = $('folder-now'), folderPick = $('folder-pick');
  let folderPath = folders && typeof caps.ownMedia.path === 'string' ? caps.ownMedia.path : '';
  const waiting = new Set();
  const paintFolder = () => { folderNow.textContent = folderPath; folderNow.title = folderPath; };
  function pickFolder(done) {
    if (!folders) return false;
    if (typeof done === 'function') waiting.add(done);
    send('own-media-pick', {});
    return true;
  }
  folderRow.hidden = !folders;
  if (folders) {
    folderPick.textContent = C.folder;
    folderPick.addEventListener('click', (ev) => { if (ev.isTrusted) pickFolder(); });
    on('own-media', (frame) => {
      const rows = Array.isArray(frame?.rows) ? frame.rows : [];
      const asked = frame?.asked === true;   // the answer to a pick; the list at start changes no source
      let kept = 0;
      if (rows.length) {
        kept = pictures.replaceOwnHosted(rows);
        if (kept) { folderPath = typeof frame.path === 'string' ? frame.path : folderPath; if (asked) pictures.setSource('own'); }
      }
      if (asked) { for (const done of [...waiting]) { try { done(kept, frame?.reason || ''); } catch { /* one card's answer never stops the rest */ } } waiting.clear(); }
      paintFolder();
      onChange();
    });
    paintFolder();
    send('own-media-list', {});   // a folder remembered from an earlier launch: its files, once the host has read them
  }

  /* ---- the app's own library, kept by the host (a folder host never is one) ---- */
  const library = !folders && libraryHost(caps, hosted) && !!pictures?.replaceOwnHosted;
  const libraryFrom = library ? libraryOrigin(caps) : '';
  let libraryRows = null;   // null until the host has answered own-media-list
  const libraryWaiting = [];
  /** "Use local files" on a library host: done(kept) with how many went in, 0 when the app has nothing ticked (the card
   * then opens the browser's own picker, as before). Answers whether done already ran, inside this call. */
  function useLibrary(done) {
    if (!library) return false;
    const apply = () => {
      const kept = libraryRows?.length ? pictures.replaceOwnHosted(libraryRows, libraryFrom) : 0;
      if (kept) { pictures.setSource('own'); onChange(); }
      try { done?.(kept); } catch { /* the card's answer never stops the page */ }
    };
    if (libraryRows) { apply(); return true; }
    libraryWaiting.push(apply);
    return false;
  }
  if (library) {
    on('own-media', (frame) => {
      libraryRows = Array.isArray(frame?.rows) ? frame.rows : [];
      for (const apply of libraryWaiting.splice(0)) apply();
    });
    send('own-media-list', {});   // the library as the app has it ticked now; nothing changes source until a press
  }

  /* ---- the host's account page ---- */
  const accountRow = $('account-row');
  accountRow.hidden = !accountHost(caps, hosted);
  if (!accountRow.hidden) {
    $('account-now').textContent = typeof identity?.displayName === 'string' ? identity.displayName : '';
    $('account-open').addEventListener('click', (ev) => { if (ev.isTrusted) send('account', {}); });
  }

  /* ---- camera ---- */
  const slot = $('options-camera');
  let camera = null, prefs = null;
  if (expo && slot) {
    prefs = createCameraPreferences(win);
    camera = mountCameraOptions(doc, prefs.options, (key, value) => { prefs.set(key, value); }, slot);
  }
  /* The Expo mounts its own block straight on the card for the length of a visit: it is the live one. */
  const expoOwnBlock = () => [...card.children].some((node) => node.classList?.contains('expo-camera-options'));
  function refreshCamera() {
    if (!camera) return;
    slot.hidden = expoOwnBlock();
    Object.assign(prefs.options, createCameraPreferences(win).options);   // a visit may have changed them since
    camera.refresh();
  }

  function open() {
    refreshCamera();
    paintFolder();
    view.hidden = false;
    card.classList.add('options-open');
    back.focus({ preventScroll: true });
  }
  function close() {
    if (view.hidden) return false;
    view.hidden = true;
    card.classList.remove('options-open');
    camera?.close();
    return true;
  }
  $('options-open').addEventListener('click', open);
  back.addEventListener('click', () => { close(); $('resume')?.focus({ preventScroll: true }); });

  return { open, close, refresh: paintFolder, pickFolder, canPickFolder: folders, useLibrary, canUseLibrary: library,
    get isOpen() { return !view.hidden; } };
}
