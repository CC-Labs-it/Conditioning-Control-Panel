import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { needsDomMedia } from '../ui/unlocks/arcade/slot-reel-media.js';

const WEB = 'https://tc.cclabs.app';

test('Scrolller files on the website go to the DOM overlay, not the reel texture', () => {
  // images.scrolller.com sends no Access-Control-Allow-Origin to our origins (checked 2026-11-08).
  assert.equal(needsDomMedia('https://images.scrolller.com/neutron/a-540x594.webp', WEB), true);
  assert.equal(needsDomMedia('https://images.scrolller.com/neutron/a.mp4', 'http://127.0.0.1:8833'), true);
});

test('same-origin, blob and desktop host files keep the WebGL path', () => {
  assert.equal(needsDomMedia(`${WEB}/fuzz/assets/a.webp`, WEB), false);
  assert.equal(needsDomMedia('blob:https://tc.cclabs.app/4f2c', WEB), false);
  assert.equal(needsDomMedia('https://ccp.assets/scrolller/a.webp', 'https://ccp.fuzz'), false);
  assert.equal(needsDomMedia('not a url', WEB), false);
});

test('the reel overlay never takes a pointer and keeps the credit above it', () => {
  const overlay = fs.readFileSync(new URL('../ui/unlocks/arcade/slot-reel-overlay.js', import.meta.url), 'utf8');
  assert.match(overlay, /\.mini-slot-reel-pics\{[^}]*pointer-events:none/);
  assert.doesNotMatch(overlay, /getImageData|toDataURL|toBlob|crossOrigin/);
  const slots = fs.readFileSync(new URL('../ui/unlocks/arcade/slots.js', import.meta.url), 'utf8');
  // Inserted before the status line, so the credit, result and Spin button stack above it.
  assert.match(slots, /createReelOverlay\(ctx,root,result\)/);
  assert.ok(slots.indexOf("'via Scrolller'") > slots.indexOf('createReelOverlay(ctx'));
});
