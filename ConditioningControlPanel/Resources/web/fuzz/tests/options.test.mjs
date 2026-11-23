/* The pause card's Options view (ui/options.js, CONTRACT.md 3.3): which rows a host gets, and the player's own folder
 * as a host serves it. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fullscreenRoute, folderHost, accountHost, FULLSCREEN_KEY } from '../ui/options.js';
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
  assert.ok(app.includes("$('to-menu').hidden = !toMenu || !!wakeGate;"));
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
