import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  CREDIT_TEXT, isScrolllerUrl, isScrolllerGif, dealHasScrolller, createCreditHolds, sourceCredit,
} from '../../shared/source-credit.js';

test('the credit is spelled with three l', () => {
  assert.equal(CREDIT_TEXT, 'via Scrolller');
});

test('scrolller hosts and the web clip hop are Scrolller, look-alikes are not', () => {
  assert.equal(isScrolllerUrl('https://images.scrolller.com/x/y.mp4'), true);
  assert.equal(isScrolllerUrl('https://scrolller.com/r/x'), true);
  const inner = encodeURIComponent('https://images.scrolller.com/a/b.mp4');
  assert.equal(isScrolllerUrl('/api/clip?u=' + inner), true);
  assert.equal(isScrolllerUrl('https://backroom-playtest.vercel.app/api/clip?u=' + inner), true);
  assert.equal(isScrolllerUrl('/api/clip?u=' + encodeURIComponent('https://example.com/a.mp4')), false);
  assert.equal(isScrolllerUrl('https://ccp.assets/.temp/ccp_temp_remote_1.webm'), false);
  assert.equal(isScrolllerUrl('https://notscrolller.com/x'), false);
  assert.equal(isScrolllerUrl('https://scrolller.com.example.org/x'), false);
  assert.equal(isScrolllerUrl(''), false);
  assert.equal(isScrolllerUrl(null), false);
});

test('a dealt gif is Scrolller by src online, never by fallback or pool', () => {
  assert.equal(isScrolllerGif({ url: 'https://ccp.assets/.temp/ccp_temp_remote_1.webm', src: 'online' }), true);
  assert.equal(isScrolllerGif({ url: 'https://ccp.assets/me/a.gif', src: 'pool' }), false);
  assert.equal(isScrolllerGif({ url: 'https://ccp.game/backroom/stations/slot/fallback/gif3.webp', src: 'fallback' }), false);
  assert.equal(isScrolllerGif({ url: 'https://images.scrolller.com/a.mp4' }), true);
  assert.equal(isScrolllerGif(null), false);
});

test('a deal carries the credit when any one picture is Scrolller', () => {
  const mixed = { gifs: [{ url: 'https://ccp.assets/me/a.gif', src: 'pool' }, { url: 'https://ccp.assets/.temp/x.webm', src: 'online' }] };
  assert.equal(dealHasScrolller(mixed), true);
  assert.equal(dealHasScrolller({ gifs: [{ url: 'https://ccp.assets/me/a.gif', src: 'pool' }] }), false);
  assert.equal(dealHasScrolller({ reqId: 'r', seed: 0, gifs: [], words: [], timeout: true }), false);
  assert.equal(dealHasScrolller(null), false);
});

test('holds: the mark stands while any surface holds it', () => {
  const h = createCreditHolds();
  assert.equal(h.visible(), false);
  h.set('room-walls', true);
  h.set('seat:slot', true);
  h.set('room-walls', false);
  assert.equal(h.visible(), true);
  h.set('seat:slot', false);
  assert.equal(h.visible(), false);
  h.set('a', true); h.clear();
  assert.equal(h.visible(), false);
});

test('the page mark is inert without a document and follows deals', () => {
  sourceCredit.follow('seat:t', { gifs: [{ url: 'x', src: 'online' }] });
  assert.equal(sourceCredit.visible(), true);
  sourceCredit.follow('seat:t', { gifs: [{ url: 'x', src: 'pool' }] });
  assert.equal(sourceCredit.visible(), false);
  sourceCredit.hold('k'); sourceCredit.drop('k'); sourceCredit.clear();
  assert.equal(sourceCredit.visible(), false);
});
