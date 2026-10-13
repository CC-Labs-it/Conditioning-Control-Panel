// Pictures first, clips sparse later (engine/cardMix.js).
import test from 'node:test';
import assert from 'node:assert/strict';
import { videoAllowed, newCardMix, noteCard, IMAGES_FIRST, VIDEO_EVERY } from '../engine/cardMix.js';

test('a run opens on pictures only', () => {
  const m = newCardMix();
  for (let i = 0; i < IMAGES_FIRST; i++) {
    assert.equal(videoAllowed(m.cards, m.sinceVideo, true), false, 'card ' + i);
    noteCard(m, 'image');
  }
  assert.equal(videoAllowed(m.cards, m.sinceVideo, true), true, 'the first clip may come now');
});

test('after a clip the next ones are pictures again', () => {
  const m = newCardMix();
  for (let i = 0; i < IMAGES_FIRST; i++) noteCard(m, 'image');
  noteCard(m, 'video');
  for (let i = 0; i < VIDEO_EVERY; i++) {
    assert.equal(videoAllowed(m.cards, m.sinceVideo, true), false);
    noteCard(m, i % 2 ? 'gif' : 'image');
  }
  assert.equal(videoAllowed(m.cards, m.sinceVideo, true), true);
});

test('a library with no pictures is never held back', () => {
  const m = newCardMix();
  assert.equal(videoAllowed(m.cards, m.sinceVideo, false), true);
});
