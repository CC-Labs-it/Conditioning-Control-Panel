/* The pause card's Options view (ui/options.js, CONTRACT.md 3.3): which rows a host gets, and the player's own folder
 * as a host serves it. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fullscreenRoute, folderHost, accountHost, libraryHost, libraryOrigin, mountOptions, FULLSCREEN_KEY } from '../ui/options.js';
import { createPictures, hostedFiles } from '../ui/pictures.js';
import { pictureRow } from '../ui/picture-card.js';
import { PICTURE_CARD } from '../data/content/terms.js';

const read = (name) => readFileSync(new URL('../' + name, import.meta.url), 'utf8');

test('the Full screen row belongs to a host that says its window can, or to the unframed page', () => {
  assert.equal(FULLSCREEN_KEY, 'window.fullscreen');
  assert.equal(fullscreenRoute({ hosted: true, caps: { window: { fullscreen: false } } }), 'host');
  assert.equal(fullscreenRoute({ hosted: true, caps: { window: { fullscreen: true } } }), 'host');
  assert.equal(fullscreenRoute({ hosted: true, caps: {} }), '', 'a host that says nothing gets no row');
  assert.equal(fullscreenRoute({ hosted: true, caps: { window: { fullscreen: 'yes' } } }), '');
  const doc = { fullscreenEnabled: true, documentElement: { requestFullscreen() {} } };
  assert.equal(fullscreenRoute({ hosted: false, doc }), 'page');
  assert.equal(fullscreenRoute({ hosted: false, doc: { fullscreenEnabled: false, documentElement: { requestFullscreen() {} } } }), '');
  assert.equal(fullscreenRoute({ hosted: true, caps: {}, doc }), '', 'a framed page never takes the browser route');
});

test('the folder row belongs only to a host that keeps the folder', () => {
  assert.equal(folderHost({ ownMedia: { folder: true } }, true), true);
  assert.equal(folderHost({ ownMedia: { folder: true } }, false), false);
  assert.equal(folderHost({ ownMedia: {} }, true), false);
  assert.equal(folderHost({}, true), false);
});

test('the Account row belongs only to a host with an account page, and Main menu only to a build with a menu', () => {
  assert.equal(accountHost({ account: true }, true), true);
  assert.equal(accountHost({ account: true }, false), false);
  assert.equal(accountHost({}, true), false);
  const html = read('index.html');
  assert.ok(html.includes('<button id="to-menu" type="button" hidden>Main menu</button>'), 'hidden until main.js hands a way back');
  assert.ok(html.includes('id="account-row" hidden'));
  const app = read('ui/app.js');
  /* Wave 4 lane E item 4: Escape on the main menu opens the pause card over it (by design: the menu has no Leave, and
     Escape always pauses); its Main menu row only reloaded to the same menu, so it does not show while the menu is up. */
  assert.ok(app.includes("$('to-menu').hidden = !toMenu || !!wakeGate || !!doc.body?.classList?.contains('mm-on');"));
  assert.ok(read('ui/main-menu.js').includes("doc.body.classList.add('mm-on')"), 'the menu marks the page while it shows');
});

test('main.js hands the pause card its way back to the menu, and the menu Options row opens the Options view', () => {
  const main = read('main.js');
  const hooks = main.slice(main.indexOf('const hooks = {'), main.indexOf('ui.attach(engine, hooks);'));
  assert.match(hooks, /\n\s*mainMenu: \(\) => \{ persist\(\); leaving = true; location\.reload\(\); \},/, 'without hooks.mainMenu the Main menu row never shows');
  assert.ok(main.includes("options: () => over('options', () => ui.openOptions()),"), 'the menu Options row opens the view, not the bare pause card');
  assert.ok(read('ui/app.js').includes('openOptions()'));
});

test('a host row counts only on the page origin, and keeps the host reading of its kind', () => {
  const origin = 'https://tc.game';
  const files = hostedFiles([
    { url: origin + '/__own/1/0', name: 'a.jpg', type: 'image/jpeg', size: 10, mtime: 5, kind: 'picture' },
    { url: 'https://elsewhere.example/b.jpg', name: 'b.jpg', type: 'image/jpeg', size: 10 },
    { url: origin + '/__own/1/2', name: 'c.webp', type: 'image/webp', size: 99, kind: 'gif' },
    { url: origin + '/__own/1/3', name: 'd.mp4', type: 'video/mp4', size: 99, kind: 'banana' },
    { url: 'not a url' }, null,
  ], origin);
  assert.deepEqual(files.map((f) => [f.name, f.kind, f.url]), [
    ['a.jpg', 'picture', origin + '/__own/1/0'], ['c.webp', 'gif', origin + '/__own/1/2'], ['d.mp4', null, origin + '/__own/1/3'],
  ]);
  assert.deepEqual(hostedFiles('nope', origin), []);
  assert.deepEqual(hostedFiles([{ url: origin + '/x' }], ''), [], 'no page origin, no rows');
});

test('a row with no host reading has its header fetched by range', async () => {
  const asked = [];
  const fetchImpl = async (url, init) => { asked.push([url, init.headers.range]); return { arrayBuffer: async () => new ArrayBuffer(4) }; };
  const [file] = hostedFiles([{ url: 'https://tc.game/__own/1/0', name: 'a.png', type: 'image/png', size: 5000 }], 'https://tc.game', fetchImpl);
  await file.slice(0, 4096).arrayBuffer();
  await file.slice(8, 16).arrayBuffer();
  assert.deepEqual(asked, [['https://tc.game/__own/1/0', 'bytes=0-4095'], ['https://tc.game/__own/1/0', 'bytes=8-15']]);
});

test('a folder from the host becomes the own files, by url, and the row says folder', () => {
  const origin = 'https://tc.game';
  let changes = 0;
  const p = createPictures({ source: 'own', pageOrigin: origin, onChange: () => { changes++; } });
  assert.equal(pictureRow(p), PICTURE_CARD.rowOwnEmpty);
  const kept = p.replaceOwnHosted([
    { url: origin + '/__own/1/0', name: 'a.jpg', type: 'image/jpeg', size: 10, mtime: 1, kind: 'picture' },
    { url: origin + '/__own/1/1', name: 'b.gif', type: 'image/gif', size: 10, mtime: 1, kind: 'gif' },
    { url: origin + '/__own/1/2', name: 'c.mp4', type: 'video/mp4', size: 10, mtime: 1, kind: 'clip' },
    { url: origin + '/__own/1/3', name: 'notes.txt', type: 'text/plain', size: 10, mtime: 1 },
    { url: 'https://elsewhere.example/x.jpg', name: 'x.jpg', type: 'image/jpeg', size: 10, kind: 'picture' },
  ]);
  assert.equal(kept, 3);
  assert.deepEqual(p.ownCounts, { picture: 1, gif: 1, clip: 1 });
  assert.equal(p.ownFromFolder, true);
  assert.equal(pictureRow(p), PICTURE_CARD.rowFolder.replace('{n}', 3));
  assert.equal(p.replaceOwnHosted([]), 0, 'an empty answer changes nothing');
  assert.equal(p.ownCount, 3);
  p.clearOwn();
  assert.equal(p.ownFromFolder, false);
});

test('the library belongs only to a host that says so, with one https media origin', () => {
  assert.equal(libraryHost({ ownMedia: { library: true } }, true), true);
  assert.equal(libraryHost({ ownMedia: { library: true } }, false), false, 'the website and a plain browser never');
  assert.equal(libraryHost({ ownMedia: { folder: true } }, true), false);
  assert.equal(libraryHost({}, true), false);
  assert.equal(libraryOrigin({ ownMedia: { mediaOrigin: 'https://ccp.assets' } }), 'https://ccp.assets');
  assert.equal(libraryOrigin({ ownMedia: { mediaOrigin: 'https://ccp.assets/x/y' } }), 'https://ccp.assets');
  assert.equal(libraryOrigin({ ownMedia: { mediaOrigin: 'http://ccp.assets' } }), '');
  assert.equal(libraryOrigin({ ownMedia: { mediaOrigin: 'nope' } }), '');
  assert.equal(libraryOrigin({}), '');
});

test('rows on the host-named media origin count beside the page origin, any other origin still does not', () => {
  const page = 'https://ccp.game', media = 'https://ccp.assets';
  const rows = [
    { url: media + '/images/a.jpg', name: 'images/a.jpg', type: 'image/jpeg', size: 10, kind: 'picture' },
    { url: page + '/own/b.gif', name: 'b.gif', type: 'image/gif', size: 10, kind: 'gif' },
    { url: 'https://elsewhere.example/c.jpg', name: 'c.jpg', type: 'image/jpeg', size: 10, kind: 'picture' },
  ];
  assert.deepEqual(hostedFiles(rows, page).map((f) => f.name), ['b.gif'], 'no media origin: the page origin only, as before');
  assert.deepEqual(hostedFiles(rows, page, undefined, media).map((f) => f.name), ['images/a.jpg', 'b.gif']);
});

/* A stand-in page for mountOptions: every id is an element that takes listeners and ignores the rest. */
function fakeDoc() {
  const els = new Map();
  const el = () => ({ hidden: false, textContent: '', title: '', checked: false, children: [], classList: { add() {}, remove() {}, contains() { return false; } },
    addEventListener() {}, focus() {}, querySelector() { return el(); } });
  return { defaultView: {}, getElementById: (id) => { if (!els.has(id)) els.set(id, el()); return els.get(id); } };
}

function mountLibrary(caps) {
  const sent = [], handlers = new Map();
  const pictures = createPictures({ pageOrigin: 'https://ccp.game' });
  const view = mountOptions({ doc: fakeDoc(), caps, hosted: true, pictures,
    send: (t, f) => sent.push([t, f]), on: (t, fn) => { handlers.set(t, fn); return () => {}; } });
  return { view, pictures, sent, answer: (frame) => handlers.get('own-media')?.(frame) };
}

const LIBRARY = { ownMedia: { library: true, mediaOrigin: 'https://ccp.assets' } };
const LIBRARY_ROWS = [
  { url: 'https://ccp.assets/images/a.jpg', name: 'images/a.jpg', type: 'image/jpeg', size: 10, mtime: 1, kind: 'picture' },
  { url: 'https://ccp.assets/images/b.gif', name: 'images/b.gif', type: 'image/gif', size: 10, mtime: 1, kind: 'gif' },
  { url: 'https://ccp.assets/videos/c.mp4', name: 'videos/c.mp4', type: 'video/mp4', size: 10, mtime: 1, kind: 'clip' },
];

test('a library host: the list is asked once at start, and only the press switches the source to it', () => {
  const { view, pictures, sent, answer } = mountLibrary(LIBRARY);
  assert.equal(view.canUseLibrary, true);
  assert.equal(view.canPickFolder, false, 'the library is not a folder host');
  assert.deepEqual(sent.map(([t]) => t), ['own-media-list']);
  answer({ type: 'own-media', v: 1, path: '', rows: LIBRARY_ROWS, asked: false });
  assert.equal(pictures.source, 'none', 'the answer alone changes nothing');
  let kept = null;
  assert.equal(view.useLibrary((n) => { kept = n; }), true, 'answered inside the press');
  assert.equal(kept, 3);
  assert.equal(pictures.source, 'own');
  assert.deepEqual(pictures.ownCounts, { picture: 1, gif: 1, clip: 1 });
});

test('a press before the answer waits for it, and an empty library hands the press back to the browser picker', () => {
  const early = mountLibrary(LIBRARY);
  let kept = null;
  assert.equal(early.view.useLibrary((n) => { kept = n; }), false, 'still on its way');
  assert.equal(kept, null);
  early.answer({ type: 'own-media', v: 1, path: '', rows: LIBRARY_ROWS, asked: false });
  assert.equal(kept, 3);
  assert.equal(early.pictures.source, 'own');

  const empty = mountLibrary(LIBRARY);
  empty.answer({ type: 'own-media', v: 1, path: '', rows: [], asked: false });
  let none = null;
  assert.equal(empty.view.useLibrary((n) => { none = n; }), true);
  assert.equal(none, 0, 'nothing ticked in the app: the card opens its own file input');
  assert.equal(empty.pictures.source, 'none');
});

test('without the library cap nothing is asked and Use local files is the browser picker, as on the website', () => {
  const { view, sent } = mountLibrary({});
  assert.equal(view.canUseLibrary, false);
  assert.deepEqual(sent, []);
  assert.equal(view.useLibrary(() => assert.fail('never called')), false);
  const card = read('ui/picture-card.js');
  assert.ok(card.includes("if (!useLibrary) { input.click(); return; }"));
  assert.ok(read('main.js').includes('useLibrary: ui.options?.canUseLibrary ? ui.options.useLibrary : null,'));
});

test('the Options view holds the rows that left the pause card, under their old ids', () => {
  const html = read('index.html');
  const view = html.slice(html.indexOf('<section id="options"'), html.indexOf('</section>', html.indexOf('<section id="options"')));
  for (const id of ['mute', 'gentle', 'fullscreen', 'pics-now', 'pics-change', 'folder-now', 'folder-pick', 'options-camera', 'options-back']) {
    assert.ok(view.includes(`id="${id}"`), id + ' is in the Options view');
    assert.equal(html.split(`id="${id}"`).length, 2, id + ' exists once');
  }
  assert.ok(html.includes('<button id="options-open" type="button">Options</button>'));
  assert.match(read('styles.css'), /#pause \.card\.options-open > :not\(#options\) \{ display: none !important; \}/);
});

test('Escape opens the card during the intro, the Wake Up button keeps its line', () => {
  const app = read('ui/app.js');
  assert.ok(app.includes("if (wakeGate && host !== true && host !== 'key') { wakeGate(); return; }"));
  assert.ok(app.includes("if (engine.paused) resume(); else pause('key');"));
  assert.ok(app.includes("el.wake.addEventListener('click', () => pause());"));
});
