// Owner 2026-11-08: whole folders as own files. A pick keeps only what a browser can show, and a drop reads folders down.
import test from 'node:test';
import assert from 'node:assert/strict';
import { ownUsable, OWN_MAX } from '../ui/pictures.js';
import { filesFromDrop } from '../ui/folder-files.js';

const f = (name, type = '', size = 10) => ({ name, type, size, lastModified: 1 });

test('a folder pick keeps pictures, GIFs and videos and drops the rest', () => {
  const got = ownUsable([
    f('a.jpg', 'image/jpeg'), f('b.gif', 'image/gif'), f('c.mp4', 'video/mp4'), f('notes.txt', 'text/plain'),
    f('._a.jpg', 'image/jpeg'), f('.thumb.png', 'image/png'), f('phone.HEIC', 'image/heic'), f('scan.tif', ''),
    f('icon.svg', 'image/svg+xml'), f('empty.png', 'image/png', 0), f('untyped.webp'), f('Thumbs.db'),
  ]).map((x) => x.name);
  assert.deepEqual(got, ['a.jpg', 'b.gif', 'c.mp4', 'untyped.webp']);
});

test('a huge folder is capped to a random spread, not its first files', () => {
  const many = Array.from({ length: OWN_MAX * 3 }, (_, i) => f(`p${i}.jpg`, 'image/jpeg'));
  const got = ownUsable(many);
  assert.equal(got.length, OWN_MAX);
  assert.ok(got.some((x) => Number(x.name.slice(1, -4)) >= OWN_MAX * 2), 'reaches the end of the folder');
});

/* A fake drop: entries shaped like the browser's FileSystemEntry API, readEntries answering in batches. */
const fileEntry = (file) => ({ isFile: true, isDirectory: false, file: (ok) => ok(file) });
const dirEntry = (children, batch = 2) => ({
  isFile: false, isDirectory: true,
  createReader() { let at = 0; return { readEntries(ok) { const b = children.slice(at, at + batch); at += batch; ok(b); } }; },
});
const drop = (...entries) => ({ items: entries.map((e) => ({ kind: 'file', webkitGetAsEntry: () => e })), files: [] });

test('a drop reads files and nested folders, any mix', async () => {
  const dt = drop(fileEntry(f('top.png', 'image/png')), dirEntry([fileEntry(f('a.jpg')), dirEntry([fileEntry(f('deep.gif')), fileEntry(f('deep2.mp4'))]), fileEntry(f('b.jpg'))]));
  const names = (await filesFromDrop(dt)).map((x) => x.name);
  assert.deepEqual(names, ['top.png', 'a.jpg', 'deep.gif', 'deep2.mp4', 'b.jpg']);
});

test('a drop stops at its limit', async () => {
  const dt = drop(dirEntry(Array.from({ length: 50 }, (_, i) => fileEntry(f(`x${i}.jpg`))), 7));
  assert.equal((await filesFromDrop(dt, { limit: 12 })).length, 12);
});
